using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private const string MixedStreamAsyncConfiguration = """
        c.Handlers<Items>(DispatchFamily.Stream,typeof(ItemsHandler));
        c.Handlers<Query>(DispatchFamily.SendAsync,typeof(QueryHandler));
        c.Pipeline<Query>(DispatchFamily.SendAsync);
        c.Pipeline<Items>(DispatchFamily.Stream,Stage.Hook<Positive>(),Stage.Hook<DoubleItem>(),Stage.Hook<Small>());
        """;

    private static string MixedStreamAsyncSource(bool runtime) => MixedStreamSendSource(false)
        .Replace(MixedStreamSendConfiguration, MixedStreamAsyncConfiguration)
        .Replace("namespace Mixed{", "namespace MixedAsync{")
        .Replace("ISyncRequest<int>", "IRequest<int>")
        .Replace("ISyncRequestHandler<Query,int>", "IRequestHandler<Query,int>")
        .Replace("public int SourceStarts,SourceEnds,Sends;", "public readonly global::Zendiator.Generator.Tests.AsyncOnceGate<int> Gate=new(11);public int SourceStarts,SourceEnds,Sends;")
        .Replace("public int Handle(Query r,CancellationToken t){p.Sends++;return r.Value+1;}", "public ValueTask<int> HandleAsync(Query r,CancellationToken t){p.Sends++;return p.Gate.Task;}")
        .Replace("public int Handle(Query r,CancellationToken t)=>r.Value+1;", "public ValueTask<int> HandleAsync(Query r,CancellationToken t)=>new(r.Value+1);")
        + (runtime ? """
        namespace MixedAsync{
        public static partial class Bridge{
            public static async Task<string> Run(){
                var p=new Probe();IServiceCollection services=new ServiceCollection();
                services.AddSingleton(p);services.AddSingleton<QueryHandler>();Register(services);
                foreach(var type in new[]{typeof(ItemsHandler),typeof(Positive),typeof(DoubleItem),typeof(Small),typeof(QueryHandler),
                    typeof(global::MixedAsync.Generated.Reads.Zendiator),typeof(global::MixedAsync.Generated.Reads.IZendiator)})
                    Probe.Check(services.Count(d=>d.ServiceType==type)==1,"unique descriptor "+type);
                Probe.Check(services.Single(d=>d.ServiceType==typeof(QueryHandler)).Lifetime==ServiceLifetime.Singleton,"TryAdd lifetime");
                var provider=services.BuildServiceProvider();
                int result;var values=new List<int>();string[] scopeEnds;
                using(var scope=provider.CreateScope()){
                    var m=scope.ServiceProvider.GetRequiredService<global::MixedAsync.Generated.Reads.IZendiator>();
                    Probe.Check(ReferenceEquals(m,scope.ServiceProvider.GetRequiredService<global::MixedAsync.Generated.Reads.Zendiator>()),"mediator identity");
                    var sequence=m.StreamAsync(new Items());
                    Probe.Check(p.SourceStarts==0&&p.Resolved.Count==0,"Stream dispatch lazy");
                    var pending=m.SendAsync(new Query(10));
                    Probe.Check(!pending.IsCompleted&&p.Gate.Consumptions==0&&p.Sends==1&&p.SourceStarts==0&&p.Resolved.SequenceEqual(new[]{"Q"}),"pending Send isolation");
                    p.Gate.Complete();result=await pending.ConfigureAwait(false);
                    Probe.Check(result==11&&p.Gate.Consumptions==1&&p.SourceStarts==0,"single Send consumption");
                    await foreach(var item in sequence)values.Add(item);
                    Probe.Check(values.SequenceEqual(new[]{2,4})&&p.Sends==1&&p.Gate.Consumptions==1&&p.SourceStarts==1&&p.SourceEnds==1,"Stream isolation/value");
                    Probe.Check(p.Ended.Count==0,"cursor borrowed cleanup");
                }
                scopeEnds=p.Ended.ToArray();
                Probe.Check(scopeEnds.Order().SequenceEqual(new[]{"F1","F2","H","M"})&&!scopeEnds.Contains("Q"),"scope ownership");
                provider.Dispose();
                Probe.Check(p.Ended.Count==5&&p.Ended.Count(n=>n=="Q")==1,"provider ownership");
                return System.Text.Json.JsonSerializer.Serialize(new{result,values,p.SourceStarts,p.SourceEnds,p.Sends,p.Gate.Consumptions,p.Resolved,scopeEnds,p.Ended});
            }
        }}
        """ : "");

    [Fact]
    public async Task Mixed_zero_stage_Stream_and_pending_SendAsync_connect_public_canonical_routes()
    {
        var source = MixedStreamAsyncSource(true)
            .Replace("c.Pipeline<Items>(DispatchFamily.Stream,Stage.Hook<Positive>(),Stage.Hook<DoubleItem>(),Stage.Hook<Small>());", "c.Pipeline<Items>(DispatchFamily.Stream);")
            .Replace("typeof(ItemsHandler),typeof(Positive),typeof(DoubleItem),typeof(Small),typeof(QueryHandler)", "typeof(ItemsHandler),typeof(QueryHandler)")
            .Replace("values.SequenceEqual(new[]{2,4})", "values.SequenceEqual(new[]{1,2,3})")
            .Replace("scopeEnds.Order().SequenceEqual(new[]{\"F1\",\"F2\",\"H\",\"M\"})", "scopeEnds.SequenceEqual(new[]{\"H\"})")
            .Replace("p.Ended.Count==5", "p.Ended.Count==2");
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "MixedZeroStreamAsync_" + Guid.NewGuid().ToString("N")),
            out var output, out var diagnostics);
        var generated = driver.GetRunResult().Results.Single().GeneratedSources;
        using var bytes = new MemoryStream();
        var emitted = output.Emit(bytes);
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        var folder = evidence == null ? null : Path.Combine(evidence, "mixed-zero-stream-async");
        if (folder != null)
        {
            Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, "input.cs"), source);
            File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
            foreach (var file in generated) File.WriteAllText(Path.Combine(folder, file.HintName), file.SourceText.ToString());
            File.WriteAllText(Path.Combine(folder, "compile.json"), JsonSerializer.Serialize(new
            {
                emitted.Success,
                diagnostics = diagnostics.Select(d => new { d.Id, severity = d.Severity.ToString(), message = d.GetMessage(), span = d.Location.SourceSpan.ToString() }),
                compiler = emitted.Diagnostics.Select(d => new { d.Id, severity = d.Severity.ToString(), message = d.GetMessage() }),
                sources = generated.Select(f => f.HintName)
            }));
        }
        Assert.Empty(diagnostics);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        Assert.Empty(emitted.Diagnostics.Where(d => d.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error));
        Assert.Single(generated, f => f.HintName.StartsWith("Zendiator.StreamCanonical.", StringComparison.Ordinal));
        Assert.DoesNotContain(generated, f => f.HintName.StartsWith("Zendiator.StreamPrototype.", StringComparison.Ordinal));
        Assert.Contains("__ZendiatorStreamCanonical_", CanonicalRouteSource(generated, "MixedAsync.Items"));
        Assert.Contains(" SendAsync(global::MixedAsync.Query request", CanonicalRouteSource(generated, "MixedAsync.Query"));
        bytes.Position = 0; var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var runtime = await (Task<string>)assembly.GetType("MixedAsync.Bridge")!.GetMethod("Run")!.Invoke(null, null)!;
        if (folder != null) File.WriteAllText(Path.Combine(folder, "runtime.json"), runtime);
        using var observed = JsonDocument.Parse(runtime);
        Assert.Equal(11, observed.RootElement.GetProperty("result").GetInt32());
        Assert.Equal(new[] { 1, 2, 3 }, observed.RootElement.GetProperty("values").EnumerateArray().Select(x => x.GetInt32()));
        Assert.Equal(1, observed.RootElement.GetProperty("Consumptions").GetInt32());
    }

    [Fact]
    public async Task Mixed_closed_Stream_and_pending_SendAsync_connect_whole_composition_and_keep_handler_identity_local()
    {
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        string Root(string name)
        {
            var path = Path.Combine(evidence!, "mixed-stream-async", name);
            Directory.CreateDirectory(path);
            return path;
        }
        void Save(string name, string source, GeneratorDriver current, Compilation output, IEnumerable<Diagnostic> diagnostics, byte[]? dll = null)
        {
            if (string.IsNullOrEmpty(evidence)) return;
            var root = Root(name);
            File.WriteAllText(Path.Combine(root, "input.cs"), source);
            foreach (var file in current.GetRunResult().Results.Single().GeneratedSources)
                File.WriteAllText(Path.Combine(root, file.HintName), file.SourceText.ToString());
            if (dll != null) File.WriteAllBytes(Path.Combine(root, "generated.dll"), dll);
            File.WriteAllText(Path.Combine(root, "compile.json"), JsonSerializer.Serialize(new
            {
                sources = current.GetRunResult().Results.Single().GeneratedSources.Select(s => s.HintName),
                diagnostics = diagnostics.Select(d => new { d.Id, message = d.GetMessage(), span = d.Location.SourceSpan.ToString() }),
                compiler = output.GetDiagnostics().Select(d => new { d.Id, severity = d.Severity.ToString(), message = d.GetMessage() })
            }));
        }

        var source = MixedStreamAsyncSource(true);
        var assemblyName = "MixedStreamAsync_" + Guid.NewGuid().ToString("N");
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, assemblyName), out var output, out var diagnostics);
        Save("accepted", source, driver, output, diagnostics);
        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        var baseline = driver.GetRunResult().Results.Single().GeneratedSources;
        Assert.Equal(2, baseline.Count(s => s.HintName.StartsWith("Zendiator.Route.", StringComparison.Ordinal)));
        Assert.Single(baseline, s => s.HintName.StartsWith("Zendiator.Composition.", StringComparison.Ordinal));
        Assert.Single(baseline, s => s.HintName.EndsWith(".Interceptors.g.cs", StringComparison.Ordinal));
        var prototype = Assert.Single(baseline, s => s.HintName.StartsWith("Zendiator.StreamPrototype.", StringComparison.Ordinal));
        var streamText = CanonicalRouteSource(baseline, "MixedAsync.Items");
        var sendText = CanonicalRouteSource(baseline, "MixedAsync.Query");
        var shared = CanonicalCompositionSource(baseline);
        Assert.Contains("StreamAsync", streamText);
        Assert.Contains(".Create(_provider, request, cancellationToken)", streamText);
        Assert.Contains("ValueTask<", sendText);
        Assert.Contains(" SendAsync(global::MixedAsync.Query request", sendText);
        Assert.Contains("ConfigureAwait(false)", sendText);
        Assert.True(shared.IndexOf("expected.Handlers<global::MixedAsync.Items>", StringComparison.Ordinal) >= 0);
        Assert.True(shared.IndexOf("expected.Handlers<global::MixedAsync.Items>", StringComparison.Ordinal) < shared.IndexOf("expected.Handlers<global::MixedAsync.Query>", StringComparison.Ordinal));
        Assert.True(shared.IndexOf("expected.Pipeline<global::MixedAsync.Query>", StringComparison.Ordinal) >= 0);
        Assert.True(shared.IndexOf("expected.Pipeline<global::MixedAsync.Query>", StringComparison.Ordinal) < shared.IndexOf("expected.Pipeline<global::MixedAsync.Items>", StringComparison.Ordinal));
        using var bytes = new MemoryStream();
        var emitted = output.Emit(bytes);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        Save("accepted", source, driver, output, diagnostics, bytes.ToArray());

        foreach (var shape in new[] { "unsupported", "duplicate" })
        {
            var rejected = MixedStreamAsyncSource(false);
            rejected = shape == "unsupported" ? rejected.Replace("Stage.Hook<Positive>(),Stage.Hook<DoubleItem>(),Stage.Hook<Small>()", "Stage.Hook<Positive>(),Stage.Hook<Small>(),Stage.Hook<DoubleItem>()") :
                rejected.Replace("public static partial class Bridge{", "public static partial class Bridge{public static IServiceCollection Duplicate(IServiceCollection s)=>s.AddZendiator<Reads>(c=>{" + MixedStreamAsyncConfiguration + "});");
            var rejectedDriver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(rejected, "MixedAsyncRejected_" + shape), out var rejectedOutput, out var rejectedDiagnostics);
            Save(shape, rejected, rejectedDriver, rejectedOutput, rejectedDiagnostics);
            Assert.Contains(rejectedDiagnostics, d => d.Id == (shape == "duplicate" ? "ZEN0035" : "ZEN0037") && d.Location.SourceTree != null &&
                rejected.Substring(d.Location.SourceSpan.Start, d.Location.SourceSpan.Length).Contains(shape == "duplicate" ? "AddZendiator" : "c.", StringComparison.Ordinal));
            var files = rejectedDriver.GetRunResult().Results.Single().GeneratedSources;
            AssertNoCanonicalOutput(files);
            Assert.DoesNotContain(files, s => s.HintName.EndsWith(".Interceptors.g.cs", StringComparison.Ordinal));
            Assert.Empty(rejectedOutput.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error && !d.Id.StartsWith("ZEN", StringComparison.Ordinal)));
        }

        var changed = source.Replace("DispatchFamily.SendAsync,typeof(QueryHandler)", "DispatchFamily.SendAsync,typeof(ReplacementQueryHandler)");
        driver = driver.RunGeneratorsAndUpdateCompilation(SyncCompilation(changed, assemblyName), out output, out diagnostics);
        Save("handler-identity", changed, driver, output, diagnostics);
        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        var updated = driver.GetRunResult().Results.Single().GeneratedSources;
        Assert.Equal(streamText, CanonicalRouteSource(updated, "MixedAsync.Items"));
        Assert.NotEqual(sendText, CanonicalRouteSource(updated, "MixedAsync.Query"));
        Assert.Contains("ReplacementQueryHandler", CanonicalRouteSource(updated, "MixedAsync.Query"));
        Assert.Contains("ReplacementQueryHandler", CanonicalCompositionSource(updated));
        Assert.Equal(prototype.SourceText.ToString(), Assert.Single(updated, s => s.HintName == prototype.HintName).SourceText.ToString());
        var reasons = driver.GetRunResult().Results.Single().TrackedSteps["CanonicalMixedStreamFacadeEntry"].SelectMany(s => s.Outputs).Select(s => s.Reason).ToArray();
        Assert.NotEmpty(reasons);
        Assert.All(reasons, reason => Assert.True(reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged));
        if (!string.IsNullOrEmpty(evidence)) File.WriteAllText(Path.Combine(Root("handler-identity"), "tracked-reasons.json"), JsonSerializer.Serialize(reasons.Select(r => r.ToString())));

        bytes.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var runtime = await (Task<string>)assembly.GetType("MixedAsync.Bridge")!.GetMethod("Run")!.Invoke(null, null)!;
        using var observed = JsonDocument.Parse(runtime);
        Assert.Equal(11, observed.RootElement.GetProperty("result").GetInt32());
        Assert.Equal(new[] { 2, 4 }, observed.RootElement.GetProperty("values").EnumerateArray().Select(x => x.GetInt32()));
        Assert.Equal(1, observed.RootElement.GetProperty("Consumptions").GetInt32());
        if (!string.IsNullOrEmpty(evidence)) File.WriteAllText(Path.Combine(Root("accepted"), "runtime.json"), runtime);
    }
}
