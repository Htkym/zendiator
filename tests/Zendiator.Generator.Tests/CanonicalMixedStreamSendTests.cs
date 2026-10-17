using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private const string MixedStreamSendConfiguration = """
        c.Handlers<Items>(DispatchFamily.Stream,typeof(ItemsHandler));
        c.Pipeline<Items>(DispatchFamily.Stream,Stage.Hook<Positive>(),Stage.Hook<DoubleItem>(),Stage.Hook<Small>());
        c.Handlers<Query>(DispatchFamily.SendSync,typeof(QueryHandler));
        c.Pipeline<Query>(DispatchFamily.SendSync);
        """;
    private static string MixedStreamSendSource(bool runtime) => """
        using System;using System.Collections.Generic;using System.Linq;using System.Threading;using System.Threading.Tasks;
        using Zendiator;using Zendiator.DependencyInjection;using Microsoft.Extensions.DependencyInjection;
        namespace Mixed{
        public sealed record Items:IStreamRequest<int>;
        public sealed record Query(int Value):ISyncRequest<int>;
        public sealed class Probe{
            public int SourceStarts,SourceEnds,Sends;public List<string> Resolved=new();public List<string> Ended=new();
            public void Resolve(string name)=>Resolved.Add(name);
            public void End(string name)=>Ended.Add(name);
            public static void Check(bool ok,string text){if(!ok)throw new InvalidOperationException(text);}
        }
        [ZendiatorHandler]public sealed class ItemsHandler:IStreamRequestHandler<Items,int>,IDisposable{
            private readonly Probe p;public ItemsHandler(Probe p){this.p=p;p.Resolve("H");}
            public IAsyncEnumerable<int> HandleAsync(Items r,CancellationToken t){p.SourceStarts++;return Source(t);}
            private async IAsyncEnumerable<int> Source([System.Runtime.CompilerServices.EnumeratorCancellation]CancellationToken t){
                try{foreach(var item in new[]{1,2,3}){t.ThrowIfCancellationRequested();yield return item;}await Task.CompletedTask;}
                finally{p.SourceEnds++;}
            }
            public void Dispose()=>p.End("H");
        }
        public sealed class Positive:IStreamItemFilter<Items,int>,IDisposable{
            private readonly Probe p;public Positive(Probe p){this.p=p;p.Resolve("F1");}
            public bool Accept(Items r,int item,CancellationToken t)=>item>0;
            public void Dispose()=>p.End("F1");
        }
        public sealed class DoubleItem:IStreamItemMap<Items,int>,IDisposable{
            private readonly Probe p;public DoubleItem(Probe p){this.p=p;p.Resolve("M");}
            public int Map(Items r,int item,CancellationToken t)=>item*2;
            public void Dispose()=>p.End("M");
        }
        public sealed class Small:IStreamItemFilter<Items,int>,IDisposable{
            private readonly Probe p;public Small(Probe p){this.p=p;p.Resolve("F2");}
            public bool Accept(Items r,int item,CancellationToken t)=>item<6;
            public void Dispose()=>p.End("F2");
        }
        [ZendiatorHandler]public sealed class QueryHandler:ISyncRequestHandler<Query,int>,IDisposable{
            private readonly Probe p;public QueryHandler(Probe p){this.p=p;p.Resolve("Q");}
            public int Handle(Query r,CancellationToken t){p.Sends++;return r.Value+1;}
            public void Dispose()=>p.End("Q");
        }
        [ZendiatorHandler]public sealed class ReplacementQueryHandler:ISyncRequestHandler<Query,int>{
            public int Handle(Query r,CancellationToken t)=>r.Value+1;
        }
        [ZendiatorComposition("mixed-read")]public sealed class Reads;
        public static partial class Bridge{
            public static IServiceCollection Register(IServiceCollection services)=>services.AddZendiator<Reads>(c=>{__CONFIG__});
        }
        }
        """.Replace("__CONFIG__", MixedStreamSendConfiguration) + (runtime ? """
        namespace Mixed{
        public static partial class Bridge{
            public static async Task<string> Run(){
                var p=new Probe();IServiceCollection services=new ServiceCollection();
                services.AddSingleton(p);services.AddSingleton<QueryHandler>();Register(services);
                foreach(var type in new[]{typeof(ItemsHandler),typeof(Positive),typeof(DoubleItem),typeof(Small),typeof(QueryHandler),
                    typeof(global::Mixed.Generated.Reads.Zendiator),typeof(global::Mixed.Generated.Reads.IZendiator)})
                    Probe.Check(services.Count(d=>d.ServiceType==type)==1,"unique descriptor "+type);
                Probe.Check(services.Single(d=>d.ServiceType==typeof(QueryHandler)).Lifetime==ServiceLifetime.Singleton,"TryAdd lifetime");
                var provider=services.BuildServiceProvider();
                int result;var values=new List<int>();string[] scopeEnds;
                using(var scope=provider.CreateScope()){
                    var m=scope.ServiceProvider.GetRequiredService<global::Mixed.Generated.Reads.IZendiator>();
                    Probe.Check(ReferenceEquals(m,scope.ServiceProvider.GetRequiredService<global::Mixed.Generated.Reads.Zendiator>()),"mediator identity");
                    var sequence=m.StreamAsync(new Items());
                    Probe.Check(p.SourceStarts==0&&p.Resolved.Count==0,"Stream dispatch lazy");
                    result=m.Send(new Query(10));
                    Probe.Check(result==11&&p.Sends==1&&p.SourceStarts==0&&p.Resolved.SequenceEqual(new[]{"Q"}),"Send isolation");
                    await foreach(var item in sequence)values.Add(item);
                    Probe.Check(values.SequenceEqual(new[]{2,4})&&p.Sends==1&&p.SourceStarts==1&&p.SourceEnds==1,"Stream isolation/value");
                    Probe.Check(p.Ended.Count==0,"cursor borrowed cleanup");
                }
                scopeEnds=p.Ended.ToArray();
                Probe.Check(scopeEnds.Order().SequenceEqual(new[]{"F1","F2","H","M"})&&!scopeEnds.Contains("Q"),"scope ownership");
                provider.Dispose();
                Probe.Check(p.Ended.Count==5&&p.Ended.Count(n=>n=="Q")==1,"provider ownership");
                return System.Text.Json.JsonSerializer.Serialize(new{result,values,p.SourceStarts,p.SourceEnds,p.Sends,p.Resolved,scopeEnds,p.Ended});
            }
        }}
        """ : "");

    [Fact]
    public async Task Mixed_closed_Stream_and_SendSync_connect_whole_composition_and_keep_handler_identity_local()
    {
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        string Root(string name)
        {
            var path = Path.Combine(evidence!, "mixed-stream-send", name);
            Directory.CreateDirectory(path);
            return path;
        }
        void Save(string name, string source, GeneratorDriver driver, Compilation output, IEnumerable<Diagnostic> diagnostics, byte[]? dll = null)
        {
            if (string.IsNullOrEmpty(evidence)) return;
            var root = Root(name); File.WriteAllText(Path.Combine(root, "input.cs"), source);
            foreach (var file in driver.GetRunResult().Results.Single().GeneratedSources)
                File.WriteAllText(Path.Combine(root, file.HintName), file.SourceText.ToString());
            if (dll != null) File.WriteAllBytes(Path.Combine(root, "generated.dll"), dll);
            File.WriteAllText(Path.Combine(root, "compile.json"), JsonSerializer.Serialize(new
            {
                sources = driver.GetRunResult().Results.Single().GeneratedSources.Select(s => s.HintName).ToArray(),
                diagnostics = diagnostics.Select(d => new { d.Id, message = d.GetMessage(), span = d.Location.SourceSpan.ToString() }),
                compiler = output.GetDiagnostics().Select(d => new { d.Id, severity = d.Severity.ToString(), message = d.GetMessage() })
            }));
        }
        var source = MixedStreamSendSource(true);
        var reuse = Environment.GetEnvironmentVariable("ZENDIATOR_MIXED_ACCEPTED_REUSE");
        var driver = SyncDriver();
        Compilation output;
        System.Collections.Immutable.ImmutableArray<Diagnostic> diagnostics;
        Dictionary<string, string> baseline;
        string assemblyName;
        if (string.IsNullOrEmpty(reuse))
        {
            assemblyName = "MixedStreamSend_" + Guid.NewGuid().ToString("N");
            driver = driver.RunGeneratorsAndUpdateCompilation(SyncCompilation(source, assemblyName), out output, out diagnostics);
            Save("accepted", source, driver, output, diagnostics);
            baseline = driver.GetRunResult().Results.Single().GeneratedSources.ToDictionary(s => s.HintName, s => s.SourceText.ToString());
        }
        else
        {
            Assert.Equal(source, File.ReadAllText(Path.Combine(reuse, "input.cs")));
            using var prior = JsonDocument.Parse(File.ReadAllText(Path.Combine(reuse, "compile.json")));
            Assert.Empty(prior.RootElement.GetProperty("diagnostics").EnumerateArray());
            baseline = prior.RootElement.GetProperty("sources").EnumerateArray().ToDictionary(s => s.GetString()!, s => File.ReadAllText(Path.Combine(reuse, s.GetString()!)));
            assemblyName = System.Text.RegularExpressions.Regex.Match(baseline["Zendiator.GeneratedCatalog.g.cs"], "MixedStreamSend_[a-f0-9]+").Value;
            Assert.NotEmpty(assemblyName);
            output = SyncCompilation(source, assemblyName).AddSyntaxTrees(baseline.Select(s => Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(s.Value, SyncParseOptions, s.Key)));
            diagnostics = System.Collections.Immutable.ImmutableArray<Diagnostic>.Empty;
        }
        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        Assert.Equal(2, baseline.Keys.Count(s => s.StartsWith("Zendiator.Route.", StringComparison.Ordinal)));
        Assert.Single(baseline.Keys, s => s.StartsWith("Zendiator.Composition.", StringComparison.Ordinal));
        Assert.Single(baseline.Keys, s => s.EndsWith(".Interceptors.g.cs", StringComparison.Ordinal));
        Assert.Single(baseline.Keys, s => s.StartsWith("Zendiator.StreamPrototype.", StringComparison.Ordinal));
        string Route(string request) => Assert.Single(baseline, s => s.Key.StartsWith("Zendiator.Route.", StringComparison.Ordinal) && s.Value.Contains("global::" + request + " request", StringComparison.Ordinal)).Value;
        var streamText = Route("Mixed.Items");
        var sendText = Route("Mixed.Query");
        var shared = Assert.Single(baseline, s => s.Key.StartsWith("Zendiator.Composition.", StringComparison.Ordinal)).Value;
        Assert.Contains("StreamAsync", streamText); Assert.Contains(".Create(_provider, request, cancellationToken)", streamText);
        Assert.Contains("QueryHandler", sendText);
        Assert.True(shared.IndexOf("expected.Handlers<global::Mixed.Items>", StringComparison.Ordinal) <
            shared.IndexOf("expected.Handlers<global::Mixed.Query>", StringComparison.Ordinal));
        Assert.True(shared.IndexOf("expected.Pipeline<global::Mixed.Items>", StringComparison.Ordinal) <
            shared.IndexOf("expected.Pipeline<global::Mixed.Query>", StringComparison.Ordinal));
        using var bytes = new MemoryStream(); var emitted = output.Emit(bytes);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        if (string.IsNullOrEmpty(reuse)) Save("accepted", source, driver, output, diagnostics, bytes.ToArray());
        else if (!string.IsNullOrEmpty(evidence)) File.WriteAllBytes(Path.Combine(Root("accepted-reused"), "generated.dll"), bytes.ToArray());
        foreach (var shape in new[] { "unsupported", "duplicate" })
        {
            var rejected = MixedStreamSendSource(false);
            rejected = shape == "unsupported" ? rejected.Replace("Stage.Hook<Positive>(),Stage.Hook<DoubleItem>(),Stage.Hook<Small>()", "Stage.Hook<Positive>(),Stage.Hook<Small>(),Stage.Hook<DoubleItem>()") :
                rejected.Replace("public static partial class Bridge{", "public static partial class Bridge{public static IServiceCollection Duplicate(IServiceCollection s)=>s.AddZendiator<Reads>(c=>{" + MixedStreamSendConfiguration + "});");
            var rejectedDriver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(rejected, "MixedRejected_" + shape), out output, out diagnostics);
            Save(shape, rejected, rejectedDriver, output, diagnostics);
            var expected = shape == "duplicate" ? "ZEN0035" : "ZEN0037";
            Assert.Contains(diagnostics, d => d.Id == expected && d.Location.SourceTree != null &&
                rejected.Substring(d.Location.SourceSpan.Start, d.Location.SourceSpan.Length).Contains(shape == "duplicate" ? "AddZendiator" : "c.", StringComparison.Ordinal));
            var files = rejectedDriver.GetRunResult().Results.Single().GeneratedSources;
            AssertNoCanonicalOutput(files);
            Assert.DoesNotContain(files, s => s.HintName.EndsWith(".Interceptors.g.cs", StringComparison.Ordinal));
            Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error && !d.Id.StartsWith("ZEN", StringComparison.Ordinal)));
        }

        // Fourth compile keeps the accepted SendSync/int shape; only the selected handler identity changes.
        var changed = source.Replace("typeof(QueryHandler)", "typeof(ReplacementQueryHandler)");
        driver = driver.RunGeneratorsAndUpdateCompilation(SyncCompilation(changed, assemblyName), out output, out diagnostics);
        Save("handler-identity", changed, driver, output, diagnostics);
        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        var updated = driver.GetRunResult().Results.Single().GeneratedSources;
        Assert.Equal(streamText, CanonicalRouteSource(updated, "Mixed.Items"));
        Assert.NotEqual(sendText, CanonicalRouteSource(updated, "Mixed.Query"));
        Assert.Contains("ReplacementQueryHandler", CanonicalRouteSource(updated, "Mixed.Query"));
        Assert.Contains("ReplacementQueryHandler", CanonicalCompositionSource(updated));
        Assert.Equal(Assert.Single(baseline, s => s.Key.StartsWith("Zendiator.StreamPrototype.")).Value,
            updated.Single(s => s.HintName.StartsWith("Zendiator.StreamPrototype.")).SourceText.ToString());
        if (string.IsNullOrEmpty(reuse))
            Assert.All(driver.GetRunResult().Results.Single().TrackedSteps["CanonicalMixedStreamFacadeEntry"].SelectMany(s => s.Outputs),
                x => Assert.True(x.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged));


        bytes.Position = 0; var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var runtime = await (Task<string>)assembly.GetType("Mixed.Bridge")!.GetMethod("Run")!.Invoke(null, null)!;
        using var observed = JsonDocument.Parse(runtime);
        Assert.Equal(11, observed.RootElement.GetProperty("result").GetInt32());
        Assert.Equal(new[] { 2, 4 }, observed.RootElement.GetProperty("values").EnumerateArray().Select(x => x.GetInt32()));
        if (!string.IsNullOrEmpty(evidence)) File.WriteAllText(Path.Combine(Root("accepted"), "runtime.json"), runtime);

    }
}
