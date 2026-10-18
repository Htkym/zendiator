using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stream_wrapper_only_public_connection_preserves_opaque_control_and_borrowed_ownership(bool mixed)
    {
        var configuration = "c.Handlers<Request>(DispatchFamily.Stream,typeof(H)); c.Pipeline<Request>(DispatchFamily.Stream,Stage.Wrapper<W>());";
        if (mixed) configuration += "c.Handlers<Ping>(DispatchFamily.SendSync,typeof(PingHandler)); c.Pipeline<Ping>(DispatchFamily.SendSync);";
        var source = PublicStreamSource("wrapper-only").Replace(PublicStreamConfiguration, configuration)
            .Replace("public bool HasToken,Overridden;", "public int Mode; public bool HasToken,Overridden;")
            .Replace("public static class Bridge {", WrapperOnlyTypes + "public static class Bridge {" + WrapperOnlyConsumer
                .Replace("__SEND__", mixed ? "Probe.Check(mediator.Send(new Ping(41))==42,\"mixed Send\");" : ""));
        if (mixed) source = source.Replace("[ZendiatorComposition(\"reads\")]", """
            public sealed record Ping(int Value):ISyncRequest<int>;
            [ZendiatorHandler] public sealed class PingHandler:ISyncRequestHandler<Ping,int> {
                public int Handle(Ping r,CancellationToken t)=>r.Value+1;
            }
            [ZendiatorComposition("reads")]
            """);
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "WrapperOnly_" + Guid.NewGuid().ToString("N")),
            out var output, out var diagnostics);
        var generated = driver.GetRunResult().Results.Single().GeneratedSources;
        using var bytes = new MemoryStream();
        var emitted = output.Emit(bytes);
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_STREAM_PUBLIC_EVIDENCE_DIR");
        var folder = evidence == null ? null : Path.Combine(evidence, mixed ? "wrapper-only-mixed" : "wrapper-only");
        if (folder != null)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "input.cs"), source);
            File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
            foreach (var file in generated) File.WriteAllText(Path.Combine(folder, file.HintName), file.SourceText.ToString());
            File.WriteAllText(Path.Combine(folder, "compile.json"), JsonSerializer.Serialize(new
            {
                mixed,
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
        var runtime = await (Task<string>)assembly.GetType("App.Bridge")!.GetMethod("Run")!.Invoke(null, null)!;
        if (folder != null) File.WriteAllText(Path.Combine(folder, "runtime.json"), runtime);
        using var json = JsonDocument.Parse(runtime);
        Assert.Equal(7, json.RootElement.GetArrayLength());
    }

    private const string WrapperOnlyTypes = """
        public sealed class W : IStreamWrapper<Request,int>,IDisposable {
            private readonly Probe p;public W(Probe probe){p=probe;p.Resolve("W");}
            public IAsyncEnumerable<int> Wrap<N>(Request r,N upstream,CancellationToken c) where N:IStreamUpstream<Request,int> {
                p.Observe("W.Wrap",r,c);return Body(r,upstream,c);
            }
            private async IAsyncEnumerable<int> Body<N>(Request r,N upstream,
                [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken c) where N:IStreamUpstream<Request,int> {
                p.Events.Add("W.Start");
                try {
                    if(p.Mode==0)yield break;
                    if(p.Mode==1){yield return 99;yield break;}
                    var selected=p.Mode==5?new Request():r;p.Request=selected;
                    for(int round=0;round<(p.Mode==3?2:1);round++)
                        await foreach(var value in upstream.Open(selected,c).WithCancellation(c).ConfigureAwait(false)) {
                            p.Events.Add("W.Item");
                            if(p.Mode==6&&value==2)throw new InvalidOperationException("wrapper");
                            yield return value;
                        }
                }finally{p.Events.Add("W.Finally");}
            }
            public void Dispose()=>p.End("W");
        }
        """;

    private const string WrapperOnlyConsumer = """
        public static async Task<string> Run() {
            var results=new List<object>();
            for(int mode=0;mode<7;mode++) {
                var p=new Probe{Mode=mode};var services=new ServiceCollection();services.AddSingleton(p);Register(services);
                await using var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});
                var scope=provider.CreateAsyncScope();var mediator=scope.ServiceProvider.GetRequiredService<global::App.Generated.Reads.IZendiator>();
                using var api=new CancellationTokenSource();using var enumeration=new CancellationTokenSource();
                var request=new Request();p.Request=request;p.Api=api.Token;p.Enumeration=enumeration.Token;
                var stream=mediator.StreamAsync(request,p.Api);Probe.Check(p.Events.Count==0,"lazy dispatch");
                var values=new List<int>();var tokens=new List<CancellationToken>();var failed=false;
                for(int pass=0;pass<(mode==5?2:1);pass++) {
                    p.Request=request;p.HasToken=false;var before=p.Events.Count;
                    var cursor=stream.GetAsyncEnumerator(p.Enumeration);Probe.Check(p.Events.Count==before,"lazy cursor");
                    try{while(await cursor.MoveNextAsync()){values.Add(cursor.Current);if(mode==4)break;}}
                    catch(InvalidOperationException e) when(mode==6&&e.Message=="wrapper"){failed=true;}
                    finally{await cursor.DisposeAsync();}
                    tokens.Add(p.Observed);
                }
                var opens=mode<2?0:mode is 3 or 5?2:1;
                var expected=mode==0?Array.Empty<int>():mode==1?new[]{99}:mode is 4 or 6?new[]{1}:opens==2?new[]{1,2,3,4,1,2,3,4}:new[]{1,2,3,4};
                Probe.Check(values.SequenceEqual(expected)&&failed==(mode==6),"opaque wrapper result and exception");
                Probe.Check(p.Starts==opens&&p.Disposals==opens,"independent lazy upstream and cleanup");
                Probe.Check(p.Moves==(mode==4?1:mode==6?2:opens*5)&&p.Reads==(mode==4?1:mode==6?2:opens*4),"source work without prefetch");
                Probe.Check(p.Resolved.SequenceEqual(opens==0?new[]{"W"}:new[]{"W","H"})&&p.Ended.Count==0,"only reached borrowed services");
                Probe.Check(p.Events.Count(e=>e=="W.Finally")== (mode==5?2:1),"wrapper cleanup once per enumeration");
                if(opens!=0) {
                    Probe.Check(p.Events.IndexOf("W.Start")<p.Events.IndexOf("H.Start"),"wrapper starts before upstream");
                    Probe.Check(p.Events.LastIndexOf("source.Dispose")<p.Events.LastIndexOf("W.Finally"),"source cleanup precedes wrapper finally");
                    Probe.Check(ReferenceEquals(p.Request,request)==(mode!=5),"wrapper selected request stays local to upstream");
                }
                __SEND__
                api.Cancel();enumeration.Cancel();Probe.Check(tokens.All(t=>!t.IsCancellationRequested),"linked tokens detached");
                await scope.DisposeAsync();Probe.Check(p.Ended.Order().SequenceEqual(p.Resolved.Order()),"scope owns borrowed handler and wrapper");
                results.Add(new{mode,values,failed,p.Starts,p.Moves,p.Reads,p.Disposals,p.Resolved,p.Ended,p.Events});
            }
            return System.Text.Json.JsonSerializer.Serialize(results);
        }
        """;
}
