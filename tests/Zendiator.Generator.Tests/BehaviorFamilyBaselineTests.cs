using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Zendiator.SourceGenerator;
using Xunit;

namespace Zendiator.Generator.Tests;

// Current no-hook consumers are controls for the later 0.5 backends, not evidence of hook support.
public sealed class BehaviorFamilyBaselineTests
{
    private const string Head = "using Zendiator; using System; using System.Threading; using System.Threading.Tasks; using System.Collections.Generic; namespace App; [GenerateZendiator] public sealed partial class Zendiator; ";
    private static readonly MetadataReference[] References = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
        .Select(p => MetadataReference.CreateFromFile(p)).ToArray();

    public static IEnumerable<object[]> Consumers()
    {
        yield return ["send-async-response", Single("IRequest<int>", "IRequestHandler<Request,int>", "public ValueTask<int> HandleAsync(Request r, CancellationToken c) => new(1);"), "SendAsync", "direct"];
        yield return ["send-async-void", Single("IRequest", "IRequestHandler<Request>", "public ValueTask HandleAsync(Request r, CancellationToken c) => default;"), "SendAsync", "direct"];
        yield return ["send-sync-response", Single("ISyncRequest<int>", "ISyncRequestHandler<Request,int>", "public int Handle(Request r, CancellationToken c) => 1;"), "SendSync", "sync"];
        yield return ["send-sync-void", Single("ISyncRequest", "ISyncRequestHandler<Request>", "public void Handle(Request r, CancellationToken c) {}"), "SendSync", "sync"];
        yield return ["send-all-async-response", Multiple("IMultiRequest<int>", "IRequestHandler<Request,int>", "public ValueTask<int> HandleAsync(Request r, CancellationToken c) => new(1);"), "SendAllAsync", "multi-async"];
        yield return ["send-all-async-void", Multiple("IMultiRequest", "IRequestHandler<Request>", "public ValueTask HandleAsync(Request r, CancellationToken c) => default;"), "SendAllAsync", "multi-async"];
        yield return ["send-all-sync-response", Multiple("ISyncMultiRequest<int>", "ISyncRequestHandler<Request,int>", "public int Handle(Request r, CancellationToken c) => 1;"), "SendAllSync", "multi-sync-span"];
        yield return ["send-all-sync-void", Multiple("ISyncMultiRequest", "ISyncRequestHandler<Request>", "public void Handle(Request r, CancellationToken c) {}"), "SendAllSync", "multi-sync"];
        yield return ["stream", Single("IStreamRequest<int>", "IStreamRequestHandler<Request,int>", "public async IAsyncEnumerable<int> HandleAsync(Request r, CancellationToken c) { await Task.Yield(); yield return 1; }"), "StreamAsync", "stream"];
        yield return ["notification-empty", "public sealed record Request : INotification;", "PublishAsync", "notification-empty"];
        yield return ["notification-single", Single("INotification", "INotificationHandler<Request>", "public ValueTask HandleAsync(Request r, CancellationToken c) => default;"), "PublishAsync", "notification-single"];
        yield return ["notification-multiple", Multiple("INotification", "INotificationHandler<Request>", "public ValueTask HandleAsync(Request r, CancellationToken c) => default;"), "PublishAsync", "notification-multiple"];
        yield return ["send-async-open", "public readonly record struct Request<T> : IRequest<T>; public sealed class Handler<T> : IRequestHandler<Request<T>,T> { public ValueTask<T> HandleAsync(Request<T> r, CancellationToken c) => new(default(T)!); }", "SendAsync", "open"];
        yield return ["send-sync-ref", "public ref struct Request : ISyncRequest<int>; public sealed class Handler : ISyncRequestHandler<Request,int> { public int Handle(Request r, CancellationToken c) => 1; }", "SendSync", "ref"];
        yield return ["send-all-sync-ref", "public ref struct Request : ISyncMultiRequest<int>; public sealed class HandlerA : ISyncRequestHandler<Request,int> { public int Handle(Request r, CancellationToken c) => 1; } public sealed class HandlerB : ISyncRequestHandler<Request,int> { public int Handle(Request r, CancellationToken c) => 2; }", "SendAllSync", "ref-span"];
    }

    private static string Single(string request, string handler, string body) =>
        $"public sealed record Request : {request}; public sealed class Handler : {handler} {{ {body} }}";
    private static string Multiple(string request, string handler, string body) =>
        $"public sealed record Request : {request}; public sealed class HandlerA : {handler} {{ {body} }} public sealed class HandlerB : {handler} {{ {body} }}";

