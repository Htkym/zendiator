using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    [Fact]
    public async Task Stream_short_sync_pipelines_connect_in_declared_order_and_resolve_only_reached_hooks()
    {
        var cases = new[]
        {
            (Name: "filter", Stages: "F1", Values: "1,3"),
            (Name: "map", Stages: "M", Values: "11,12,13,14"),
            (Name: "filter-map", Stages: "F1,M", Values: "11,13"),
            (Name: "map-filter", Stages: "M,F2", Values: "11,12"),
            (Name: "filter-filter", Stages: "F1,F2", Values: "1,3"),
            (Name: "map-map", Stages: "M,M2", Values: "22,24,26,28"),
            (Name: "mixed-filter-map", Stages: "F1,M", Values: "11,13")
        };
        foreach (var test in cases)
        {
            var mixed = test.Name.StartsWith("mixed-", StringComparison.Ordinal);
            var stages = test.Stages.Split(',');
            var configuration = "c.Handlers<Request>(DispatchFamily.Stream,typeof(H)); c.Pipeline<Request>(DispatchFamily.Stream," +
                string.Join(",", stages.Select(s => "Stage.Hook<" + s + ">()")) + ");";
            if (mixed) configuration += "c.Handlers<Ping>(DispatchFamily.SendSync,typeof(PingHandler)); c.Pipeline<Ping>(DispatchFamily.SendSync);";
            var source = PublicStreamSource("short-sync").Replace(PublicStreamConfiguration, configuration)
                .Replace("public bool HasToken,Overridden;", "public int InputCount; public bool RejectAll; public bool HasToken,Overridden;")
                .Replace("new(++item<=4)", "new(++item<=p.InputCount)")
                .Replace("return value%2==1;", "return !p.RejectAll && value%2==1;")
                .Replace("public static class Bridge {", """
                    public sealed class M2 : IStreamItemMap<Request,int>,IDisposable {
                        private readonly Probe p;public M2(Probe probe){p=probe;p.Resolve("M2");}
                        public int Map(Request r,int value,CancellationToken c){p.Observe("M2",r,c);return value*2;}
                        public void Dispose()=>p.End("M2");
                    }
                    public static class Bridge {
                    """ + ShortStreamConsumer
                    .Replace("__VALUES__", test.Values)
                    .Replace("__TYPES__", string.Join(",", stages.Select(s => "typeof(" + s + ")")))
                    .Replace("__NAMES__", string.Join(",", stages.Select(s => "\"" + s + "\"")))
                    .Replace("__FILTER_FIRST__", stages[0] == "F1" ? "true" : "false")
                    .Replace("__SEND__", mixed ? "Probe.Check(mediator.Send(new Ping(41))==42,\"mixed Send\");" : ""));
            if (mixed) source = source.Replace("[ZendiatorComposition(\"reads\")]", """
                public sealed record Ping(int Value):ISyncRequest<int>;
                [ZendiatorHandler] public sealed class PingHandler:ISyncRequestHandler<Ping,int> {
                    public int Handle(Ping r,CancellationToken t)=>r.Value+1;
                }
                [ZendiatorComposition("reads")]
                """);
            var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "ShortStream_" + Guid.NewGuid().ToString("N")),
                out var output, out var diagnostics);
            var generated = driver.GetRunResult().Results.Single().GeneratedSources;
            using var bytes = new MemoryStream();
            var emitted = output.Emit(bytes);
            var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_STREAM_PUBLIC_EVIDENCE_DIR");
            var folder = evidence == null ? null : Path.Combine(evidence, "short-sync", test.Name);
            if (folder != null)
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "input.cs"), source);
                File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
                foreach (var file in generated) File.WriteAllText(Path.Combine(folder, file.HintName), file.SourceText.ToString());
                File.WriteAllText(Path.Combine(folder, "compile.json"), JsonSerializer.Serialize(new
                {
                    test.Name,
                    emitted.Success,
                    diagnostics = diagnostics.Select(d => new { d.Id, severity = d.Severity.ToString(), message = d.GetMessage() }),
                    compiler = emitted.Diagnostics.Select(d => new { d.Id, severity = d.Severity.ToString(), message = d.GetMessage() }),
                    sources = generated.Select(f => f.HintName)
                }));
            }
            Assert.Empty(diagnostics);
            Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
            Assert.Empty(emitted.Diagnostics.Where(d => d.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error));
            Assert.Single(generated, f => f.HintName.StartsWith("Zendiator.StreamCanonical.", StringComparison.Ordinal));
            Assert.DoesNotContain(generated, f => f.HintName.StartsWith("Zendiator.StreamPrototype.", StringComparison.Ordinal));
            var facade = mixed ? CanonicalRouteSource(generated, "App.Request") : Assert.Single(generated,
                f => f.HintName.StartsWith("Zendiator.Canonical.Stream.Public.", StringComparison.Ordinal)).SourceText.ToString();
            Assert.Contains("__ZendiatorStreamCanonical_", facade);
            bytes.Position = 0;
            var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
            var result = await (Task<string>)assembly.GetType("App.Bridge")!.GetMethod("Run")!.Invoke(null, null)!;
            if (folder != null) File.WriteAllText(Path.Combine(folder, "runtime.json"), result);
            using var json = JsonDocument.Parse(result);
            Assert.Equal(4, json.RootElement.GetArrayLength());
        }
    }

    [Fact]
    public void Stream_short_sync_acceptance_keeps_other_stage_counts_and_async_hooks_blocked()
    {
        foreach (var shape in new[] { "three-sync", "one-async" })
        {
            var stages = shape == "three-sync" ? "Stage.Hook<F1>(),Stage.Hook<F2>(),Stage.Hook<M>()" : "Stage.Hook<M>()";
            var source = PublicStreamSource("short-rejected").Replace(PublicStreamConfiguration,
                "c.Handlers<Request>(DispatchFamily.Stream,typeof(H)); c.Pipeline<Request>(DispatchFamily.Stream," + stages + ");");
            if (shape == "one-async") source = source.Replace("IStreamItemMap<Request,int>", "IAsyncStreamItemMap<Request,int>")
                .Replace("public int Map(Request r,int value,CancellationToken c)", "public ValueTask<int> MapAsync(Request r,int value,CancellationToken c)")
                .Replace("return value+10;", "return new(value+10);");
            var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "ShortRejected_" + Guid.NewGuid().ToString("N")),
                out var output, out var diagnostics);
            var generated = driver.GetRunResult().Results.Single().GeneratedSources;
            Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error));
            Assert.Contains(diagnostics, d => d.Id == "ZEN0037" && d.Severity == DiagnosticSeverity.Error && d.Location.IsInSource);
            Assert.DoesNotContain(generated, f => f.HintName.StartsWith("Zendiator.Canonical.Stream.Public.", StringComparison.Ordinal));
            Assert.DoesNotContain(generated, IsCanonicalComposition);
            var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_STREAM_PUBLIC_EVIDENCE_DIR");
            if (evidence == null) continue;
            var folder = Path.Combine(evidence, "short-sync-rejected", shape);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "input.cs"), source);
            foreach (var file in generated) File.WriteAllText(Path.Combine(folder, file.HintName), file.SourceText.ToString());
            File.WriteAllText(Path.Combine(folder, "diagnostics.json"), JsonSerializer.Serialize(diagnostics.Select(d => new
            { d.Id, severity = d.Severity.ToString(), message = d.GetMessage(), inSource = d.Location.IsInSource })));
        }
    }

    private const string ShortStreamConsumer = """
        public static async Task<string> Run() {
            var results=new List<object>();
            for(int mode=0;mode<4;mode++) {
                var p=new Probe{InputCount=mode==1?0:4,RejectAll=mode==3};
                var services=new ServiceCollection();services.AddSingleton(p);Register(services);
                var selected=new[]{typeof(H),__TYPES__};
                foreach(var type in selected)
                    Probe.Check(services.Single(d=>d.ServiceType==type).Lifetime==ServiceLifetime.Transient,"borrowed registration");
                await using var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});
                var scope=provider.CreateAsyncScope();
                var mediator=scope.ServiceProvider.GetRequiredService<global::App.Generated.Reads.IZendiator>();
                using var api=new CancellationTokenSource();using var enumeration=new CancellationTokenSource();
                var request=new Request();p.Request=request;p.Api=api.Token;p.Enumeration=enumeration.Token;
                var stream=mediator.StreamAsync(request,p.Api);var cursor=stream.GetAsyncEnumerator(p.Enumeration);
                Probe.Check(p.Resolved.Count==0&&p.Starts==0,"lazy dispatch and cursor");
                var values=new List<int>();
                try {while(await cursor.MoveNextAsync()){values.Add(cursor.Current);if(mode==2)break;}}
                finally {await cursor.DisposeAsync();}
                var expected=new[]{__VALUES__};
                if(mode==1 || mode==3 && __FILTER_FIRST__)expected=Array.Empty<int>();
                if(mode==2)expected=expected.Take(1).ToArray();
                Probe.Check(values.SequenceEqual(expected),"declared stage order");
                var hooks=mode==1?Array.Empty<string>():mode==3&&__FILTER_FIRST__?new[]{"F1"}:new[]{__NAMES__};
                Probe.Check(p.Resolved.SequenceEqual(new[]{"H"}.Concat(hooks)),"only reached hooks resolve");
                Probe.Check(p.Starts==1&&p.Disposals==1&&p.Ended.Count==0,"source cleanup and borrowed DI ownership");
                Probe.Check(p.Moves==(mode==2?1:p.InputCount+1)&&p.Reads==(mode==2?1:p.InputCount),"no prefetch or repeated Current");
                __SEND__
                api.Cancel();enumeration.Cancel();Probe.Check(!p.Observed.IsCancellationRequested,"linked token detached");
                await scope.DisposeAsync();
                Probe.Check(p.Ended.Order().SequenceEqual(new[]{"H"}.Concat(hooks).Order()),"DI disposes reached services");
                results.Add(new{mode,values,p.Starts,p.Moves,p.Reads,p.Disposals,p.Resolved,p.Ended,p.Events});
            }
            return System.Text.Json.JsonSerializer.Serialize(results);
        }
        """;
}
