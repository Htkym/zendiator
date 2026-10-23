using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Zendiator.Tooling;

namespace Zendiator.CodeFixes;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MissingHandlerAttributeCodeFixProvider)), Shared]
public sealed class MissingHandlerAttributeCodeFixProvider : CodeFixProvider
{
    private const string Title = "Add ZendiatorHandler attribute";
    private const string EquivalenceKey = "Zendiator.AddHandlerAttribute";
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create("ZEN0035");
    public override FixAllProvider GetFixAllProvider() => new DocumentFixAll();

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        foreach (var diagnostic in context.Diagnostics)
        {
            if (await ResolveAsync(context.Document, diagnostic, context.CancellationToken).ConfigureAwait(false) == null) continue;
            var workspace = context.Document.Project.Solution.Workspace;
            var id = context.Document.Id;
            context.RegisterCodeFix(CodeAction.Create(Title, async ct =>
            {
                var current = workspace.CurrentSolution;
                var document = current.GetDocument(id);
                return document == null ? current : await ApplyAsync(document, ImmutableArray.Create(diagnostic), false, ct).ConfigureAwait(false);
            }, EquivalenceKey), diagnostic);
        }
    }

    private static async Task<(Document Document, ClassDeclarationSyntax Declaration)?> ResolveAsync(Document document, Diagnostic diagnostic, CancellationToken ct)
    {
        if (diagnostic.Id != "ZEN0035" || !diagnostic.Properties.TryGetValue(MissingHandlerAttribute.ReasonKey, out var reason) ||
            reason != MissingHandlerAttribute.Reason || diagnostic.AdditionalLocations.Count != 1 ||
            !document.Project.Solution.Workspace.CanApplyChange(ApplyChangesKind.ChangeDocument) || IsLinked(document)) return null;
        var tree = await document.GetSyntaxTreeAsync(ct).ConfigureAwait(false);
        if (tree == null || diagnostic.Location.SourceTree != tree) return null;
        var compilation = await document.Project.GetCompilationAsync(ct).ConfigureAwait(false);
        if (compilation == null || MissingHandlerAttribute.Find(compilation, tree, diagnostic.Location.SourceSpan, ct) is not { } target ||
            target.Properties.Any(p => !diagnostic.Properties.TryGetValue(p.Key, out var value) || value != p.Value)) return null;
        var location = diagnostic.AdditionalLocations[0];
        if (location.SourceTree != target.Declaration.SyntaxTree || location.SourceSpan != target.Declaration.Identifier.Span) return null;
        var targetDocument = document.Project.Solution.GetDocument(target.Declaration.SyntaxTree);
        if (targetDocument == null || targetDocument.Project.Id != document.Project.Id || IsLinked(targetDocument)) return null;
        if (target.RequiresGenerationCheck && !await RegeneratesAsync(targetDocument, target.Declaration, ct).ConfigureAwait(false)) return null;
        return (targetDocument, target.Declaration);
    }

    private static async Task<bool> RegeneratesAsync(Document document, ClassDeclarationSyntax declaration, CancellationToken ct)
    {
        // Reuse the generator installed in this host; do not duplicate its route eligibility rules.
        var references = document.Project.AnalyzerReferences.Where(reference => reference.GetGenerators(LanguageNames.CSharp).Any(generator =>
            generator.GetGeneratorType().FullName == "Zendiator.SourceGenerator.ZendiatorGenerator" &&
            generator.GetGeneratorType().Assembly.GetName().Name == "Zendiator.SourceGenerator")).ToArray();
        if (references.Length != 1) return false;
        var generators = references[0].GetGenerators(LanguageNames.CSharp);
        if (generators.Length != 1) return false;
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root == null || document.Project.ParseOptions is not CSharpParseOptions parse) return false;
        var project = document.WithSyntaxRoot(root.ReplaceNode(declaration, AddAttribute(declaration))).Project;
        // Removing only this generator reference also removes its old generated trees.
        var input = await project.WithAnalyzerReferences(project.AnalyzerReferences.Where(reference => reference != references[0]))
            .GetCompilationAsync(ct).ConfigureAwait(false);
        if (input == null) return false;
        var options = project.AnalyzerOptions;
        var driver = CSharpGeneratorDriver.Create(generators, options.AdditionalFiles, parse, options.AnalyzerConfigOptionsProvider)
            .RunGeneratorsAndUpdateCompilation(input, out var output, out var diagnostics, ct);
        return !diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) &&
            !output.GetDiagnostics(ct).Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) &&
            driver.GetRunResult().Results.Single().GeneratedSources.Any();
    }

    private static bool IsLinked(Document document) => document.FilePath != null &&
        document.Project.Solution.GetDocumentIdsWithFilePath(document.FilePath).Length != 1;

    private static async Task<Solution> ApplyAsync(Document document, ImmutableArray<Diagnostic> diagnostics, bool sameDocument, CancellationToken ct)
    {
        var solution = document.Project.Solution;
        var targets = new Dictionary<DocumentId, List<ClassDeclarationSyntax>>();
        foreach (var diagnostic in diagnostics)
        {
            var target = await ResolveAsync(document, diagnostic, ct).ConfigureAwait(false);
            if (target == null || sameDocument && target.Value.Document.Id != document.Id) continue;
            if (!targets.TryGetValue(target.Value.Document.Id, out var declarations))
                targets.Add(target.Value.Document.Id, declarations = new List<ClassDeclarationSyntax>());
            if (!declarations.Any(d => d.Span == target.Value.Declaration.Span)) declarations.Add(target.Value.Declaration);
        }
        foreach (var pair in targets)
        {
            var targetDocument = solution.GetDocument(pair.Key)!;
            var root = await targetDocument.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            if (root == null) continue;
            solution = solution.WithDocumentSyntaxRoot(pair.Key, root.ReplaceNodes(pair.Value, (original, _) => AddAttribute(original)));
        }
        return solution;
    }

    private static ClassDeclarationSyntax AddAttribute(ClassDeclarationSyntax declaration)
    {
        var leading = declaration.GetLeadingTrivia();
        var text = declaration.SyntaxTree.GetText();
        var line = text.Lines.GetLineFromPosition(declaration.SpanStart);
        var indent = text.ToString(Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(line.Start, declaration.SpanStart));
        if (indent.Any(c => c != ' ' && c != '\t')) indent = "";
        var eol = text.Lines.Select(l => text.ToString(Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(l.End, l.EndIncludingLineBreak)))
            .FirstOrDefault(s => s.Length != 0) ?? "\n";
        var attribute = SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(
            SyntaxFactory.Attribute(SyntaxFactory.ParseName("global::" + MissingHandlerAttribute.AttributeName))))
            .WithLeadingTrivia(leading).WithTrailingTrivia(SyntaxFactory.EndOfLine(eol), SyntaxFactory.Whitespace(indent));
        return declaration.WithoutLeadingTrivia().WithAttributeLists(declaration.WithoutLeadingTrivia().AttributeLists.Insert(0, attribute));
    }

    private sealed class DocumentFixAll : FixAllProvider
    {
        public override IEnumerable<FixAllScope> GetSupportedFixAllScopes() => new[] { FixAllScope.Document };
        public override async Task<CodeAction?> GetFixAsync(FixAllContext context)
        {
            if (context.Scope != FixAllScope.Document || context.Document == null) return null;
            var diagnostics = await context.GetDocumentDiagnosticsAsync(context.Document).ConfigureAwait(false);
            var currentDocument = context.Document;
            if (!diagnostics.Any()) return null;
            var applicable = new List<Diagnostic>();
            foreach (var diagnostic in diagnostics)
            {
                var target = await ResolveAsync(currentDocument, diagnostic, context.CancellationToken).ConfigureAwait(false);
                if (target?.Document.Id == currentDocument.Id) applicable.Add(diagnostic);
            }
            if (applicable.Count == 0) return null;
            var workspace = currentDocument.Project.Solution.Workspace;
            return CodeAction.Create(Title, async ct =>
            {
                var current = workspace.CurrentSolution;
                var document = current.GetDocument(currentDocument.Id);
                return document == null ? current : await ApplyAsync(document, applicable.ToImmutableArray(), true, ct).ConfigureAwait(false);
            }, EquivalenceKey);
        }
    }
}
