using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    [Fact]
    public async Task Stream_open_zero_stage_public_calls_preserve_constraints_closed_DI_types_and_borrowed_ownership()
    {
        foreach (var shape in new[] { "general", "constrained", "unbound-handler", "consumer-constraint" })
        {
            var constrained = shape is "constrained" or "consumer-constraint";
            var body = "c.Handlers(typeof(GenericItems<>),DispatchFamily.Stream,typeof(GenericHandler<>));" +
                "c.Pipeline(typeof(GenericItems<>),DispatchFamily.Stream);";
            var types = StreamGenericTypes.Replace("public System.Collections.Generic.IAsyncEnumerable<T> HandleAsync",
                "public static CancellationToken Token; public static object? LastRequest; public System.Collections.Generic.IAsyncEnumerable<T> HandleAsync")
                .Replace("{ p.Call(this);return p.Source(r.Value); }", "{ Token=c;LastRequest=r;p.Call(this);return p.Source(r.Value); }");
            if (constrained) types = types.Replace("IStreamRequestHandler<GenericItems<T>,T>, IDisposable {",
                "IStreamRequestHandler<GenericItems<T>,T>, IDisposable where T:class,new() {");
            var source = "using System.Linq;using System.Collections.Generic;using Microsoft.Extensions.DependencyInjection;" + ListFixture(body) + types;
            if (shape == "unbound-handler") source = source.Replace("typeof(GenericHandler<>)", "typeof(GenericHandler<,>)")
                .Replace("class GenericHandler<T>", "class GenericHandler<T,TUnused>");
            else if (shape == "consumer-constraint") source += """
                public sealed class NoDefault { public NoDefault(int value){} }
                public static class InvalidZeroConsumer {
                    public static object Call(IServiceProvider p)=>p.GetRequiredService<global::App.Generated.Reads.IZendiator>()
                        .StreamAsync(new GenericItems<NoDefault>(new NoDefault(1)));
                }
                """;
            else source += OpenZeroConsumer.Replace("__WHERE__", constrained ? "where T:class,new()" : "")
                .Replace("__CALLS__", constrained ? "results.Add(await Read(mediator,new GenericItems<OpenValue>(new OpenValue()),p));" :
                    "results.Add(await Read(mediator,new GenericItems<int>(7),p));results.Add(await Read(mediator,new GenericItems<string>(\"item\"),p));")
                .Replace("__REQUEST__", constrained ? "new GenericItems<OpenValue>(new OpenValue())" : "new GenericItems<int>(7)")
                .Replace("__CLOSED_TYPES__", constrained ? "1" : "2");
            var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "OpenZero_" + Guid.NewGuid().ToString("N")),
                out var output, out var diagnostics);
            var generated = driver.GetRunResult().Results.Single().GeneratedSources;
            using var bytes = new MemoryStream();
            var emitted = output.Emit(bytes);
            var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_STREAM_PUBLIC_EVIDENCE_DIR");
            var folder = evidence == null ? null : Path.Combine(evidence, "open-zero", shape);
            if (folder != null)
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "input.cs"), source);
                File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
                foreach (var file in generated) File.WriteAllText(Path.Combine(folder, file.HintName), file.SourceText.ToString());
                File.WriteAllText(Path.Combine(folder, "compile.json"), JsonSerializer.Serialize(new
                {
                    shape,
                    emitted.Success,
                    diagnostics = diagnostics.Select(d => new { d.Id, severity = d.Severity.ToString(), message = d.GetMessage(), inSource = d.Location.IsInSource }),
                    compiler = emitted.Diagnostics.Select(d => new { d.Id, severity = d.Severity.ToString(), message = d.GetMessage(), inSource = d.Location.IsInSource }),
                    sources = generated.Select(f => f.HintName)
                }));
            }
            if (shape == "unbound-handler")
            {
                Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
                Assert.Contains(diagnostics, d => d.Id == "ZEN0035" && d.Severity == DiagnosticSeverity.Error && d.Location.IsInSource);
                Assert.DoesNotContain(generated, f => f.HintName.StartsWith("Zendiator.Canonical.Stream.Public.", StringComparison.Ordinal));
                continue;
            }
            Assert.Empty(diagnostics);
            var facade = Assert.Single(generated, f => f.HintName.StartsWith("Zendiator.Canonical.Stream.Public.", StringComparison.Ordinal)).SourceText.ToString();
            Assert.Contains("__ZendiatorStreamCanonical_", facade);
            if (constrained) Assert.Contains(" : class, new()", facade);
            if (shape == "consumer-constraint")
            {
                Assert.False(emitted.Success);
                Assert.Contains(emitted.Diagnostics, d => d.Id == "CS0310" && d.Severity == DiagnosticSeverity.Error && d.Location.IsInSource);
                continue;
            }
            Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
            Assert.Empty(emitted.Diagnostics.Where(d => d.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error));
            Assert.Single(generated, f => f.HintName.StartsWith("Zendiator.StreamCanonical.", StringComparison.Ordinal));
            Assert.DoesNotContain(generated, f => f.HintName.StartsWith("Zendiator.StreamPrototype.", StringComparison.Ordinal));
            bytes.Position = 0;
            var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
            var runtime = await (Task<string>)assembly.GetType("App.OpenZeroConsumer")!.GetMethod("Run")!.Invoke(null, null)!;
            if (folder != null) File.WriteAllText(Path.Combine(folder, "runtime.json"), runtime);
        }
    }

    private const string OpenZeroConsumer = """
        public sealed class OpenValue;
        public static class OpenZeroConsumer {
            private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
            private static async Task<object> Read<T>(global::App.Generated.Reads.IZendiator mediator,GenericItems<T> request,
                global::Zendiator.Generator.Tests.StreamGenericScopeProbe p) __WHERE__ {
                try{mediator.StreamAsync<T>(null!);throw new InvalidOperationException("null guard");}
                catch(ArgumentNullException e){Check(e.ParamName=="request","request null parameter");}
                using var api=new CancellationTokenSource();using var enumeration=new CancellationTokenSource();
                var stream=mediator.StreamAsync(request,api.Token);var calls=p.Called.Count;
                var values=new List<T>();
                for(int i=0;i<2;i++) {
                    var cursor=stream.GetAsyncEnumerator(enumeration.Token);Check(p.Called.Count==calls+i,"lazy cursor");
                    try {
                        Check(await cursor.MoveNextAsync(),"first item");values.Add(cursor.Current);
                        Check(EqualityComparer<T>.Default.Equals(cursor.Current,request.Value),"typed item");
                        Check(ReferenceEquals(GenericHandler<T>.LastRequest,request),"request identity");
                        Check(GenericHandler<T>.Token.CanBeCanceled&&GenericHandler<T>.Token!=api.Token&&GenericHandler<T>.Token!=enumeration.Token,"linked execution token");
                        Check(!await cursor.MoveNextAsync(),"one source item");
                    }finally{await cursor.DisposeAsync();}
                }
                api.Cancel();enumeration.Cancel();Check(!GenericHandler<T>.Token.IsCancellationRequested,"linked token detached");
                return new{itemType=typeof(T).Name,values,enumerations=2};
            }
            public static async Task<string> Run() {
                var p=new global::Zendiator.Generator.Tests.StreamGenericScopeProbe();
                var services=new ServiceCollection();services.AddSingleton(p);Startup.Configure(services);var count=services.Count;Startup.Configure(services);
                Check(services.Count==count,"idempotent registration");
                Check(services.Single(d=>d.ServiceType==typeof(GenericHandler<>)).Lifetime==ServiceLifetime.Scoped,"open handler registration");
                await using var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});
                var scope=provider.CreateAsyncScope();var mediator=scope.ServiceProvider.GetRequiredService<global::App.Generated.Reads.IZendiator>();
                var results=new List<object>();
                __CALLS__
                Check(p.Created.Count==__CLOSED_TYPES__&&p.Called.Count==2*__CLOSED_TYPES__&&p.Released.Count==0,"closed type captures and borrowed lifetime");
                Check(p.Created.All(c=>p.Called.Count(call=>ReferenceEquals(c.Instance,call))==2),"scoped instance reused per closed type");
                Check(p.SourceDisposals==2*__CLOSED_TYPES__,"fresh source per enumeration");
                var dangling=mediator.StreamAsync(__REQUEST__);await scope.DisposeAsync();
                Check(p.Released.Count==__CLOSED_TYPES__,"DI owns handler disposal");
                var cursor=dangling.GetAsyncEnumerator();
                try{await cursor.MoveNextAsync();throw new InvalidOperationException("disposed scope accepted");}
                catch(ObjectDisposedException){}finally{await cursor.DisposeAsync();}
                return System.Text.Json.JsonSerializer.Serialize(new{results,created=p.Created.Count,called=p.Called.Count,released=p.Released.Count,p.SourceDisposals,disposedScopeRejected=true});
            }
        }
        """;
}
