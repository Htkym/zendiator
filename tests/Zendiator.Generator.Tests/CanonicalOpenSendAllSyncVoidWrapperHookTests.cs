using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using Zendiator.SourceGenerator;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private const string OpenAllSyncVoidWrapperHookSource = """
        using System;using System.Collections.Generic;using System.Linq;using System.Threading;using System.Text.Json;
        using Microsoft.Extensions.DependencyInjection;using Zendiator;using Zendiator.DependencyInjection;
        namespace OpenAllVoidWrapper {
        public interface ILeft;public interface IRight;public interface IWrap;
        public sealed class Left:ILeft,IWrap;public sealed class Right:IRight;public sealed class OtherLeft:ILeft,IWrap;
        public sealed class Shared {public int Value=3;}
        public sealed record Request<L,R>(int Value,Shared Shared):ISyncMultiRequest;
        public sealed class Probe {
            public static readonly AsyncLocal<string?> Ambient=new();
            public readonly CancellationTokenSource Outer=new(),Inner=new();public readonly string Mode;public readonly Exception Failure;
            public object Original=null!,WrapperInput=null!,Saved=null!;public Shared Data=null!;public int Branch,Call,NextCalls;
            public readonly List<string> Trace=new(),Phases=new(),Handlers=new();public readonly List<string?> AmbientSeen=new();
            public readonly List<object> Created=new(),Disposed=new(),Inputs=new(),Effective=new();
            public Probe(string mode){Mode=mode;Failure=mode=="cancel"?new OperationCanceledException("inner token",Inner.Token):new FormatException("first finally");}
            public static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
            public void Resolve(object receiver,string role){Created.Add(receiver);Trace.Add("resolve:"+role);}
            public void Hit(string phase,object request,int value,Shared shared,CancellationToken token){
                var outer=phase.StartsWith("W.",StringComparison.Ordinal);
                Check(token==(outer?Outer.Token:Inner.Token)&&ReferenceEquals(request,outer?Original:phase=="K.before"?WrapperInput:Saved)&&ReferenceEquals(shared,Data),"per-invocation request/token/shared reference");
                Phases.Add(phase);Trace.Add(phase+":"+Branch+":"+Call+":"+value+":"+shared.Value+":"+(outer?"outer":"inner"));AmbientSeen.Add(Ambient.Value);Ambient.Value=phase+":"+Branch;
            }
            public void Body(string role,object request,int value,Shared shared,CancellationToken token){
                Hit("H:"+role,request,value,shared,token);Handlers.Add(role);shared.Value+=role=="Z"?10:100;
                if(Mode=="cancel"&&Branch==1){Inner.Cancel();throw Failure;}
            }
        }
        [ZendiatorHandler]public sealed class ZHandler<X,Y>:ISyncRequestHandler<Request<Y,X>>,IDisposable where X:class,new() where Y:class {
            private readonly Probe p;public ZHandler(Probe p){this.p=p;p.Resolve(this,"Z");}
            public void Handle(Request<Y,X> r,CancellationToken token)=>p.Body("Z",r,r.Value,r.Shared,token);
            public void Dispose(){p.Disposed.Add(this);}
        }
        [ZendiatorHandler]public sealed class AHandler<X,Y>:ISyncRequestHandler<Request<X,Y>>,IDisposable where X:class,ILeft,new() where Y:class {
            private readonly Probe p;public AHandler(Probe p){this.p=p;p.Resolve(this,"A");}
            public void Handle(Request<X,Y> r,CancellationToken token)=>p.Body("A",r,r.Value,r.Shared,token);
            public void Dispose(){p.Disposed.Add(this);}
        }
        public sealed class Hook<X,Y>:ISendRequestTransform<Request<Y,X>>,ISendSuccessObserver<Request<Y,X>>,ISendExceptionObserver<Request<Y,X>>,ISendFinallyObserver<Request<Y,X>>,IDisposable where X:class,IRight where Y:class {
            private readonly Probe p;public Hook(Probe p){this.p=p;p.Resolve(this,"K");}
            public Request<Y,X> Before(Request<Y,X> r,CancellationToken token){p.Hit("K.before",r,r.Value,r.Shared,token);var saved=r with{Value=r.Value+1};p.Saved=saved;p.Effective.Add(saved);return saved;}
            public void After(Request<Y,X> r,CancellationToken token)=>p.Hit("K.after",r,r.Value,r.Shared,token);
            public void OnException(Request<Y,X> r,Exception error,CancellationToken token){Probe.Check(ReferenceEquals(error,p.Failure),"observer identity");p.Hit("K.exception",r,r.Value,r.Shared,token);}
            public void Finally(Request<Y,X> r,SendExitInfo exit,CancellationToken token){
                var failed=p.Mode=="cancel"&&p.Branch==1;Probe.Check(exit.Success!=failed&&ReferenceEquals(exit.Error,failed?p.Failure:null),"entered outcome");
                p.Hit("K.finally",r,r.Value,r.Shared,token);if(p.Mode=="finally"&&p.Branch==1)throw p.Failure;
            }
            public void Dispose(){p.Disposed.Add(this);}
        }
        public sealed class Logging<X,Y>:ISendWrapper<Request<X,Y>>,IDisposable where X:class,IWrap where Y:class {
            private readonly Probe p;public Logging(Probe p){this.p=p;p.Resolve(this,"W");}
            public void Invoke<N>(Request<X,Y> r,N next,CancellationToken token) where N:ISendContinuation<Request<X,Y>>{
                p.Branch++;p.Call=0;p.Hit("W.before",r,r.Value,r.Shared,token);Probe.Check(r.Value==7,"every branch starts from original request");
                try{
                    if(p.Mode=="skip"&&p.Branch==1)return;
                    for(var i=0;i<(p.Mode=="repeat"?2:1);i++){
                        p.Call=i+1;p.NextCalls++;var input=r with{Value=r.Value+10+i};p.WrapperInput=input;p.Inputs.Add(input);next.Invoke(input,p.Inner.Token);
                    }
                }finally{p.Hit("W.finally",r,r.Value,r.Shared,token);}
            }
            public void Dispose(){p.Disposed.Add(this);}
        }
        [ZendiatorComposition("openAllVoidWrapper")]public sealed class Composition;
        public static class Register {
            public static IServiceCollection Add(IServiceCollection s)=>s.AddZendiator<Composition>(c=>{
                c.Handlers(typeof(Request<,>),DispatchFamily.SendAllSync,typeof(ZHandler<,>),typeof(AHandler<,>));
                c.Pipeline(typeof(Request<,>),DispatchFamily.SendAllSync,Stage.Wrapper(typeof(Logging<,>)),Stage.Hook(typeof(Hook<,>)));
            });
        }
        }
        """;

    private const string OpenAllSyncVoidWrapperHookConsumer = """
        namespace OpenAllVoidWrapper {
        public static class Entry {
            // Independent orchestration; the user wrapper owns its continuation calls.
            private readonly struct Next<L,R>(IServiceProvider provider,int branch):ISendContinuation<Request<L,R>> where L:class,ILeft,IWrap,new() where R:class,IRight,new(){
                public void Invoke(Request<L,R> request,CancellationToken token){
                    ArgumentNullException.ThrowIfNull(request);token.ThrowIfCancellationRequested();var hook=provider.GetRequiredService<Hook<R,L>>();var saved=request;var success=false;Exception? error=null;
                    try{
                        saved=hook.Before(saved,token);ArgumentNullException.ThrowIfNull(saved);token.ThrowIfCancellationRequested();
                        if(branch==0)provider.GetRequiredService<ZHandler<R,L>>().Handle(saved,token);else provider.GetRequiredService<AHandler<L,R>>().Handle(saved,token);
                        hook.After(saved,token);success=true;
                    }catch(Exception failure){error=failure;try{hook.OnException(saved,failure,token);}catch(Exception replacement){error=replacement;throw;}throw;}
                    finally{hook.Finally(saved,new(success,error),token);}
                }
            }
            private static void Branch<L,R>(IServiceProvider p,Request<L,R> r,int branch,CancellationToken token) where L:class,ILeft,IWrap,new() where R:class,IRight,new(){
                ArgumentNullException.ThrowIfNull(r);token.ThrowIfCancellationRequested();p.GetRequiredService<Logging<L,R>>().Invoke(r,new Next<L,R>(p,branch),token);
            }
            private static void Reference<L,R>(IServiceProvider p,Request<L,R> r,CancellationToken token) where L:class,ILeft,IWrap,new() where R:class,IRight,new(){
                ArgumentNullException.ThrowIfNull(r);token.ThrowIfCancellationRequested();Branch(p,r,0,token);Branch(p,r,1,token);
            }
            public static void OtherTypeCompileSentinel(Generated.Composition.IZendiator m)=>m.SendAll(new Request<OtherLeft,Right>(7,new Shared()));
            private static string Observe(bool generated,string mode){
                var p=new Probe(mode);using var outer=p.Outer;using var inner=p.Inner;
                IServiceCollection services=new ServiceCollection();services.AddSingleton(p);
                services.AddScoped(typeof(Logging<,>));services.AddScoped(typeof(ZHandler<,>));services.AddScoped(typeof(AHandler<,>));services.AddScoped(typeof(Hook<,>));var existing=services.Last();Register.Add(services);var count=services.Count;Register.Add(services);
                Probe.Check(services.Count==count&&services.Contains(existing),"TryAdd/repeat registration");
                foreach(var type in new[]{typeof(ZHandler<,>),typeof(AHandler<,>),typeof(Hook<,>),typeof(Logging<,>),typeof(Generated.Composition.IZendiator)})Probe.Check(services.Single(d=>d.ServiceType==type).Lifetime==ServiceLifetime.Scoped,"explicit scoped comparison services and facade");
                Probe.Check(!services.Any(d=>d.ServiceType==typeof(Generated.Composition.Zendiator))&&services.Single(d=>d.ServiceType==typeof(Generated.Composition.IZendiator)).ImplementationType==typeof(Generated.Composition.Zendiator),"single interface facade registration");
                var mismatchRejected=0;foreach(var reverseHandlers in new[]{true,false}){
                    var c=new ZendiatorConfiguration();c.Handlers(typeof(Request<,>),DispatchFamily.SendAllSync,reverseHandlers?new[]{typeof(AHandler<,>),typeof(ZHandler<,>)}:new[]{typeof(ZHandler<,>),typeof(AHandler<,>)});
                    if(reverseHandlers)c.Pipeline(typeof(Request<,>),DispatchFamily.SendAllSync,Stage.Wrapper(typeof(Logging<,>)),Stage.Hook(typeof(Hook<,>)));
                    else c.Pipeline(typeof(Request<,>),DispatchFamily.SendAllSync,Stage.Hook(typeof(Hook<,>)),Stage.Wrapper(typeof(Logging<,>)));
                    IServiceCollection rejected=new ServiceCollection();try{Generated.Composition.ZendiatorGeneratedRegistrar.Add(rejected,c.Snapshot());}catch(InvalidOperationException){mismatchRejected++;}Probe.Check(rejected.Count==0,"snapshot before descriptor addition");
                }Probe.Check(mismatchRejected==2,"both ordered snapshot lists");
                using var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});using var scope=provider.CreateScope();var sp=scope.ServiceProvider;
                var mediator=sp.GetRequiredService<Generated.Composition.IZendiator>();Probe.Check(ReferenceEquals(mediator,sp.GetRequiredService<Generated.Composition.IZendiator>()),"one facade");
                var data=new Shared();var request=new Request<Left,Right>(7,data);p.Original=request;p.Data=data;Probe.Ambient.Value="caller";Exception? caught=null;
                try{if(generated)mediator.SendAll(request,outer.Token);else Reference(sp,request,outer.Token);}catch(Exception error)when(ReferenceEquals(error,p.Failure)){caught=error;}
                var failed=mode is "finally" or "cancel";
                Probe.Check(failed?ReferenceEquals(caught,p.Failure):caught==null,"synchronous failure identity");
                Probe.Check(mode!="cancel"||caught is OperationCanceledException canceled&&canceled.CancellationToken==inner.Token&&inner.IsCancellationRequested&&!outer.IsCancellationRequested,"wrapper token replacement");
                var phases=new List<string>();
                for(var branch=1;branch<=(failed?1:2);branch++){
                    phases.Add("W.before");if(mode!="skip"||branch!=1)for(var call=0;call<(mode=="repeat"?2:1);call++){
                        phases.AddRange(new[]{"K.before",branch==1?"H:Z":"H:A",mode=="cancel"?"K.exception":"K.after","K.finally"});
                    }phases.Add("W.finally");
                }
                var handlers=mode=="repeat"?new[]{"Z","Z","A","A"}:mode=="skip"?new[]{"A"}:failed?new[]{"Z"}:new[]{"Z","A"};
                Probe.Check(p.Phases.SequenceEqual(phases)&&p.Handlers.SequenceEqual(handlers)&&p.NextCalls==handlers.Length&&p.Branch==(failed?1:2),"opaque multiplicity/branch order/entered unwind");
                Probe.Check(request.Value==7&&data.Value==(mode=="repeat"?223:mode=="skip"?103:13),"request isolation/shared effects");
                Probe.Check(p.Inputs.Count==p.NextCalls&&p.Effective.Count==p.NextCalls&&p.Inputs.Distinct(ReferenceEqualityComparer.Instance).Count()==p.NextCalls&&p.Effective.Distinct(ReferenceEqualityComparer.Instance).Count()==p.NextCalls,"fresh invocation slots");
                Probe.Check(p.Effective.All(r=>!ReferenceEquals(r,request)&&!p.Inputs.Contains(r,ReferenceEqualityComparer.Instance)),"inner replacement stays local");
                Probe.Check(Probe.Ambient.Value==("W.finally:"+(failed?1:2)),"native synchronous caller context");
                var types=mode=="skip"?new[]{typeof(Logging<Left,Right>),typeof(Hook<Right,Left>),typeof(AHandler<Left,Right>)}:failed?new[]{typeof(Logging<Left,Right>),typeof(Hook<Right,Left>),typeof(ZHandler<Right,Left>)}:new[]{typeof(Logging<Left,Right>),typeof(Hook<Right,Left>),typeof(ZHandler<Right,Left>),typeof(AHandler<Left,Right>)};
                Probe.Check(p.Created.Select(r=>r.GetType()).SequenceEqual(types),"closed DI keys/no unstarted receiver resolution");
                Probe.Check(p.Disposed.Count==0,"no dispatch cleanup");scope.Dispose();Probe.Check(p.Disposed.SequenceEqual(p.Created.AsEnumerable().Reverse()),"one reverse scope cleanup");
                return JsonSerializer.Serialize(new{mode,p.Branch,p.NextCalls,p.Handlers,p.Trace,p.AmbientSeen,shared=data.Value,callerContext=Probe.Ambient.Value,error=caught==null?null:"sentinel",canceled=inner.IsCancellationRequested,created=p.Created.Select(r=>r.GetType().ToString()),disposed=p.Disposed.Select(r=>r.GetType().ToString()),registrations=count,mismatchRejected});
            }
            public static string Run(){var rows=new List<object>();foreach(var mode in new[]{"repeat","skip","finally","cancel"}){
                var reference=Observe(false,mode);var generated=Observe(true,mode);Probe.Check(reference==generated,"reference/generated mismatch\n"+reference+"\n"+generated);rows.Add(JsonDocument.Parse(generated).RootElement.Clone());
            }return JsonSerializer.Serialize(new{pairs=4,dispatches=8,rows});}
        }
        }
        """;


    [Fact]
    public void OpenSendAllSync_void_wrapper_then_hook_preserves_opaque_branches_and_registration()
    {
        var source = OpenAllSyncVoidWrapperHookSource + OpenAllSyncVoidWrapperHookConsumer;
        var compilation = SyncCompilation(source, "OpenAllVoidWrapper_" + Guid.NewGuid().ToString("N"))
            .AddSyntaxTrees(new[] { OpenSendAllSyncSource(), OpenSendAllSyncVoidSource, OpenAllSyncWrapperHookSource + OpenAllSyncWrapperHookConsumer, OpenSyncWrapperHookSource(true) + OpenSyncWrapperHookConsumer(true) }.Select(s => CSharpSyntaxTree.ParseText(s, SyncParseOptions)));
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        var folder = OpenSendAllSyncFolder("void-wrapper-hook");
        SaveSendAllSync(folder, source, driver, output, diagnostics);
        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        AssertOpenSendAllSyncOutput(output, driver);
        foreach (var name in new[] { "OpenAllVoid.Generated.Composition", "OpenAllWrapper.Generated.Composition", "OpenSyncVoidWrapperHook.Generated.Mixed" })
            Assert.NotNull(output.GetTypeByMetadataName(name + ".IZendiator"));
        var api = output.GetTypeByMetadataName("OpenAllVoidWrapper.Generated.Composition.IZendiator")!;
        Assert.NotNull(api);
        Assert.False(api.IsGenericType);
        var mediator = output.GetTypeByMetadataName("OpenAllVoidWrapper.Generated.Composition.Zendiator")!;
        var publicMethod = Assert.Single(mediator.GetMembers("SendAll").OfType<IMethodSymbol>());
        var implementation = CanonicalRouteEntry(output, publicMethod);
        var branches = CanonicalEntryMethods(implementation, "Branch").ToArray();
        Assert.Equal(2, branches.Length);
        foreach (var method in new[] { Assert.Single(api.GetMembers("SendAll").OfType<IMethodSymbol>()), publicMethod }.Concat(branches))
        {
            Assert.True(method.ReturnsVoid);
            Assert.False(method.IsAsync);
            Assert.Equal(2, method.Arity);
            Assert.Equal(2, method.Parameters.Length);
            Assert.All(method.TypeParameters, p =>
            {
                Assert.True(p.HasReferenceTypeConstraint);
                Assert.True(p.HasConstructorConstraint);
            });
            foreach (var name in new[] { "ILeft", "IRight", "IWrap" })
                Assert.Contains(method.TypeParameters, p => p.ConstraintTypes.Any(t => t.Name == name));
        }
        var catalog = Assert.IsType<CompositionSelectionCatalog>(Assert.Single(driver.GetRunResult().Results.Single().TrackedSteps["CompositionSelectionModel"].SelectMany(s => s.Outputs)).Value);
        var route = Assert.Single(Assert.Single(catalog.Compositions, c => c.Key == "openAllVoidWrapper").Pipelines);
        Assert.Equal(FusionResultKind.Void, route.ResultKind);
        Assert.All(route.Handlers, h => Assert.Null(h.SyncEmission!.ResultName));
        var emission = Assert.Single(CanonicalSendAllSyncEmissionNormalizer.Create(catalog, CanonicalExecutionGraphNormalizer.Create(catalog)).Compositions, c => c.Registration.Namespace == "OpenAllVoidWrapper.Generated.Composition");
        Assert.Equal(2, emission.Wrappers.Count);
        Assert.Equal(2, emission.Branches.Count);
        Assert.All(emission.Wrappers, b =>
        {
            Assert.Equal(BoundDispatchFamily.SendSync, b.Family);
            Assert.Null(b.Registration.Handler.ResultName);
            Assert.Equal(route.SendGeneric, b.Registration.Generic);
            Assert.Equal(route.SendOpenRegistration, b.Registration.OpenRegistration);
        });
        Assert.Equal(new[] { "global::OpenAllVoidWrapper.ZHandler<,>", "global::OpenAllVoidWrapper.AHandler<,>" }, route.SendOpenRegistration!.HandlerDefinitionNames);
        Assert.Equal(new[] { "Wrapper", "Hook" }, route.SendOpenRegistration.Stages.Select(s => s.Kind));
        using var bytes = new MemoryStream();
        var emitted = output.Emit(bytes);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        if (folder != null)
        {
            File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
            for (var i = 0; i < compilation.SyntaxTrees.Count(); i++)
                File.WriteAllText(Path.Combine(folder, "input-" + i + ".cs"), compilation.SyntaxTrees.ElementAt(i).ToString());
        }
        bytes.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var runtime = assembly.GetType("OpenAllVoidWrapper.Entry")!.GetMethod("Run")!.CreateDelegate<Func<string>>()();
        if (folder != null)
            File.WriteAllText(Path.Combine(folder, "runtime.json"), runtime);
        using var json = JsonDocument.Parse(runtime);
        Assert.Equal(8, json.RootElement.GetProperty("dispatches").GetInt32());
    }

    [Fact]
    public void OpenSendAllSync_void_wrapper_then_hook_rejects_unproved_bindings_shapes_and_ownership()
    {
        const string wrapper = "Stage.Wrapper(typeof(Logging<,>))";
        const string hook = "Stage.Hook(typeof(Hook<,>))";
        var source = OpenAllSyncVoidWrapperHookSource + """
            namespace OpenAllVoidWrapper {
            public sealed class Unbound<X,Y,Unused>:ISendWrapper<Request<X,Y>>{public void Invoke<N>(Request<X,Y> r,N next,CancellationToken t) where N:ISendContinuation<Request<X,Y>> =>next.Invoke(r,t);}
            [ZendiatorHandler]public sealed class Missing<X,Y,Unused>:ISyncRequestHandler<Request<X,Y>>{public void Handle(Request<X,Y> r,CancellationToken t){}}
            [ZendiatorHandler]public sealed class StructHandler<X,Y>:ISyncRequestHandler<Request<X,Y>> where X:struct{public void Handle(Request<X,Y> r,CancellationToken t){}}
            public sealed class Partial<X,Y>:ISendRequestTransform<Request<X,Y>>{public Request<X,Y> Before(Request<X,Y> r,CancellationToken t)=>r;}
            public sealed class Factory<X,Y>:IBorrowedSessionFactory<Request<X,Y>,Logging<X,Y>> where X:class,IWrap where Y:class{public Logging<X,Y> Create(Request<X,Y> r,CancellationToken t)=>throw new NotSupportedException();}
            public sealed class HookFactory<X,Y>:IBorrowedSessionFactory<Request<X,Y>,Hook<Y,X>> where X:class where Y:class,IRight{public Hook<Y,X> Create(Request<X,Y> r,CancellationToken t)=>throw new NotSupportedException();}
            public sealed class HandlerFactory<X,Y>:IBorrowedSessionFactory<Request<X,Y>,AHandler<X,Y>> where X:class,ILeft,new() where Y:class{public AHandler<X,Y> Create(Request<X,Y> r,CancellationToken t)=>throw new NotSupportedException();}
            public sealed class AsyncLogging<X,Y>:IAsyncSendWrapper<Request<X,Y>>{public System.Threading.Tasks.ValueTask InvokeAsync<N>(Request<X,Y> r,N next,CancellationToken t) where N:IAsyncSendContinuation<Request<X,Y>> =>next.InvokeAsync(r,t);}
            public sealed record DualRequest<X,Y>:ISyncMultiRequest,ISyncMultiRequest<int>;
            [ZendiatorHandler]public sealed class DualVoid<X,Y>:ISyncRequestHandler<DualRequest<X,Y>>{public void Handle(DualRequest<X,Y> r,CancellationToken t){}}
            [ZendiatorHandler]public sealed class DualResult<X,Y>:ISyncRequestHandler<DualRequest<X,Y>,int>{public int Handle(DualRequest<X,Y> r,CancellationToken t)=>0;}
            public sealed class DualWrapper<X,Y>:ISendWrapper<DualRequest<X,Y>>{public void Invoke<N>(DualRequest<X,Y> r,N next,CancellationToken t) where N:ISendContinuation<DualRequest<X,Y>> =>next.Invoke(r,t);}
            public sealed class DualHook<X,Y>:ISendRequestTransform<DualRequest<X,Y>>,ISendSuccessObserver<DualRequest<X,Y>>,ISendExceptionObserver<DualRequest<X,Y>>,ISendFinallyObserver<DualRequest<X,Y>>{
                public DualRequest<X,Y> Before(DualRequest<X,Y> r,CancellationToken t)=>r;public void After(DualRequest<X,Y> r,CancellationToken t){}public void OnException(DualRequest<X,Y> r,Exception e,CancellationToken t){}public void Finally(DualRequest<X,Y> r,SendExitInfo e,CancellationToken t){}
            }
            }
            """;
        foreach (var (name, invalid) in new[]
        {
            ("unbound-wrapper", source.Replace(wrapper, "Stage.Wrapper(typeof(Unbound<,,>))")),
            ("unbound-handler", source.Replace("typeof(AHandler<,>)", "typeof(Missing<,,>)")),
            ("constraint", source.Replace("typeof(AHandler<,>)", "typeof(StructHandler<,>)")),
            ("reverse", source.Replace(wrapper + "," + hook, hook + "," + wrapper)),
            ("wrapper-session", source.Replace(wrapper, "Stage.Wrapper(typeof(Logging<,>),typeof(Factory<,>))")),
            ("hook-session", source.Replace(hook, "Stage.Hook(typeof(Hook<,>),typeof(HookFactory<,>))")),
            ("handler-session", source.Replace("typeof(AHandler<,>)", "HandlerEntry.Session(typeof(AHandler<,>),typeof(HandlerFactory<,>))")),
            ("partial", source.Replace(hook, "Stage.Hook(typeof(Partial<,>))")),
            ("wrapper-only", source.Replace(wrapper + "," + hook, wrapper)),
            ("async", source.Replace(wrapper, "Stage.Wrapper(typeof(AsyncLogging<,>))")),
            ("wrong-contract", source.Replace(wrapper, "Stage.Wrapper(typeof(Hook<,>))")),
            ("mixed", source.Replace("typeof(Request<,>)", "typeof(DualRequest<,>)").Replace("typeof(ZHandler<,>),typeof(AHandler<,>)", "typeof(DualVoid<,>),typeof(DualResult<,>)").Replace("typeof(Logging<,>)", "typeof(DualWrapper<,>)").Replace("typeof(Hook<,>)", "typeof(DualHook<,>)"))
        })
        {
            var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(invalid, "OpenAllVoidWrapperRejected_" + name + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
            SaveSendAllSync(OpenSendAllSyncFolder("void-wrapper-hook-" + name), invalid, driver, output, diagnostics);
            Assert.NotEmpty(diagnostics);
            Assert.All(diagnostics, d => Assert.Equal("ZEN0035", d.Id));
            if (name is "unbound-wrapper" or "unbound-handler")
            {
                var anchor = name == "unbound-wrapper" ? "Stage.Wrapper(typeof(Unbound<,,>))" : "typeof(Missing<,,>)";
                var error = Assert.Single(diagnostics, d => invalid.Substring(d.Location.SourceSpan.Start, d.Location.SourceSpan.Length) == anchor);
                Assert.Empty(error.AdditionalLocations);
            }
            Assert.Null(output.GetTypeByMetadataName("OpenAllVoidWrapper.Generated.Composition.IZendiator"));
            Assert.Null(output.GetTypeByMetadataName("OpenAllVoidWrapper.Generated.Composition.ZendiatorGeneratedRegistrar"));
            Assert.DoesNotContain(driver.GetRunResult().Results.Single().GeneratedSources, s => s.HintName.EndsWith("Interceptors.g.cs", StringComparison.Ordinal));
            Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        }
    }
}
