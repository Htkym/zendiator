using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using Zendiator.SourceGenerator;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private const string OpenSendZeroPairModule = """
        using System;using System.Collections.Generic;using System.Linq;using System.Threading;using System.Threading.Tasks;using System.Threading.Tasks.Sources;using System.Text.Json;
        using Microsoft.Extensions.DependencyInjection;using Zendiator;using Zendiator.DependencyInjection;
        namespace OpenZeroPair {
        public interface ILeft;public interface IRight;
        public sealed class Left:ILeft;public sealed class OtherLeft:ILeft;public sealed class CanceledLeft:ILeft;public sealed class Right:IRight;
        public sealed class Shared {public int Value=3;}
        public sealed record Query<L,R>(int Value,Shared Shared):IRequest<int>;
        public sealed record StatusQuery(int Value,Shared Shared):IRequest<int>;
        public sealed class Once:IValueTaskSource<int> {
            private ManualResetValueTaskSourceCore<int> core;private readonly Probe p;private readonly string step,role;private readonly int value;public int Consumptions;
            public Once(Probe p,int value){this.p=p;this.value=value;step=p.Step;role=p.Role;core.RunContinuationsAsynchronously=true;}
            public ValueTask<int> Task=>new(this,core.Version);public void Complete()=>core.SetResult(value);
            public int GetResult(short token){Probe.Check(++Consumptions==1,"single ValueTask consumption");Probe.Check(Probe.Ambient.Value=="handler:"+role,"source consumed inside async boundary");p.Trace.Add("consume:"+step);return core.GetResult(token);}
            public ValueTaskSourceStatus GetStatus(short token)=>core.GetStatus(token);
            public void OnCompleted(Action<object?> continuation,object? state,short token,ValueTaskSourceOnCompletedFlags flags)=>core.OnCompleted(continuation,state,token,flags);
        }
        public sealed class Probe {
            public static readonly AsyncLocal<string?> Ambient=new();
            public string Step="",Role="";public object Original=null!;public Shared Data=null!;public CancellationToken Token;public bool Pending;
            public readonly List<string> Trace=new(),Steps=new();public readonly List<string?> AmbientSeen=new();
            public readonly List<object> Created=new(),Disposed=new(),Receivers=new();public readonly List<Once> Sources=new();
            public static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
            public void Resolve(object receiver){Created.Add(receiver);Trace.Add("resolve:"+receiver.GetType()+":"+Ambient.Value);Ambient.Value="resolve";}
            public ValueTask<int> Handle(object receiver,string role,object request,int value,Shared shared,CancellationToken token){
                Check(role==Role&&ReferenceEquals(request,Original)&&ReferenceEquals(shared,Data)&&token==Token,"selected route/request/shared/token");
                Check(!Steps.Contains(Step),"one invocation per dispatch");Steps.Add(Step);Receivers.Add(receiver);AmbientSeen.Add(Ambient.Value);
                Trace.Add("call:"+Step+":"+role+":"+value+":"+shared.Value+":"+Ambient.Value);Ambient.Value="handler:"+role;
                shared.Value+=role=="open"?1:10;var source=new Once(this,value+(role=="open"?100:200));Sources.Add(source);if(!Pending)source.Complete();return source.Task;
            }
            public void Dispose(object receiver){Disposed.Add(receiver);Trace.Add("dispose:"+receiver.GetType());}
        }
        [ZendiatorHandler]public sealed class Handler<X,Y>:IRequestHandler<Query<Y,X>,int>,IDisposable where X:class,IRight,new() where Y:class,ILeft,new(){
            private readonly Probe p;public Handler(Probe p){this.p=p;p.Resolve(this);}
            public ValueTask<int> HandleAsync(Query<Y,X> r,CancellationToken token)=>p.Handle(this,"open",r,r.Value,r.Shared,token);
            public void Dispose()=>p.Dispose(this);
        }
        [ZendiatorHandler]public sealed class StatusHandler:IRequestHandler<StatusQuery,int>,IDisposable {
            private readonly Probe p;public StatusHandler(Probe p){this.p=p;p.Resolve(this);}
            public ValueTask<int> HandleAsync(StatusQuery r,CancellationToken token)=>p.Handle(this,"closed",r,r.Value,r.Shared,token);
            public void Dispose()=>p.Dispose(this);
        }
        public sealed record ExtraQuery<T>:IRequest<int>;
        [ZendiatorHandler]public sealed class ExtraHandler<T>:IRequestHandler<ExtraQuery<T>,int>{public ValueTask<int> HandleAsync(ExtraQuery<T> r,CancellationToken token)=>new(0);}
        [ZendiatorHandler]public sealed class Missing<X,Y,Unused>:IRequestHandler<Query<Y,X>,int>{public ValueTask<int> HandleAsync(Query<Y,X> r,CancellationToken token)=>new(0);}
        }
        """;

    private static string OpenSendZeroPairSource(bool consumer, bool sentinel = true, string invalid = "")
    {
        string Configuration(string form, bool empty) => $$"""
            namespace OpenZeroPair {
            [ZendiatorComposition("openZeroPair-{{form}}")]public sealed class {{form}};
            public static class Register{{form}} {
                public static IServiceCollection Add(IServiceCollection s)=>s.AddZendiator<{{form}}>(c=>{
                    c.Handlers(typeof(Query<,>),DispatchFamily.SendAsync,typeof({{(invalid == "unbound-handler" ? "Missing<,,>" : "Handler<,>")}}));
                    {{(invalid == "second-open" ? "c.Handlers(typeof(ExtraQuery<>),DispatchFamily.SendAsync,typeof(ExtraHandler<>));" : "c.Handlers<StatusQuery>(DispatchFamily.SendAsync,typeof(StatusHandler));")}}
                    {{(empty ? "c.Pipeline(typeof(Query<,>),DispatchFamily.SendAsync);c.Pipeline<StatusQuery>(DispatchFamily.SendAsync);" : "")}}
                    {{(invalid == "orphan" ? "c.AddHandler(typeof(ExtraHandler<>));" : "")}}
                });
            }
            }
            """;
        return OpenSendZeroPairModule + Configuration("Omitted", false) + (sentinel ? Configuration("Empty", true) : "") + (consumer ? OpenSendZeroPairConsumer : "");
    }

    private const string OpenSendZeroPairConsumer = """
        namespace OpenZeroPair {
        public static class Entry {
            private static ValueTask<int> Reference<L,R>(IServiceProvider p,Query<L,R> r,CancellationToken token) where L:class,ILeft,new() where R:class,IRight,new(){ArgumentNullException.ThrowIfNull(r);return Core(p,r,token);}
            private static async ValueTask<int> Core<L,R>(IServiceProvider p,Query<L,R> r,CancellationToken token) where L:class,ILeft,new() where R:class,IRight,new(){token.ThrowIfCancellationRequested();return await p.GetRequiredService<Handler<R,L>>().HandleAsync(r,token).ConfigureAwait(false);}
            private static ValueTask<int> Reference(IServiceProvider p,StatusQuery r,CancellationToken token){ArgumentNullException.ThrowIfNull(r);return Core(p,r,token);}
            private static async ValueTask<int> Core(IServiceProvider p,StatusQuery r,CancellationToken token){token.ThrowIfCancellationRequested();return await p.GetRequiredService<StatusHandler>().HandleAsync(r,token).ConfigureAwait(false);}
            private static ValueTask<int> Dispatch<L,R>(object mediator,bool empty,Query<L,R> r,CancellationToken token) where L:class,ILeft,new() where R:class,IRight,new()=>empty?((Generated.Empty.IZendiator)mediator).SendAsync(r,token):((Generated.Omitted.IZendiator)mediator).SendAsync(r,token);
            private static ValueTask<int> Dispatch(object mediator,bool empty,StatusQuery r,CancellationToken token)=>empty?((Generated.Empty.IZendiator)mediator).SendAsync(r,token):((Generated.Omitted.IZendiator)mediator).SendAsync(r,token);
            private static async Task<string> Observe(bool generated,bool empty){
                var p=new Probe();IServiceCollection services=new ServiceCollection();services.AddSingleton(p);services.AddScoped(typeof(Handler<,>));var existing=services.Last();
                if(empty)RegisterEmpty.Add(services);else RegisterOmitted.Add(services);var count=services.Count;if(empty)RegisterEmpty.Add(services);else RegisterOmitted.Add(services);
                Probe.Check(count==5&&services.Count==count&&services.Contains(existing),"TryAdd/repeat registration");
                var facade=empty?typeof(Generated.Empty.IZendiator):typeof(Generated.Omitted.IZendiator);var concrete=empty?typeof(Generated.Empty.Zendiator):typeof(Generated.Omitted.Zendiator);
                foreach(var type in new[]{typeof(Handler<,>),typeof(StatusHandler),facade,concrete})Probe.Check(services.Single(d=>d.ServiceType==type).Lifetime==ServiceLifetime.Scoped,"scoped keys");
                var mismatch=new ZendiatorConfiguration();mismatch.Handlers(typeof(Query<,>),DispatchFamily.SendAsync,typeof(Handler<,>));mismatch.Handlers<StatusQuery>(DispatchFamily.SendAsync,typeof(StatusHandler));
                if(!empty){mismatch.Pipeline(typeof(Query<,>),DispatchFamily.SendAsync);mismatch.Pipeline<StatusQuery>(DispatchFamily.SendAsync);}
                IServiceCollection rejected=new ServiceCollection();var mismatchRejected=false;
                try{if(empty)Generated.Empty.ZendiatorGeneratedRegistrar.Add(rejected,mismatch.Snapshot());else Generated.Omitted.ZendiatorGeneratedRegistrar.Add(rejected,mismatch.Snapshot());}catch(InvalidOperationException){mismatchRejected=true;}
                Probe.Check(mismatchRejected&&rejected.Count==0,"snapshot form before descriptor addition");
                using var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});using var scope=provider.CreateScope();var sp=scope.ServiceProvider;
                var mediator=sp.GetRequiredService(facade);Probe.Check(ReferenceEquals(mediator,sp.GetRequiredService(concrete)),"same facade instance");
                using var normal=new CancellationTokenSource();using var canceled=new CancellationTokenSource();canceled.Cancel();var data=new Shared();var attempts=0;var pendingCount=0;string? cancellationPhase=null;
                async Task<int?> Invoke(string step,string role,object request,CancellationToken token,bool pending,bool cancel,Func<ValueTask<int>> invoke){
                    attempts++;p.Step=step;p.Role=role;p.Original=request;p.Data=data;p.Token=token;p.Pending=pending;Probe.Ambient.Value="caller";
                    var traceCount=p.Trace.Count;var sourceCount=p.Sources.Count;ValueTask<int> operation=default;var returned=false;
                    try{operation=invoke();returned=true;}catch(Exception error){throw new InvalidOperationException("unexpected synchronous failure",error);}
                    Probe.Check(returned&&Probe.Ambient.Value=="caller","non-async public boundary/caller context");
                    Probe.Check(p.Sources.Count==sourceCount+(cancel?0:1),"one callback source per entered route");
                    if(cancel){
                        Probe.Check(operation.IsCanceled&&p.Trace.Count==traceCount,"entry guard before unresolved closed generic key");
                        try{_=await operation.ConfigureAwait(false);throw new InvalidOperationException("expected cancellation");}catch(OperationCanceledException error){Probe.Check(error.CancellationToken==token,"cancellation token identity");cancellationPhase="await";}
                        Probe.Check(Probe.Ambient.Value=="caller","canceled call context");return null;
                    }
                    var source=p.Sources.Last();
                    if(pending){Probe.Check(!operation.IsCompleted&&source.Consumptions==0,"genuine pending source before consumption");pendingCount++;source.Complete();}
                    else Probe.Check(operation.IsCompletedSuccessfully&&source.Consumptions==1,"synchronous ValueTask completion consumed once");
                    var value=await operation.ConfigureAwait(false);Probe.Check(source.Consumptions==1&&Probe.Ambient.Value=="caller","one consumption/context restoration");return value;
                }
                var a=new Query<Left,Right>(7,data);var status=new StatusQuery(5,data);var b=new Query<OtherLeft,Right>(8,data);var again=new Query<Left,Right>(9,data);var stopped=new Query<CanceledLeft,Right>(88,data);var after=new StatusQuery(6,data);
                var results=new List<int?> {
                    await Invoke("open-A","open",a,normal.Token,false,false,()=>generated?Dispatch(mediator,empty,a,normal.Token):Reference(sp,a,normal.Token)),
                    await Invoke("closed-1","closed",status,normal.Token,false,false,()=>generated?Dispatch(mediator,empty,status,normal.Token):Reference(sp,status,normal.Token)),
                    await Invoke("open-B","open",b,normal.Token,true,false,()=>generated?Dispatch(mediator,empty,b,normal.Token):Reference(sp,b,normal.Token)),
                    await Invoke("open-A-repeat","open",again,normal.Token,false,false,()=>generated?Dispatch(mediator,empty,again,normal.Token):Reference(sp,again,normal.Token)),
                    await Invoke("canceled-open","open",stopped,canceled.Token,false,true,()=>generated?Dispatch(mediator,empty,stopped,canceled.Token):Reference(sp,stopped,canceled.Token)),
                    await Invoke("closed-after-cancel","closed",after,normal.Token,false,false,()=>generated?Dispatch(mediator,empty,after,normal.Token):Reference(sp,after,normal.Token))
                };
                Probe.Check(results.SequenceEqual(new int?[]{107,205,108,109,null,206})&&data.Value==26,"selected route results/shared effects");
                Probe.Check(attempts==6&&pendingCount==1&&cancellationPhase=="await"&&p.Steps.SequenceEqual(new[]{"open-A","closed-1","open-B","open-A-repeat","closed-after-cancel"}),"route sequence/cancellation isolation");
                Probe.Check(p.AmbientSeen.SequenceEqual(new[]{"resolve","resolve","resolve","caller","caller"}),"native async receiver/caller contexts");
                Probe.Check(p.Created.Select(r=>r.GetType()).SequenceEqual(new[]{typeof(Handler<Right,Left>),typeof(StatusHandler),typeof(Handler<Right,OtherLeft>)}),"closed DI keys/no canceled receiver");
                Probe.Check(p.Receivers.SequenceEqual(new[]{p.Created[0],p.Created[1],p.Created[2],p.Created[0],p.Created[1]}),"same scope receivers across routes/closed type reuse");
                foreach(var receiver in p.Created)Probe.Check(ReferenceEquals(receiver,sp.GetRequiredService(receiver.GetType())),"DI owns scoped receiver");
                Probe.Check(p.Disposed.Count==0&&p.Sources.Count==5&&p.Sources.All(s=>s.Consumptions==1),"no dispatch cleanup/exact source consumption");scope.Dispose();
                Probe.Check(p.Disposed.SequenceEqual(p.Created.AsEnumerable().Reverse()),"exact reverse scope cleanup");
                return JsonSerializer.Serialize(new{empty,attempts,pendingCount,cancellationPhase,results,shared=data.Value,p.Steps,p.Trace,p.AmbientSeen,consumptions=p.Sources.Select(s=>s.Consumptions),created=p.Created.Select(r=>r.GetType().ToString()),disposed=p.Disposed.Select(r=>r.GetType().ToString()),registrations=count,mismatchRejected});
            }
            public static async Task<string> Run(){var rows=new List<object>();foreach(var empty in new[]{false,true}){
                var reference=await Observe(false,empty);var generated=await Observe(true,empty);Probe.Check(reference==generated,"reference/generated mismatch\n"+reference+"\n"+generated);
                rows.Add(new{equal=true,reference=JsonDocument.Parse(reference).RootElement.Clone(),generated=JsonDocument.Parse(generated).RootElement.Clone()});
            }return JsonSerializer.Serialize(new{pairs=rows.Count,dispatchAttempts=24,callbackInvocations=20,rows});}
        }
        }
        """;

    private static string? OpenSendZeroPairFolder(string name)
    {
        var root = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        if (string.IsNullOrEmpty(root)) return null;
        var folder = Path.Combine(root, "open-send-zero-pair-" + name); Directory.CreateDirectory(folder); return folder;
    }

    [Fact]
    public async Task OpenSend_zero_stage_pair_shares_facade_and_scope_across_generic_and_closed_routes()
    {
        var source = OpenSendZeroPairSource(consumer: true);
        var sentinel = OpenSendWrapperPairSource(orphan: false, consumer: false);
        var compilation = SyncCompilation(source, "OpenSendZeroPair_" + Guid.NewGuid().ToString("N")).AddSyntaxTrees(CSharpSyntaxTree.ParseText(sentinel, SyncParseOptions));
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        var folder = OpenSendZeroPairFolder("runtime"); SaveSendAllSync(folder, source, driver, output, diagnostics);
        if (folder != null) { File.WriteAllText(Path.Combine(folder, "input-0.cs"), source); File.WriteAllText(Path.Combine(folder, "input-1.cs"), sentinel); }
        Assert.Empty(diagnostics); Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        var catalog = Assert.IsType<CompositionSelectionCatalog>(Assert.Single(driver.GetRunResult().Results.Single().TrackedSteps["CompositionSelectionModel"].SelectMany(s => s.Outputs)).Value);
        var model = CanonicalMultiEmissionNormalizer.Create(catalog, CanonicalExecutionGraphNormalizer.Create(catalog)); Assert.Equal(3, model.Compositions.Count);
        foreach (var form in new[] { "Omitted", "Empty" })
        {
            var ns = "OpenZeroPair.Generated." + form;
            var composition = Assert.Single(model.Compositions, c => c.Namespace == ns); Assert.Equal(2, composition.Routes.Count);
            Assert.All(composition.Routes, route => { Assert.NotNull(route.Async); Assert.Null(route.Wrapper); Assert.Null(route.Async.Factory); Assert.Empty(route.Async.Hooks); Assert.Equal(form == "Empty", route.Registration.HasEmptyPipeline); });
            Assert.Single(composition.Routes, route => route.Registration.OpenRegistration != null && route.Registration.Generic != null);
            Assert.Single(composition.Routes, route => route.Registration.OpenRegistration == null && route.Registration.Generic == null);
            Assert.True(CanonicalRegistrationPlan.Compatible(composition)); Assert.Equal(2, CanonicalRegistrationPlan.Multi(composition).Routes.Count);
            var api = output.GetTypeByMetadataName(ns + ".IZendiator")!; var mediator = output.GetTypeByMetadataName(ns + ".Zendiator")!;
            Assert.NotNull(api); Assert.NotNull(mediator); Assert.False(api.IsGenericType); Assert.False(mediator.IsGenericType);
            foreach (var type in new[] { api, mediator })
            {
                var methods = type.GetMembers("SendAsync").OfType<IMethodSymbol>().ToArray(); Assert.Equal(2, methods.Length);
                var generic = Assert.Single(methods, m => m.Arity == 2); var closed = Assert.Single(methods, m => m.Arity == 0);
                Assert.Equal("StatusQuery", closed.Parameters[0].Type.Name);
                Assert.All(generic.TypeParameters, p => { Assert.True(p.HasReferenceTypeConstraint); Assert.True(p.HasConstructorConstraint); });
                Assert.Contains(generic.TypeParameters[0].ConstraintTypes, t => t.Name == "IRight"); Assert.Contains(generic.TypeParameters[1].ConstraintTypes, t => t.Name == "ILeft");
                Assert.All(methods, m => { Assert.False(m.IsAsync); var result = Assert.IsAssignableFrom<INamedTypeSymbol>(m.ReturnType); Assert.Equal("ValueTask", result.Name); Assert.Equal(SpecialType.System_Int32, Assert.Single(result.TypeArguments).SpecialType); });
            }
            var helpers = mediator.GetMembers().OfType<IMethodSymbol>().Where(m => m.Name.StartsWith("ExecuteAsync__", StringComparison.Ordinal)).ToArray(); Assert.Equal(2, helpers.Length); Assert.Equal(2, helpers.Select(m => m.Name).Distinct(StringComparer.Ordinal).Count());
            Assert.All(helpers, m => Assert.True(m.IsAsync)); Assert.Single(helpers, m => m.Arity == 0); Assert.Single(helpers, m => m.Arity == 2);
        }
        var wrapper = Assert.Single(model.Compositions, c => c.Namespace == "OpenSendWrapper.Generated.Pair"); Assert.Equal(2, wrapper.Routes.Count); Assert.Single(wrapper.Routes, r => r.Wrapper != null); Assert.Single(wrapper.Routes, r => r.Async != null);
        Assert.Null(output.GetTypeByMetadataName("OpenSendWrapper.PairEntry"));
        var sentinelApi = output.GetTypeByMetadataName("OpenSendWrapper.Generated.Pair.IZendiator")!;
        Assert.Equal(new[] { 0, 1 }, sentinelApi.GetMembers("SendAsync").OfType<IMethodSymbol>().Select(m => m.Arity).OrderBy(a => a));
        using var bytes = new MemoryStream(); var emitted = output.Emit(bytes); Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        if (folder != null) File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray()); bytes.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var runtime = await assembly.GetType("OpenZeroPair.Entry")!.GetMethod("Run")!.CreateDelegate<Func<Task<string>>>()().WaitAsync(TimeSpan.FromSeconds(20));
        if (folder != null) File.WriteAllText(Path.Combine(folder, "runtime.json"), runtime);
        using var json = JsonDocument.Parse(runtime); Assert.Equal(2, json.RootElement.GetProperty("pairs").GetInt32()); Assert.Equal(24, json.RootElement.GetProperty("dispatchAttempts").GetInt32()); Assert.Equal(20, json.RootElement.GetProperty("callbackInvocations").GetInt32());
    }

    [Fact]
    public void OpenSend_zero_stage_pair_rejects_partial_compositions_and_preserves_compiler_constraints()
    {
        foreach (var name in new[] { "orphan", "second-open", "unbound-handler" })
        {
            var source = OpenSendZeroPairSource(consumer: false, sentinel: false, invalid: name);
            var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "OpenSendZeroPairRejected_" + name + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
            SaveSendAllSync(OpenSendZeroPairFolder(name), source, driver, output, diagnostics);
            Assert.NotEmpty(diagnostics);
            if (name == "unbound-handler")
            {
                Assert.Equal(2, diagnostics.Length);
                var binding = Assert.Single(diagnostics, d => d.Id == "ZEN0035"); var blocked = Assert.Single(diagnostics, d => d.Id == "ZEN0037");
                Assert.Contains("typeof(Missing<,,>)", source.Substring(binding.Location.SourceSpan.Start, binding.Location.SourceSpan.Length));
                Assert.Contains("Handlers<StatusQuery>", source.Substring(blocked.Location.SourceSpan.Start, blocked.Location.SourceSpan.Length));
            }
            else Assert.All(diagnostics, d => Assert.Equal("ZEN0037", d.Id));
            Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
            Assert.Null(output.GetTypeByMetadataName("OpenZeroPair.Generated.Omitted.IZendiator")); Assert.Null(output.GetTypeByMetadataName("OpenZeroPair.Generated.Omitted.ZendiatorGeneratedRegistrar"));
            Assert.DoesNotContain(driver.GetRunResult().Results.Single().GeneratedSources, s => s.HintName.StartsWith("Zendiator.Route.", StringComparison.Ordinal));
        }
        var constraints = OpenSendZeroPairSource(consumer: false, sentinel: false) + """
            namespace OpenZeroPair {
            public sealed class NoDefault:ILeft{public NoDefault(int value){}}
            public static class BadConsumer {public static ValueTask<int> Send(Generated.Omitted.IZendiator mediator)=>mediator.SendAsync(new Query<NoDefault,Right>(7,new Shared()));}
            }
            """;
        var accepted = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(constraints, "OpenSendZeroPairCompiler_" + Guid.NewGuid().ToString("N")), out var compiler, out var generatorDiagnostics);
        SaveSendAllSync(OpenSendZeroPairFolder("constraints"), constraints, accepted, compiler, generatorDiagnostics); Assert.Empty(generatorDiagnostics);
        var error = Assert.Single(compiler.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error); Assert.Equal("CS0310", error.Id); Assert.Contains("NoDefault", error.GetMessage()); Assert.True(error.Location.IsInSource); Assert.Equal("SendAsync", constraints.Substring(error.Location.SourceSpan.Start, error.Location.SourceSpan.Length));
        var api = compiler.GetTypeByMetadataName("OpenZeroPair.Generated.Omitted.IZendiator")!; Assert.NotNull(api); Assert.Equal(2, api.GetMembers("SendAsync").OfType<IMethodSymbol>().Count()); Assert.NotNull(compiler.GetTypeByMetadataName("OpenZeroPair.Generated.Omitted.ZendiatorGeneratedRegistrar"));
    }
}
