using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private const string PublicStreamConfiguration = "c.Handlers<Request>(DispatchFamily.Stream,typeof(H)); c.Pipeline<Request>(DispatchFamily.Stream,Stage.Hook<F1>(),Stage.Hook<M>(),Stage.Hook<F2>());";

    private static string PublicStreamSource(string shape) => """
        using System; using System.Linq; using System.Threading; using System.Threading.Tasks;
        using System.Collections.Generic; using Zendiator; using Zendiator.DependencyInjection;
        using Microsoft.Extensions.DependencyInjection;
        namespace App {
            public sealed record Request : IStreamRequest<int>;
            [ZendiatorComposition("reads")] public sealed class Reads;
            public sealed class Probe {
                public Request? Request; public CancellationToken Api,Enumeration,Observed;
                public bool HasToken,Overridden; public int Starts,Moves,Reads,Disposals;
                public readonly List<string> Events=new(); public readonly List<string> Resolved=new(),Ended=new();
                public void Resolve(string name) {Resolved.Add(name);Events.Add("resolve."+name);}
                public void Observe(string phase,Request r,CancellationToken token) {
                    Check(ReferenceEquals(Request,r),"request identity");
                    if(!HasToken) {
                        HasToken=true;Observed=token;
                        Check(!Api.CanBeCanceled ? token==Enumeration : token.CanBeCanceled && token!=Api && token!=Enumeration,"merged token");
                    }
                    Check(token==Observed,"token continuity");Events.Add(phase);
                }
                public void End(string name) {Ended.Add(name);Events.Add("DI.Dispose."+name);}
                public static void Check(bool value,string message) {if(!value)throw new InvalidOperationException(message);}
            }
            [ZendiatorHandler] public sealed class H : IStreamRequestHandler<Request,int>,IDisposable {
                private readonly Probe p;
                public H(Probe probe){p=probe;p.Resolve("H");}
                public IAsyncEnumerable<int> HandleAsync(Request r,CancellationToken c){p.Starts++;p.Observe("H.Start",r,c);return new Source(p,r);}
                public void Dispose()=>p.End("H");
            }
            public sealed class F1 : IStreamItemFilter<Request,int>,IDisposable {
                private readonly Probe p;public bool Existing;
                public F1(Probe probe){p=probe;p.Resolve("F1");}
                public bool Accept(Request r,int value,CancellationToken c){p.Overridden=Existing;p.Observe("F1",r,c);return value%2==1;}
                public void Dispose()=>p.End("F1");
            }
            public sealed class M : IStreamItemMap<Request,int>,IDisposable {
                private readonly Probe p;public M(Probe probe){p=probe;p.Resolve("M");}
                public int Map(Request r,int value,CancellationToken c){p.Observe("M",r,c);return value+10;}
                public void Dispose()=>p.End("M");
            }
            public sealed class F2 : IStreamItemFilter<Request,int>,IDisposable {
                private readonly Probe p;public F2(Probe probe){p=probe;p.Resolve("F2");}
                public bool Accept(Request r,int value,CancellationToken c){p.Observe("F2",r,c);return value<13;}
                public void Dispose()=>p.End("F2");
            }
            public sealed class Source(Probe p,Request r) : IAsyncEnumerable<int> {
                public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken c=default){p.Observe("source.Get",r,c);return new Cursor(p);}
                private sealed class Cursor(Probe p) : IAsyncEnumerator<int> {
                    private int item;
                    public int Current {get{p.Reads++;p.Events.Add("source.Current");return item;}}
                    public ValueTask<bool> MoveNextAsync(){p.Moves++;p.Events.Add("source.Move");return new(++item<=4);}
                    public ValueTask DisposeAsync(){p.Disposals++;p.Events.Add("source.Dispose");return default;}
                }
            }
            public static class Bridge {
                public static IServiceCollection Register(IServiceCollection services)=>services.AddZendiator<Reads>(c=>{ __CONFIG__ });
                __COLLISION__
                __CONSUMER__
            }
            __EXTRA__
        }
        """.Replace("__CONFIG__", PublicStreamConfiguration + (shape == "unsupported-route" ?
            "c.Handlers<Other>(DispatchFamily.Stream,typeof(OtherHandler));" : ""))
        .Replace("__COLLISION__", shape == "collision" ?
            "public static IServiceCollection Collision(IServiceCollection services)=>services.AddZendiator<Reads>(c=>{" + PublicStreamConfiguration + "});" : "")
        .Replace("__EXTRA__", shape == "unsupported-route" ? """
            public sealed record Other : IStreamRequest<int>;
            [ZendiatorHandler] public sealed class OtherHandler : IStreamRequestHandler<Other,int> {
                public IAsyncEnumerable<int> HandleAsync(Other r,CancellationToken c)=>throw new NotSupportedException();
            }
            """ : "")
        .Replace("__CONSUMER__", shape == "accepted" ? PublicStreamConsumer : "");

    private const string PublicStreamConsumer = """
        public static async Task<string> Run() {
            var results=new List<object>();
            for(int index=0;index<3;index++) {
                var p=new Probe();var services=new ServiceCollection();services.AddSingleton(p);
                services.AddScoped<F1>(_=>new F1(p){Existing=true});
                Register(services);var count=services.Count;Register(services);
                Probe.Check(count==services.Count,"TryAdd idempotence");
                foreach(var type in new[]{typeof(H),typeof(F1),typeof(M),typeof(F2),typeof(global::App.Generated.Reads.IZendiator),typeof(global::App.Generated.Reads.Zendiator)})
                    Probe.Check(services.Count(d=>d.ServiceType==type)==1 && services.Single(d=>d.ServiceType==type).Lifetime==ServiceLifetime.Scoped,"Scoped registrar");
                await using var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});
                var scope=provider.CreateAsyncScope();
                var mediator=scope.ServiceProvider.GetRequiredService<global::App.Generated.Reads.IZendiator>();
                Probe.Check(ReferenceEquals(mediator,scope.ServiceProvider.GetRequiredService<global::App.Generated.Reads.Zendiator>()),"mediator identity");
                try{mediator.StreamAsync(null!);throw new InvalidOperationException("missing null guard");}
                catch(ArgumentNullException e){Probe.Check(e.ParamName=="request","dispatch null guard");}
                var request=new Request();
                _=mediator.StreamAsync(request,new CancellationToken(true));
                using var api=new CancellationTokenSource();using var enumeration=new CancellationTokenSource();
                p.Request=request;p.Api=index==0?default:api.Token;p.Enumeration=index==0?default:enumeration.Token;
                var stream=index==0?mediator.StreamAsync(request):mediator.StreamAsync(request,p.Api);
                Probe.Check(p.Resolved.Count==0&&p.Events.Count==0,"dispatch lazy");
                var cursor=stream.GetAsyncEnumerator(p.Enumeration);
                Probe.Check(p.Resolved.Count==0&&p.Events.Count==0,"cursor lazy");
                var values=new List<int>();
                try {
                    while(await cursor.MoveNextAsync()) {
                        values.Add(cursor.Current);p.Events.Add("yield");
                        if(index==2)break;
                    }
                } finally {await cursor.DisposeAsync();}
                Probe.Check(values.SequenceEqual(new[]{11}),"output");
                Probe.Check(p.Starts==1&&p.Disposals==1&&p.Moves==(index==2?1:5)&&p.Reads==(index==2?1:4),"source lifetime and work");
                Probe.Check(p.Resolved.SequenceEqual(new[]{"H","F1","M","F2"}),"reached hook resolution");
                var first=new[]{"resolve.H","H.Start","source.Get","source.Move","source.Current","resolve.F1","F1","resolve.M","M","resolve.F2","F2","yield"};
                Probe.Check(p.Events.Take(first.Length).SequenceEqual(first),"first Move stage and lazy order");
                Probe.Check(p.Overridden&&p.Ended.Count==0,"borrowed services and existing registration");
                if(index!=0){api.Cancel();enumeration.Cancel();Probe.Check(!p.Observed.IsCancellationRequested,"linked token detached by cursor");}
                var beforeScope=p.Events.ToArray();
                await scope.DisposeAsync();
                Probe.Check(p.Ended.Order().SequenceEqual(new[]{"F1","F2","H","M"}),"DI owns borrowed services");
                results.Add(new{shape=index==0?"default-full":index==1?"linked-full":"linked-early",values,starts=p.Starts,moves=p.Moves,currentReads=p.Reads,
                    sourceDisposals=p.Disposals,resolved=p.Resolved,borrowedDisposalsBeforeScope=0,borrowedDisposalsAfterScope=p.Ended,
                    dispatchAndCursorLazy=true,requestIdentity=true,tokenContinuity=true,linkedDetached=index!=0,existingRegistrationPreserved=p.Overridden,eventsBeforeScope=beforeScope,events=p.Events});
            }
            return System.Text.Json.JsonSerializer.Serialize(results);
        }
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stream_zero_stage_public_registration_connects_canonical_and_preserves_terminal_paths(bool mixed)
    {
        var configuration = "c.Handlers<Request>(DispatchFamily.Stream,typeof(H)); c.Pipeline<Request>(DispatchFamily.Stream);";
        if (mixed) configuration += "c.Handlers<Ping>(DispatchFamily.SendSync,typeof(PingHandler)); c.Pipeline<Ping>(DispatchFamily.SendSync);";
        var source = PublicStreamSource("zero-stage").Replace(PublicStreamConfiguration, configuration)
            .Replace("public bool HasToken,Overridden;", "public int InputCount; public bool HasToken,Overridden;")
            .Replace("private int item;", "private int item=-1;")
            .Replace("new(++item<=4)", "new(++item<p.InputCount)");
        if (mixed) source = source.Replace("[ZendiatorComposition(\"reads\")]", """
            public sealed record Ping(int Value):ISyncRequest<int>;
            [ZendiatorHandler] public sealed class PingHandler:ISyncRequestHandler<Ping,int> {
                public int Handle(Ping r,CancellationToken t)=>r.Value+1;
            }
            [ZendiatorComposition("reads")]
            """);
        var consumer = """
            public static async Task<string> Run() {
                var results=new List<object>();
                for(int mode=0;mode<3;mode++) {
                    var p=new Probe{InputCount=mode==0?0:16};var services=new ServiceCollection();services.AddSingleton(p);Register(services);
                    foreach(var type in new[]{typeof(H),typeof(global::App.Generated.Reads.IZendiator),typeof(global::App.Generated.Reads.Zendiator)})
                        Probe.Check(services.Single(d=>d.ServiceType==type).Lifetime==ServiceLifetime.Scoped,"Scoped registrar");
                    await using var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});
                    var scope=provider.CreateAsyncScope();
                    var mediator=scope.ServiceProvider.GetRequiredService<global::App.Generated.Reads.IZendiator>();
                    var request=new Request();p.Request=request;
                    var stream=mediator.StreamAsync(request);
                    Probe.Check(p.Starts==0&&p.Resolved.Count==0,"dispatch lazy");
                    var cursor=stream.GetAsyncEnumerator();
                    Probe.Check(p.Starts==0&&p.Resolved.Count==0,"cursor lazy");
                    var values=new List<int>();
                    try {while(await cursor.MoveNextAsync()){values.Add(cursor.Current);if(mode==2)break;}}
                    finally {await cursor.DisposeAsync();}
                    var count=mode==0?0:mode==2?1:16;
                    Probe.Check(values.SequenceEqual(Enumerable.Range(0,count)),"zero-stage values");
                    Probe.Check(p.Starts==1&&p.Disposals==1&&p.Moves==(mode==2?1:count+1)&&p.Reads==count,"source work and cleanup");
                    Probe.Check(p.Resolved.SequenceEqual(new[]{"H"})&&p.Ended.Count==0,"borrowed handler only");
                    __SEND__
                    await scope.DisposeAsync();
                    Probe.Check(p.Ended.SequenceEqual(new[]{"H"}),"scope owns handler");
                    results.Add(new{mode,values,p.Starts,p.Moves,p.Reads,p.Disposals});
                }
                return System.Text.Json.JsonSerializer.Serialize(results);
            }
            """.Replace("__SEND__", mixed ? "Probe.Check(mediator.Send(new Ping(41))==42,\"mixed Send\");" : "");
        source = source.Replace("public static class Bridge {", "public static class Bridge {" + consumer);
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "PublicZeroStage_" + Guid.NewGuid().ToString("N")),
            out var output, out var diagnostics);
        var generated = driver.GetRunResult().Results.Single().GeneratedSources;
        using var bytes = new MemoryStream();
        var emitted = output.Emit(bytes);
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_STREAM_PUBLIC_EVIDENCE_DIR");
        var folder = evidence == null ? null : Path.Combine(evidence, mixed ? "zero-stage-mixed" : "zero-stage");
        if (folder != null)
        {
            Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder, "input.cs"), source);
            File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
            foreach (var file in generated) File.WriteAllText(Path.Combine(folder, file.HintName), file.SourceText.ToString());
            File.WriteAllText(Path.Combine(folder, "compile.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                mixed, emitted.Success,
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
        Assert.Contains(generated, f => f.SourceText.ToString().Contains("return __ZendiatorStreamCanonical_", StringComparison.Ordinal));
        bytes.Position = 0;var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var result = await (Task<string>)assembly.GetType("App.Bridge")!.GetMethod("Run")!.Invoke(null, null)!;
        if (folder != null) File.WriteAllText(Path.Combine(folder, "runtime.json"), result);
        using var json = System.Text.Json.JsonDocument.Parse(result);
        Assert.Equal(3, json.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task Stream_public_named_registration_connects_selected_candidate_and_rejects_unaccepted_compositions()
    {
        foreach (var shape in new[] { "accepted", "unsupported-route", "collision" })
        {
            var source = PublicStreamSource(shape);
            var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "PublicStream_" + Guid.NewGuid().ToString("N")),
                out var output, out var diagnostics);
            var generated = driver.GetRunResult().Results.Single().GeneratedSources;
            using var bytes = new MemoryStream();
            var emitted = output.Emit(bytes);
            var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_STREAM_PUBLIC_EVIDENCE_DIR");
            var folder = evidence == null ? null : Path.Combine(evidence, shape);
            if (folder != null)
            {
                Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, "input.cs"), source);
                File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
                foreach (var file in generated) File.WriteAllText(Path.Combine(folder, file.HintName), file.SourceText.ToString());
                File.WriteAllText(Path.Combine(folder, "compile.json"), System.Text.Json.JsonSerializer.Serialize(new
                {
                    shape,
                    emitted.Success,
                    diagnostics = diagnostics.Select(d => new
                    {
                        d.Id,
                        severity = d.Severity.ToString(),
                        message = d.GetMessage(),
                        inSource = d.Location.IsInSource,
                        anchor = d.Location.IsInSource ? d.Location.SourceTree!.GetText().ToString(d.Location.SourceSpan) : null
                    }),
                    compiler = emitted.Diagnostics.Select(d => new { d.Id, severity = d.Severity.ToString(), message = d.GetMessage() }),
                    sources = generated.Select(f => f.HintName)
                }));
            }
            Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error));
            Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
            if (shape != "accepted")
            {
                Assert.Contains(diagnostics, d => d.Id == (shape == "collision" ? "ZEN0035" : "ZEN0037") &&
                    d.Severity == DiagnosticSeverity.Error && d.Location.IsInSource &&
                    d.Location.SourceTree!.GetText().ToString(d.Location.SourceSpan).Contains(
                        shape == "collision" ? "AddZendiator" : "c.", StringComparison.Ordinal));
                Assert.DoesNotContain(generated, f => f.HintName.StartsWith("Zendiator.Canonical.Stream.", StringComparison.Ordinal));
                continue;
            }
            Assert.Empty(diagnostics);
            Assert.Single(generated, f => f.HintName.StartsWith("Zendiator.StreamPrototype.", StringComparison.Ordinal));
            Assert.DoesNotContain(generated, f => f.HintName.StartsWith("Zendiator.StreamCanonical.", StringComparison.Ordinal));
            var facade = Assert.Single(generated, f => f.HintName.StartsWith("Zendiator.Canonical.Stream.Public.", StringComparison.Ordinal)).SourceText.ToString();
            Assert.Contains("__ZendiatorStreamPrototype_", facade);
            Assert.DoesNotContain("ThrowIfCancellationRequested", facade);
            bytes.Position = 0; var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
            // Reflection loads the independent consumer entry only; its registration and dispatch are statically typed.
            var result = await (Task<string>)assembly.GetType("App.Bridge")!.GetMethod("Run")!.Invoke(null, null)!;
            if (folder != null) File.WriteAllText(Path.Combine(folder, "runtime.json"), result);
            using var json = System.Text.Json.JsonDocument.Parse(result);
            Assert.Equal(3, json.RootElement.GetArrayLength());
        }
    }
}
