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

    [Fact]
    public void Stream_typed_source_compiles_and_is_lazy()
    {
        var result = Run(Compilation(Head + StreamReq + StreamHandler), true);
        var source = result.GeneratedTrees.Single().ToString();
        Assert.Contains("StreamAsync(global::App.GetItems request", source);
        Assert.Contains("IAsyncEnumerable<int>", source);
        Assert.Contains("IStreamContinuation<", source);
        Assert.DoesNotContain("object request", source);
        // Lazy: StreamAsync returns enumerable without resolving.
        Assert.Contains("StreamRoute0Enumerable", source);
        Assert.Contains("MergeStreamTokens", source);
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
    public void Stream_generic_open_handler_works()
    {
        var body = "public sealed record StreamEntities<T>(int Count) : IStreamRequest<T> where T : new(); public sealed class StreamEntitiesHandler<T> : IStreamRequestHandler<StreamEntities<T>,T> where T : new() { public async IAsyncEnumerable<T> HandleAsync(StreamEntities<T> r, CancellationToken c) { await Task.Yield(); yield return new T(); } }";
        var result = Run(Compilation(Head + body), true);
        var source = result.GeneratedTrees.Single().ToString();
        Assert.Contains("StreamAsync<T>", source);
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
    public void Stream_behavior_pipeline_compiles()
    {
        var body = StreamReq + StreamHandler + """
            public sealed class Pass : IStreamPipelineBehavior<GetItems,int> {
                public async IAsyncEnumerable<int> HandleAsync<N>(GetItems r, N n, CancellationToken c) where N : struct, IStreamContinuation<GetItems,int> { await foreach (var i in n.InvokeAsync(r, c)) yield return i; }
            }
            """;
        var withAttr = Head.Replace("[GenerateZendiator]", "[GenerateZendiator, PipelineBehavior(typeof(Pass), Order = 0)]") + body;
        var result = Run(Compilation(withAttr), true);
        var source = result.GeneratedTrees.Single().ToString();
        Assert.Contains("StreamRoute0Node0", source);
        Assert.Contains("StreamRoute0Node1", source);
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
