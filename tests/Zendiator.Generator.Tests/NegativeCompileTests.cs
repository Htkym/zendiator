using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Zendiator.SourceGenerator;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed class NegativeCompileTests
{
    private const string Head = "using Zendiator; using System; using System.Threading; using System.Threading.Tasks; namespace App; [GenerateZendiator] public sealed partial class Zendiator; ";
    private const string SyncHead = Head
        + "public readonly ref struct ParseRequest : ISyncRequest<int> { public ParseRequest(ReadOnlySpan<byte> data) => Data = data; public ReadOnlySpan<byte> Data { get; } } "
        + "public sealed class ParseHandler : ISyncRequestHandler<ParseRequest, int> { public int Handle(scoped ParseRequest r, CancellationToken c) => r.Data.Length; } ";
    private static readonly MetadataReference[] References = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
        .Select(p => MetadataReference.CreateFromFile(p)).ToArray();
    private static CSharpCompilation Compilation(string source) => CSharpCompilation.Create("Test",
        [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview))], References,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

    private static List<string> EmitErrorIds(string source)
    {
        var input = Compilation(source);
        var driver = CSharpGeneratorDriver.Create([new ZendiatorGenerator().AsSourceGenerator()],
            parseOptions: new CSharpParseOptions(LanguageVersion.Preview), driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, true));
        driver.RunGeneratorsAndUpdateCompilation(input, out var output, out var generatorDiagnostics);
        Assert.Empty(generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        using var stream = new MemoryStream();
        var emitted = output.Emit(stream);
        return emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Id).Distinct().ToList();
    }

    [Theory]
    [InlineData("public static class Misuse { public static object Box(ParseRequest r) => r; }", "CS0029")]
    [InlineData("public static class Misuse { public static ISyncRequest<int> Box(ParseRequest r) => r; }", "CS0029")]
    [InlineData("public sealed class Holder { public ParseRequest? Value; }", "CS9244")]
    [InlineData("public sealed class Holder { public ParseRequest Value; }", "CS8345")]
    [InlineData("public static class Misuse { public static Func<int> Capture(ParseRequest r) => () => r.Data.Length; }", "CS9108")]
    [InlineData("public static class Misuse { public static async Task<int> Run(ParseRequest r) { await Task.Yield(); return r.Data.Length; } }", "CS4012")]
    public void Ref_request_misuse_fails_with_the_expected_compiler_error(string misuse, string errorId)
    {
        var ids = EmitErrorIds(SyncHead + misuse);
        Assert.Contains(errorId, ids);
    }

    [Fact]
    public void Ref_request_has_no_async_overload_to_misuse()
    {
        var input = Compilation(SyncHead);
        var driver = CSharpGeneratorDriver.Create([new ZendiatorGenerator().AsSourceGenerator()],
            parseOptions: new CSharpParseOptions(LanguageVersion.Preview), driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, true));
        var text = driver.RunGenerators(input).GetRunResult().GeneratedTrees.Single().ToString();
        Assert.DoesNotContain("SendAsync(global::App.ParseRequest", text);
        Assert.DoesNotContain("Task.Run", text);
        Assert.DoesNotContain("object request", text);
    }
}
