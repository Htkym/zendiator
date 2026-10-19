using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using Zendiator.SourceGenerator;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private const string OpenSendAllSyncVoidSource = """
        using System;using System.Collections.Generic;using System.Linq;using System.Threading;using System.Text.Json;
        using Microsoft.Extensions.DependencyInjection;using Zendiator;using Zendiator.DependencyInjection;
        namespace OpenAllVoid {
        public interface ILeft;public interface IRight;
        public sealed class Left:ILeft;public sealed class Right:IRight;public sealed class OtherLeft:ILeft;
        public sealed class Shared {public int Value=3;}
        public sealed record Request<L,R>(int Value,Shared Shared):ISyncMultiRequest;
        public sealed class Probe {
            public static readonly AsyncLocal<string?> Ambient=new();
            public readonly CancellationTokenSource Cancellation=new();public readonly string Mode;public readonly Exception Failure;
            public object Original=null!,Saved=null!;public Shared Data=null!;public int Branch;
            public readonly List<string> Trace=new(),Handlers=new();public readonly List<string?> AmbientSeen=new();
            public readonly List<object> Created=new(),Disposed=new(),SavedRequests=new();
            public Probe(string mode){Mode=mode;Failure=mode=="cancel"?new OperationCanceledException("first branch",Cancellation.Token):new FormatException("first finally");}
            public static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
            public void Resolve(object receiver,string role){Created.Add(receiver);Trace.Add("resolve:"+role);}
            public void Hit(string phase,object request,int value,Shared shared,CancellationToken token){
                Check(token==Cancellation.Token&&ReferenceEquals(request,phase=="before"?Original:Saved)&&ReferenceEquals(shared,Data),"branch request/token/shared reference");
                Trace.Add(phase+":"+Branch+":"+value+":"+shared.Value);AmbientSeen.Add(Ambient.Value);Ambient.Value=phase+":"+Branch;
            }
            public void Body(string role,object request,int value,Shared shared,CancellationToken token){
                Hit("handler:"+role,request,value,shared,token);Handlers.Add(role);shared.Value+=role=="Z"?10:100;
                if(Mode=="cancel"&&Branch==1){Cancellation.Cancel();throw Failure;}
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
            public Request<Y,X> Before(Request<Y,X> r,CancellationToken token){
                p.Branch++;p.Hit("before",r,r.Value,r.Shared,token);Probe.Check(r.Value==7,"each branch starts from original");
                var saved=new Request<Y,X>(r.Value+1,r.Shared);p.Saved=saved;p.SavedRequests.Add(saved);return saved;
            }
            public void After(Request<Y,X> r,CancellationToken token)=>p.Hit("after",r,r.Value,r.Shared,token);
            public void OnException(Request<Y,X> r,Exception error,CancellationToken token){Probe.Check(ReferenceEquals(error,p.Failure),"observer identity");p.Hit("exception",r,r.Value,r.Shared,token);}
            public void Finally(Request<Y,X> r,SendExitInfo exit,CancellationToken token){
                var failed=p.Mode=="cancel"&&p.Branch==1;Probe.Check(exit.Success!=failed&&ReferenceEquals(exit.Error,failed?p.Failure:null),"finally outcome");
                p.Hit("finally:"+exit.Success,r,r.Value,r.Shared,token);if(p.Mode=="finally"&&p.Branch==1)throw p.Failure;
            }
            public void Dispose(){p.Disposed.Add(this);}
        }
        [ZendiatorComposition("openAllVoid")]public sealed class Composition;
        public static class Register {
            public static IServiceCollection Add(IServiceCollection s)=>s.AddZendiator<Composition>(c=>{
                c.Handlers(typeof(Request<,>),DispatchFamily.SendAllSync,typeof(ZHandler<,>),typeof(AHandler<,>));
                c.Pipeline(typeof(Request<,>),DispatchFamily.SendAllSync,Stage.Hook(typeof(Hook<,>)));
            });
        }
        }
        """;

    private const string OpenSendAllSyncVoidConsumer = """
        namespace OpenAllVoid {
        public static class Entry {
            // Independent ordered branch protocol using the same user callbacks.
            private static void Branch<L,R>(IServiceProvider p,Request<L,R> request,int branch,CancellationToken token) where L:class,ILeft,new() where R:class,IRight,new(){
                ArgumentNullException.ThrowIfNull(request);token.ThrowIfCancellationRequested();var hook=p.GetRequiredService<Hook<R,L>>();var saved=request;var success=false;Exception? error=null;
                try{
                    saved=hook.Before(saved,token);ArgumentNullException.ThrowIfNull(saved);token.ThrowIfCancellationRequested();
                    if(branch==0)p.GetRequiredService<ZHandler<R,L>>().Handle(saved,token);else p.GetRequiredService<AHandler<L,R>>().Handle(saved,token);
                    hook.After(saved,token);success=true;
                }catch(Exception failure){error=failure;try{hook.OnException(saved,failure,token);}catch(Exception replacement){error=replacement;throw;}throw;}
                finally{hook.Finally(saved,new(success,error),token);}
            }
            private static void Reference<L,R>(IServiceProvider p,Request<L,R> request,CancellationToken token) where L:class,ILeft,new() where R:class,IRight,new(){
                ArgumentNullException.ThrowIfNull(request);token.ThrowIfCancellationRequested();Branch(p,request,0,token);Branch(p,request,1,token);
            }
            public static void OtherTypeCompileSentinel(Generated.Composition.IZendiator m)=>m.SendAll(new Request<OtherLeft,Right>(7,new Shared()));
            private static string Observe(bool generated,string mode){
                var p=new Probe(mode);using var token=p.Cancellation;IServiceCollection services=new ServiceCollection();services.AddSingleton(p);
                services.AddScoped(typeof(ZHandler<,>));var existing=services.Last();Register.Add(services);var count=services.Count;Register.Add(services);
                Probe.Check(count==6&&services.Count==count&&services.Contains(existing),"TryAdd/repeat registration");
                foreach(var type in new[]{typeof(ZHandler<,>),typeof(AHandler<,>),typeof(Hook<,>),typeof(Generated.Composition.IZendiator),typeof(Generated.Composition.Zendiator)})Probe.Check(services.Single(d=>d.ServiceType==type).Lifetime==ServiceLifetime.Scoped,"scoped receivers/facade");
                var mismatch=new ZendiatorConfiguration();mismatch.Handlers(typeof(Request<,>),DispatchFamily.SendAllSync,typeof(AHandler<,>),typeof(ZHandler<,>));mismatch.Pipeline(typeof(Request<,>),DispatchFamily.SendAllSync,Stage.Hook(typeof(Hook<,>)));
                IServiceCollection rejected=new ServiceCollection();var mismatchRejected=false;try{Generated.Composition.ZendiatorGeneratedRegistrar.Add(rejected,mismatch.Snapshot());}catch(InvalidOperationException){mismatchRejected=true;}
                Probe.Check(mismatchRejected&&rejected.Count==0,"snapshot handler order checked before registration");
                using var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});using var scope=provider.CreateScope();var sp=scope.ServiceProvider;
                var mediator=sp.GetRequiredService<Generated.Composition.IZendiator>();Probe.Check(ReferenceEquals(mediator,sp.GetRequiredService<Generated.Composition.Zendiator>()),"one facade");
                var data=new Shared();var request=new Request<Left,Right>(7,data);p.Original=request;p.Data=data;Probe.Ambient.Value="caller";Exception? caught=null;
                try{if(generated)mediator.SendAll(request,token.Token);else Reference(sp,request,token.Token);}catch(Exception error)when(ReferenceEquals(error,p.Failure)){caught=error;}
                var normal=mode=="normal";Probe.Check(normal?caught==null:ReferenceEquals(caught,p.Failure),"synchronous error identity");
                Probe.Check(mode!="cancel"||caught is OperationCanceledException cancellation&&cancellation.CancellationToken==token.Token&&token.IsCancellationRequested,"cancellation token");
                var expected=new List<string>{"resolve:K","before:1:7:3","resolve:Z","handler:Z:1:8:3"};
                expected.AddRange(mode=="cancel"?new[]{"exception:1:8:13","finally:False:1:8:13"}:new[]{"after:1:8:13","finally:True:1:8:13"});
                if(normal)expected.AddRange(new[]{"before:2:7:13","resolve:A","handler:A:2:8:13","after:2:8:113","finally:True:2:8:113"});
                Probe.Check(p.Trace.SequenceEqual(expected)&&p.Handlers.SequenceEqual(normal?new[]{"Z","A"}:new[]{"Z"})&&p.Branch==(normal?2:1),"declared order/entered unwind/no later branch");
                Probe.Check(request.Value==7&&data.Value==(normal?113:13)&&p.SavedRequests.Count==p.Branch,"original request/shared effects");
                Probe.Check(p.SavedRequests.All(r=>!ReferenceEquals(r,request))&&(p.Branch==1||!ReferenceEquals(p.SavedRequests[0],p.SavedRequests[1])),"independent branch request slots");
                Probe.Check(Probe.Ambient.Value==(mode=="cancel"?"finally:False:1":normal?"finally:True:2":"finally:True:1"),"native synchronous caller context");
                var types=new[]{typeof(Hook<Right,Left>),typeof(ZHandler<Right,Left>),typeof(AHandler<Left,Right>)}.Take(normal?3:2);
                Probe.Check(p.Created.Select(r=>r.GetType()).SequenceEqual(types),"structural closed keys/no unstarted resolution");
                foreach(var receiver in p.Created)Probe.Check(ReferenceEquals(receiver,sp.GetRequiredService(receiver.GetType())),"one borrowed scoped receiver");
                Probe.Check(p.Disposed.Count==0,"no dispatch disposal");scope.Dispose();Probe.Check(p.Disposed.SequenceEqual(p.Created.AsEnumerable().Reverse()),"one reverse scope cleanup");
                return JsonSerializer.Serialize(new{mode,p.Branch,p.Handlers,p.Trace,p.AmbientSeen,callerContext=Probe.Ambient.Value,shared=data.Value,error=caught==null?null:"sentinel",canceled=token.IsCancellationRequested,created=p.Created.Select(r=>r.GetType().ToString()),disposed=p.Disposed.Select(r=>r.GetType().ToString()),registrations=count,mismatchRejected});
            }
            public static string Run(){var rows=new List<object>();foreach(var mode in new[]{"normal","finally","cancel"}){
                var reference=Observe(false,mode);var generated=Observe(true,mode);Probe.Check(reference==generated,"reference/generated mismatch\n"+reference+"\n"+generated);rows.Add(JsonDocument.Parse(generated).RootElement.Clone());
            }return JsonSerializer.Serialize(new{pairs=3,dispatches=6,rows});}
        }
        }
        """;

    [Fact]
    public void OpenSendAllSync_void_preserves_order_shared_effects_failure_cancellation_and_registration()
    {
        var source = OpenSendAllSyncVoidSource + OpenSendAllSyncVoidConsumer;
        var compilation = SyncCompilation(source, "OpenAllVoid_" + Guid.NewGuid().ToString("N"))
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(OpenSendAllSyncSource(), SyncParseOptions));
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        var folder = OpenSendAllSyncFolder("void-runtime"); SaveSendAllSync(folder, source, driver, output, diagnostics);
        Assert.Empty(diagnostics); Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        AssertOpenSendAllSyncOutput(output, driver);
        var api = output.GetTypeByMetadataName("OpenAllVoid.Generated.Composition.IZendiator")!; Assert.NotNull(api); Assert.False(api.IsGenericType);
        var mediator = output.GetTypeByMetadataName("OpenAllVoid.Generated.Composition.Zendiator")!;
        var publicMethod = Assert.Single(mediator.GetMembers("SendAll").OfType<IMethodSymbol>());
        var branches = mediator.GetMembers().OfType<IMethodSymbol>().Where(m => m.DeclaredAccessibility == Accessibility.Private && m.Name.StartsWith("Branch", StringComparison.Ordinal)).ToArray(); Assert.Equal(2, branches.Length);
        foreach (var method in new[] { Assert.Single(api.GetMembers("SendAll").OfType<IMethodSymbol>()), publicMethod }.Concat(branches))
        {
            Assert.True(method.ReturnsVoid); Assert.False(method.IsAsync); Assert.Equal(2, method.Arity);
            Assert.All(method.TypeParameters, p => { Assert.True(p.HasReferenceTypeConstraint); Assert.True(p.HasConstructorConstraint); });
            Assert.Contains(method.TypeParameters, p => p.ConstraintTypes.Any(t => t.Name == "ILeft")); Assert.Contains(method.TypeParameters, p => p.ConstraintTypes.Any(t => t.Name == "IRight"));
        }
        var catalog = Assert.IsType<CompositionSelectionCatalog>(Assert.Single(driver.GetRunResult().Results.Single().TrackedSteps["CompositionSelectionModel"].SelectMany(s => s.Outputs)).Value);
        var route = Assert.Single(Assert.Single(catalog.Compositions, c => c.Key == "openAllVoid").Pipelines);
        var emission = Assert.Single(CanonicalSendAllSyncEmissionNormalizer.Create(catalog, CanonicalExecutionGraphNormalizer.Create(catalog)).Compositions, c => c.Registration.Namespace == "OpenAllVoid.Generated.Composition");
        Assert.Equal(2, emission.Branches.Count); Assert.All(emission.Branches, b => { Assert.Equal(route.SendGeneric, b.Generic); Assert.Equal(route.SendOpenRegistration, b.OpenRegistration); });
        Assert.Equal(new[] { "global::OpenAllVoid.ZHandler<,>", "global::OpenAllVoid.AHandler<,>" }, route.SendOpenRegistration!.HandlerDefinitionNames);
        using var bytes = new MemoryStream(); var emitted = output.Emit(bytes); Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        if (folder != null)
        {
            File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
            for (var i = 0; i < compilation.SyntaxTrees.Count(); i++) File.WriteAllText(Path.Combine(folder, "input-" + i + ".cs"), compilation.SyntaxTrees.ElementAt(i).ToString());
        }
        bytes.Position = 0; var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var runtime = assembly.GetType("OpenAllVoid.Entry")!.GetMethod("Run")!.CreateDelegate<Func<string>>()();
        if (folder != null) File.WriteAllText(Path.Combine(folder, "runtime.json"), runtime);
        using var json = JsonDocument.Parse(runtime); Assert.Equal(6, json.RootElement.GetProperty("dispatches").GetInt32());
    }

    [Fact]
    public void OpenSendAllSync_void_rejects_unbound_mixed_partial_and_owned_shapes()
    {
        var source = OpenSendAllSyncVoidSource + """
            namespace OpenAllVoid {
            [ZendiatorHandler]public sealed class Unbound<X,Y,Unused>:ISyncRequestHandler<Request<X,Y>>{public void Handle(Request<X,Y> r,CancellationToken t){}}
            [ZendiatorHandler]public sealed class StructHandler<X,Y>:ISyncRequestHandler<Request<X,Y>> where X:struct{public void Handle(Request<X,Y> r,CancellationToken t){}}
            [ZendiatorHandler]public sealed class Third<X,Y>:ISyncRequestHandler<Request<X,Y>>{public void Handle(Request<X,Y> r,CancellationToken t){}}
            public sealed class Partial<X,Y>:ISendRequestTransform<Request<X,Y>>{public Request<X,Y> Before(Request<X,Y> r,CancellationToken t)=>r;}
            public sealed class Wrapper<X,Y>:ISendWrapper<Request<X,Y>>{public void Invoke<N>(Request<X,Y> r,N next,CancellationToken t) where N:ISendContinuation<Request<X,Y>> =>next.Invoke(r,t);}
            public sealed class HookFactory<X,Y>:IBorrowedSessionFactory<Request<X,Y>,Hook<Y,X>> where X:class where Y:class,IRight{public Hook<Y,X> Create(Request<X,Y> r,CancellationToken t)=>throw new NotSupportedException();}
            public sealed class HandlerFactory<X,Y>:IBorrowedSessionFactory<Request<X,Y>,AHandler<X,Y>> where X:class,ILeft,new() where Y:class{public AHandler<X,Y> Create(Request<X,Y> r,CancellationToken t)=>throw new NotSupportedException();}
            public sealed record DualRequest<X,Y>:ISyncRequest,ISyncRequest<int>;
            [ZendiatorHandler]public sealed class DualVoid<X,Y>:ISyncRequestHandler<DualRequest<X,Y>>{public void Handle(DualRequest<X,Y> r,CancellationToken t){}}
            [ZendiatorHandler]public sealed class DualResult<X,Y>:ISyncRequestHandler<DualRequest<X,Y>,int>{public int Handle(DualRequest<X,Y> r,CancellationToken t)=>0;}
            public sealed class DualHook<X,Y>:ISendRequestTransform<DualRequest<X,Y>>,ISendSuccessObserver<DualRequest<X,Y>>,ISendExceptionObserver<DualRequest<X,Y>>,ISendFinallyObserver<DualRequest<X,Y>>{
                public DualRequest<X,Y> Before(DualRequest<X,Y> r,CancellationToken t)=>r;public void After(DualRequest<X,Y> r,CancellationToken t){}public void OnException(DualRequest<X,Y> r,Exception e,CancellationToken t){}public void Finally(DualRequest<X,Y> r,SendExitInfo e,CancellationToken t){}
            }
            }
            """;
        foreach (var (name, invalid) in new[]
        {
            ("unbound", source.Replace("typeof(AHandler<,>)", "typeof(Unbound<,,>)")),
            ("constraint", source.Replace("typeof(AHandler<,>)", "typeof(StructHandler<,>)")),
            ("partial", source.Replace("Stage.Hook(typeof(Hook<,>))", "Stage.Hook(typeof(Partial<,>))")),
            ("hook-session", source.Replace("Stage.Hook(typeof(Hook<,>))", "Stage.Hook(typeof(Hook<,>),typeof(HookFactory<,>))")),
            ("handler-session", source.Replace("typeof(AHandler<,>)", "HandlerEntry.Session(typeof(AHandler<,>),typeof(HandlerFactory<,>))")),
            ("wrapper", source.Replace("Stage.Hook(typeof(Hook<,>))", "Stage.Wrapper(typeof(Wrapper<,>))")),
            ("three", source.Replace("typeof(ZHandler<,>),typeof(AHandler<,>)", "typeof(ZHandler<,>),typeof(AHandler<,>),typeof(Third<,>)")),
            ("mixed", source.Replace("typeof(Request<,>)", "typeof(DualRequest<,>)").Replace("typeof(ZHandler<,>),typeof(AHandler<,>)", "typeof(DualVoid<,>),typeof(DualResult<,>)").Replace("typeof(Hook<,>)", "typeof(DualHook<,>)"))
        })
        {
            var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(invalid, "OpenAllVoidRejected_" + name + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
            SaveSendAllSync(OpenSendAllSyncFolder("void-" + name), invalid, driver, output, diagnostics);
            Assert.NotEmpty(diagnostics); Assert.All(diagnostics, d => Assert.Equal("ZEN0035", d.Id));
            if (name == "unbound")
            {
                var error = Assert.Single(diagnostics, d => invalid.Substring(d.Location.SourceSpan.Start, d.Location.SourceSpan.Length) == "typeof(Unbound<,,>)"); Assert.Empty(error.AdditionalLocations);
            }
            Assert.Null(output.GetTypeByMetadataName("OpenAllVoid.Generated.Composition.IZendiator")); Assert.Null(output.GetTypeByMetadataName("OpenAllVoid.Generated.Composition.ZendiatorGeneratedRegistrar"));
            Assert.DoesNotContain(driver.GetRunResult().Results.Single().GeneratedSources, s => s.HintName.EndsWith("Interceptors.g.cs", StringComparison.Ordinal));
            Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        }
    }
}
