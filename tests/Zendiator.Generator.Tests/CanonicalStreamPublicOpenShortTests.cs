using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    [Fact]
    public async Task Stream_open_short_sync_public_calls_preserve_hook_order_lazy_resolution_and_hook_constraints()
    {
        foreach (var shape in new[] { "filter", "map", "filter-map", "map-filter", "constrained", "consumer-constraint" })
            await CompileOpenShortInput(shape, false);
    }

    [Fact]
    public async Task Mixed_open_short_Stream_and_closed_SendSync_preserve_registration_order_constraints_and_both_routes()
    {
        foreach (var shape in new[] { "filter-map", "map-filter", "constrained", "consumer-constraint" })
            await CompileOpenShortInput(shape, true);
    }

    private static async Task CompileOpenShortInput(string shape, bool mixed)
    {
        var constrained = shape is "constrained" or "consumer-constraint";
        var stages = shape == "filter" ? new[] { "GenericFirst" } : shape is "map" or "constrained" or "consumer-constraint" ?
            new[] { "GenericMap" } : shape == "filter-map" ? new[] { "GenericFirst", "GenericMap" } : new[] { "GenericMap", "GenericFirst" };
        var types = StreamGenericTypes.Replace("(T Value) : IStreamRequest<T>;",
            "(T Value,Func<T,T> Project,bool Allow) : IStreamRequest<T>;")
            .Replace("return true;", "return r.Allow;").Replace("return item;", "return r.Project(item);");
        if (constrained) types = types.Replace("IStreamItemMap<GenericItems<T>,T>, IDisposable {",
            "IStreamItemMap<GenericItems<T>,T>, IDisposable where T:class,new() {");
        var body = "c.Handlers(typeof(GenericItems<>),DispatchFamily.Stream,typeof(GenericHandler<>));" +
            "c.Pipeline(typeof(GenericItems<>),DispatchFamily.Stream," +
            string.Join(",", stages.Select(s => "Stage.Hook(typeof(" + s + "<>))")) + ");";
        if (mixed)
        {
            var send = "c.Handlers<OpenShortPing>(DispatchFamily.SendSync,typeof(OpenShortHandler));c.Pipeline<OpenShortPing>(DispatchFamily.SendSync);";
            body = shape == "map-filter" ? send + body : body + send;
        }
        var source = "using System.Linq;using System.Collections.Generic;using Microsoft.Extensions.DependencyInjection;" + ListFixture(body) + types;
        if (mixed) source += """
            public sealed record OpenShortPing(int Value):ISyncRequest<int>;
            [ZendiatorHandler] public sealed class OpenShortHandler:ISyncRequestHandler<OpenShortPing,int> {
                public int Handle(OpenShortPing r,CancellationToken c)=>r.Value+1;
            }
            """;
        if (shape == "consumer-constraint") source += """
            public sealed class NoDefault { public NoDefault(int value){} }
            public static class InvalidShortConsumer {
                public static object Call(IServiceProvider p)=>p.GetRequiredService<global::App.Generated.Reads.IZendiator>()
                    .StreamAsync(new GenericItems<NoDefault>(new NoDefault(1),x=>x,true));
            }
            """;
        else source += OpenShortConsumer.Replace("__WHERE__", constrained ? "where T:class,new()" : "")
            .Replace("__CALLS__", constrained ? "results.Add(await Read(new OpenValue(),x=>x,true));" :
                "results.Add(await Read(7,x=>x+10,true));results.Add(await Read(\"item\",x=>x+\"!\",true));" +
                (stages.Contains("GenericFirst") ? "results.Add(await Read(7,x=>x+10,false));" : ""))
            .Replace("__HAS_FILTER__", stages.Contains("GenericFirst") ? "true" : "false")
            .Replace("__HAS_MAP__", stages.Contains("GenericMap") ? "true" : "false")
            .Replace("__FILTER_FIRST__", stages[0] == "GenericFirst" ? "true" : "false")
            .Replace("__ROLES__", string.Join(",", stages.Select(s => s == "GenericFirst" ? "\"F1\"" : "\"M\"")))
            .Replace("__TYPES__", string.Join(",", stages.Select(s => "typeof(" + s + "<>)")));
        if (mixed && shape != "consumer-constraint") source = source
            .Replace("var request=new GenericItems<T>", "Check(mediator.Send(new OpenShortPing(41))==42,\"mixed Send before Stream\");var request=new GenericItems<T>")
            .Replace("await scope.DisposeAsync();", "Check(mediator.Send(new OpenShortPing(9))==10,\"mixed Send after Stream\");await scope.DisposeAsync();");
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, (mixed ? "MixedOpenShort_" : "OpenShort_") + Guid.NewGuid().ToString("N")),
            out var output, out var diagnostics);
        var generated = driver.GetRunResult().Results.Single().GeneratedSources;
        using var bytes = new MemoryStream(); var emitted = output.Emit(bytes);
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_STREAM_PUBLIC_EVIDENCE_DIR");
        var folder = evidence == null ? null : Path.Combine(evidence, mixed ? "mixed-open-short" : "open-short", shape);
        if (folder != null)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "input.cs"), source); File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
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
        Assert.Empty(diagnostics);
        var facade = Assert.Single(generated, f => mixed ? IsCanonicalRoute(f) && f.SourceText.ToString().Contains("global::App.GenericItems<", StringComparison.Ordinal) :
            f.HintName.StartsWith("Zendiator.Canonical.Stream.Public.", StringComparison.Ordinal)).SourceText.ToString();
        var registration = mixed ? CanonicalCompositionSource(generated) : facade;
        if (mixed)
        {
            Assert.Equal(2, generated.Count(IsCanonicalRoute));
            Assert.Contains("Send(", CanonicalRouteSource(generated, "App.OpenShortPing"));
            Assert.Contains("expected.Handlers<global::App.OpenShortPing>", registration);
            var sendOrder = registration.IndexOf("expected.Pipeline<global::App.OpenShortPing>", StringComparison.Ordinal);
            var streamOrder = registration.IndexOf("expected.Pipeline(typeof(global::App.GenericItems<>)", StringComparison.Ordinal);
            Assert.True(sendOrder >= 0 && streamOrder >= 0);
            Assert.Equal(shape == "map-filter", sendOrder < streamOrder);
        }
        Assert.Contains("__ZendiatorStreamCanonical_", facade);
        Assert.Contains("typeof(global::App.GenericHandler<>)", registration);
        foreach (var stage in stages) Assert.Contains("typeof(global::App." + stage + "<>)", registration);
        if (constrained) Assert.Contains(" : class, new()", facade);
        if (shape == "consumer-constraint")
        {
            Assert.False(emitted.Success);
            Assert.Contains(emitted.Diagnostics, d => d.Id == "CS0310" && d.Severity == DiagnosticSeverity.Error && d.Location.IsInSource);
            return;
        }
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        Assert.Empty(emitted.Diagnostics.Where(d => d.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error));
        bytes.Position = 0; var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var runtime = await (Task<string>)assembly.GetType("App.OpenShortConsumer")!.GetMethod("Run")!.Invoke(null, null)!;
        if (folder != null) File.WriteAllText(Path.Combine(folder, "runtime.json"), runtime);
    }

    private const string OpenShortConsumer = """
        public sealed class OpenValue;
        public static class OpenShortConsumer {
            private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
            private static async Task<object> Read<T>(T value,Func<T,T> project,bool allow) __WHERE__ {
                var p=new global::Zendiator.Generator.Tests.StreamGenericScopeProbe();
                var services=new ServiceCollection();services.AddSingleton(p);Startup.Configure(services);var count=services.Count;Startup.Configure(services);
                Check(services.Count==count,"idempotent registration");
                foreach(var type in new[]{typeof(GenericHandler<>),__TYPES__})
                    Check(services.Single(d=>d.ServiceType==type).Lifetime==ServiceLifetime.Scoped,"open borrowed descriptor");
                await using var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});
                var scope=provider.CreateAsyncScope();var mediator=scope.ServiceProvider.GetRequiredService<global::App.Generated.Reads.IZendiator>();
                var request=new GenericItems<T>(value,project,allow);var stream=mediator.StreamAsync(request);
                var roles=new[]{"H"}.Concat(!allow&&__FILTER_FIRST__?new[]{"F1"}:new[]{__ROLES__}).ToArray();
                var values=new List<T>();Check(p.Created.Count==0,"lazy public dispatch");
                for(int i=0;i<2;i++) {
                    var cursor=stream.GetAsyncEnumerator();Check(p.Called.Count==i*roles.Length,"lazy enumeration");
                    try {
                        var moved=await cursor.MoveNextAsync();Check(moved==(!__HAS_FILTER__||allow),"filter short circuit");
                        if(moved){values.Add(cursor.Current);Check(EqualityComparer<T>.Default.Equals(cursor.Current,__HAS_MAP__?project(value):value),"typed mapped item");Check(!await cursor.MoveNextAsync(),"single item");}
                    }finally{await cursor.DisposeAsync();}
                    Check(p.Created.Select(c=>c.Role).SequenceEqual(roles),"only reached closed services resolve");
                    Check(p.Called.Skip(i*roles.Length).Select(call=>p.Created.Single(c=>ReferenceEquals(c.Instance,call)).Role).SequenceEqual(roles),"declared callback order");
                }
                Check(p.Released.Count==0&&p.SourceDisposals==2,"borrowed receivers and fresh source cleanup");
                Check(p.Created.All(c=>p.Called.Count(call=>ReferenceEquals(c.Instance,call))==2),"scoped closed instance reused");
                await scope.DisposeAsync();Check(p.Released.Count==roles.Length,"DI owns disposal");
                return new{itemType=typeof(T).Name,allow,values,roles,created=p.Created.Count,called=p.Called.Count,released=p.Released.Count,p.SourceDisposals};
            }
            public static async Task<string> Run(){var results=new List<object>();__CALLS__ return System.Text.Json.JsonSerializer.Serialize(results);}
        }
        """;
}
