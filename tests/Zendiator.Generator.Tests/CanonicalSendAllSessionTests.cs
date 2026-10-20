using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Zendiator.Generator.Tests;

// Reuses the existing independent Single references once per ordered branch.
public static class AllSessionReference
{
    public static object? Branch(string profile, int id, Type request, Type hook, Type handler, Type hookFactory, Type? handlerFactory,
        IServiceProvider provider, object value, CancellationToken token)
    {
        var args = new List<Type> { request }; var response = profile.EndsWith('R'); if (response) args.Add(typeof(int)); args.Add(hook); args.Add(handler);
        Type owner; string name;
        if (profile.StartsWith('S'))
        {
            owner = typeof(CanonicalSendSyncReference);
            name = profile == "SV" ? "SendBorrowedHookBorrowedHandlerVoid" : id == 2 ? "SendOwnedHookResponse" : id == 1 ? "SendOwnedHookOwnedHandlerResponse" : "SendOwnedHookBorrowedHandlerResponse";
            args.Add(hookFactory); if (handlerFactory != null) args.Add(handlerFactory);
        }
        else
        {
            owner = typeof(CanonicalAsyncMixedReference); name = response ? "SendResponse" : "SendVoid";
            var lease = profile == "AR" ? typeof(AsyncOwnedReferenceLease<,,>) : typeof(OwnedReferenceLease<,,>);
            args.Add(lease.MakeGenericType(request, hook, hookFactory)); args.Add(lease.MakeGenericType(request, handler, handlerFactory!));
        }
        try { return owner.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(args.ToArray()).Invoke(null, [provider, value, token]); }
        catch (TargetInvocationException error) when (error.InnerException != null) { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
}

public sealed partial class GeneratorTests
{
    private static string AllSessionModule(string profile)
    {
        var async = profile[0] == 'A'; var isVoid = profile[1] == 'V'; var borrowed = profile == "SV"; var mixed = profile == "SR"; var asyncFactory = profile == "AR";
        var value = isVoid ? "void" : "int"; var ret = async ? isVoid ? "ValueTask" : "ValueTask<int>" : value;
        var allRet = async ? isVoid ? "ValueTask" : "ValueTask<int[]>" : isVoid ? "void" : "int[]";
        var factoryRole = "I" + (asyncFactory ? "Async" : "") + (borrowed ? "Borrowed" : "Owned") + "SessionFactory";
        var factoryMethod = asyncFactory ? "CreateAsync" : "Create";
        string Cleanup(string kind) => async
            ? "public async ValueTask DisposeAsync(){await p.CleanupAsync(this,\"" + kind + "\",branch,request,p.Cancellation.Token).ConfigureAwait(false);p.CleanupDone(this);}"
            : "public void Dispose(){p.CleanupSync(this,\"" + kind + "\",branch,request,p.Cancellation.Token);p.CleanupDone(this);}";
        var handlers = string.Join("\n", Enumerable.Range(1, 3).Select(id => $$"""
            [ZendiatorHandler]public sealed class H{{id}}:{{(async ? "IRequestHandler" : "ISyncRequestHandler")}}<Ping{{(isVoid ? "" : ",int")}}>,{{(async ? "IAsyncDisposable" : "IDisposable")}}{
                private readonly Probe p;private Ping? request;private int branch;
                public H{{id}}(Probe p){this.p=p;branch=p.Branch;p.Created(this,"H{{id}}",branch,{{(!borrowed && (!mixed || id == 1) ? "true" : "false")}},{{(mixed && id == 2 ? "true" : "false")}});}
                public {{ret}} {{(async ? "HandleAsync" : "Handle")}}(Ping r,CancellationToken t){request=r;branch=p.Branch;{{(async ? "return p." + (isVoid ? "BodyVoid" : "BodyAsync") + "(r,r.Value," + id + ",t);" : (isVoid ? "" : "return ") + "p.Body(r,r.Value," + id + ",t);")}}}
                {{Cleanup("H" + id)}}
            }
            """));
        var factories = string.Join("\n", Enumerable.Range(1, 3).Where(id => !(mixed && id == 2)).Select(id =>
        {
            var role = mixed && id == 3 ? "IBorrowedSessionFactory" : factoryRole;
            return $$"""
                public sealed class F{{id}}:{{role}}<Ping,H{{id}}>,IDisposable{
                    private readonly Probe p;public F{{id}}(Probe p){this.p=p;p.FactoryResolved("{{profile}}.F{{id}}");}
                    public {{(asyncFactory ? "ValueTask<H" + id + ">" : "H" + id)}} {{factoryMethod}}(Ping r,CancellationToken t){p.HandlerAcquire(r,r.Value,{{id}},t);var h=new H{{id}}(p);return {{(asyncFactory ? "p.Acquired(h," + id + ")" : "h")}};}
                    public void Dispose()=>p.FactoryEnds.Add("{{profile}}.F{{id}}");
                }
                """;
        }));
        var referenceCalls = string.Join("\n", new[] { 2, 1, 3 }.Select((id, index) =>
            (isVoid ? "" : "results[" + index + "]=") + (async ? "await (" : isVoid ? "_ = " : "") + (isVoid && !async ? "" : "(" + ret + ")") +
            "AllSessionReference.Branch(\"" + profile + "\"," + id + ",typeof(Ping),typeof(Hook),typeof(H" + id + "),typeof(HF)," + (mixed && id == 2 ? "null" : "typeof(F" + id + ")") + ",p,request,t)!" + (async ? ").ConfigureAwait(false)" : "") + ";"));
        return $$"""
            namespace SessionCases.{{profile}} {
            public sealed record Ping(int Value):{{(async ? "IRequest" : "ISyncRequest")}}{{(isVoid ? "" : "<int>")}};
            public sealed class Hook:{{(async ? "IAsyncSendRequestTransform" : "ISendRequestTransform")}}<Ping>,{{(async ? "IAsyncSend" : "ISend")}}{{(isVoid ? "SuccessObserver<Ping>" : "ResponseTransform<Ping,int>")}},{{(async ? "IAsyncSendExceptionObserver" : "ISendExceptionObserver")}}<Ping>,{{(async ? "IAsyncSendFinallyObserver" : "ISendFinallyObserver")}}<Ping>,{{(async ? "IAsyncDisposable" : "IDisposable")}}{
                private readonly Probe p;private readonly Ping request;private readonly int branch;
                public Hook(Probe p,Ping request){this.p=p;this.request=request;branch=p.Branch;p.Created(this,"K",branch,{{(borrowed ? "false" : "true")}},false);}
                public {{(async ? "ValueTask<Ping>" : "Ping")}} {{(async ? "BeforeAsync" : "Before")}}(Ping r,CancellationToken t){p.Before(r,r.Value,t);var next=new Ping(r.Value+10);p.Save(next);return {{(async ? "new(next)" : "next")}};}
                public {{ret}} {{(async ? "AfterAsync" : "After")}}(Ping r,{{(isVoid ? "" : "int value,")}}CancellationToken t){p.After(r,r.Value,t);{{(async ? "return " + (isVoid ? "default" : "new(value+100)") + ";" : isVoid ? "" : "return value+100;")}}}
                public {{(async ? "ValueTask" : "void")}} {{(async ? "OnExceptionAsync" : "OnException")}}(Ping r,Exception e,CancellationToken t){p.Observe(r,r.Value,e,t);{{(async ? "return default;" : "")}}}
                public {{(async ? "ValueTask" : "void")}} {{(async ? "FinallyAsync" : "Finally")}}(Ping r,SendExitInfo e,CancellationToken t){p.Finally(r,r.Value,e,t);{{(async ? "return default;" : "")}}}
                {{Cleanup("K")}}
            }
            public sealed class HF:{{factoryRole}}<Ping,Hook>,IDisposable{
                private readonly Probe p;public HF(Probe p){this.p=p;p.FactoryResolved("{{profile}}.HF");}
                public {{(asyncFactory ? "ValueTask<Hook>" : "Hook")}} {{factoryMethod}}(Ping r,CancellationToken t){p.HookAcquire(r,r.Value,t);var hook=new Hook(p,r);return {{(asyncFactory ? "new(hook)" : "hook")}};}
                public void Dispose()=>p.FactoryEnds.Add("{{profile}}.HF");
            }
            {{handlers}}
            {{factories}}
            public static class Reference {
                public static {{allRet}} Run(IServiceProvider p,Ping request,CancellationToken t){ArgumentNullException.ThrowIfNull(request);{{(async ? "return Execute(p,request,t);" : "t.ThrowIfCancellationRequested();" + (isVoid ? "" : "var results=new int[3];") + referenceCalls + (isVoid ? "" : "return results;"))}}}
                {{(async ? "private static async " + allRet + " Execute(IServiceProvider p,Ping request,CancellationToken t){t.ThrowIfCancellationRequested();" + (isVoid ? "" : "var results=new int[3];") + referenceCalls + (isVoid ? "" : "return results;") + "}" : "")}}
            }
            {{(profile == "SR" ? "public sealed class BadHF:IAsyncOwnedSessionFactory<Ping,Hook>{public ValueTask<Hook> CreateAsync(Ping r,CancellationToken t)=>throw new NotSupportedException();}" : "")}}
            }
            """;
    }

    private const string AllSessionProbeSource = """
        namespace SessionCases {
        public sealed class Probe {
            public static readonly AsyncLocal<string?> Ambient=new();
            public readonly CancellationTokenSource Cancellation=new();public readonly Exception Failure=new FormatException("planned");
            public readonly List<string> Trace=new(),Contexts=new(),Factories=new(),FactoryEnds=new(),CreatedNames=new(),CleanupStarts=new(),CleanupEnds=new();
            public readonly List<int> Handlers=new();public readonly Dictionary<object,string> Requests=new(ReferenceEqualityComparer.Instance);
            public readonly Dictionary<object,(string Name,bool Owned,bool Direct)> Resources=new(ReferenceEqualityComparer.Instance);
            public readonly HashSet<object> Started=new(ReferenceEqualityComparer.Instance),Completed=new(ReferenceEqualityComparer.Instance);
            public object Original=null!;public string Case="",Profile="";public int Branch,Version;public IAsyncOnceGate? Gate;public string? PendingPoint;
            public static void Check(bool value,string text){if(!value)throw new InvalidOperationException(text);}
            public string Error(Exception? e)=>e==null?"none":ReferenceEquals(e,Failure)?"sentinel":e is OperationCanceledException c&&c.CancellationToken==Cancellation.Token?"cancel":e.GetType().Name;
            private void Hit(string phase,int branch,object request,CancellationToken t){Check(t==Cancellation.Token,"factory/callback token identity");var role=Requests[request];Trace.Add(phase+":"+branch+":"+role);Contexts.Add(phase+":"+branch+"|"+Ambient.Value+"|"+(SynchronizationContext.Current?.GetType().FullName??"none"));Ambient.Value=phase+":"+branch;}
            public void FactoryResolved(string name){Check(name.StartsWith(Profile+".",StringComparison.Ordinal),"unrelated route factory activated");Factories.Add(name);}
            public void HookAcquire(object r,int value,CancellationToken t){Check(ReferenceEquals(r,Original)&&value==1,"hook acquire original request");Branch++;Hit("hook.acquire",Branch,r,t);if(Case=="sync-hook-fail"&&Branch==2)throw Failure;}
            public void Before(object r,int value,CancellationToken t){Check(ReferenceEquals(r,Original)&&value==1,"branch original request");Hit("before",Branch,r,t);}
            public void Save(object r)=>Requests.Add(r,"saved"+Branch);
            public void HandlerAcquire(object r,int value,int id,CancellationToken t){Check(Requests[r]=="saved"+Branch&&value==11,"handler acquire transformed request");Hit("handler.acquire.H"+id,Branch,r,t);if(Case=="sync-handler-fail"&&Branch==2)throw Failure;}
            public void Created(object r,string kind,int branch,bool owned,bool direct){var name=Profile+"."+kind+"@"+branch;Resources.Add(r,(name,owned,direct));CreatedNames.Add(name);Trace.Add("create:"+name);}
            public ValueTask<H> Acquired<H>(H value,int id){if(Case!="async-acquire"||id!=1)return new(value);var gate=new AsyncOnceGate<H>(value);Gate=gate;PendingPoint="handler.acquire";return gate.Task;}
            public int Body(object r,int value,int id,CancellationToken t){Check(Requests[r]=="saved"+Branch&&value==11,"handler saved request");Hit("body.H"+id,Branch,r,t);Handlers.Add(id);Version++;return value+id;}
            public ValueTask<int> BodyAsync(object r,int value,int id,CancellationToken t)=>new(Body(r,value,id,t));
            public ValueTask BodyVoid(object r,int value,int id,CancellationToken t){Body(r,value,id,t);return default;}
            public void After(object r,int value,CancellationToken t){Check(Requests[r]=="saved"+Branch&&value==11,"After saved request");Hit("after",Branch,r,t);}
            public void Observe(object r,int value,Exception e,CancellationToken t){Check(Requests[r]=="saved"+Branch&&ReferenceEquals(e,Failure),"observer error/request identity");Hit("exception."+Error(e),Branch,r,t);}
            public void Finally(object r,int value,SendExitInfo e,CancellationToken t){Check(Requests[r]=="saved"+Branch&&value==11,"Finally saved request");Hit("finally."+e.Success+"."+Error(e.Error),Branch,r,t);if(Case=="async-finally-fail"&&Branch==2)throw Failure;}
            private void Cleanup(object resource,string kind,int branch,object? r,CancellationToken t){
                Check(r!=null&&Requests[r]==(kind=="K"?"original":"saved"+branch),"cleanup original/saved request");Check(Started.Add(resource),"session cleanup invoked twice");
                CleanupStarts.Add(Resources[resource].Name);Hit(kind+".cleanup",branch,r!,t);if(Case=="async-cleanup-fail"&&kind=="H1")throw Failure;
            }
            public void CleanupSync(object resource,string kind,int branch,object? r,CancellationToken t)=>Cleanup(resource,kind,branch,r,t);
            public async ValueTask CleanupAsync(object resource,string kind,int branch,object? r,CancellationToken t){
                Cleanup(resource,kind,branch,r,t);
                if(Case=="async-cleanup"&&kind=="H1"){var gate=new AsyncOnceGate<int>(0);Gate=gate;PendingPoint="handler.cleanup";await gate.Void.ConfigureAwait(false);}
            }
            public void CleanupDone(object resource){Check(Completed.Add(resource),"cleanup completion twice");CleanupEnds.Add(Resources[resource].Name);}
        }}
        """;

    private static string AllSessionSource(bool gate)
    {
        var source = SendAllCompositionSource(false); source = source[..source.IndexOf("namespace AllMix {", StringComparison.Ordinal)];
        var config = """
            c.Handlers<T.Ping>(DispatchFamily.SendAllSync,typeof(T.H2),HandlerEntry.Session<T.H1,T.F1>(),HandlerEntry.Session<T.H3,T.F3>());
            c.Handlers<A.Ping>(DispatchFamily.SendAsync,HandlerEntry.Session<A.Handler,A.Factory>());
            c.Handlers<S.Items>(DispatchFamily.Stream,typeof(S.H));
            c.Handlers<U.Ping>(DispatchFamily.SendAllSync,HandlerEntry.Session<U.H2,U.F2>(),HandlerEntry.Session<U.H1,U.F1>(),HandlerEntry.Session<U.H3,U.F3>());
            c.Handlers<R.Ping>(DispatchFamily.SendSync,HandlerEntry.Session<R.Handler,R.Factory>());
            c.Handlers<V.Ping>(DispatchFamily.SendAllAsync,HandlerEntry.Session<V.H2,V.F2>(),HandlerEntry.Session<V.H1,V.F1>(),HandlerEntry.Session<V.H3,V.F3>());
            c.Handlers<W.Ping>(DispatchFamily.SendAllAsync,HandlerEntry.Session<W.H2,W.F2>(),HandlerEntry.Session<W.H1,W.F1>(),HandlerEntry.Session<W.H3,W.F3>());
            c.Pipeline<W.Ping>(DispatchFamily.SendAllAsync,Stage.Hook<W.Hook,W.HF>());
            c.Pipeline<S.Items>(DispatchFamily.Stream,Stage.Hook<S.F1>(),Stage.Hook<S.M>(),Stage.Hook<S.F2>());
            c.Pipeline<U.Ping>(DispatchFamily.SendAllSync,Stage.Hook<U.Hook,U.HF>());
            c.Pipeline<R.Ping>(DispatchFamily.SendSync,Stage.Hook<R.Hook>());
            c.Pipeline<T.Ping>(DispatchFamily.SendAllSync,Stage.Hook<T.Hook,T.HF>());
            c.Pipeline<A.Ping>(DispatchFamily.SendAsync,Stage.Hook<A.Hook>());
            c.Pipeline<V.Ping>(DispatchFamily.SendAllAsync,Stage.Hook<V.Hook,V.HF>());
            """;
        return "using Zendiator.Generator.Tests;\n" + source + AllSessionProbeSource + string.Join("\n", new[] { "SR", "SV", "AR", "AV" }.Select(AllSessionModule)) + """
            namespace AllMix {
            using System;using System.Collections.Generic;using System.Linq;using System.Text.Json;using System.Threading;using System.Threading.Tasks;
            using Zendiator;using Zendiator.DependencyInjection;using Microsoft.Extensions.DependencyInjection;using Zendiator.Generator.Tests;
            using T=global::SessionCases.SR;using U=global::SessionCases.SV;using V=global::SessionCases.AR;using W=global::SessionCases.AV;
            using S=global::Closed.AllMixStream;using R=global::Closed.AllMixSync;using A=global::Closed.AllMixAsync;using P=global::SessionCases.Probe;
            [ZendiatorComposition("all-sessions")]public sealed class Many;
            public static class Bridge {public static IServiceCollection Register(IServiceCollection s)=>s.AddZendiator<Many>(c=>{__CONFIG__});__CONSUMER__}
            __NEGATIVE__
            }
            """.Replace("__CONFIG__", config).Replace("__CONSUMER__", gate ? "" : AllSessionConsumer)
            .Replace("__NEGATIVE__", gate ? """
                [ZendiatorComposition("all-sessions-invalid")]public sealed class Rejected;
                public static class RejectedBridge {public static IServiceCollection RegisterRejected(IServiceCollection s)=>s.AddZendiator<Rejected>(c=>{__BAD__});} /* rejected end */
                """.Replace("__BAD__", config.Replace("Stage.Hook<T.Hook,T.HF>()", "Stage.Hook<T.Hook,T.BadHF>()")) : "");
    }

    private const string AllSessionConsumer = """
        private sealed class EntryContext:SynchronizationContext;
        private static int dispatches;
        private static async Task<(string Snapshot,object? Interleave)> Observe(string name,string profile,bool generated){
            var oldContext=SynchronizationContext.Current;SynchronizationContext.SetSynchronizationContext(new EntryContext());
            var p=new P{Case=name,Profile=profile};var other=new ManagedShapeProbe();var streamProbe=new PublicValueRequestProbe();
            try{
                IServiceCollection services=new ServiceCollection();services.AddSingleton(p);services.AddSingleton(other);services.AddSingleton(streamProbe);Register(services);
                foreach(var ns in new[]{typeof(T.Ping).Namespace!,typeof(U.Ping).Namespace!,typeof(V.Ping).Namespace!,typeof(W.Ping).Namespace!}){
                    var assembly=typeof(T.Ping).Assembly;var hook=assembly.GetType(ns+".Hook")!;P.Check(!services.Any(d=>d.ServiceType==hook),"hook session must come from factory");
                    foreach(var id in new[]{2,1,3}){var h=assembly.GetType(ns+".H"+id)!;var f=assembly.GetType(ns+".F"+id);var d=services.Single(x=>x.ServiceType==(f??h));P.Check(d.Lifetime==ServiceLifetime.Scoped&&d.ImplementationType==(f??h),"all factory/direct registrations");if(f!=null)P.Check(!services.Any(x=>x.ServiceType==h),"session handler not registered as DI service");}
                    var hf=assembly.GetType(ns+".HF")!;P.Check(services.Single(d=>d.ServiceType==hf).Lifetime==ServiceLifetime.Scoped,"borrowed hook factory scope");
                }
                P.Check(services.GroupBy(d=>d.ServiceType).All(g=>g.Count()==1),"unique service registrations");
                await using var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});await using var scope=provider.CreateAsyncScope();var sp=scope.ServiceProvider;
                var mediator=sp.GetRequiredService<global::AllMix.Generated.Many.IZendiator>();P.Check(ReferenceEquals(mediator,sp.GetRequiredService<global::AllMix.Generated.Many.Zendiator>()),"shared mediator identity");
                object request=profile switch{"SR"=>new T.Ping(1),"SV"=>new U.Ping(1),"AR"=>new V.Ping(1),_=>new W.Ping(1)};p.Original=request;p.Requests.Add(request,"original");
                P.Ambient.Value="caller";Exception? error=null;int[]? results=null;bool syncThrow=false;var returned=false;var initial="Success";var final="Success";
                ValueTask<int[]> responseTask=default;ValueTask voidTask=default;object? waiting=null;object? interleave=null;
                try{
                    dispatches++;
                    switch(profile){
                        case "SR":results=generated?mediator.SendAll((T.Ping)request,p.Cancellation.Token):T.Reference.Run(sp,(T.Ping)request,p.Cancellation.Token);break;
                        case "SV":if(generated)mediator.SendAll((U.Ping)request,p.Cancellation.Token);else U.Reference.Run(sp,(U.Ping)request,p.Cancellation.Token);break;
                        case "AR":responseTask=generated?mediator.SendAllAsync((V.Ping)request,p.Cancellation.Token):V.Reference.Run(sp,(V.Ping)request,p.Cancellation.Token);initial=responseTask.IsCompletedSuccessfully?"Success":responseTask.IsFaulted?"Faulted":responseTask.IsCanceled?"Canceled":"Pending";break;
                        default:voidTask=generated?mediator.SendAllAsync((W.Ping)request,p.Cancellation.Token):W.Reference.Run(sp,(W.Ping)request,p.Cancellation.Token);initial=voidTask.IsCompletedSuccessfully?"Success":voidTask.IsFaulted?"Faulted":voidTask.IsCanceled?"Canceled":"Pending";break;
                    }returned=true;
                }catch(Exception failure){syncThrow=true;error=failure;initial=final="sync-throw";}
                var ambientAtReturn=P.Ambient.Value;
                if(profile[0]=='A')P.Check(ambientAtReturn=="caller","async factory/callback ambient isolated from caller");
                if(name=="async-acquire"||name=="async-cleanup"){
                    var gate=p.Gate!;P.Check(initial=="Pending"&&p.Branch==2&&gate.Consumptions==0,"pending session boundary");
                    P.Check(!p.Factories.Any(x=>x.EndsWith(".F3",StringComparison.Ordinal))&&!p.CreatedNames.Any(x=>x.Contains("@3",StringComparison.Ordinal)),"later branch not acquired/resolved");
                    P.Check(p.Handlers.SequenceEqual(name=="async-acquire"?new[]{2}:new[]{2,1}),"pending acquire/cleanup stops body/After progression");
                    P.Check(!p.Trace.Any(x=>x.StartsWith("after:2",StringComparison.Ordinal)),"pending cleanup completes before After");
                    waiting=new{p.Branch,p.PendingPoint,handlers=p.Handlers.ToArray(),factories=p.Factories.ToArray(),trace=p.Trace.ToArray(),created=p.CreatedNames.ToArray(),cleanupStarted=p.CleanupStarts.ToArray(),cleanupCompleted=p.CleanupEnds.ToArray(),contexts=p.Contexts.ToArray(),consumptions=gate.Consumptions,caller=P.Ambient.Value};
                    if(generated&&name=="async-acquire"){
                        dispatches++;var reply=mediator.Send(new R.Ping(10),other.Token.Token);P.Check(reply.A==12,"accepted single Sync interleave");
                        var values=new List<int>();dispatches++;await foreach(var item in mediator.StreamAsync(new S.Items(new S.SharedState())))values.Add(item);
                        P.Check(values.SequenceEqual(new[]{2,4})&&streamProbe.SourceDisposals==1,"accepted Stream interleave");
                        P.Check(p.Branch==2&&p.Handlers.SequenceEqual(new[]{2})&&gate.Consumptions==0&&!responseTask.IsCompleted,"other routes preserve pending session state");interleave=new{syncValue=reply.A,values,pendingConsumptions=gate.Consumptions};
                    }
                    P.Ambient.Value="completer";gate.Complete();P.Ambient.Value="caller";
                }
                if(returned&&profile[0]=='A')try{if(profile=="AR")results=await responseTask;else await voidTask;}catch(Exception failure){error=failure;}
                if(returned&&profile[0]=='A')final=profile=="AR"?(responseTask.IsCompletedSuccessfully?"Success":responseTask.IsCanceled?"Canceled":"Faulted"):(voidTask.IsCompletedSuccessfully?"Success":voidTask.IsCanceled?"Canceled":"Faulted");
                var failed=name.EndsWith("fail",StringComparison.Ordinal);
                if(failed){P.Check(ReferenceEquals(error,p.Failure)&&p.Branch==2&&p.Handlers.SequenceEqual(name=="sync-handler-fail"||name=="sync-hook-fail"?new[]{2}:new[]{2,1}),"failure identity and later stop");P.Check(profile[0]=='S'?syncThrow&&final=="sync-throw":!syncThrow&&final=="Faulted","sync throw vs returned faulted state");}
                else{P.Check(error==null&&p.Branch==3&&p.Handlers.SequenceEqual(new[]{2,1,3})&&p.Version==3,"branch order and complete work");P.Check(profile[1]=='V'?results==null:results!.SequenceEqual(new[]{113,112,114}),"response/void aggregation");}
                if(p.Gate!=null)P.Check(p.Gate.Consumptions==1,"session ValueTask consumed once");
                P.Check(p.FactoryEnds.Count==0,"factory services live until scope");
                var owned=p.Resources.Where(x=>x.Value.Owned).Select(x=>x.Key).ToArray();P.Check(p.Started.SetEquals(owned),"only owned sessions cleaned up during operation, including failure");
                P.Check(p.Completed.Count==owned.Length-(name=="async-cleanup-fail"?1:0),"cleanup completion count");
                var beforeScope=new{cleanupStarted=p.CleanupStarts.ToArray(),cleanupCompleted=p.CleanupEnds.ToArray(),ambient=P.Ambient.Value};
                await scope.DisposeAsync();P.Check(p.FactoryEnds.SequenceEqual(p.Factories.AsEnumerable().Reverse()),"scope disposes each borrowed factory once");
                var expected=p.Resources.Where(x=>x.Value.Owned||x.Value.Direct).Select(x=>x.Key);P.Check(p.Started.SetEquals(expected),"scope owns direct receiver; borrowed sessions remain untouched");
                var snapshot=JsonSerializer.Serialize(new{name,profile,syncThrow,returned,initial,final,error=p.Error(error),results,p.Trace,p.Contexts,p.Factories,p.FactoryEnds,p.CreatedNames,p.CleanupStarts,p.CleanupEnds,p.Handlers,p.Branch,p.Version,ambientAtReturn,beforeScope,waiting,consumptions=p.Gate?.Consumptions??0});return(snapshot,interleave);
            }finally{P.Ambient.Value=null;p.Cancellation.Dispose();other.Token.Dispose();SynchronizationContext.SetSynchronizationContext(oldContext);}
        }
        public static async Task<string> Run(){
            dispatches=0;var rows=new List<object>();object? interleave=null;
            foreach(var c in new[]{("sync-mixed","SR"),("sync-borrowed","SV"),("async-acquire","AR"),("async-cleanup","AV"),("sync-handler-fail","SR"),("sync-hook-fail","SV"),("async-cleanup-fail","AR"),("async-finally-fail","AV")}){
                var g=await Observe(c.Item1,c.Item2,true);var r=await Observe(c.Item1,c.Item2,false);P.Check(g.Snapshot==r.Snapshot,"session generated/reference mismatch: "+c.Item1+"\n"+g.Snapshot+"\n"+r.Snapshot);
                if(g.Interleave!=null)interleave=g.Interleave;rows.Add(new{name=c.Item1,profile=c.Item2,generated=JsonDocument.Parse(g.Snapshot).RootElement.Clone(),reference=JsonDocument.Parse(r.Snapshot).RootElement.Clone()});
            }
            P.Check(dispatches==18,"fixed18dispatch budget");return JsonSerializer.Serialize(new{generatorInputs=1,scenarios=8,pairedDispatches=16,interleaveDispatches=2,dispatches,interleave,rows});
        }
        """;

    private static object AssertAllSessionComposition(Compilation output)
    {
        var api = output.GetTypeByMetadataName("AllMix.Generated.Many.IZendiator"); Assert.NotNull(api);
        Assert.Equal(3, api.GetMembers("SendAll").Length); Assert.Equal(2, api.GetMembers("SendAllAsync").Length);
        foreach (var name in new[] { "Send", "SendAsync", "StreamAsync" }) Assert.Single(api.GetMembers(name));
        var classes = output.SyntaxTrees.SelectMany(t => t.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()).ToArray(); var registrar = Assert.Single(classes, t => t.Identifier.ValueText == "ZendiatorGeneratedRegistrar");
        var calls = registrar.DescendantNodes().OfType<InvocationExpressionSyntax>().Where(c => c.Expression is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "expected" }, Name: GenericNameSyntax }).ToArray();
        string Request(InvocationExpressionSyntax c) => ((GenericNameSyntax)((MemberAccessExpressionSyntax)c.Expression).Name).TypeArgumentList.Arguments[0].ToString();
        var handlers = calls.Where(c => ((MemberAccessExpressionSyntax)c.Expression).Name.Identifier.ValueText == "Handlers").ToArray(); var pipelines = calls.Where(c => ((MemberAccessExpressionSyntax)c.Expression).Name.Identifier.ValueText == "Pipeline").ToArray();
        Assert.Equal(new[] { "global::SessionCases.SR.Ping", "global::Closed.AllMixAsync.Ping", "global::Closed.AllMixStream.Items", "global::SessionCases.SV.Ping", "global::Closed.AllMixSync.Ping", "global::SessionCases.AR.Ping", "global::SessionCases.AV.Ping" }, handlers.Select(Request));
        Assert.Equal(new[] { "global::SessionCases.AV.Ping", "global::Closed.AllMixStream.Items", "global::SessionCases.SV.Ping", "global::Closed.AllMixSync.Ping", "global::SessionCases.SR.Ping", "global::Closed.AllMixAsync.Ping", "global::SessionCases.AR.Ping" }, pipelines.Select(Request));
        foreach (var profile in new[] { "SR", "SV", "AR", "AV" })
        {
            var call = Assert.Single(handlers, c => Request(c) == "global::SessionCases." + profile + ".Ping"); var entries = call.ArgumentList.Arguments.Skip(1).Select(a => a.ToString()).ToArray();
            foreach (var (id, index) in new[] { (2, 0), (1, 1), (3, 2) })
                Assert.Equal(profile == "SR" && id == 2 ? "typeof(global::SessionCases.SR.H2)" : "global::Zendiator.HandlerEntry.Session<global::SessionCases." + profile + ".H" + id + ", global::SessionCases." + profile + ".F" + id + ">()", entries[index]);
        }
        var mediator = output.GetTypeByMetadataName("AllMix.Generated.Many.Zendiator")!;
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var names = new List<string>();
        foreach (var entry in mediator.GetMembers("SendAll").Concat(mediator.GetMembers("SendAllAsync")).OfType<IMethodSymbol>())
        {
            var owner = CanonicalRouteEntry(output, entry).ContainingType;
            if (!seen.Add(owner)) continue;
            var selected = Assert.Single(handlers, c => Request(c) == entry.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            var branches = owner.GetMembers().OfType<IMethodSymbol>().Where(m => m.Name.StartsWith("Branch", StringComparison.Ordinal)).ToArray();
            Assert.Equal(selected.ArgumentList.Arguments.Count - 1, branches.Length);
            Assert.Equal(branches.Length, branches.Select(m => m.Name).Distinct(StringComparer.Ordinal).Count());
            names.AddRange(branches.Select(m => m.ToDisplayString()));
        }
        return new { handlerOrder = handlers.Select(Request), pipelineOrder = pipelines.Select(Request), branchNames = names };
    }

    [Fact]
    public void AllSession_gate_preserves_factory_entries_and_rejects_async_factory_on_sync_composition_as_a_whole()
    {
        var source = AllSessionSource(true); var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "AllSessionGate_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        var e = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR"); var folder = string.IsNullOrEmpty(e) ? null : Path.Combine(e, "all-session-gate"); SaveSendAllSync(folder, source, driver, output, diagnostics);
        Assert.Contains(diagnostics, d => d.GetMessage().Contains("asynchronous session factory cannot run on a synchronous route", StringComparison.Ordinal));
        var start = source.IndexOf("public static IServiceCollection RegisterRejected(", StringComparison.Ordinal); var end = source.IndexOf("/* rejected end */", start, StringComparison.Ordinal); Assert.All(diagnostics, d => Assert.InRange(d.Location.SourceSpan.Start, start, end));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)); var registration = AssertAllSessionComposition(output);
        Assert.Null(output.GetTypeByMetadataName("AllMix.Generated.Rejected.IZendiator")); Assert.Null(output.GetTypeByMetadataName("AllMix.Generated.Rejected.ZendiatorGeneratedRegistrar"));
        var files = driver.GetRunResult().Results.Single().GeneratedSources; Assert.DoesNotContain(files, f => f.HintName.EndsWith("Interceptors.g.cs", StringComparison.Ordinal) && f.SourceText.ToString().Contains("AllMix.Generated.Rejected.ZendiatorGeneratedRegistrar", StringComparison.Ordinal));
        if (folder != null) File.WriteAllText(Path.Combine(folder, "gate-result.json"), JsonSerializer.Serialize(new { generatorInputs = 1, dispatches = 0, acceptedRoutes = 7, invalidFactoryWholeCompositionRejected = true, diagnostics = diagnostics.Select(d => new { d.Id, message = d.GetMessage() }), registration }));
    }

    [Fact]
    public async Task AllSession_eight_cases_and_two_interleaves_preserve_acquisition_cleanup_context_and_ownership()
    {
        var source = AllSessionSource(false); var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "AllSessionRuntime_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        var e = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR"); var folder = string.IsNullOrEmpty(e) ? null : Path.Combine(e, "all-session-runtime"); SaveSendAllSync(folder, source, driver, output, diagnostics);
        Assert.Empty(diagnostics); Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)); var registration = AssertAllSessionComposition(output);
        Assert.Equal(7, driver.GetRunResult().Results.Single().GeneratedSources.Count(f => IsCanonicalRoute(f))); using var bytes = new MemoryStream(); var emitted = output.Emit(bytes); Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        if (folder != null) { File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray()); File.WriteAllText(Path.Combine(folder, "registration.json"), JsonSerializer.Serialize(registration)); }
        bytes.Position = 0; var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes); var run = assembly.GetType("AllMix.Bridge")!.GetMethod("Run")!.CreateDelegate<Func<Task<string>>>(); var result = await run().WaitAsync(TimeSpan.FromSeconds(20));
        if (folder != null) File.WriteAllText(Path.Combine(folder, "runtime.json"), result); using var json = JsonDocument.Parse(result); Assert.Equal(8, json.RootElement.GetProperty("scenarios").GetInt32()); Assert.Equal(18, json.RootElement.GetProperty("dispatches").GetInt32());
    }
}
