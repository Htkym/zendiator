using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Zendiator.SourceGenerator;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed class StreamGeneratorTests
{
    private const string Head = "using Zendiator; using System; using System.Threading; using System.Threading.Tasks; using System.Collections.Generic; namespace App; [GenerateZendiator] public sealed partial class Zendiator; ";
    private const string StreamReq = "public sealed record GetItems(int Count) : IStreamRequest<int>; ";
    private const string StreamHandler = "public sealed class GetItemsHandler : IStreamRequestHandler<GetItems,int> { public async IAsyncEnumerable<int> HandleAsync(GetItems r, CancellationToken c) { await Task.Yield(); yield return 0; } } ";
    private static readonly MetadataReference[] References = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
        .Select(p => MetadataReference.CreateFromFile(p)).ToArray();
    private static CSharpCompilation Compilation(string source, string name = "Test") => CSharpCompilation.Create(name,
        [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview))], References,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    private static GeneratorDriver Driver() => CSharpGeneratorDriver.Create([new ZendiatorGenerator().AsSourceGenerator()],
        parseOptions: new CSharpParseOptions(LanguageVersion.Preview), driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, true));

    private static GeneratorDriverRunResult Run(CSharpCompilation input, bool success)
    {
        var driver = Driver().RunGeneratorsAndUpdateCompilation(input, out var output, out var diagnostics);
        if (success)
        {
            Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
            Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
            using var stream = new MemoryStream();
            Assert.True(output.Emit(stream).Success, string.Join("\n", output.GetDiagnostics()));
        }
        return driver.GetRunResult();
    }

    [Theory]
    [InlineData("typeof(V)", "public sealed class V;")]
    [InlineData("typeof(V<>)", "public sealed class V<T> : IStreamRequestValidator<GetItems> { public void Validate(GetItems r) {} }")]
    [InlineData("typeof(V)", "public abstract class V : IStreamRequestValidator<GetItems> { public void Validate(GetItems r) {} }")]
    [InlineData("typeof(V)", "file sealed class V : IStreamRequestValidator<GetItems> { public void Validate(GetItems r) {} }")]
    [InlineData("typeof(V)", "public sealed class V : IStreamRequestValidator<GetItems>, IStreamRequestValidator<object> { public void Validate(GetItems r) {} public void Validate(object r) {} }")]
    [InlineData("null", "")]
    public void Explicit_validator_registration_rejects_unavailable_or_unmatched_contracts(string type, string declaration)
    {
        var head = Head.Replace("[GenerateZendiator]", $"[GenerateZendiator, StreamRequestValidator({type})]");
        var result = Run(Compilation(head + StreamReq + StreamHandler + declaration), false);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "ZEN0024" &&
            diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Location.IsInSource);
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void Unregistered_validators_and_other_registered_routes_preserve_stream_emission()
    {
        const string validator = "public sealed class V : IStreamRequestValidator<GetItems> { public void Validate(GetItems r) {} }";
        var plain = Head + StreamReq + StreamHandler;
        var without = Run(Compilation(plain), true).GeneratedTrees.Single().ToString();
        Assert.Equal(without, Run(Compilation(plain + validator), true).GeneratedTrees.Single().ToString());

        const string other = "public readonly record struct OtherItems : IStreamRequest<int>; public sealed class OtherHandler : IStreamRequestHandler<OtherItems,int> { public async IAsyncEnumerable<int> HandleAsync(OtherItems r, CancellationToken c) { await Task.Yield(); yield return 1; } } public sealed class OtherValidator : IStreamRequestValidator<OtherItems> { public void Validate(OtherItems r) {} }";
        var a = Run(Compilation(plain + other), true).GeneratedTrees.Single();
        var b = Run(Compilation(plain.Replace("[GenerateZendiator]", "[GenerateZendiator, StreamRequestValidator(typeof(OtherValidator))]") + other), true).GeneratedTrees.Single();
        static string OriginalRoute(SyntaxTree tree)
        {
            var mediator = tree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>()
                .Single(declaration => declaration.Identifier.ValueText == "Zendiator");
            return string.Join("\n", mediator.Members.Where(member => member switch
            {
                Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax method => method.Identifier.ValueText == "StreamAsync" &&
                    method.ParameterList.Parameters[0].Type!.ToString() == "global::App.GetItems",
                Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax type => type.Identifier.ValueText == "StreamRoute0Enumerable",
                Microsoft.CodeAnalysis.CSharp.Syntax.StructDeclarationSyntax type => type.Identifier.ValueText.StartsWith("StreamRoute0Node", StringComparison.Ordinal),
                _ => false
            }).Select(static member => member.NormalizeWhitespace().ToFullString()));
        }
        Assert.Equal(OriginalRoute(a), OriginalRoute(b));
    }

    [Theory]
    [InlineData("ZEN0001", "public sealed record GetItems(int Count) : IStreamRequest<int>;")]
    [InlineData("ZEN0002", StreamReq + StreamHandler + "public sealed class Other : IStreamRequestHandler<GetItems,int> { public async IAsyncEnumerable<int> HandleAsync(GetItems r, CancellationToken c) { await Task.Yield(); yield return 1; } }")]
    [InlineData("ZEN0012", "public ref struct GetItems : IStreamRequest<int>; public sealed class H : IStreamRequestHandler<GetItems,int> { public async IAsyncEnumerable<int> HandleAsync(GetItems r, CancellationToken c) { await Task.Yield(); yield return 0; } }")]
    public void Stream_invalid_contracts_report_stable_diagnostics(string id, string body)
    {
        var result = Run(Compilation(Head + body), false);
        Assert.Contains(result.Diagnostics, d => d.Id == id);
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void Stream_ref_item_is_diagnosed()
    {
        var body = "public ref struct Item { public int V; } public sealed record GetItems(int Count) : IStreamRequest<Item>; public sealed class H : IStreamRequestHandler<GetItems,Item> { public async IAsyncEnumerable<Item> HandleAsync(GetItems r, CancellationToken c) { await Task.Yield(); yield return default; } }";
        var result = Run(Compilation(Head + body), false);
        Assert.Contains(result.Diagnostics, d => d.Id == "ZEN0012");
    }

    [Fact]
    public void Stream_ambiguous_closed_open_is_diagnosed()
    {
        // Closed Ent<int> handler plus open Ent<T> handler overlap -> ambiguity.
        var ambiguous = "public sealed record Ent<T>(int Count) : IStreamRequest<T>; public sealed class OpenH<T> : IStreamRequestHandler<Ent<T>,T> { public async IAsyncEnumerable<T> HandleAsync(Ent<T> r, CancellationToken c) { await Task.Yield(); yield break; } } public sealed class ClosedIntH : IStreamRequestHandler<Ent<int>,int> { public async IAsyncEnumerable<int> HandleAsync(Ent<int> r, CancellationToken c) { await Task.Yield(); yield return 0; } }";
        var amb = Run(Compilation(Head + ambiguous), false);
        Assert.Contains(amb.Diagnostics, d => d.Id == "ZEN0010");
    }

    [Fact]
    public void Stream_duplicate_handler_order_is_globally_unique()
    {
        // Stream behavior order collides with regular behavior order -> ZEN0004 (global uniqueness, consistent with sync).
        var body = "public readonly record struct Ping : IRequest<int>; public sealed class H : IRequestHandler<Ping,int> { public ValueTask<int> HandleAsync(Ping r, CancellationToken c) => new(1); } " + StreamReq + StreamHandler + """
            public sealed class A : IPipelineBehavior<Ping,int> { public ValueTask<int> HandleAsync<N>(Ping r, N n, CancellationToken c) where N : struct, IRequestContinuation<Ping,int> => n.InvokeAsync(r, c); }
            public sealed class B : IStreamPipelineBehavior<GetItems,int> { public IAsyncEnumerable<int> HandleAsync<N>(GetItems r, N n, CancellationToken c) where N : struct, IStreamContinuation<GetItems,int> => n.InvokeAsync(r, c); }
            """;
        var withAttr = Head.Replace("[GenerateZendiator]", "[GenerateZendiator, PipelineBehavior(typeof(A), Order = 0), PipelineBehavior(typeof(B), Order = 0)]") + body;
        var result = Run(Compilation(withAttr), false);
        Assert.Contains(result.Diagnostics, d => d.Id == "ZEN0004");
    }
}
