using System.Runtime.Loader;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    [Fact]
    public async Task Stream_single_sync_hook_and_wrapper_keep_request_boundaries_opaque_control_and_borrowed_ownership()
    {
        foreach (var shape in new[] { "map-before", "map-after", "filter-before", "filter-after", "constrained", "consumer-constraint", "closed" })
            await CompileWrapperHookInput(shape);
    }

    [Fact]
    public Task Closed_mixed_single_hook_wrapper_preserves_public_Send_Stream_registration_and_request_boundaries() => CompileWrapperHookInput("closed-mixed");

    [Fact]
    public async Task Open_generic_mixed_single_hook_wrapper_connects_public_Send_Stream_and_constraints()
    {
        foreach (var shape in new[] { "map-before", "filter-after", "constrained", "consumer-constraint" })
            await CompileWrapperHookInput(shape, true);
    }

    [Fact]
    public Task Open_generic_mixed_single_hook_wrapper_with_two_hooks_rejects_composition() => CompileWrapperHookInput("mixed-blocked");

    private static async Task CompileWrapperHookInput(string shape, bool includeSend = false)
    {
        var before = shape.EndsWith("before", StringComparison.Ordinal) || shape is "closed" or "closed-mixed";
        var filter = shape.StartsWith("filter", StringComparison.Ordinal);
        var constrained = shape is "constrained" or "consumer-constraint";
        var closed = shape is "closed" or "closed-mixed";
        var mixed = includeSend || shape is "mixed-blocked" or "closed-mixed";
        var hook = filter ? "GenericFirst" : "GenericMap";
        var types = StreamGenericTypes.Replace("(T Value) : IStreamRequest<T>;",
            "(T Value,int Mode,Func<T,T> Map,Func<T,T> SelectedMap,bool Allow,bool SelectedAllow) : IStreamRequest<T>;")
            .Replace("public System.Collections.Generic.IAsyncEnumerable<T> HandleAsync", "public static object? LastRequest; public System.Collections.Generic.IAsyncEnumerable<T> HandleAsync")
            .Replace("{ p.Call(this);return p.Source(r.Value); }", "{ LastRequest=r;p.Call(this);return p.Source(r.Value); }")
            .Replace("public T Map(GenericItems<T> r,T item,CancellationToken c){p.Call(this);return item;}",
                "public static object? LastRequest;public T Map(GenericItems<T> r,T item,CancellationToken c){LastRequest=r;p.Call(this);return r.Map(item);}")
            .Replace("public bool Accept(GenericItems<T> r,T item,CancellationToken c){p.Call(this);return true;}",
                "public static object? LastRequest;public bool Accept(GenericItems<T> r,T item,CancellationToken c){LastRequest=r;p.Call(this);return r.Allow;}")
            .Replace("where N:IStreamUpstream<GenericItems<T>,T>{p.Call(this);return n.Open(r,c);}", """
                where N:IStreamUpstream<GenericItems<T>,T>{p.Call(this);return Body(r,n,c);}
                public static object? Selected;
                private async IAsyncEnumerable<T> Body<N>(GenericItems<T> r,N n,
                    [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken c) where N:IStreamUpstream<GenericItems<T>,T> {
                    if(r.Mode==0)yield break;
                    if(r.Mode==3){yield return r.Value;yield break;}
                    var selected=new GenericItems<T>(r.Value,0,r.SelectedMap,r.SelectedMap,r.SelectedAllow,r.SelectedAllow);Selected=selected;
                    for(int i=0;i<r.Mode;i++)await foreach(var item in n.Open(selected,c).WithCancellation(c).ConfigureAwait(false))yield return item;
                }
                """);
        if (constrained) types = types.Replace("IStreamWrapper<GenericItems<T>,T>, IDisposable {",
            "IStreamWrapper<GenericItems<T>,T>, IDisposable where T:class,new() {");
        if (closed)
        {
            foreach (var name in new[] { "GenericItems", "GenericHandler", "GenericFirst", "GenericMap", "GenericLast", "GenericWrapper" }) types = types.Replace(name + "<T>", name);
            types = Regex.Replace(types, @"\bT\b", "int");
        }
        var request = closed ? "GenericItems" : "GenericItems<>";
        var handler = closed ? "GenericHandler" : "GenericHandler<>";
        var hookStage = "Stage.Hook(typeof(" + hook + (closed ? "" : "<>") + "))";
        var wrapperStage = "Stage.Wrapper(typeof(GenericWrapper" + (closed ? "" : "<>") + "))";
        var body = "c.Handlers(typeof(" + request + "),DispatchFamily.Stream,typeof(" + handler + "));" +
            "c.Pipeline(typeof(" + request + "),DispatchFamily.Stream," + (before ? hookStage + "," + wrapperStage : wrapperStage + "," + hookStage) +
            (shape == "mixed-blocked" ? ",Stage.Hook(typeof(GenericFirst<>))" : "") + ");";
        if (mixed) body += "c.Handlers<HookPing>(DispatchFamily.SendSync,typeof(HookPingHandler));c.Pipeline<HookPing>(DispatchFamily.SendSync);";
        var source = "using System.Linq;using System.Collections.Generic;using Microsoft.Extensions.DependencyInjection;" + ListFixture(body) + types;
        if (mixed) source += """
            public sealed record HookPing(int Value):ISyncRequest<int>;
            [ZendiatorHandler]public sealed class HookPingHandler:ISyncRequestHandler<HookPing,int>{public int Handle(HookPing r,CancellationToken c)=>r.Value;}
            """;
        if (shape == "consumer-constraint") source += """
            public sealed class NoDefault{public NoDefault(int value){}}
            public static class InvalidWrapperHookConsumer{public static object Call(IServiceProvider p)=>
                p.GetRequiredService<global::App.Generated.Reads.IZendiator>().StreamAsync(new GenericItems<NoDefault>(new NoDefault(1),1,x=>x,x=>x,true,false));}
            """;
        else if (shape != "mixed-blocked")
        {
            var consumer = WrapperHookConsumer.Replace("__WHERE__", constrained ? "where T:class,new()" : "")
                .Replace("__BEFORE__", before ? "true" : "false").Replace("__FILTER__", filter ? "true" : "false")
                .Replace("__HOOK__", hook).Replace("__ROLE__", filter ? "F1" : "M")
                .Replace("__CALLS__", constrained ? "results.Add(await Read(new OpenValue(),mode,x=>x,x=>x));" :
                    "results.Add(await Read(7,mode,x=>x+10,x=>x+20));" + (closed ? "" : "results.Add(await Read(\"item\",mode,x=>x+\"!\",x=>x+\"?\"));"));
            if (closed)
            {
                foreach (var name in new[] { "GenericItems", "GenericHandler", "GenericMap", "GenericWrapper" }) consumer = consumer.Replace(name + "<T>", name).Replace(name + "<>", name);
                consumer = Regex.Replace(consumer.Replace("Read<T>(", "Read("), @"\bT\b", "int");
            }
            if (mixed) consumer = consumer
                .Replace("var request=new GenericItems", "Check(services.Single(d=>d.ServiceType==typeof(HookPingHandler)).Lifetime==ServiceLifetime.Scoped,\"closed Send registration\");Check(mediator.Send(new HookPing(41))==41,\"Send before Stream\");var request=new GenericItems")
                .Replace("await scope.DisposeAsync();", "Check(mediator.Send(new HookPing(9))==9,\"Send after Stream\");await scope.DisposeAsync();");
            source += consumer;
        }
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "WrapperHook_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        var generated = driver.GetRunResult().Results.Single().GeneratedSources;
        using var bytes = new MemoryStream(); var emitted = output.Emit(bytes);
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_STREAM_PUBLIC_EVIDENCE_DIR");
        var folder = evidence == null ? null : Path.Combine(evidence, includeSend || shape == "mixed-blocked" ? "open-mixed-wrapper-hook" : "wrapper-hook", shape);
        if (folder != null)
        {
            Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, "input.cs"), source); File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
            foreach (var file in generated) File.WriteAllText(Path.Combine(folder, file.HintName), file.SourceText.ToString());
            File.WriteAllText(Path.Combine(folder, "compile.json"), JsonSerializer.Serialize(new
            {
                shape,
                emitted.Success,
                diagnostics = diagnostics.Select(d => new { d.Id, message = d.GetMessage(), inSource = d.Location.IsInSource }),
                compiler = emitted.Diagnostics.Select(d => new { d.Id, severity = d.Severity.ToString(), message = d.GetMessage(), inSource = d.Location.IsInSource }),
                sources = generated.Select(f => f.HintName)
            }));
        }
        if (shape == "mixed-blocked")
        {
            Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics)); Assert.Equal(2, diagnostics.Length);
            Assert.All(diagnostics, d => { Assert.Equal("ZEN0037", d.Id); Assert.True(d.Location.IsInSource); }); AssertNoCanonicalOutput(generated);
            Assert.DoesNotContain(generated, f => f.HintName.EndsWith(".Interceptors.g.cs", StringComparison.Ordinal)); return;
        }
        Assert.Empty(diagnostics);
        var facade = mixed ? closed ? CanonicalRouteSource(generated, "App.GenericItems") :
            Assert.Single(generated, f => IsCanonicalRoute(f) && f.SourceText.ToString().Contains("global::App.GenericItems<", StringComparison.Ordinal)).SourceText.ToString() :
            Assert.Single(generated, f => f.HintName.StartsWith("Zendiator.Canonical.Stream.Public.", StringComparison.Ordinal)).SourceText.ToString();
        if (mixed)
        {
            Assert.Equal(2, generated.Count(IsCanonicalRoute)); Assert.Contains("Send(", CanonicalRouteSource(generated, "App.HookPing"));
            var registration = CanonicalCompositionSource(generated);
            foreach (var requestType in closed ? new[] { "GenericItems", "HookPing" } : new[] { "HookPing" })
            {
                Assert.Contains("expected.Handlers<global::App." + requestType + ">", registration);
                Assert.Contains("expected.Pipeline<global::App." + requestType + ">", registration);
            }
            if (!closed)
            {
                Assert.Contains("expected.Handlers(typeof(global::App.GenericItems<>)", registration);
                Assert.Contains("expected.Pipeline(typeof(global::App.GenericItems<>)", registration);
            }
        }
        Assert.Contains("__ZendiatorStreamCanonical_", facade);
        if (constrained) Assert.Contains(" : class, new()", facade);
        if (shape == "consumer-constraint")
        {
            Assert.False(emitted.Success); Assert.Contains(emitted.Diagnostics, d => d.Id == "CS0310" && d.Severity == DiagnosticSeverity.Error && d.Location.IsInSource); return;
        }
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics)); Assert.Empty(emitted.Diagnostics.Where(d => d.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error));
        bytes.Position = 0; var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var runtime = await (Task<string>)assembly.GetType("App.WrapperHookConsumer")!.GetMethod("Run")!.Invoke(null, null)!;
        if (folder != null) File.WriteAllText(Path.Combine(folder, "runtime.json"), runtime);
    }

    private const string WrapperHookConsumer = """
        public sealed class OpenValue;
        public static class WrapperHookConsumer {
            private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
            private static async Task<object> Read<T>(T value,int mode,Func<T,T> map,Func<T,T> selectedMap) __WHERE__ {
                var p=new global::Zendiator.Generator.Tests.StreamGenericScopeProbe();var services=new ServiceCollection();services.AddSingleton(p);Startup.Configure(services);
                foreach(var type in new[]{typeof(GenericHandler<>),typeof(GenericWrapper<>),typeof(__HOOK__<>)})Check(services.Single(d=>d.ServiceType==type).Lifetime==ServiceLifetime.Scoped,"scoped descriptor");
                await using var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});
                var scope=provider.CreateAsyncScope();var mediator=scope.ServiceProvider.GetRequiredService<global::App.Generated.Reads.IZendiator>();
                var request=new GenericItems<T>(value,mode,map,selectedMap,true,false);var stream=mediator.StreamAsync(request);Check(p.Created.Count==0,"lazy dispatch");
                var opens=mode is 0 or 3?0:mode;var reached=mode!=0&&(!__BEFORE__||opens!=0);var hookCalls=reached?(mode==3?1:opens):0;
                var expectedCount=__FILTER__&&__BEFORE__&&opens!=0?0:mode==3?1:opens;
                var expectedValue=__FILTER__||__BEFORE__&&opens==0?value:__BEFORE__?selectedMap(value):map(value);var values=new List<T>();
                for(int pass=0;pass<2;pass++) {
                    var cursor=stream.GetAsyncEnumerator();Check(p.Called.Count==pass*(1+opens+hookCalls),"lazy cursor");
                    try{while(await cursor.MoveNextAsync()){values.Add(cursor.Current);Check(EqualityComparer<T>.Default.Equals(cursor.Current,expectedValue),"hook uses its segment request");}}
                    finally{await cursor.DisposeAsync();}
                    if(opens!=0)Check(ReferenceEquals(GenericHandler<T>.LastRequest,GenericWrapper<T>.Selected)&&!ReferenceEquals(request,GenericWrapper<T>.Selected),"selected upstream request");
                    if(reached)Check(ReferenceEquals(__HOOK__<T>.LastRequest,__BEFORE__?GenericWrapper<T>.Selected:request),"hook request boundary");
                }
                Check(values.Count==2*expectedCount,"filter/map and opaque control");
                var roles=opens!=0?new[]{"W","H","__ROLE__"}:reached?new[]{"W","__ROLE__"}:new[]{"W"};Check(p.Created.Select(c=>c.Role).SequenceEqual(roles),"only reached receivers");
                foreach(var created in p.Created)Check(p.Called.Count(c=>ReferenceEquals(c,created.Instance))==2*(created.Role=="W"?1:created.Role=="H"?opens:hookCalls),"scoped captures reused");
                Check(p.SourceDisposals==2*opens&&p.Released.Count==0,"independent source cleanup and borrowed receivers");
                await scope.DisposeAsync();Check(p.Released.Count==roles.Length,"DI owns disposal");
                return new{itemType=typeof(T).Name,mode,values,opens,roles,created=p.Created.Count,called=p.Called.Count,released=p.Released.Count,p.SourceDisposals};
            }
            public static async Task<string> Run(){var results=new List<object>();for(int mode=0;mode<4;mode++){__CALLS__}return System.Text.Json.JsonSerializer.Serialize(results);}
        }
        """;
}