    [Theory]
    [MemberData(nameof(Consumers))]
    public void No_hook_consumers_keep_family_specific_typed_generation(string id, string body, string methodName, string shape)
    {
        var source = Head + body;
        var options = new CSharpParseOptions(LanguageVersion.Preview);
        var input = CSharpCompilation.Create("G01_" + id.Replace('-', '_'), [CSharpSyntaxTree.ParseText(source, options)], References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new ZendiatorGenerator().AsSourceGenerator()], parseOptions: options);
        driver = driver.RunGeneratorsAndUpdateCompilation(input, out var output, out var diagnostics);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        using var assembly = new MemoryStream();
        var emitted = output.Emit(assembly);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        var tree = Assert.Single(driver.GetRunResult().GeneratedTrees);
        var text = tree.ToString();
        var mediator = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(node => node.Identifier.ValueText == "Zendiator");
        var methods = mediator.Members.OfType<MethodDeclarationSyntax>()
            .Where(node => node.Identifier.ValueText == methodName && node.ParameterList.Parameters[0].Type!.ToString().StartsWith("global::App.Request", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(methods);
        Assert.DoesNotContain("IPipelineBehavior", text);
        Assert.DoesNotContain("IStreamPipelineBehavior", text);
        Assert.DoesNotContain("ISyncPipelineBehavior", text);
        Assert.DoesNotContain("BehaviorFusion", text);
        if (shape == "direct")
        {
            var method = Assert.Single(methods);
            Assert.False(method.Modifiers.Any(SyntaxKind.AsyncKeyword));
            Assert.Empty(method.DescendantNodes().OfType<AwaitExpressionSyntax>());
            var returned = Assert.Single(method.DescendantNodes().OfType<ReturnStatementSyntax>());
            var call = Assert.IsType<InvocationExpressionSyntax>(returned.Expression);
            Assert.Equal("HandleAsync", Assert.IsType<MemberAccessExpressionSyntax>(call.Expression).Name.Identifier.ValueText);
            Assert.DoesNotContain("Route0Node", text);
        }
        if (shape is "sync" or "open" or "ref" or "multi-sync" or "multi-sync-span" or "ref-span")
        {
            Assert.All(methods, method => Assert.Empty(method.DescendantNodes().OfType<AwaitExpressionSyntax>()));
            Assert.Contains(mediator.Members.OfType<StructDeclarationSyntax>(), node => node.Modifiers.Any(SyntaxKind.ReadOnlyKeyword));
        }
        if (shape == "multi-async")
        {
            var awaits = Assert.Single(methods).DescendantNodes().OfType<AwaitExpressionSyntax>().ToArray();
            Assert.Equal(2, awaits.Length);
            Assert.Contains("Branch0Node0", awaits[0].ToString());
            Assert.Contains("Branch1Node0", awaits[1].ToString());
        }
        if (shape is "multi-sync-span" or "ref-span")
        {
            var method = methods.Single(node => node.ParameterList.Parameters.Any(p => p.Identifier.ValueText == "destination"));
            var invokes = method.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Where(call => call.Expression is MemberAccessExpressionSyntax member && member.Name.Identifier.ValueText == "Invoke").ToArray();
            var writes = method.DescendantNodes().OfType<AssignmentExpressionSyntax>()
                .Where(assignment => assignment.Left is ElementAccessExpressionSyntax element && element.Expression.ToString() == "destination").ToArray();
            Assert.Equal(2, invokes.Length);
            Assert.Equal(2, writes.Length);
            Assert.True(writes[0].SpanStart > invokes[^1].SpanStart);
            Assert.Contains(method.DescendantNodes().OfType<IfStatementSyntax>(), guard => guard.Condition.ToString().Contains("destination.Length", StringComparison.Ordinal) && guard.SpanStart < invokes[0].SpanStart);
        }
        if (shape is "ref" or "ref-span")
            Assert.All(methods, method => Assert.Contains(method.ParameterList.Parameters[0].Modifiers, token => token.IsKind(SyntaxKind.ScopedKeyword)));
        if (shape == "stream")
        {
            Assert.Contains(mediator.Members.OfType<ClassDeclarationSyntax>(), node => node.Identifier.ValueText == "StreamRoute0Enumerable" && node.Modifiers.Any(SyntaxKind.SealedKeyword));
            Assert.Contains("StartCoreAsync", text);
        }
        if (shape == "notification-empty") Assert.Contains("return default;", Assert.Single(methods).ToString());
        if (shape == "notification-single") Assert.Contains("AsyncValueTaskMethodBuilder", text);
        if (shape == "notification-multiple") Assert.Contains("PoolingAsyncValueTaskMethodBuilder", text);
        SaveSnapshot(id, source, text);
    }

    private static void SaveSnapshot(string id, string input, string generated)
    {
        var directory = Environment.GetEnvironmentVariable("ZENDIATOR_G01_SNAPSHOTS");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, id + ".input.cs"), input);
        File.WriteAllText(Path.Combine(directory, id + ".generated.cs"), generated);
        File.WriteAllText(Path.Combine(directory, id + ".host.json"), JsonSerializer.Serialize(new
        {
            Roslyn = typeof(CSharpCompilation).Assembly.GetName().Version!.ToString(),
            Generator = typeof(ZendiatorGenerator).Assembly.GetName().Version!.ToString(),
            EmittedConsumer = true,
            LegacyDiscoveryControl = true
        }));
    }
}
