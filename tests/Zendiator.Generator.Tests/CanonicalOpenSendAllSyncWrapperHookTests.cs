using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using Zendiator.SourceGenerator;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private const string OpenAllSyncWrapperHookSource = """
        using System;using System.Collections.Generic;using System.Linq;using System.Threading;using System.Text.Json;
        using Microsoft.Extensions.DependencyInjection;using Zendiator;using Zendiator.DependencyInjection;
        namespace OpenAllWrapper {
        public interface ILeft;public interface IRight;public interface IWrap;
        public sealed class Left:ILeft,IWrap;public sealed class Right:IRight;public sealed class OtherLeft:ILeft,IWrap;
        public sealed class Shared {public int Value=3;}
        public sealed record Request<L,R>(int Value,Shared Shared):ISyncMultiRequest<int>;
        public sealed class Probe {
            public static readonly AsyncLocal<string?> Ambient=new();
            public readonly CancellationTokenSource Outer=new(),Inner=new();public readonly string Mode;public readonly Exception Failure;
            public object Original=null!,WrapperInput=null!,Saved=null!;public Shared Data=null!;public int Branch,Call,NextCalls;
            public int[] Destination=null!,Initial=null!;
            public readonly List<string> Trace=new(),Phases=new(),Handlers=new();public readonly List<string?> AmbientSeen=new();
            public readonly List<object> Created=new(),Disposed=new(),Inputs=new(),Effective=new();
            public Probe(string mode){Mode=mode;Failure=mode=="cancel"?new OperationCanceledException("inner token",Inner.Token):new FormatException("first finally");}
            public static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
            public void Resolve(object receiver,string role){Check(Destination.SequenceEqual(Initial),"no early Span commit at resolution");Created.Add(receiver);Trace.Add("resolve:"+role);}
            public void Hit(string phase,object request,int value,Shared shared,CancellationToken token){
                var outer=phase.StartsWith("W.",StringComparison.Ordinal);
                Check(token==(outer?Outer.Token:Inner.Token)&&ReferenceEquals(request,outer?Original:phase=="K.before"?WrapperInput:Saved)&&ReferenceEquals(shared,Data),"per-invocation request/token/shared reference");
                Check(Destination.SequenceEqual(Initial),"no partial Span write through Finally");
                Phases.Add(phase);Trace.Add(phase+":"+Branch+":"+Call+":"+value+":"+shared.Value+":"+(outer?"outer":"inner"));AmbientSeen.Add(Ambient.Value);Ambient.Value=phase+":"+Branch;
            }
            public int Body(string role,object request,int value,Shared shared,CancellationToken token){
                Hit("H:"+role,request,value,shared,token);Handlers.Add(role);var result=value+shared.Value+(role=="Z"?1000:2000);shared.Value+=role=="Z"?10:100;
                if(Mode=="cancel"&&Branch==1){Inner.Cancel();throw Failure;}return result;
            }
        }
        [ZendiatorHandler]public sealed class ZHandler<X,Y>:ISyncRequestHandler<Request<Y,X>,int>,IDisposable where X:class,new() where Y:class {
            private readonly Probe p;public ZHandler(Probe p){this.p=p;p.Resolve(this,"Z");}
            public int Handle(Request<Y,X> r,CancellationToken token)=>p.Body("Z",r,r.Value,r.Shared,token);
            public void Dispose(){p.Disposed.Add(this);}
        }
        [ZendiatorHandler]public sealed class AHandler<X,Y>:ISyncRequestHandler<Request<X,Y>,int>,IDisposable where X:class,ILeft,new() where Y:class {
            private readonly Probe p;public AHandler(Probe p){this.p=p;p.Resolve(this,"A");}
            public int Handle(Request<X,Y> r,CancellationToken token)=>p.Body("A",r,r.Value,r.Shared,token);
            public void Dispose(){p.Disposed.Add(this);}
        }
        public sealed class Hook<X,Y>:ISendRequestTransform<Request<Y,X>>,ISendResponseTransform<Request<Y,X>,int>,ISendExceptionObserver<Request<Y,X>>,ISendFinallyObserver<Request<Y,X>>,IDisposable where X:class,IRight where Y:class {
            private readonly Probe p;public Hook(Probe p){this.p=p;p.Resolve(this,"K");}
            public Request<Y,X> Before(Request<Y,X> r,CancellationToken token){p.Hit("K.before",r,r.Value,r.Shared,token);var saved=r with{Value=r.Value+1};p.Saved=saved;p.Effective.Add(saved);return saved;}
            public int After(Request<Y,X> r,int value,CancellationToken token){p.Hit("K.after",r,r.Value,r.Shared,token);return value+100;}
            public void OnException(Request<Y,X> r,Exception error,CancellationToken token){Probe.Check(ReferenceEquals(error,p.Failure),"observer identity");p.Hit("K.exception",r,r.Value,r.Shared,token);}
            public void Finally(Request<Y,X> r,SendExitInfo exit,CancellationToken token){
                var failed=p.Mode=="cancel"&&p.Branch==1;Probe.Check(exit.Success!=failed&&ReferenceEquals(exit.Error,failed?p.Failure:null),"entered outcome");
                p.Hit("K.finally",r,r.Value,r.Shared,token);if(p.Mode=="finally"&&p.Branch==1)throw p.Failure;
            }
            public void Dispose(){p.Disposed.Add(this);}
        }
        public sealed class Logging<X,Y>:ISendWrapper<Request<X,Y>,int>,IDisposable where X:class,IWrap where Y:class {
            private readonly Probe p;public Logging(Probe p){this.p=p;p.Resolve(this,"W");}
            public int Invoke<N>(Request<X,Y> r,N next,CancellationToken token) where N:ISendContinuation<Request<X,Y>,int>{
                p.Branch++;p.Call=0;p.Hit("W.before",r,r.Value,r.Shared,token);Probe.Check(r.Value==7,"every branch starts from original request");
                try{
                    if(p.Mode=="skip"&&p.Branch==1)return -5;
                    var result=0;for(var i=0;i<(p.Mode=="repeat"?2:1);i++){
                        p.Call=i+1;p.NextCalls++;var input=r with{Value=r.Value+10+i};p.WrapperInput=input;p.Inputs.Add(input);result+=next.Invoke(input,p.Inner.Token);
                    }return result+10000;
                }finally{p.Hit("W.finally",r,r.Value,r.Shared,token);}
            }
            public void Dispose(){p.Disposed.Add(this);}
        }
        [ZendiatorComposition("openAllWrapper")]public sealed class Composition;
        public static class Register {
            public static IServiceCollection Add(IServiceCollection s)=>s.AddZendiator<Composition>(c=>{
                c.Handlers(typeof(Request<,>),DispatchFamily.SendAllSync,typeof(ZHandler<,>),typeof(AHandler<,>));
                c.Pipeline(typeof(Request<,>),DispatchFamily.SendAllSync,Stage.Wrapper(typeof(Logging<,>)),Stage.Hook(typeof(Hook<,>)));
            });
        }
        }
        """;

    private const string OpenAllSyncWrapperHookConsumer = """
        namespace OpenAllWrapper {
        public static class Entry {
            // Independent orchestration; the user wrapper owns its continuation calls.
            private readonly struct Next<L,R>(IServiceProvider provider,int branch):ISendContinuation<Request<L,R>,int> where L:class,ILeft,IWrap,new() where R:class,IRight,new(){
                public int Invoke(Request<L,R> request,CancellationToken token){
                    ArgumentNullException.ThrowIfNull(request);token.ThrowIfCancellationRequested();var hook=provider.GetRequiredService<Hook<R,L>>();var saved=request;var success=false;Exception? error=null;
                    try{
                        saved=hook.Before(saved,token);ArgumentNullException.ThrowIfNull(saved);token.ThrowIfCancellationRequested();
                        var value=branch==0?provider.GetRequiredService<ZHandler<R,L>>().Handle(saved,token):provider.GetRequiredService<AHandler<L,R>>().Handle(saved,token);
                        value=hook.After(saved,value,token);success=true;return value;
                    }catch(Exception failure){error=failure;try{hook.OnException(saved,failure,token);}catch(Exception replacement){error=replacement;throw;}throw;}
                    finally{hook.Finally(saved,new(success,error),token);}
                }
            }
            private static int Branch<L,R>(IServiceProvider p,Request<L,R> r,int branch,CancellationToken token) where L:class,ILeft,IWrap,new() where R:class,IRight,new(){
                ArgumentNullException.ThrowIfNull(r);token.ThrowIfCancellationRequested();return p.GetRequiredService<Logging<L,R>>().Invoke(r,new Next<L,R>(p,branch),token);
            }
            private static int[] Reference<L,R>(IServiceProvider p,Request<L,R> r,CancellationToken token) where L:class,ILeft,IWrap,new() where R:class,IRight,new(){
                ArgumentNullException.ThrowIfNull(r);token.ThrowIfCancellationRequested();return new[]{Branch(p,r,0,token),Branch(p,r,1,token)};
            }
            private static int ReferenceSpan<L,R>(IServiceProvider p,Request<L,R> r,Span<int> destination,CancellationToken token) where L:class,ILeft,IWrap,new() where R:class,IRight,new(){
                ArgumentNullException.ThrowIfNull(r);if(destination.Length<2)throw new ArgumentException("Destination is too short.",nameof(destination));var values=Reference(p,r,token);values.AsSpan().CopyTo(destination);return values.Length;
            }
            public static int[] OtherTypeCompileSentinel(Generated.Composition.IZendiator m)=>m.SendAll(new Request<OtherLeft,Right>(7,new Shared()));
            private static string Observe(bool generated,string mode){
                var p=new Probe(mode);using var outer=p.Outer;using var inner=p.Inner;
                p.Destination=mode=="short"?new[]{701}:new[]{701,702,703};p.Initial=p.Destination.ToArray();IServiceCollection services=new ServiceCollection();services.AddSingleton(p);
                services.AddScoped(typeof(Hook<,>));var existing=services.Last();Register.Add(services);var count=services.Count;Register.Add(services);
                Probe.Check(services.Count(d=>d.ServiceType.DeclaringType!=typeof(Generated.Composition.Zendiator))==7&&services.Count==count&&services.Contains(existing),"TryAdd/repeat registration");
                foreach(var type in new[]{typeof(ZHandler<,>),typeof(AHandler<,>),typeof(Hook<,>),typeof(Logging<,>),typeof(Generated.Composition.IZendiator),typeof(Generated.Composition.Zendiator)})Probe.Check(services.Single(d=>d.ServiceType==type).Lifetime==ServiceLifetime.Scoped,"scoped receivers/facade");
                var mismatchRejected=0;foreach(var reverseHandlers in new[]{true,false}){
                    var c=new ZendiatorConfiguration();c.Handlers(typeof(Request<,>),DispatchFamily.SendAllSync,reverseHandlers?new[]{typeof(AHandler<,>),typeof(ZHandler<,>)}:new[]{typeof(ZHandler<,>),typeof(AHandler<,>)});
                    if(reverseHandlers)c.Pipeline(typeof(Request<,>),DispatchFamily.SendAllSync,Stage.Wrapper(typeof(Logging<,>)),Stage.Hook(typeof(Hook<,>)));
                    else c.Pipeline(typeof(Request<,>),DispatchFamily.SendAllSync,Stage.Hook(typeof(Hook<,>)),Stage.Wrapper(typeof(Logging<,>)));
                    IServiceCollection rejected=new ServiceCollection();try{Generated.Composition.ZendiatorGeneratedRegistrar.Add(rejected,c.Snapshot());}catch(InvalidOperationException){mismatchRejected++;}Probe.Check(rejected.Count==0,"snapshot before descriptor addition");
                }Probe.Check(mismatchRejected==2,"both ordered snapshot lists");
                using var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});using var scope=provider.CreateScope();var sp=scope.ServiceProvider;
                var mediator=sp.GetRequiredService<Generated.Composition.IZendiator>();Probe.Check(ReferenceEquals(mediator,sp.GetRequiredService<Generated.Composition.Zendiator>()),"one facade");
                var data=new Shared();var request=new Request<Left,Right>(7,data);p.Original=request;p.Data=data;Probe.Ambient.Value="caller";Exception? caught=null;int[]? values=null;var copied=-1;
                try{
                    if(mode is "repeat" or "skip")values=generated?mediator.SendAll(request,outer.Token):Reference(sp,request,outer.Token);
                    else copied=generated?mediator.SendAll(request,p.Destination.AsSpan(),outer.Token):ReferenceSpan(sp,request,p.Destination.AsSpan(),outer.Token);
                }catch(Exception error)when(ReferenceEquals(error,p.Failure)||mode=="short"&&error is ArgumentException {ParamName:"destination"}){caught=error;}
                var failed=mode is "finally" or "cancel";var shortSpan=mode=="short";
                Probe.Check(failed?ReferenceEquals(caught,p.Failure):shortSpan?caught is ArgumentException:caught==null,"synchronous failure identity");
                Probe.Check(mode!="cancel"||caught is OperationCanceledException canceled&&canceled.CancellationToken==inner.Token&&inner.IsCancellationRequested&&!outer.IsCancellationRequested,"wrapper token replacement");
                var phases=new List<string>();
                if(!shortSpan)for(var branch=1;branch<=(failed?1:2);branch++){
                    phases.Add("W.before");if(mode!="skip"||branch!=1)for(var call=0;call<(mode=="repeat"?2:1);call++){
                        phases.AddRange(new[]{"K.before",branch==1?"H:Z":"H:A",mode=="cancel"?"K.exception":"K.after","K.finally"});
                    }phases.Add("W.finally");
                }
                var handlers=shortSpan?Array.Empty<string>():mode=="repeat"?new[]{"Z","Z","A","A"}:mode=="skip"?new[]{"A"}:failed?new[]{"Z"}:new[]{"Z","A"};
                Probe.Check(p.Phases.SequenceEqual(phases)&&p.Handlers.SequenceEqual(handlers)&&p.NextCalls==handlers.Length&&p.Branch==(shortSpan?0:failed?1:2),"opaque multiplicity/branch order/entered unwind");
                Probe.Check(request.Value==7&&data.Value==(shortSpan?3:mode=="repeat"?223:mode=="skip"?103:failed?13:113),"request isolation/shared effects");
                Probe.Check(p.Inputs.Count==p.NextCalls&&p.Effective.Count==p.NextCalls&&p.Inputs.Distinct(ReferenceEqualityComparer.Instance).Count()==p.NextCalls&&p.Effective.Distinct(ReferenceEqualityComparer.Instance).Count()==p.NextCalls,"fresh invocation slots");
                Probe.Check(p.Effective.All(r=>!ReferenceEquals(r,request)&&!p.Inputs.Contains(r,ReferenceEqualityComparer.Instance)),"inner replacement stays local");
                Probe.Check(mode=="repeat"?values!.SequenceEqual(new[]{12253,14383}):mode=="skip"?values!.SequenceEqual(new[]{-5,12121}):values==null,"ordered response aggregation");
                Probe.Check(copied==(mode=="span"?2:-1)&&p.Destination.SequenceEqual(mode=="span"?new[]{11121,12131,703}:p.Initial),"atomic Span commit/untouched tail/short precheck");
                Probe.Check(Probe.Ambient.Value==(shortSpan?"caller":"W.finally:"+(failed?1:2)),"native synchronous caller context");
                var types=shortSpan?Array.Empty<Type>():mode=="skip"?new[]{typeof(Logging<Left,Right>),typeof(Hook<Right,Left>),typeof(AHandler<Left,Right>)}:failed?new[]{typeof(Logging<Left,Right>),typeof(Hook<Right,Left>),typeof(ZHandler<Right,Left>)}:new[]{typeof(Logging<Left,Right>),typeof(Hook<Right,Left>),typeof(ZHandler<Right,Left>),typeof(AHandler<Left,Right>)};
                Probe.Check(p.Created.Select(r=>r.GetType()).SequenceEqual(types),"closed DI keys/no unstarted receiver resolution");
                foreach(var receiver in p.Created)Probe.Check(ReferenceEquals(receiver,sp.GetRequiredService(receiver.GetType())),"borrowed scoped receiver");
                Probe.Check(p.Disposed.Count==0,"no dispatch cleanup");scope.Dispose();Probe.Check(p.Disposed.SequenceEqual(p.Created.AsEnumerable().Reverse()),"one reverse scope cleanup");
                return JsonSerializer.Serialize(new{mode,values,copied,destination=p.Destination,p.Branch,p.NextCalls,p.Handlers,p.Trace,p.AmbientSeen,shared=data.Value,callerContext=Probe.Ambient.Value,error=caught==null?null:shortSpan?"short":"sentinel",canceled=inner.IsCancellationRequested,created=p.Created.Select(r=>r.GetType().ToString()),disposed=p.Disposed.Select(r=>r.GetType().ToString()),registrations=count,mismatchRejected});
            }
            public static string Run(){var rows=new List<object>();foreach(var mode in new[]{"repeat","span","skip","finally","cancel","short"}){
                var reference=Observe(false,mode);var generated=Observe(true,mode);Probe.Check(reference==generated,"reference/generated mismatch\n"+reference+"\n"+generated);rows.Add(JsonDocument.Parse(generated).RootElement.Clone());
            }return JsonSerializer.Serialize(new{pairs=6,dispatches=12,rows});}
        }
        }
        """;

    [Fact]
    public void OpenSendAllSync_wrapper_then_hook_preserves_opaque_branches_atomic_Span_and_registration()
    {
        var source = OpenAllSyncWrapperHookSource + OpenAllSyncWrapperHookConsumer;
        var closedAll = AllWrapperSource(true); closedAll = closedAll[closedAll.IndexOf("namespace Closed.", StringComparison.Ordinal)..];
        var compilation = SyncCompilation(source, "OpenAllWrapper_" + Guid.NewGuid().ToString("N"))
            .AddSyntaxTrees(new[] { OpenSendAllSyncSource(), OpenSendAllSyncVoidSource, OpenSyncWrapperHookSource() + OpenSyncWrapperHookConsumer(), closedAll }.Select(s => CSharpSyntaxTree.ParseText(s, SyncParseOptions)));
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        var folder = OpenSendAllSyncFolder("wrapper-hook"); SaveSendAllSync(folder, source, driver, output, diagnostics);
        Assert.DoesNotContain(diagnostics, d => d.Location.SourceTree == compilation.SyntaxTrees.First());
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        AssertOpenSendAllSyncOutput(output, driver); Assert.NotNull(output.GetTypeByMetadataName("OpenAllVoid.Generated.Composition.IZendiator"));
        Assert.NotNull(output.GetTypeByMetadataName("OpenSyncWrapperHook.Generated.Mixed.IZendiator")); AssertAllWrapperRegistration(output);
        var api = output.GetTypeByMetadataName("OpenAllWrapper.Generated.Composition.IZendiator")!; Assert.NotNull(api); Assert.False(api.IsGenericType);
        var mediator = output.GetTypeByMetadataName("OpenAllWrapper.Generated.Composition.Zendiator")!;
        var methods = api.GetMembers("SendAll").OfType<IMethodSymbol>().ToArray(); Assert.Equal(2, methods.Length);
        Assert.Single(methods, m => m.ReturnType is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Int32 });
        Assert.Single(methods, m => m.ReturnType.SpecialType == SpecialType.System_Int32 && m.Parameters[1].Type.Name == "Span");
        var publicMethod = Assert.Single(mediator.GetMembers("SendAll").OfType<IMethodSymbol>(), m => m.ReturnType is IArrayTypeSymbol);
        var implementation = CanonicalRouteEntry(output, publicMethod).ContainingType;
        var branches = implementation.GetMembers().OfType<IMethodSymbol>().Where(m => m.Name.StartsWith("Branch", StringComparison.Ordinal)).ToArray(); Assert.Equal(2, branches.Length);
        foreach (var method in methods.Concat(branches))
        {
            Assert.False(method.IsAsync); Assert.Equal(2, method.Arity); Assert.All(method.TypeParameters, p => { Assert.True(p.HasReferenceTypeConstraint); Assert.True(p.HasConstructorConstraint); });
            foreach (var name in new[] { "ILeft", "IRight", "IWrap" }) Assert.Contains(method.TypeParameters, p => p.ConstraintTypes.Any(t => t.Name == name));
        }
        var catalog = Assert.IsType<CompositionSelectionCatalog>(Assert.Single(driver.GetRunResult().Results.Single().TrackedSteps["CompositionSelectionModel"].SelectMany(s => s.Outputs)).Value);
        var route = Assert.Single(Assert.Single(catalog.Compositions, c => c.Key == "openAllWrapper").Pipelines);
        var emission = Assert.Single(CanonicalSendAllSyncEmissionNormalizer.Create(catalog, CanonicalExecutionGraphNormalizer.Create(catalog)).Compositions, c => c.Registration.Namespace == "OpenAllWrapper.Generated.Composition");
        Assert.Equal(2, emission.Wrappers.Count); Assert.Equal(2, emission.Branches.Count);
        Assert.All(emission.Wrappers, b => { Assert.Equal(BoundDispatchFamily.SendSync, b.Family); Assert.Equal(route.SendGeneric, b.Registration.Generic); Assert.Equal(route.SendOpenRegistration, b.Registration.OpenRegistration); });
        Assert.Equal(new[] { "global::OpenAllWrapper.ZHandler<,>", "global::OpenAllWrapper.AHandler<,>" }, route.SendOpenRegistration!.HandlerDefinitionNames);
        Assert.Equal(new[] { "Wrapper", "Hook" }, route.SendOpenRegistration.Stages.Select(s => s.Kind));
        using var bytes = new MemoryStream(); var emitted = output.Emit(bytes); Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        if (folder != null)
        {
            File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
            for (var i = 0; i < compilation.SyntaxTrees.Count(); i++) File.WriteAllText(Path.Combine(folder, "input-" + i + ".cs"), compilation.SyntaxTrees.ElementAt(i).ToString());
        }
        bytes.Position = 0; var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var runtime = assembly.GetType("OpenAllWrapper.Entry")!.GetMethod("Run")!.CreateDelegate<Func<string>>()();
        if (folder != null) File.WriteAllText(Path.Combine(folder, "runtime.json"), runtime);
        using var json = JsonDocument.Parse(runtime); Assert.Equal(12, json.RootElement.GetProperty("dispatches").GetInt32());
    }

    [Fact]
    public void OpenSendAllSync_wrapper_then_hook_rejects_unproved_bindings_shapes_and_ownership()
    {
        const string wrapper = "Stage.Wrapper(typeof(Logging<,>))"; const string hook = "Stage.Hook(typeof(Hook<,>))";
        var source = OpenAllSyncWrapperHookSource + """
            namespace OpenAllWrapper {
            public sealed class Unbound<X,Y,Unused>:ISendWrapper<Request<X,Y>,int>{public int Invoke<N>(Request<X,Y> r,N next,CancellationToken t) where N:ISendContinuation<Request<X,Y>,int> =>next.Invoke(r,t);}
            [ZendiatorHandler]public sealed class Missing<X,Y,Unused>:ISyncRequestHandler<Request<X,Y>,int>{public int Handle(Request<X,Y> r,CancellationToken t)=>0;}
            [ZendiatorHandler]public sealed class StructHandler<X,Y>:ISyncRequestHandler<Request<X,Y>,int> where X:struct{public int Handle(Request<X,Y> r,CancellationToken t)=>0;}
            [ZendiatorHandler]public sealed class Third<X,Y>:ISyncRequestHandler<Request<X,Y>,int>{public int Handle(Request<X,Y> r,CancellationToken t)=>0;}
            public sealed class Partial<X,Y>:ISendRequestTransform<Request<X,Y>>{public Request<X,Y> Before(Request<X,Y> r,CancellationToken t)=>r;}
            public sealed class Factory<X,Y>:IBorrowedSessionFactory<Request<X,Y>,Logging<X,Y>> where X:class,IWrap where Y:class{public Logging<X,Y> Create(Request<X,Y> r,CancellationToken t)=>throw new NotSupportedException();}
            public sealed class HookFactory<X,Y>:IBorrowedSessionFactory<Request<X,Y>,Hook<Y,X>> where X:class where Y:class,IRight{public Hook<Y,X> Create(Request<X,Y> r,CancellationToken t)=>throw new NotSupportedException();}
            public sealed class HandlerFactory<X,Y>:IBorrowedSessionFactory<Request<X,Y>,AHandler<X,Y>> where X:class,ILeft,new() where Y:class{public AHandler<X,Y> Create(Request<X,Y> r,CancellationToken t)=>throw new NotSupportedException();}
            public sealed class AsyncLogging<X,Y>:IAsyncSendWrapper<Request<X,Y>,int>{public System.Threading.Tasks.ValueTask<int> InvokeAsync<N>(Request<X,Y> r,N next,CancellationToken t) where N:IAsyncSendContinuation<Request<X,Y>,int> =>next.InvokeAsync(r,t);}
            }
            """;
        var voidSource = OpenSendAllSyncVoidSource.Replace("Stage.Hook(typeof(Hook<,>))", "Stage.Wrapper(typeof(Wrapper<,>)),Stage.Hook(typeof(Hook<,>))") + "namespace OpenAllVoid {public sealed class Wrapper<X,Y>:ISendWrapper<Request<X,Y>>{public void Invoke<N>(Request<X,Y> r,N next,CancellationToken t) where N:ISendContinuation<Request<X,Y>> =>next.Invoke(r,t);}}";
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
            ("two-wrappers", source.Replace(wrapper, wrapper + "," + wrapper)),
            ("async", source.Replace(wrapper, "Stage.Wrapper(typeof(AsyncLogging<,>))")),
            ("wrong-contract", source.Replace(wrapper, "Stage.Wrapper(typeof(Hook<,>))")),
            ("three", source.Replace("typeof(ZHandler<,>),typeof(AHandler<,>)", "typeof(ZHandler<,>),typeof(AHandler<,>),typeof(Third<,>)"))
        })
        {
            var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(invalid, "OpenAllWrapperRejected_" + name + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
            SaveSendAllSync(OpenSendAllSyncFolder("wrapper-hook-" + name), invalid, driver, output, diagnostics);
            Assert.NotEmpty(diagnostics); Assert.All(diagnostics, d => Assert.Equal("ZEN0035", d.Id));
            if (name is "unbound-wrapper" or "unbound-handler")
            {
                var anchor = name == "unbound-wrapper" ? "Stage.Wrapper(typeof(Unbound<,,>))" : "typeof(Missing<,,>)";
                var error = Assert.Single(diagnostics, d => invalid.Substring(d.Location.SourceSpan.Start, d.Location.SourceSpan.Length) == anchor); Assert.Empty(error.AdditionalLocations);
            }
            Assert.Null(output.GetTypeByMetadataName("OpenAllWrapper.Generated.Composition.IZendiator")); Assert.Null(output.GetTypeByMetadataName("OpenAllWrapper.Generated.Composition.ZendiatorGeneratedRegistrar"));
            Assert.DoesNotContain(driver.GetRunResult().Results.Single().GeneratedSources, s => s.HintName.EndsWith("Interceptors.g.cs", StringComparison.Ordinal));
            Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        }
        var accepted = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(voidSource, "OpenAllWrapperVoidAccepted_" + Guid.NewGuid().ToString("N")), out var voidOutput, out var voidDiagnostics);
        SaveSendAllSync(OpenSendAllSyncFolder("wrapper-hook-native-void"), voidSource, accepted, voidOutput, voidDiagnostics);
        Assert.Empty(voidDiagnostics); Assert.Empty(voidOutput.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        var api = voidOutput.GetTypeByMetadataName("OpenAllVoid.Generated.Composition.IZendiator"); Assert.NotNull(api);
        Assert.True(Assert.Single(api.GetMembers("SendAll").OfType<IMethodSymbol>()).ReturnsVoid);
        Assert.NotNull(voidOutput.GetTypeByMetadataName("OpenAllVoid.Generated.Composition.ZendiatorGeneratedRegistrar"));
    }
}
