using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Xunit;
using Zendiator.SourceGenerator;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private static string OpenSendWrapperPairSource(bool orphan, bool consumer)
    {
        var configuration = $$"""
            [ZendiatorComposition("openSendWrapperPair")]public sealed class Pair;
            public static class RegisterPair {
                public static IServiceCollection Add(IServiceCollection s)=>s.AddZendiator<Pair>(c=>{
                    c.Handlers<ClosedQuery>(DispatchFamily.SendAsync,typeof(ClosedHandler));
                    c.Pipeline(typeof(Query<>),DispatchFamily.SendAsync,Stage.Wrapper(typeof(Logging<,>)));
                    c.Handlers(typeof(Query<>),DispatchFamily.SendAsync,typeof(Handler<>));
                    c.Pipeline<ClosedQuery>(DispatchFamily.SendAsync);
                    {{(orphan ? "c.AddHandler(typeof(OrphanHandler<>));" : "")}}
                });
            }
            """;
        return OpenSendWrapperModule.Replace("__CONFIGURATION__", configuration) + """
            namespace OpenSendWrapper {
            public sealed record ClosedQuery(int Value):IRequest<int>;
            [ZendiatorHandler]public sealed class ClosedHandler:IRequestHandler<ClosedQuery,int>,IDisposable {
                private readonly Probe p;
                public ClosedHandler(Probe p){this.p=p;p.Created.Add(this);p.Events.Add("resolve:C");}
                public ValueTask<int> HandleAsync(ClosedQuery r,CancellationToken token){Probe.Check(token==p.Outer.Token,"closed token");p.Events.Add("closed");return new(r.Value+1);}
                public void Dispose(){p.Disposed.Add(this);}
            }
            }
            """ + (orphan ? """
            namespace OpenSendWrapper {
            public sealed record OrphanQuery<T>:IRequest<int>;
            [ZendiatorHandler]public sealed class OrphanHandler<T>:IRequestHandler<OrphanQuery<T>,int> {
                public ValueTask<int> HandleAsync(OrphanQuery<T> r,CancellationToken token)=>new(99);
            }
            }
            """ : "") + (consumer ? """
            namespace OpenSendWrapper {
            public static class PairEntry {
                public static async Task<string> Run(){
                    var p=new Probe();using var outer=p.Outer;using var inner=p.Inner;
                    IServiceCollection services=new ServiceCollection();services.AddSingleton(p);RegisterPair.Add(services);
                    foreach(var type in new[]{typeof(Logging<,>),typeof(Handler<>),typeof(ClosedHandler),typeof(Generated.Pair.IZendiator),typeof(Generated.Pair.Zendiator)}){
                        var descriptor=services.Single(d=>d.ServiceType==type);Probe.Check(descriptor.Lifetime==ServiceLifetime.Scoped,"scoped registrations");
                    }
                    using var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});
                    using var scope=provider.CreateScope();var mediator=scope.ServiceProvider.GetRequiredService<Generated.Pair.IZendiator>();
                    Probe.Check(ReferenceEquals(mediator,scope.ServiceProvider.GetRequiredService<Generated.Pair.Zendiator>()),"one facade instance");
                    var first=await mediator.SendAsync(new Query<int>(42),outer.Token);var firstGate=p.Gate!;
                    var closed=await mediator.SendAsync(new ClosedQuery(7),outer.Token);
                    var second=await mediator.SendAsync(new Query<string>(21),outer.Token);
                    Probe.Check(first==42&&closed==8&&second==21,"both public overloads");
                    Probe.Check(firstGate.Consumptions==1&&p.Gate!.Consumptions==1,"open handler consumptions");
                    Probe.Check(p.Events.SequenceEqual(new[]{"resolve:W","wrapper.before","resolve:H","handler","wrapper.finally","resolve:C","closed","resolve:W","wrapper.before","resolve:H","handler","wrapper.finally"}),"route isolation");
                    var types=new[]{typeof(Logging<Query<int>,int>),typeof(Handler<int>),typeof(ClosedHandler),typeof(Logging<Query<string>,int>),typeof(Handler<string>)};
                    Probe.Check(p.Created.Select(x=>x.GetType()).SequenceEqual(types),"open definitions close per request");
                    foreach(var receiver in p.Created)Probe.Check(ReferenceEquals(receiver,scope.ServiceProvider.GetRequiredService(receiver.GetType())),"same scope receiver");
                    Probe.Check(p.Disposed.Count==0,"borrowed ownership");scope.Dispose();
                    Probe.Check(p.Disposed.SequenceEqual(p.Created.AsEnumerable().Reverse()),"scope owns both routes");
                    return JsonSerializer.Serialize(new{publicOperations=3,first,closed,second,openConsumptions=new[]{firstGate.Consumptions,p.Gate!.Consumptions},events=p.Events,
                        created=p.Created.Select(x=>x.GetType().ToString()),disposed=p.Disposed.Select(x=>x.GetType().ToString())});
                }
            }
            }
            """ : "");
    }

    [Fact]
    public async Task OpenSendAsync_wrapper_and_closed_route_share_facade_scope_without_partial_generation()
    {
        var root = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        var folder = string.IsNullOrEmpty(root) ? null : Path.Combine(root, "open-send-wrapper-pair");
        var source = OpenSendWrapperPairSource(orphan: false, consumer: true);
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "OpenSendWrapperPair_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        SaveSendAllSync(folder, source, driver, output, diagnostics);
        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        var sources = driver.GetRunResult().Results.Single().GeneratedSources;
        Assert.Equal(2, sources.Count(IsCanonicalRoute));
        var api = output.GetTypeByMetadataName("OpenSendWrapper.Generated.Pair.IZendiator")!;
        Assert.False(api.IsGenericType);
        var methods = api.GetMembers("SendAsync").OfType<IMethodSymbol>().ToArray();
        Assert.Equal(2, methods.Length);
        Assert.Single(methods, m => m.Arity == 1);
        Assert.Single(methods, m => m.Arity == 0 && m.Parameters[0].Type.ToDisplayString() == "OpenSendWrapper.ClosedQuery");
        using var bytes = new MemoryStream();
        var emitted = output.Emit(bytes);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        if (folder != null) File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
        bytes.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var runtime = await assembly.GetType("OpenSendWrapper.PairEntry")!.GetMethod("Run")!.CreateDelegate<Func<Task<string>>>()();
        if (folder != null) File.WriteAllText(Path.Combine(folder, "runtime.json"), runtime);
        using var json = JsonDocument.Parse(runtime);
        Assert.Equal(3, json.RootElement.GetProperty("publicOperations").GetInt32());

        var negative = OpenSendWrapperPairSource(orphan: true, consumer: false);
        var rejected = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(negative, "OpenSendWrapperPairOrphan_" + Guid.NewGuid().ToString("N")), out var refusedOutput, out var refused);
        SaveSendAllSync(folder == null ? null : Path.Combine(folder, "orphan"), negative, rejected, refusedOutput, refused);
        var catalog = Assert.IsType<CompositionSelectionCatalog>(Assert.Single(rejected.GetRunResult().Results.Single().TrackedSteps["CompositionSelectionModel"].SelectMany(s => s.Outputs)).Value);
        var composition = Assert.Single(catalog.Compositions);
        Assert.Empty(composition.Errors);
        Assert.False(composition.CompilerOwnsHandlerError);
        var definitions = composition.Handlers.Select(h => h.Definition.Identity).Distinct(StringComparer.Ordinal).Count();
        Assert.Equal(3, definitions);
        Assert.Equal(2, composition.Pipelines.Count);
        Assert.All(Assert.Single(CanonicalExecutionGraphNormalizer.Create(catalog).Compositions), graph => Assert.True(graph.IsValid));
        Assert.Equal(2, refused.Length);
        Assert.All(refused, d => { Assert.Equal("ZEN0037", d.Id); Assert.True(d.Location.IsInSource); });
        Assert.Null(refusedOutput.GetTypeByMetadataName("OpenSendWrapper.Generated.Pair.IZendiator"));
        Assert.Null(refusedOutput.GetTypeByMetadataName("OpenSendWrapper.Generated.Pair.Zendiator"));
        Assert.Null(refusedOutput.GetTypeByMetadataName("OpenSendWrapper.Generated.Pair.ZendiatorGeneratedRegistrar"));
        Assert.DoesNotContain(rejected.GetRunResult().Results.Single().GeneratedSources, s => s.HintName.EndsWith("Interceptors.g.cs", StringComparison.Ordinal));
        Assert.Empty(refusedOutput.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        if (folder != null) File.WriteAllText(Path.Combine(folder, "membership.json"), JsonSerializer.Serialize(new { completeSelectedHandlers = composition.Handlers.Count, definitions, pipelines = composition.Pipelines.Count, frontendErrors = 0, validRouteGraphs = 2, wholeCompositionRefused = true }));
    }
}
