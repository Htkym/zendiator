using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private static string ComposedMissingMarkerSource()
    {
        using var reader = new StreamReader(typeof(GeneratorTests).Assembly.GetManifestResourceStream("Zendiator.ComposedApp.Source")!);
        return reader.ReadToEnd().Replace("\r\n", "\n").Replace("[ZendiatorHandler]\npublic sealed class RefreshInventoryHandler", "public sealed class RefreshInventoryHandler");
    }

    private static string ComposedMarkerFolder(string profile)
    {
        var root = Environment.GetEnvironmentVariable("ZENDIATOR_N04_EVIDENCE");
        var folder = root == null ? "" : Path.Combine(root, profile);
        if (folder.Length != 0)
            Directory.CreateDirectory(folder);
        return folder;
    }

    [Fact]
    public async Task MissingHandlerAttributeFix_ComposedApp_changes_only_the_closed_marker_and_regenerates_all_routes()
    {
        var source = ComposedMissingMarkerSource();
        var (workspace, document) = HandlerFixWorkspace(source);
        using (workspace)
        {
            var diagnostic = Assert.Single(await HandlerFixDiagnostics(document));
            Assert.Equal("typeof(RefreshInventoryHandler)", source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length));
            Assert.Equal("RefreshInventoryHandler", source.Substring(Assert.Single(diagnostic.AdditionalLocations).SourceSpan.Start, diagnostic.AdditionalLocations[0].SourceSpan.Length));
            Assert.Empty(await HandlerFixActions(document, diagnostic)); // No installed generator means no new Type-registration fix.
            Assert.True(workspace.TryApplyChanges(document.Project.AddAnalyzerReference(new HandlerFixGeneratorReference()).Solution));
            document = workspace.CurrentSolution.GetDocument(document.Id)!;
            var action = Assert.Single(await HandlerFixActions(document, diagnostic));
            Assert.Equal("Add ZendiatorHandler attribute", action.Title);
            document = await ApplyHandlerFix(workspace, document, action);
            var fixedSource = (await document.GetTextAsync()).ToString();
            var expected = source.Replace("public sealed class RefreshInventoryHandler", "[global::Zendiator.ZendiatorHandlerAttribute]\npublic sealed class RefreshInventoryHandler");
            Assert.Equal(expected, fixedSource);
            Assert.Empty(await HandlerFixActions(document, diagnostic));
            var output = (await document.Project.GetCompilationAsync())!;
            Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
            AssertComposedProfile(output, "ComposedApp.Generated.App", ("Send", 1), ("SendAsync", 3), ("SendAll", 2), ("SendAllAsync", 1), ("StreamAsync", 1));
            var generated = await document.Project.GetSourceGeneratedDocumentsAsync();
            Assert.Equal(7, generated.Count(d => d.HintName.StartsWith("Zendiator.Route.", StringComparison.Ordinal)));
            var folder = ComposedMarkerFolder("accepted");
            if (folder.Length != 0)
            {
                File.WriteAllText(Path.Combine(folder, "missing-Program.cs"), source);
                File.WriteAllText(Path.Combine(folder, "fixed-Program.cs"), fixedSource);
                foreach (var file in generated)
                    File.WriteAllText(Path.Combine(folder, file.HintName), (await file.GetTextAsync()).ToString());
                using var binary = new MemoryStream();
                var emit = output.Emit(binary);
                Assert.True(emit.Success, string.Join("\n", emit.Diagnostics));
                File.WriteAllBytes(Path.Combine(folder, "consumer.dll"), binary.ToArray());
                File.WriteAllText(Path.Combine(folder, "result.json"), JsonSerializer.Serialize(new
                {
                    markerOnly = true,
                    routes = 7,
                    actions = 1,
                    staleActions = 0,
                    generatorInstalledRequired = true,
                    runtimeExecuted = false
                }));
            }
        }
    }

    [Fact]
    public async Task MissingHandlerAttributeFix_ComposedApp_refuses_invalid_unadopted_ambiguous_and_changed_other_documents()
    {
        var source = ComposedMissingMarkerSource();
        var profiles = new[]
        {
            ("invalid", source.Replace("Stage.Wrapper(typeof(CountCache<>))", "Stage.Wrapper(typeof(InvalidCache<>))") + "\npublic sealed class InvalidCache<T> {}"),
            ("unadopted", source.Replace("typeof(CachedCollector<>), typeof(WarehouseCollector<>)", "typeof(CachedCollector<>), typeof(WarehouseCollector<>), typeof(ThirdCollector<>)") + "\n[Zendiator.ZendiatorHandler]public sealed class ThirdCollector<T>:Zendiator.ISyncRequestHandler<CollectCounts<T>,int> where T:class { public int Handle(CollectCounts<T> r,System.Threading.CancellationToken t)=>0; }"),
            ("ambiguous", source.Replace("typeof(RefreshInventoryHandler));", "typeof(RefreshInventoryHandler), typeof(OtherRefreshHandler));") + "\n[Zendiator.ZendiatorHandler]public sealed class OtherRefreshHandler:Zendiator.IRequestHandler<RefreshInventory> { public System.Threading.Tasks.ValueTask HandleAsync(RefreshInventory r,System.Threading.CancellationToken t)=>default; }")
        };
        foreach (var (profile, invalid) in profiles)
        {
            Assert.NotEqual(source, invalid);
            var (workspace, document) = HandlerFixWorkspace(invalid);
            using (workspace)
            {
                var diagnostic = Assert.Single(await HandlerFixDiagnostics(document));
                Assert.True(workspace.TryApplyChanges(document.Project.AddAnalyzerReference(new HandlerFixGeneratorReference()).Solution));
                document = workspace.CurrentSolution.GetDocument(document.Id)!;
                Assert.Empty(await HandlerFixActions(document, diagnostic));
                Assert.Equal(invalid, (await document.GetTextAsync()).ToString());
                var folder = ComposedMarkerFolder(profile);
                if (folder.Length != 0)
                    File.WriteAllText(Path.Combine(folder, "input.cs"), invalid);
            }
        }
        var tree = CSharpSyntaxTree.ParseText(source, SyncParseOptions);
        var root = tree.GetRoot();
        var wrapper = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == "CountCache");
        var withoutWrapper = root.RemoveNode(wrapper, SyntaxRemoveOptions.KeepExteriorTrivia)!.ToFullString();
        var (changedWorkspace, changedDocument) = HandlerFixWorkspace(withoutWrapper);
        using (changedWorkspace)
        {
            changedWorkspace.AddDocument(changedDocument.Project.Id, "Wrapper.cs", SourceText.From("using Zendiator; namespace ComposedApp {" + wrapper.ToFullString() + "}"));
            changedDocument = changedWorkspace.CurrentSolution.GetDocument(changedDocument.Id)!;
            var diagnostic = Assert.Single(await HandlerFixDiagnostics(changedDocument));
            Assert.True(changedWorkspace.TryApplyChanges(changedDocument.Project.AddAnalyzerReference(new HandlerFixGeneratorReference()).Solution));
            changedDocument = changedWorkspace.CurrentSolution.GetDocument(changedDocument.Id)!;
            var action = Assert.Single(await HandlerFixActions(changedDocument, diagnostic));
            var other = changedDocument.Project.Documents.Single(d => d.Name == "Wrapper.cs");
            Assert.True(changedWorkspace.TryApplyChanges(other.WithText(SourceText.From("namespace ComposedApp { public sealed class CountCache<T> {} }")).Project.Solution));
            changedDocument = changedWorkspace.CurrentSolution.GetDocument(changedDocument.Id)!;
            changedDocument = await ApplyHandlerFix(changedWorkspace, changedDocument, action);
            Assert.Equal(withoutWrapper, (await changedDocument.GetTextAsync()).ToString());
            Assert.Empty(await HandlerFixActions(changedDocument, diagnostic));
            var folder = ComposedMarkerFolder("other-document-changed");
            if (folder.Length != 0)
                File.WriteAllText(Path.Combine(folder, "unchanged-Program.cs"), withoutWrapper);
        }
    }
}
