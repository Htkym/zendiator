using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Zendiator.SourceGenerator;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private sealed record StreamConnectionFixture(GeneratorDriver Driver, ImmutableArray<Diagnostic> Diagnostics);
    private static CanonicalStreamConnectionCatalog StreamConnections(GeneratorDriver driver) => Assert.IsType<CanonicalStreamConnectionCatalog>(
        Assert.Single(driver.GetRunResult().Results.Single().TrackedSteps["CanonicalStreamConnectionModel"].SelectMany(s => s.Outputs)).Value);
    private static StreamConnectionFixture RunStreamConnection(string source, string label, bool suppress = false)
    {
        // Keep assembly identity fixed when comparing suppression-only compilation options.
        var input = SyncCompilation(source, "StreamConnection");
        if (suppress) input = input.WithOptions(input.Options.WithSpecificDiagnosticOptions(
            input.Options.SpecificDiagnosticOptions.SetItem("ZEN0037", ReportDiagnostic.Suppress)));
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(input, out var output, out var diagnostics);
        var files = driver.GetRunResult().Results.Single().GeneratedSources;
        using var bytes = new MemoryStream(); var emitted = output.Emit(bytes);
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(evidence))
        {
            var folder = Path.Combine(evidence, "stream-connection", label); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "input.cs"), source);
            foreach (var file in files) File.WriteAllText(Path.Combine(folder, file.HintName), file.SourceText.ToString());
            File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
            File.WriteAllText(Path.Combine(folder, "connection.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                label, suppress, candidateOnly = true, productionEnabled = false, emitted.Success,
                connections = StreamConnections(driver).Routes.Select(r => new { r.RouteKey, r.Anchor, kind = r.Kind.ToString(), r.Reason, hint = r.Candidate?.HintName }),
                generatorDiagnostics = diagnostics.Select(d => new { d.Id, d.IsSuppressed, message = d.GetMessage(),
                    start = d.Location.SourceSpan.Start, length = d.Location.SourceSpan.Length,
                    source = d.Location.SourceTree?.GetText().ToString(d.Location.SourceSpan), additional = d.AdditionalLocations.Select(l => l.SourceSpan.ToString()) }),
                compilerDiagnostics = emitted.Diagnostics.Select(d => new { d.Id, severity = d.Severity.ToString(), message = d.GetMessage() }),
                files = files.Select(f => f.HintName)
            }));
        }
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        Assert.DoesNotContain(files, f => f.HintName.StartsWith("Zendiator.Route.", StringComparison.Ordinal) ||
            f.HintName.StartsWith("Zendiator.Composition.", StringComparison.Ordinal) || f.HintName.Contains("Interceptors", StringComparison.Ordinal));
        return new(driver, diagnostics);
    }

    [Fact]
    public void Stream_connection_supported_shape_uses_lowered_rendered_candidate_without_public_registration()
    {
        var run = RunStreamConnection(StreamGraphFixture(), "selected");
        var connection = Assert.Single(StreamConnections(run.Driver).Routes);
        Assert.Equal(CanonicalStreamConnectionKind.Candidate, connection.Kind); Assert.NotNull(connection.Candidate);
        var backend = Assert.IsType<CanonicalRouteBackend>(Assert.Single(run.Driver.GetRunResult().Results.Single()
            .TrackedSteps["CanonicalRouteBackendEntry"].SelectMany(s => s.Outputs)).Value);
        Assert.Equal(connection.Candidate, backend.StreamCandidate);
        var rendered = Assert.IsType<CanonicalRouteText>(Assert.Single(run.Driver.GetRunResult().Results.Single()
            .TrackedSteps["CanonicalRouteSourceEntry"].SelectMany(s => s.Outputs)).Value);
        Assert.Empty(rendered.Source); // No public Stream route is accepted.
        var generated = Assert.Single(run.Driver.GetRunResult().Results.Single().GeneratedSources,
            s => s.HintName == connection.Candidate!.HintName);
        Assert.Equal(rendered.StreamCandidateFile!.Source, generated.SourceText.ToString());
        Assert.Contains("RunSegment", generated.SourceText.ToString());
        Assert.DoesNotContain("CanonicalStreamItemReference", generated.SourceText.ToString());
        Assert.Contains("connected to route lowering and rendering", Assert.Single(run.Diagnostics).GetMessage());
        Assert.Equal("ZEN0037", Assert.Single(run.Diagnostics).Id);
    }

    [Theory]
    [InlineData("order")] [InlineData("wrapper")] [InlineData("item")]
    public void Stream_connection_unsupported_valid_shape_stays_diagnosed_without_selecting_reference_or_legacy(string shape)
    {
        var source = shape == "order" ? StreamGraphFixture("Stage.Hook<DoubleItem>(),Stage.Hook<Positive>(),Stage.Hook<Small>()") :
            shape == "wrapper" ? StreamGraphFixture("Stage.Hook<Positive>(),Stage.Wrapper<StreamOpaque>(),Stage.Hook<Small>()") :
            StreamGraphFixture().Replace("IStreamRequest<int>", "IStreamRequest<long>")
                .Replace("<Items,int>", "<Items,long>").Replace("IAsyncEnumerable<int>", "IAsyncEnumerable<long>")
                .Replace("int item", "long item").Replace("public int Map", "public long Map");
        var run = RunStreamConnection(source, "deferred-" + shape);
        var connection = Assert.Single(StreamConnections(run.Driver).Routes);
        Assert.Equal(CanonicalStreamConnectionKind.CanonicalPending, connection.Kind); Assert.Null(connection.Candidate);
        Assert.DoesNotContain(run.Driver.GetRunResult().Results.Single().GeneratedSources, f => f.HintName.StartsWith("Zendiator.StreamPrototype.", StringComparison.Ordinal));
        var diagnostic = Assert.Single(run.Diagnostics); Assert.Equal("ZEN0037", diagnostic.Id);
        Assert.Contains("Same-contract canonical runtime generation", diagnostic.GetMessage());
        Assert.Contains("No legacy/reference fallback is selected", diagnostic.GetMessage());
        Assert.StartsWith("c.Pipeline<Items>", diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan));
    }

    [Fact]
    public void Stream_connection_wrong_item_contract_retains_stage_source_diagnostic_and_no_candidate()
    {
        var source = StreamGraphFixture().Replace("Stage.Hook<Positive>()", "Stage.Hook<Wrong>()") + """
            public sealed class Wrong : IStreamItemFilter<Items,long> {
                public bool Accept(Items r,long item,CancellationToken c)=>true;
            }
            """;
        var run = RunStreamConnection(source, "wrong-contract");
        Assert.Equal(CanonicalStreamConnectionKind.InvalidContract, Assert.Single(StreamConnections(run.Driver).Routes).Kind);
        Assert.DoesNotContain(run.Driver.GetRunResult().Results.Single().GeneratedSources, f => f.HintName.StartsWith("Zendiator.StreamPrototype.", StringComparison.Ordinal));
        var diagnostic = Assert.Single(run.Diagnostics); Assert.Equal("ZEN0026", diagnostic.Id);
        Assert.Equal("Stage.Hook<Wrong>()", diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan));
    }

    [Fact]
    public void Stream_connection_unbound_constrained_stage_retains_rejection_without_open_generic_adoption()
    {
        var source = StreamGraphFixture().Replace("Stage.Hook<Positive>()", "Stage.Hook(typeof(Constrained<>))") + """
            public sealed class Constrained<T> : IStreamItemFilter<Items,int> where T : class {
                public bool Accept(Items r,int item,CancellationToken c)=>true;
            }
            """;
        var run = RunStreamConnection(source, "unbound-constraints");
        Assert.Equal(CanonicalStreamConnectionKind.InvalidContract, Assert.Single(StreamConnections(run.Driver).Routes).Kind);
        Assert.DoesNotContain(run.Driver.GetRunResult().Results.Single().GeneratedSources, f => f.HintName.StartsWith("Zendiator.StreamPrototype.", StringComparison.Ordinal));
        var diagnostic = Assert.Single(run.Diagnostics); Assert.Equal("ZEN0035", diagnostic.Id);
        Assert.Equal("Stage.Hook(typeof(Constrained<>))", diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan));
    }

    [Fact]
    public void Stream_connection_duplicate_composition_blocks_both_outputs_with_current_and_original_locations()
    {
        var source = StreamGraphFixture() + """
            public static class DuplicateStartup {
                public static void Configure(Microsoft.Extensions.DependencyInjection.IServiceCollection services) =>
                    global::Zendiator.DependencyInjection.ZendiatorServiceCollectionExtensions.AddZendiator<Reads>(services, c => {
                        c.Handlers<Items>(DispatchFamily.Stream,typeof(ItemsHandler));
                        c.Pipeline<Items>(Stage.Hook<Positive>(),Stage.Hook<DoubleItem>(),Stage.Hook<Small>());
                    });
            }
            """;
        var run = RunStreamConnection(source, "duplicate-composition");
        Assert.Equal(2, StreamConnections(run.Driver).Routes.Count);
        Assert.All(StreamConnections(run.Driver).Routes, c => { Assert.Equal(CanonicalStreamConnectionKind.ConflictingComposition, c.Kind); Assert.Null(c.Candidate); });
        Assert.DoesNotContain(run.Driver.GetRunResult().Results.Single().GeneratedSources, f => f.HintName.StartsWith("Zendiator.StreamPrototype.", StringComparison.Ordinal));
        var duplicate = Assert.Single(run.Diagnostics, d => d.Id == "ZEN0035");
        Assert.Single(duplicate.AdditionalLocations); Assert.Same(duplicate.Location.SourceTree, duplicate.AdditionalLocations[0].SourceTree);
        Assert.True(duplicate.Location.SourceSpan.Start > duplicate.AdditionalLocations[0].SourceSpan.Start);
        Assert.Contains("authoritative composition", Assert.Single(run.Diagnostics, d => d.Id == "ZEN0037").GetMessage());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Stream_connection_diagnostic_suppression_keeps_same_candidate_or_deferred_semantics(bool deferred)
    {
        var source = deferred ? StreamGraphFixture("Stage.Hook<DoubleItem>(),Stage.Hook<Positive>(),Stage.Hook<Small>()") : StreamGraphFixture();
        var normal = RunStreamConnection(source, deferred ? "suppression-deferred-before" : "suppression-selected-before");
        var suppressed = RunStreamConnection(source, deferred ? "suppression-deferred-after" : "suppression-selected-after", suppress: true);
        Assert.Equal("ZEN0037", Assert.Single(normal.Diagnostics).Id);
        Assert.DoesNotContain(suppressed.Diagnostics, d => d.Id == "ZEN0037" && !d.IsSuppressed);
        Assert.Equal(StreamConnections(normal.Driver), StreamConnections(suppressed.Driver));
        var before = normal.Driver.GetRunResult().Results.Single().GeneratedSources.OrderBy(f => f.HintName).Select(f => (f.HintName, f.SourceText.ToString())).ToArray();
        var after = suppressed.Driver.GetRunResult().Results.Single().GeneratedSources.OrderBy(f => f.HintName).Select(f => (f.HintName, f.SourceText.ToString())).ToArray();
        Assert.Equal(before, after);
        Assert.Equal(deferred ? CanonicalStreamConnectionKind.CanonicalPending : CanonicalStreamConnectionKind.Candidate,
            Assert.Single(StreamConnections(suppressed.Driver).Routes).Kind);
    }

    [Fact]
    public void Stream_connection_one_deferred_sibling_does_not_hide_supported_route_or_enable_either_publicly()
    {
        var source = StreamGraphFixture().Replace("c.Handlers<Items>(DispatchFamily.Stream,typeof(ItemsHandler));",
            "c.Handlers<Items>(DispatchFamily.Stream,typeof(ItemsHandler));c.Handlers<Others>(DispatchFamily.Stream,typeof(OtherHandler));") + """
            public sealed record Others : IStreamRequest<int>;
            [ZendiatorHandler] public sealed class OtherHandler : IStreamRequestHandler<Others,int> {
                public System.Collections.Generic.IAsyncEnumerable<int> HandleAsync(Others r,CancellationToken c)=>throw new NotImplementedException();
            }
            """;
        var run = RunStreamConnection(source, "sibling"); var connections = StreamConnections(run.Driver).Routes;
        Assert.Equal(2, connections.Count);
        var candidate = Assert.Single(connections, c => c.Kind == CanonicalStreamConnectionKind.Candidate);
        Assert.Single(connections, c => c.Kind == CanonicalStreamConnectionKind.CanonicalPending);
        Assert.Single(run.Driver.GetRunResult().Results.Single().GeneratedSources, f => f.HintName == candidate.Candidate!.HintName);
        Assert.Equal(2, run.Diagnostics.Length); Assert.All(run.Diagnostics, d => Assert.Equal("ZEN0037", d.Id));
    }
}
