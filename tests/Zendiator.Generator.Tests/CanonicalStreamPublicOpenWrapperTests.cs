using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    [Fact]
    public async Task Stream_open_wrapper_only_public_calls_preserve_opaque_opens_constraints_and_borrowed_ownership()
    {
        foreach (var shape in new[] { "general", "constrained", "consumer-constraint" })
            await CompileOpenWrapperInput(shape, false);
    }

    [Fact]
    public async Task Mixed_open_wrapper_only_Stream_and_closed_SendSync_preserve_both_routes_and_declaration_orders()
    {
        foreach (var shape in new[] { "general", "constrained", "consumer-constraint" })
            await CompileOpenWrapperInput(shape, true);
    }

    [Fact]
    public Task Mixed_open_wrapper_with_three_hooks_keeps_composition_wide_rejection() => CompileOpenWrapperInput("three-hook-wrapper-blocked", true);

    private static async Task CompileOpenWrapperInput(string shape, bool mixed)
    {
        var constrained = shape is "constrained" or "consumer-constraint";
        var body = "c.Handlers(typeof(GenericItems<>),DispatchFamily.Stream,typeof(GenericHandler<>));" +
            "c.Pipeline(typeof(GenericItems<>),DispatchFamily.Stream,Stage.Wrapper(typeof(GenericWrapper<>))" +
            (shape == "three-hook-wrapper-blocked" ? ",Stage.Hook(typeof(GenericFirst<>)),Stage.Hook(typeof(GenericMap<>)),Stage.Hook(typeof(GenericLast<>))" : "") + ");";
        if (mixed)
        {
            var send = "c.Handlers<WrapperPing>(DispatchFamily.SendSync,typeof(WrapperPingHandler));c.Pipeline<WrapperPing>(DispatchFamily.SendSync);";
            body = constrained ? send + body : body + send;
        }
        var types = StreamGenericTypes.Replace("(T Value) : IStreamRequest<T>;", "(T Value,int Mode=1) : IStreamRequest<T>;")
            .Replace("public System.Collections.Generic.IAsyncEnumerable<T> HandleAsync", "public static object? LastRequest; public System.Collections.Generic.IAsyncEnumerable<T> HandleAsync")
            .Replace("{ p.Call(this);return p.Source(r.Value); }", "{ LastRequest=r;p.Call(this);return p.Source(r.Value); }")
            .Replace("where N:IStreamUpstream<GenericItems<T>,T>{p.Call(this);return n.Open(r,c);}", """
                where N:IStreamUpstream<GenericItems<T>,T>{p.Call(this);return Body(r,n,c);}
                public static object? Selected;
                private async IAsyncEnumerable<T> Body<N>(GenericItems<T> r,N n,
                    [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken c) where N:IStreamUpstream<GenericItems<T>,T> {
                    if(r.Mode==0)yield break;
                    if(r.Mode==3){yield return r.Value;yield break;}
                    var selected=new GenericItems<T>(r.Value,0);Selected=selected;
                    for(int i=0;i<r.Mode;i++)
                        await foreach(var item in n.Open(selected,c).WithCancellation(c).ConfigureAwait(false))yield return item;
                }
                """);
        if (constrained) types = types.Replace("IStreamWrapper<GenericItems<T>,T>, IDisposable {",
            "IStreamWrapper<GenericItems<T>,T>, IDisposable where T:class,new() {");
        var source = "using System.Linq;using System.Collections.Generic;using Microsoft.Extensions.DependencyInjection;" + ListFixture(body) + types;
        if (mixed) source += """
            public sealed record WrapperPing(int Value):ISyncRequest<int>;
            [ZendiatorHandler] public sealed class WrapperPingHandler:ISyncRequestHandler<WrapperPing,int> {public int Handle(WrapperPing r,CancellationToken c)=>r.Value+1;}
            """;
        if (shape == "consumer-constraint") source += """
            public sealed class NoDefault {public NoDefault(int value){}}
            public static class InvalidOpenWrapperConsumer {
                public static object Call(IServiceProvider p)=>p.GetRequiredService<global::App.Generated.Reads.IZendiator>()
                    .StreamAsync(new GenericItems<NoDefault>(new NoDefault(1)));
            }
            """;
        else if (shape != "three-hook-wrapper-blocked") source += OpenWrapperConsumer.Replace("__WHERE__", constrained ? "where T:class,new()" : "")
            .Replace("__CALLS__", constrained ? "results.Add(await Read(new OpenValue(),mode));" :
                "results.Add(await Read(7,mode));results.Add(await Read(\"item\",mode));");
        if (mixed && shape is not ("consumer-constraint" or "three-hook-wrapper-blocked")) source = source
            .Replace("var request=new GenericItems<T>", "Check(mediator.Send(new WrapperPing(41))==42,\"mixed Send before Stream\");var request=new GenericItems<T>")
            .Replace("await scope.DisposeAsync();", "Check(mediator.Send(new WrapperPing(9))==10,\"mixed Send after Stream\");await scope.DisposeAsync();");
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, (mixed ? "MixedOpenWrapper_" : "OpenWrapper_") + Guid.NewGuid().ToString("N")),
            out var output, out var diagnostics);
        var generated = driver.GetRunResult().Results.Single().GeneratedSources;
        using var bytes = new MemoryStream(); var emitted = output.Emit(bytes);
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_STREAM_PUBLIC_EVIDENCE_DIR");
        var folder = evidence == null ? null : Path.Combine(evidence, mixed ? "mixed-open-wrapper" : "open-wrapper", shape);
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
        if (shape == "three-hook-wrapper-blocked")
        {
            Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
            Assert.Equal(2, diagnostics.Length); Assert.All(diagnostics, d => { Assert.Equal("ZEN0037", d.Id); Assert.True(d.Location.IsInSource); });
            AssertNoCanonicalOutput(generated);
            Assert.DoesNotContain(generated, f => f.HintName.EndsWith(".Interceptors.g.cs", StringComparison.Ordinal));
            return;
        }
        Assert.Empty(diagnostics);
        var facade = Assert.Single(generated, f => mixed ? IsCanonicalRoute(f) && f.SourceText.ToString().Contains("global::App.GenericItems<", StringComparison.Ordinal) :
            f.HintName.StartsWith("Zendiator.Canonical.Stream.Public.", StringComparison.Ordinal)).SourceText.ToString();
        var registration = mixed ? CanonicalCompositionSource(generated) : facade;
        if (mixed)
        {
            Assert.Equal(2, generated.Count(IsCanonicalRoute));
            Assert.Contains("Send(", CanonicalRouteSource(generated, "App.WrapperPing"));
            var sendPipeline = registration.IndexOf("expected.Pipeline<global::App.WrapperPing>", StringComparison.Ordinal);
            var streamPipeline = registration.IndexOf("expected.Pipeline(typeof(global::App.GenericItems<>)", StringComparison.Ordinal);
            var sendHandler = registration.IndexOf("expected.Handlers<global::App.WrapperPing>", StringComparison.Ordinal);
            var streamHandler = registration.IndexOf("expected.Handlers(typeof(global::App.GenericItems<>)", StringComparison.Ordinal);
            Assert.True(sendPipeline >= 0 && streamPipeline >= 0 && sendHandler >= 0 && streamHandler >= 0);
            Assert.Equal(constrained, sendPipeline < streamPipeline); Assert.Equal(constrained, sendHandler < streamHandler);
        }
        Assert.Contains("__ZendiatorStreamCanonical_", facade); Assert.Contains("typeof(global::App.GenericWrapper<>)", registration);
        if (constrained) Assert.Contains(" : class, new()", facade);
        if (shape == "consumer-constraint")
        {
            Assert.False(emitted.Success); Assert.Contains(emitted.Diagnostics, d => d.Id == "CS0310" && d.Severity == DiagnosticSeverity.Error && d.Location.IsInSource);
            return;
        }
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        Assert.Empty(emitted.Diagnostics.Where(d => d.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error));
        bytes.Position = 0; var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var runtime = await (Task<string>)assembly.GetType("App.OpenWrapperConsumer")!.GetMethod("Run")!.Invoke(null, null)!;
        if (folder != null) File.WriteAllText(Path.Combine(folder, "runtime.json"), runtime);
    }

    private const string OpenWrapperConsumer = """
        public sealed class OpenValue;
        public static class OpenWrapperConsumer {
            private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
            private static async Task<object> Read<T>(T value,int mode) __WHERE__ {
                var p=new global::Zendiator.Generator.Tests.StreamGenericScopeProbe();var services=new ServiceCollection();services.AddSingleton(p);
                Startup.Configure(services);var count=services.Count;Startup.Configure(services);Check(count==services.Count,"idempotent registration");
                foreach(var type in new[]{typeof(GenericHandler<>),typeof(GenericWrapper<>)})Check(services.Single(d=>d.ServiceType==type).Lifetime==ServiceLifetime.Transient,"open transient descriptor");
                await using var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});
                var scope=provider.CreateAsyncScope();var mediator=scope.ServiceProvider.GetRequiredService<global::App.Generated.Reads.IZendiator>();
                var request=new GenericItems<T>(value,mode);var stream=mediator.StreamAsync(request);Check(p.Created.Count==0,"lazy dispatch");
                var opens=mode is 0 or 3?0:mode;var values=new List<T>();
                for(int pass=0;pass<2;pass++) {
                    var cursor=stream.GetAsyncEnumerator();Check(p.Called.Count==pass*(1+opens),"lazy cursor");
                    try{while(await cursor.MoveNextAsync()){values.Add(cursor.Current);Check(EqualityComparer<T>.Default.Equals(cursor.Current,value),"typed wrapper result");}}
                    finally{await cursor.DisposeAsync();}
                    if(opens!=0)Check(!ReferenceEquals(GenericWrapper<T>.Selected,request)&&ReferenceEquals(GenericHandler<T>.LastRequest,GenericWrapper<T>.Selected),"wrapper-selected request");
                }
                Check(values.Count==2*(mode==3?1:opens),"opaque zero/one/multiple open and direct return");
                var roles=opens==0?new[]{"W"}:new[]{"W","H"};Check(p.Created.Select(c=>c.Role).SequenceEqual(roles),"only reached borrowed closed services");
                foreach(var created in p.Created)Check(p.Called.Count(c=>ReferenceEquals(c,created.Instance))==2*(created.Role=="W"?1:opens),"scoped service reused");
                Check(p.SourceDisposals==2*opens&&p.Released.Count==0,"source cleanup without DI disposal");
                await scope.DisposeAsync();Check(p.Released.Count==roles.Length,"scope owns receivers");
                return new{itemType=typeof(T).Name,mode,values,opens,roles,created=p.Created.Count,called=p.Called.Count,released=p.Released.Count,p.SourceDisposals};
            }
            public static async Task<string> Run(){var results=new List<object>();for(int mode=0;mode<4;mode++){__CALLS__}return System.Text.Json.JsonSerializer.Serialize(results);}
        }
        """;
}
