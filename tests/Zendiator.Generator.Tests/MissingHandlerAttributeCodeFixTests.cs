using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;
using Xunit;
using Zendiator.CodeFixes;
using Zendiator.SourceGenerator;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private const string MissingMarkerReason = "MissingHandlerAttribute";
    private static string GettingStartedWithoutMarker(string name = "AddNumbersHandler")
    {
        using var reader = new StreamReader(typeof(GeneratorTests).Assembly.GetManifestResourceStream("Zendiator.GettingStarted.Source")!);
        return reader.ReadToEnd().Replace("\r\n", "\n").Replace("[ZendiatorHandler]\npublic sealed class " + name,
            "public sealed class " + name);
    }

    private static (AdhocWorkspace Workspace, Document Document) HandlerFixWorkspace(string source)
    {
        var host = MefHostServices.Create(MefHostServices.DefaultAssemblies.Add(Assembly.Load("Microsoft.CodeAnalysis.CSharp.Workspaces")));
        var workspace = new AdhocWorkspace(host);
        var project = workspace.AddProject("HandlerFix", LanguageNames.CSharp)
            .WithMetadataReferences(SyncCompilation("", "References").References)
            .WithParseOptions(SyncParseOptions)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        Assert.True(workspace.TryApplyChanges(project.Solution));
        workspace.AddDocument(project.Id, "GlobalUsings.cs", SourceText.From("global using System; global using System.Collections.Generic; global using System.Threading; global using System.Threading.Tasks;"));
        var id = DocumentId.CreateNewId(project.Id);
        var solution = workspace.CurrentSolution.AddDocument(id, "Program.cs", SourceText.From(source), filePath: "D:/HandlerFix/Program.cs");
        Assert.True(workspace.TryApplyChanges(solution));
        return (workspace, workspace.CurrentSolution.GetDocument(id)!);
    }

    private static async Task<ImmutableArray<Diagnostic>> HandlerFixDiagnostics(Document document)
    {
        var compilation = (await document.Project.GetCompilationAsync())!;
        SyncDriver().RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);
        return diagnostics.Where(d => d.Id == "ZEN0035" && d.Properties.TryGetValue("Zendiator.Reason", out var reason) && reason == MissingMarkerReason).ToImmutableArray();
    }

    private static async Task<List<CodeAction>> HandlerFixActions(Document document, Diagnostic diagnostic)
    {
        var actions = new List<CodeAction>();
        await new MissingHandlerAttributeCodeFixProvider().RegisterCodeFixesAsync(
            new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), CancellationToken.None));
        return actions;
    }

    private static async Task<Document> ApplyHandlerFix(AdhocWorkspace workspace, Document document, CodeAction action)
    {
        var operation = Assert.Single((await action.GetOperationsAsync(CancellationToken.None)).OfType<ApplyChangesOperation>());
        Assert.True(workspace.TryApplyChanges(operation.ChangedSolution));
        return workspace.CurrentSolution.GetDocument(document.Id)!;
    }

    [Fact]
    public async Task MissingHandlerAttributeFix_UsesProperties_PreservesTrivia_AndBuildsU01()
    {
        using var exports = new System.Composition.Hosting.ContainerConfiguration().WithAssembly(typeof(MissingHandlerAttributeCodeFixProvider).Assembly).CreateContainer();
        Assert.IsType<MissingHandlerAttributeCodeFixProvider>(Assert.Single(exports.GetExports<CodeFixProvider>()));
        var source = GettingStartedWithoutMarker().Replace("public sealed class AddNumbersHandler", "// keep Handler documentation\n#nullable enable\n[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]\npublic sealed class AddNumbersHandler");
        var (workspace, document) = HandlerFixWorkspace(source);
        using (workspace)
        {
            var diagnostic = Assert.Single(await HandlerFixDiagnostics(document));
            Assert.Equal("AddNumbersHandler", (await document.GetTextAsync()).ToString(diagnostic.AdditionalLocations.Single().SourceSpan));
            // Localized or changed wording must not control applicability.
            var renamedMessage = Diagnostic.Create(new DiagnosticDescriptor("ZEN0035", "test", "unrelated localized text", "test", DiagnosticSeverity.Error, true),
                diagnostic.Location, diagnostic.AdditionalLocations, diagnostic.Properties);
            var action = Assert.Single(await HandlerFixActions(document, renamedMessage));
            document = await ApplyHandlerFix(workspace, document, action);
            var fixedSource = (await document.GetTextAsync()).ToString();
            Assert.Equal(source.Replace("[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]", "[global::Zendiator.ZendiatorHandlerAttribute]\n[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]"), fixedSource);
            Assert.Empty(await HandlerFixDiagnostics(document));
            Assert.Empty(await HandlerFixActions(document, diagnostic));
            SyncDriver().RunGeneratorsAndUpdateCompilation((await document.Project.GetCompilationAsync())!, out var output, out var diagnostics);
            Assert.Empty(diagnostics);
            Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
            var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_N04_EVIDENCE");
            if (evidence != null)
            {
                Directory.CreateDirectory(evidence);
                await File.WriteAllTextAsync(Path.Combine(evidence, "fixed-Program.cs"), fixedSource);
                await File.WriteAllTextAsync(Path.Combine(evidence, "missing-Program.cs"), source);
            }
        }
    }

    [Fact]
    public async Task MissingHandlerAttributeFix_RejectsStale_Ambiguous_Incomplete_AndUnrelatedDiagnostics()
    {
        var source = GettingStartedWithoutMarker();
        var (workspace, document) = HandlerFixWorkspace(source);
        using (workspace)
        {
            var diagnostic = Assert.Single(await HandlerFixDiagnostics(document));
            var action = Assert.Single(await HandlerFixActions(document, diagnostic));
            Assert.True(workspace.TryApplyChanges(document.WithText(SourceText.From(source + "\n// newer edit\n")).Project.Solution));
            document = await ApplyHandlerFix(workspace, document, action);
            Assert.Equal(source + "\n// newer edit\n", (await document.GetTextAsync()).ToString());
            Assert.Empty(await HandlerFixActions(document, diagnostic));
        }
        foreach (var invalid in new[]
        {
            source.Replace("public int Handle(AddNumbers request", "public long Handle(AddNumbers request"),
            source.Replace("public sealed class AddNumbersHandler", "[ZendiatorHandler]\npublic sealed class AddNumbersHandler"),
            source.Replace("public sealed class AddNumbersHandler", "[Other.ZendiatorHandler]\npublic sealed class AddNumbersHandler") + "\nnamespace Other { public sealed class ZendiatorHandlerAttribute : System.Attribute {} }\n",
            source.Replace("public sealed class AddNumbersHandler", "public sealed class AddNumbersHandler<"),
            source.Replace("configuration.Handlers<AddNumbers>", "configuration.UseLocalHandlers();\n            configuration.Handlers<AddNumbers>")
        })
        {
            var (invalidWorkspace, invalidDocument) = HandlerFixWorkspace(invalid);
            using (invalidWorkspace) Assert.Empty(await HandlerFixDiagnostics(invalidDocument));
        }
        var (otherWorkspace, otherDocument) = HandlerFixWorkspace(source);
        using (otherWorkspace)
        {
            var diagnostic = Assert.Single(await HandlerFixDiagnostics(otherDocument));
            var unsupported = Diagnostic.Create(diagnostic.Descriptor, diagnostic.Location, diagnostic.AdditionalLocations, null, diagnostic.GetMessage());
            Assert.Empty(await HandlerFixActions(otherDocument, unsupported));
            var linkedId = DocumentId.CreateNewId(otherDocument.Project.Id);
            Assert.True(otherWorkspace.TryApplyChanges(otherWorkspace.CurrentSolution.AddDocument(linkedId, "Linked.cs", SourceText.From(""), filePath: otherDocument.FilePath)));
            otherDocument = otherWorkspace.CurrentSolution.GetDocument(otherDocument.Id)!;
            Assert.Empty(await HandlerFixActions(otherDocument, diagnostic));
        }
    }

    [Fact]
    public async Task MissingHandlerAttributeFix_DocumentFixAll_DeduplicatesTypes_AndExcludesCrossDocumentPartials()
    {
        var source = GettingStartedWithoutMarker().Replace("[ZendiatorHandler]\npublic sealed class GreetHandler", "public sealed class GreetHandler")
            .Replace("public sealed class AddNumbersHandler", "public sealed partial class AddNumbersHandler")
            + "\npublic sealed partial class AddNumbersHandler {}\n";
        source = source.Replace("[ZendiatorComposition(\"gettingStarted\")]", """
            public static class SecondRegistration
            {
                public static void Add(IServiceCollection services) => services.AddZendiator<SecondApp>(configuration =>
                {
                    configuration.Handlers<AddNumbers>(DispatchFamily.SendSync, typeof(AddNumbersHandler));
                    configuration.Pipeline<AddNumbers>(DispatchFamily.SendSync);
                });
            }
            [ZendiatorComposition("second")] public sealed class SecondApp;
            [ZendiatorComposition("gettingStarted")]
            """);
        var (workspace, document) = HandlerFixWorkspace(source);
        using (workspace)
        {
            var diagnostics = await HandlerFixDiagnostics(document);
            Assert.Equal(3, diagnostics.Length);
            var provider = new MissingHandlerAttributeCodeFixProvider();
            var fixAll = provider.GetFixAllProvider();
            Assert.Equal(new[] { FixAllScope.Document }, fixAll.GetSupportedFixAllScopes());
            var context = new FixAllContext(document, provider, FixAllScope.Document, "Zendiator.AddHandlerAttribute", provider.FixableDiagnosticIds,
                new HandlerFixDiagnosticProvider(diagnostics), CancellationToken.None);
            var action = await fixAll.GetFixAsync(context);
            Assert.NotNull(action);
            document = await ApplyHandlerFix(workspace, document, action);
            var text = (await document.GetTextAsync()).ToString();
            Assert.Equal(2, text.Split("[global::Zendiator.ZendiatorHandlerAttribute]").Length - 1);
            Assert.Empty(await HandlerFixDiagnostics(document));
            var projectContext = new FixAllContext(document, provider, FixAllScope.Project, "Zendiator.AddHandlerAttribute", provider.FixableDiagnosticIds,
                new HandlerFixDiagnosticProvider(diagnostics), CancellationToken.None);
            Assert.Null(await fixAll.GetFixAsync(projectContext));
        }
        var (partialWorkspace, partialDocument) = HandlerFixWorkspace(GettingStartedWithoutMarker().Replace("public sealed class AddNumbersHandler", "public sealed partial class AddNumbersHandler"));
        using (partialWorkspace)
        {
            partialWorkspace.AddDocument(partialDocument.Project.Id, "OtherPartial.cs", SourceText.From("namespace GettingStarted; public sealed partial class AddNumbersHandler {}"));
            partialDocument = partialWorkspace.CurrentSolution.GetDocument(partialDocument.Id)!;
            Assert.Empty(await HandlerFixDiagnostics(partialDocument));
        }
    }

    private sealed class HandlerFixDiagnosticProvider(ImmutableArray<Diagnostic> diagnostics) : FixAllContext.DiagnosticProvider
    {
        public override Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken cancellationToken) => Task.FromResult<IEnumerable<Diagnostic>>(diagnostics);
        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken cancellationToken) => Task.FromResult(Enumerable.Empty<Diagnostic>());
        public override Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken cancellationToken) => Task.FromResult<IEnumerable<Diagnostic>>(diagnostics);
    }

    [Fact]
    public async Task MissingHandlerAttributeFix_RevalidatesWithGeneratedRegistrarsInTheWorkspace()
    {
        var source = GettingStartedWithoutMarker() + """

            [ZendiatorComposition("other")] public sealed class OtherApp;
            public static class AdditionalRegistration
            {
                public static void Add(IServiceCollection services) => services.AddZendiator<OtherApp>(configuration =>
                {
                    configuration.Handlers<Greet>(DispatchFamily.SendAsync, typeof(GreetHandler));
                    configuration.Pipeline<Greet>(DispatchFamily.SendAsync);
                });
            }
            """;
        var (workspace, document) = HandlerFixWorkspace(source);
        using (workspace)
        {
            var diagnostic = Assert.Single(await HandlerFixDiagnostics(document));
            Assert.True(workspace.TryApplyChanges(document.Project.AddAnalyzerReference(new HandlerFixGeneratorReference()).Solution));
            document = workspace.CurrentSolution.GetDocument(document.Id)!;
            Assert.Same(diagnostic.Location.SourceTree, await document.GetSyntaxTreeAsync());
            var generated = await document.Project.GetSourceGeneratedDocumentsAsync();
            var generatedText = new List<string>();
            foreach (var item in generated) generatedText.Add((await item.GetTextAsync()).ToString());
            Assert.Contains(generatedText, text => text.Contains("Snapshot("));
            var action = Assert.Single(await HandlerFixActions(document, diagnostic));
            document = await ApplyHandlerFix(workspace, document, action);
            var output = (await document.Project.GetCompilationAsync())!;
            Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        }
    }

    private sealed class HandlerFixGeneratorReference : AnalyzerReference
    {
        private readonly ImmutableArray<ISourceGenerator> _generators = ImmutableArray.Create(new ZendiatorGenerator().AsSourceGenerator());
        public override string FullPath => typeof(ZendiatorGenerator).Assembly.Location;
        public override object Id => typeof(HandlerFixGeneratorReference);
        public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzers(string language) => ImmutableArray<DiagnosticAnalyzer>.Empty;
        public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzersForAllLanguages() => ImmutableArray<DiagnosticAnalyzer>.Empty;
        public override ImmutableArray<ISourceGenerator> GetGenerators(string language) => _generators;
    }

    [Fact]
    public async Task MissingHandlerAttributeFix_DoesNotModifyAnExplicitMetadataHandler()
    {
        var producer = SyncCompilation("""
            using System.Threading; using Zendiator;
            namespace External;
            public sealed record AddNumbers(int Left, int Right) : ISyncRequest<int>;
            public sealed class AddNumbersHandler : ISyncRequestHandler<AddNumbers, int>
            {
                public int Handle(AddNumbers request, CancellationToken token) => request.Left + request.Right;
            }
            """, "ExternalHandlers");
        using var bytes = new MemoryStream();
        var emit = producer.Emit(bytes);
        Assert.True(emit.Success, string.Join("\n", emit.Diagnostics));
        var source = GettingStartedWithoutMarker();
        var root = CSharpSyntaxTree.ParseText(source, SyncParseOptions).GetRoot();
        var declarations = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.BaseTypeDeclarationSyntax>()
            .Where(d => d.Identifier.ValueText is "AddNumbers" or "AddNumbersHandler");
        source = "using AddNumbers = External.AddNumbers; using AddNumbersHandler = External.AddNumbersHandler;\n" +
            root.RemoveNodes(declarations, SyntaxRemoveOptions.KeepExteriorTrivia)!.ToFullString();
        var (workspace, document) = HandlerFixWorkspace(source);
        using (workspace)
        {
            Assert.True(workspace.TryApplyChanges(document.Project.AddMetadataReference(MetadataReference.CreateFromImage(bytes.ToArray())).Solution));
            document = workspace.CurrentSolution.GetDocument(document.Id)!;
            Assert.Empty(await HandlerFixDiagnostics(document));
            var compilation = (await document.Project.GetCompilationAsync())!;
            SyncDriver().RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
            Assert.Empty(diagnostics);
            Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        }
    }
}
