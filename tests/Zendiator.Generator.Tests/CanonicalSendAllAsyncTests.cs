using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private static string SendAllAsyncModule(bool isVoid)
    {
        var handlers = string.Join("\n", Enumerable.Range(1, 3).Select(id =>
            "[ZendiatorHandler]public sealed class H" + id + ":IRequestHandler<Ping" + (isVoid ? "" : ",int") + ">,IAsyncDisposable{" +
            "private readonly Probe p;public H" + id + "(Probe p){this.p=p;p.Resolved.Add(\"H" + id + "\");}" +
            "public " + (isVoid ? "ValueTask" : "ValueTask<int>") + " HandleAsync(Ping r,CancellationToken t)=>p." + (isVoid ? "BodyVoid" : "Body") + "(r," + id + ",r.Value,r.Shared,t);" +
            "public ValueTask DisposeAsync(){p.Ended.Add(\"H" + id + "\");return default;}}"));
        return """
            namespace AllAsyncCases.__NS__ {
            public sealed record Ping(int Value,SharedData Shared):__MARKER__;
            public sealed class Hook:IAsyncSendRequestTransform<Ping>,__SUCCESS__,IAsyncSendExceptionObserver<Ping>,IAsyncSendFinallyObserver<Ping>,IAsyncDisposable{
                private readonly Probe p;public Hook(Probe p){this.p=p;p.Resolved.Add("K");}
                public ValueTask<Ping> BeforeAsync(Ping r,CancellationToken t){p.Before(r,r.Value,r.Shared,t);var next=new Ping(r.Value+10,r.Shared);p.Saved=next;return new(next);}
                public __RET__ AfterAsync(Ping r,__ARG__CancellationToken t)=>p.__AFTER__(r,r.Value,r.Shared,__VALUE__t);
                public ValueTask OnExceptionAsync(Ping r,Exception e,CancellationToken t)=>p.Observe(r,r.Value,r.Shared,e,t);
                public ValueTask FinallyAsync(Ping r,SendExitInfo e,CancellationToken t)=>p.Finally(r,r.Value,r.Shared,e,t);
                public ValueTask DisposeAsync(){p.Ended.Add("K");return default;}
            }
            __HANDLERS__
            public static class Reference {
                public static __ALL_RET__ Run(IServiceProvider p,Ping request,CancellationToken t){ArgumentNullException.ThrowIfNull(request);return All(p,request,t);}
                private static async __ALL_RET__ All(IServiceProvider p,Ping request,CancellationToken t){
                    t.ThrowIfCancellationRequested();__ARRAY__
                    foreach(var id in new[]{2,1,3}){__ASSIGN__await Branch(p,request,id,t).ConfigureAwait(false);}
                    __ALL_RETURN__
                }
                private static async __RET__ Branch(IServiceProvider p,Ping request,int id,CancellationToken t){
                    ArgumentNullException.ThrowIfNull(request);t.ThrowIfCancellationRequested();var hook=p.GetRequiredService<Hook>();
                    var saved=request;bool success=false;Exception? error=null;
                    try{saved=await hook.BeforeAsync(saved,t).ConfigureAwait(false);ArgumentNullException.ThrowIfNull(saved);t.ThrowIfCancellationRequested();
                        __BODY__
                        __TRANSFORM__await hook.AfterAsync(saved,__RESPONSE__t).ConfigureAwait(false);success=true;__RETURN__
                    }catch(Exception failure){error=failure;try{await hook.OnExceptionAsync(saved,failure,t).ConfigureAwait(false);}catch(Exception replacement){error=replacement;throw;}throw;}
                    finally{await hook.FinallyAsync(saved,new SendExitInfo(success,error),t).ConfigureAwait(false);}
                }
            }
            __OPAQUE__
            }
            """.Replace("__NS__", isVoid ? "Command" : "Response").Replace("__MARKER__", isVoid ? "IRequest" : "IRequest<int>")
            .Replace("__SUCCESS__", isVoid ? "IAsyncSendSuccessObserver<Ping>" : "IAsyncSendResponseTransform<Ping,int>")
            .Replace("__RET__", isVoid ? "ValueTask" : "ValueTask<int>").Replace("__ARG__", isVoid ? "" : "int value,")
            .Replace("__AFTER__", isVoid ? "AfterVoid" : "After").Replace("__VALUE__", isVoid ? "" : "value,").Replace("__HANDLERS__", handlers)
            .Replace("__ALL_RET__", isVoid ? "ValueTask" : "ValueTask<int[]>")
            .Replace("__ARRAY__", isVoid ? "" : "var results=new int[3];var index=0;").Replace("__ASSIGN__", isVoid ? "" : "results[index++]=")
            .Replace("__ALL_RETURN__", isVoid ? "" : "return results;")
            .Replace("__BODY__", (isVoid ? "" : "var response=") + "await (id switch{2=>p.GetRequiredService<H2>().HandleAsync(saved,t),1=>p.GetRequiredService<H1>().HandleAsync(saved,t),_=>p.GetRequiredService<H3>().HandleAsync(saved,t)}).ConfigureAwait(false);")
            .Replace("__TRANSFORM__", isVoid ? "" : "response=").Replace("__RESPONSE__", isVoid ? "" : "response,").Replace("__RETURN__", isVoid ? "" : "return response;")
            .Replace("__OPAQUE__", isVoid ? "" : "public sealed class Opaque:IAsyncSendWrapper<Ping,int>{public ValueTask<int> InvokeAsync<TNext>(Ping r,TNext next,CancellationToken t)where TNext:IAsyncSendContinuation<Ping,int> =>next.InvokeAsync(r,t);}");
    }

    private const string SendAllAsyncProbeSource = """
        namespace AllAsyncCases {
        public sealed class SharedData{public int Version;public int[] Values=new[]{3,4,5,-9};}
        public sealed class Probe {
            public static readonly AsyncLocal<string?> Ambient=new();
            public readonly List<string> Trace=new(),Contexts=new(),Resolved=new(),Ended=new();public readonly List<int> Handlers=new();
            public readonly CancellationTokenSource Cancellation=new();public readonly Exception Failure=new FormatException("planned");
            public readonly SharedData Shared=new();public object? Original,Saved;public string Case="";public int Branch;
            public Zendiator.Generator.Tests.IAsyncOnceGate? Gate;
            public static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
            public string Error(Exception? e)=>e==null?"none":ReferenceEquals(e,Failure)?"sentinel":e is OperationCanceledException c&&c.CancellationToken==Cancellation.Token?"cancel":e.GetType().Name;
            private void SavedRequest(object r,int value,SharedData shared,CancellationToken t){Check(ReferenceEquals(r,Saved)&&value==11&&ReferenceEquals(shared,Shared)&&t==Cancellation.Token,"saved request/shared/token identity");}
            private void Hit(string phase){Contexts.Add(phase+":"+Branch+"|"+Ambient.Value+"|"+(SynchronizationContext.Current?.GetType().FullName??"none"));Ambient.Value=phase+":"+Branch;}
            public void Before(object r,int value,SharedData shared,CancellationToken t){
                Check(ReferenceEquals(r,Original)&&value==1&&ReferenceEquals(shared,Shared)&&t==Cancellation.Token,"each branch starts with original");
                Branch++;Hit("before");Trace.Add("before:"+Branch+":"+shared.Version);
            }
            private int BodyCore(object r,int id,int value,SharedData shared,CancellationToken t){
                SavedRequest(r,value,shared,t);Hit("body");Handlers.Add(id);Trace.Add("body:H"+id+":"+shared.Version);
                if(Case=="body"&&id==1)throw Failure;var response=value+shared.Values[id-1];shared.Version++;return response;
            }
            public ValueTask<int> Body(object r,int id,int value,SharedData shared,CancellationToken t){
                var response=BodyCore(r,id,value,shared,t);if(Case!="pending-response"||id!=1)return new(response);
                var gate=new Zendiator.Generator.Tests.AsyncOnceGate<int>(response);Gate=gate;return gate.Task;
            }
            public ValueTask BodyVoid(object r,int id,int value,SharedData shared,CancellationToken t){BodyCore(r,id,value,shared,t);return default;}
            private void AfterCore(object r,int value,SharedData shared,CancellationToken t){SavedRequest(r,value,shared,t);Hit("after");Trace.Add("after:"+Branch+":"+shared.Version);if(Case=="after"&&Branch==2)throw Failure;}
            public ValueTask<int> After(object r,int value,SharedData shared,int response,CancellationToken t){AfterCore(r,value,shared,t);return new(response+100);}
            public ValueTask AfterVoid(object r,int value,SharedData shared,CancellationToken t){AfterCore(r,value,shared,t);return default;}
            public ValueTask Observe(object r,int value,SharedData shared,Exception error,CancellationToken t){SavedRequest(r,value,shared,t);Hit("exception");Trace.Add("exception:"+Branch+":"+Error(error));return default;}
            public ValueTask Finally(object r,int value,SharedData shared,SendExitInfo exit,CancellationToken t){
                SavedRequest(r,value,shared,t);Hit("finally");Trace.Add("finally:"+Branch+":"+exit.Success+":"+Error(exit.Error));
                if(Case=="finally"&&Branch==2)throw Failure;if(Case=="between-cancel"&&Branch==1)Cancellation.Cancel();
                if(Case!="pending-void"||Branch!=2)return default;var gate=new Zendiator.Generator.Tests.AsyncOnceGate<int>(0);Gate=gate;return gate.Void;
            }
        }}
        """;

    private static string SendAllAsyncSource(bool gate)
    {
        var old = SendAllCompositionSource(false);
        old = old[..old.IndexOf("namespace AllMix {", StringComparison.Ordinal)];
        var config = SendAllCompositionConfiguration;
        var split = config.IndexOf("c.Pipeline<", StringComparison.Ordinal);
        config = "c.Handlers<D.Ping>(DispatchFamily.SendAllAsync,typeof(D.H2),typeof(D.H1),typeof(D.H3));\n" + config[..split] +
            "c.Handlers<E.Ping>(DispatchFamily.SendAllAsync,typeof(E.H2),typeof(E.H1),typeof(E.H3));\n" +
            "c.Pipeline<E.Ping>(DispatchFamily.SendAllAsync,Stage.Hook<E.Hook>());\n" + config[split..] +
            "\nc.Pipeline<D.Ping>(DispatchFamily.SendAllAsync,Stage.Hook<D.Hook>());";
        return old + SendAllAsyncProbeSource + SendAllAsyncModule(false) + SendAllAsyncModule(true) + """
            namespace AllMix {
            using System;using System.Collections.Generic;using System.Linq;using System.Text.Json;using System.Threading;using System.Threading.Tasks;
            using Zendiator;using Zendiator.DependencyInjection;using Microsoft.Extensions.DependencyInjection;using Zendiator.Generator.Tests;
            using B=global::AllSync.Response;using C=global::AllSync.Command;
            using S=global::Closed.AllMixStream;using R=global::Closed.AllMixSync;using A=global::Closed.AllMixAsync;
            using D=global::AllAsyncCases.Response;using E=global::AllAsyncCases.Command;using P=global::AllAsyncCases.Probe;
            [ZendiatorComposition("all-async-mix")]public sealed class Many;
            public static class Bridge {
                public static IServiceCollection Register(IServiceCollection s)=>s.AddZendiator<Many>(c=>{__CONFIG__});
                __CONSUMER__
            }
            __NEGATIVE__
            }
            """.Replace("__CONFIG__", config).Replace("__CONSUMER__", gate ? "" : SendAllAsyncConsumer)
            .Replace("__NEGATIVE__", gate ? """
                public sealed class RejectedOpaque:IAsyncSendWrapper<D.Ping,int>{
                    public ValueTask<int> InvokeAsync<TNext>(D.Ping r,TNext next,CancellationToken t)where TNext:IAsyncSendContinuation<D.Ping,int> =>next.InvokeAsync(r,t);
                }
                [ZendiatorComposition("all-async-rejected")]public sealed class Rejected;
                public static class RejectedBridge{
                    public static IServiceCollection RegisterRejected(IServiceCollection s)=>s.AddZendiator<Rejected>(c=>{__BAD__});
                } /* rejected end */
                """.Replace("__BAD__", config.Replace("Stage.Hook<D.Hook>()", "Stage.Hook<D.Hook>(),Stage.Wrapper<D.Opaque>(),Stage.Wrapper<RejectedOpaque>()")) : "");
    }

    private const string SendAllAsyncConsumer = """
        private sealed class EntryContext:SynchronizationContext;
        private static int dispatches;
        private static string State(bool completed,bool success,bool canceled,bool faulted)=>success?"Success":canceled?"Canceled":faulted?"Faulted":completed?"Completed":"Pending";
        private static async Task<(string Snapshot,object? Interleave)> Observe(string name,bool generated){
            var priorContext=SynchronizationContext.Current;SynchronizationContext.SetSynchronizationContext(new EntryContext());
            var p=new P{Case=name};var syncProbe=new AllSync.Probe();var streamProbe=new PublicValueRequestProbe();var unused=new ManagedShapeProbe();
            try{
                IServiceCollection services=new ServiceCollection();services.AddSingleton(p);services.AddSingleton(syncProbe);services.AddSingleton(streamProbe);services.AddSingleton(unused);
                services.AddScoped<D.Hook>(); services.AddScoped<E.Hook>();
                Register(services);
                var branches=new[]{typeof(D.Hook),typeof(D.H2),typeof(D.H1),typeof(D.H3),typeof(E.Hook),typeof(E.H2),typeof(E.H1),typeof(E.H3),typeof(B.Hook),typeof(B.H2),typeof(B.H1),typeof(B.H3),typeof(C.Hook),typeof(C.H2),typeof(C.H1),typeof(C.H3)};
                foreach(var type in branches){var d=services.Single(d=>d.ServiceType==type);P.Check(d.Lifetime==(type==typeof(D.Hook)||type==typeof(E.Hook)?ServiceLifetime.Scoped:ServiceLifetime.Transient)&&d.ImplementationType==type,"default branch registrations and shared hook overrides");}
                P.Check(services.GroupBy(d=>d.ServiceType).All(g=>g.Count()==1),"unique DI descriptors");
                await using var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});
                await using var scope=provider.CreateAsyncScope();var sp=scope.ServiceProvider;
                var mediator=sp.GetRequiredService<global::AllMix.Generated.Many.IZendiator>();
                P.Check(ReferenceEquals(mediator,sp.GetRequiredService<global::AllMix.Generated.Many.IZendiator>()),"shared composition mediator identity");
                var isVoid=name=="pending-void";object? request=isVoid?new E.Ping(1,p.Shared):new D.Ping(1,p.Shared);if(name=="null")request=null;p.Original=request;
                if(name=="pre-cancel")p.Cancellation.Cancel();
                P.Ambient.Value="caller";var syncThrow=false;var returned=false;var initial="sync-throw";Exception? error=null;int[]? results=null;
                ValueTask<int[]> responseTask=default;ValueTask voidTask=default;object? interleave=null;
                try{
                    dispatches++;
                    if(isVoid){voidTask=generated?mediator.SendAllAsync((E.Ping)request!,p.Cancellation.Token):E.Reference.Run(sp,(E.Ping)request!,p.Cancellation.Token);initial=State(voidTask.IsCompleted,voidTask.IsCompletedSuccessfully,voidTask.IsCanceled,voidTask.IsFaulted);}
                    else{responseTask=generated?mediator.SendAllAsync((D.Ping)request!,p.Cancellation.Token):D.Reference.Run(sp,(D.Ping)request!,p.Cancellation.Token);initial=State(responseTask.IsCompleted,responseTask.IsCompletedSuccessfully,responseTask.IsCanceled,responseTask.IsFaulted);}
                    returned=true;
                }catch(Exception failure){syncThrow=true;error=failure;}
                P.Check(P.Ambient.Value=="caller","callback ambient does not leak to caller at return");
                object? waiting=null;
                if(name.StartsWith("pending-",StringComparison.Ordinal)){
                    var gate=p.Gate!;P.Check(initial=="Pending"&&gate.Consumptions==0&&p.Branch==2&&p.Handlers.SequenceEqual(new[]{2,1}),"waiting branch blocks following handler");
                    P.Check(p.Resolved.SequenceEqual(new[]{"K","H2","H1"})&&p.Ended.Count==0,"later branch neither resolved nor disposed while pending");
                    P.Check(p.Trace.Count==(isVoid?8:6),"pending phase and callback count");
                    waiting=new{p.Branch,handlers=p.Handlers.ToArray(),resolved=p.Resolved.ToArray(),trace=p.Trace.ToArray(),contexts=p.Contexts.ToArray(),consumptions=gate.Consumptions,caller=P.Ambient.Value};
                    if(generated&&name=="pending-response"){
                        var shared=new AllSync.SharedData();var r=new B.Ping(1,shared);syncProbe.Original=r;dispatches++;
                        var copied=mediator.SendAll(r,shared.Values,syncProbe.Cancellation.Token);
                        P.Check(copied==3&&shared.Values.SequenceEqual(new[]{115,114,116,-9}),"interleaved accepted Sync branch response");
                        var values=new List<int>();dispatches++;await foreach(var item in mediator.StreamAsync(new S.Items(new S.SharedState())))values.Add(item);
                        P.Check(values.SequenceEqual(new[]{2,4})&&streamProbe.Starts==1&&streamProbe.SourceDisposals==1,"interleaved accepted Stream");
                        P.Check(p.Branch==2&&p.Gate==gate&&gate.Consumptions==0&&p.Resolved.Count==3&&p.Ended.Count==0&&p.Trace.Count==6,"interleaved routes do not alter pending branch");
                        interleave=new{copied,destination=shared.Values,values,handlers=syncProbe.Handlers.ToArray(),p.Branch,pendingConsumptions=gate.Consumptions};
                    }
                    P.Ambient.Value="completer";gate.Complete();P.Ambient.Value="caller";
                }
                if(returned)try{if(isVoid)await voidTask;else results=await responseTask;}catch(Exception failure){error=failure;}
                P.Check(P.Ambient.Value=="caller","continuation ambient does not leak to caller");
                var final=returned?(isVoid?State(voidTask.IsCompleted,voidTask.IsCompletedSuccessfully,voidTask.IsCanceled,voidTask.IsFaulted):State(responseTask.IsCompleted,responseTask.IsCompletedSuccessfully,responseTask.IsCanceled,responseTask.IsFaulted)):"sync-throw";
                if(name=="null"){P.Check(syncThrow&&error is ArgumentNullException a&&a.ParamName=="request"&&p.Resolved.Count==0&&p.Trace.Count==0,"null request synchronous guard before DI");}
                else if(name=="pre-cancel"||name=="between-cancel"){
                    P.Check(!syncThrow&&returned&&final=="Canceled"&&p.Error(error)=="cancel","cancellation ValueTask state and token identity");
                    P.Check(name=="pre-cancel"?p.Branch==0&&p.Resolved.Count==0:p.Branch==1&&p.Handlers.SequenceEqual(new[]{2}),"cancellation stops subsequent branches");
                }else if(name=="body"||name=="after"||name=="finally"){
                    P.Check(!syncThrow&&returned&&final=="Faulted"&&ReferenceEquals(error,p.Failure)&&p.Branch==2&&p.Handlers.SequenceEqual(new[]{2,1}),"branch failure identity and later stop");
                }else{
                    P.Check(error==null&&!syncThrow&&final=="Success"&&p.Branch==3&&p.Handlers.SequenceEqual(new[]{2,1,3})&&p.Shared.Version==3,"all successful branches in declared order");
                    P.Check(isVoid?results==null:results!.SequenceEqual(new[]{115,114,116}),"response/void shape and result order");
                }
                if(p.Gate!=null)P.Check(p.Gate.Consumptions==1,"pending ValueTask consumed exactly once");
                P.Check(p.Shared.Values.SequenceEqual(new[]{3,4,5,-9}),"async result aggregation does not overwrite request data");
                P.Check(p.Ended.Count==0,"operations do not dispose borrowed receivers");
                P.Check(p.Contexts.Where(x=>x.StartsWith("before:",StringComparison.Ordinal)).All(x=>x.Contains("|caller|",StringComparison.Ordinal)),"each branch enters original outer execution context");
                await scope.DisposeAsync();P.Check(p.Ended.SequenceEqual(p.Resolved.AsEnumerable().Reverse()),"scope disposes each borrowed receiver once");
                var snapshot=JsonSerializer.Serialize(new{name,syncThrow,returned,initial,final,error=p.Error(error),parameter=(error as ArgumentNullException)?.ParamName,results,
                    p.Trace,p.Contexts,p.Resolved,p.Ended,p.Handlers,p.Branch,version=p.Shared.Version,waiting,consumptions=p.Gate?.Consumptions??0,caller=P.Ambient.Value});
                return(snapshot,interleave);
            }finally{P.Ambient.Value=null;p.Cancellation.Dispose();syncProbe.Cancellation.Dispose();unused.Token.Dispose();SynchronizationContext.SetSynchronizationContext(priorContext);}
        }
        public static async Task<string> Run(){
            dispatches=0;var rows=new List<object>();object? interleave=null;
            foreach(var name in new[]{"completed","pending-response","pending-void","null","pre-cancel","body","after","finally","between-cancel"}){
                var g=await Observe(name,true);var r=await Observe(name,false);
                P.Check(g.Snapshot==r.Snapshot,"generated/reference branch mismatch: "+name+"\n"+g.Snapshot+"\n"+r.Snapshot);
                if(g.Interleave!=null)interleave=g.Interleave;
                rows.Add(new{name,generated=JsonDocument.Parse(g.Snapshot).RootElement.Clone(),reference=JsonDocument.Parse(r.Snapshot).RootElement.Clone()});
            }
            P.Check(dispatches==20,"fixed dispatch budget");
            return JsonSerializer.Serialize(new{generatorInputs=1,scenarios=9,pairedDispatches=18,interleaveDispatches=2,dispatches,interleave,rows});
        }
        """;

    private static object AssertSendAllAsyncComposition(Compilation output)
    {
        AssertSendAllCompositionContract(output);
        var contract = output.GetTypeByMetadataName("AllMix.Generated.Many.IZendiator")!;
        var methods = contract.GetMembers("SendAllAsync").OfType<IMethodSymbol>().ToArray();
        Assert.Equal(2, methods.Length);
        Assert.Contains(methods, m => m.ReturnType is INamedTypeSymbol { Name: "ValueTask", TypeArguments.Length: 1 } t && t.TypeArguments[0] is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Int32 });
        Assert.Contains(methods, m => m.ReturnType is INamedTypeSymbol { Name: "ValueTask", TypeArguments.Length: 0 });
        var classes = output.SyntaxTrees.SelectMany(t => t.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()).ToArray();
        var registrar = Assert.Single(classes, c => c.Identifier.ValueText == "ZendiatorGeneratedRegistrar");
        var calls = registrar.DescendantNodes().OfType<InvocationExpressionSyntax>().Where(c => c.Expression is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "expected" }, Name: GenericNameSyntax }).ToArray();
        var handlers = calls.Where(c => ((MemberAccessExpressionSyntax)c.Expression).Name.Identifier.ValueText == "Handlers").ToArray();
        var pipelines = calls.Where(c => ((MemberAccessExpressionSyntax)c.Expression).Name.Identifier.ValueText == "Pipeline").ToArray();
        string Request(InvocationExpressionSyntax c) => ((GenericNameSyntax)((MemberAccessExpressionSyntax)c.Expression).Name).TypeArgumentList.Arguments[0].ToString();
        Assert.Equal(new[] { "global::AllAsyncCases.Response.Ping", "global::AllSync.Response.Ping", "global::Closed.AllMixAsync.Ping", "global::Closed.AllMixStream.Items", "global::AllSync.Command.Ping", "global::Closed.AllMixSync.Ping", "global::AllAsyncCases.Command.Ping" }, handlers.Select(Request));
        Assert.Equal(new[] { "global::AllAsyncCases.Command.Ping", "global::Closed.AllMixStream.Items", "global::AllSync.Command.Ping", "global::Closed.AllMixSync.Ping", "global::AllSync.Response.Ping", "global::Closed.AllMixAsync.Ping", "global::AllAsyncCases.Response.Ping" }, pipelines.Select(Request));
        foreach (var ns in new[] { "AllSync.Response", "AllSync.Command", "AllAsyncCases.Response", "AllAsyncCases.Command" })
        {
            var call = Assert.Single(handlers, c => Request(c) == "global::" + ns + ".Ping");
            Assert.Equal(new[] { "global::" + ns + ".H2", "global::" + ns + ".H1", "global::" + ns + ".H3" }, call.ArgumentList.Arguments.Skip(1).Select(a => Assert.IsType<TypeOfExpressionSyntax>(a.Expression).Type.ToString()));
            Assert.EndsWith(ns.StartsWith("AllAsyncCases", StringComparison.Ordinal) ? "SendAllAsync" : "SendAllSync", call.ArgumentList.Arguments[0].ToString(), StringComparison.Ordinal);
        }
        var members = classes.Where(c => c.Identifier.ValueText == "Zendiator").SelectMany(c => c.Members).ToArray();
        var mediator = output.GetTypeByMetadataName("AllMix.Generated.Many.Zendiator")!;
        var names = mediator.GetMembers().OfType<IMethodSymbol>().Where(m => m.DeclaredAccessibility == Accessibility.Private &&
            m.Name.StartsWith("Branch", StringComparison.Ordinal)).Select(m => m.Name).ToArray();
        Assert.Equal(12, names.Length);
        foreach (var entry in mediator.GetMembers("SendAll").Concat(mediator.GetMembers("SendAllAsync")).OfType<IMethodSymbol>().GroupBy(CanonicalEntrySuffix).Select(g => g.First()))
        {
            var local = CanonicalEntryMethods(entry, "Branch").Select(m => m.Name).ToArray();
            Assert.Equal(3, local.Length);
            Assert.Equal(local.Length, local.Distinct(StringComparer.Ordinal).Count());
        }
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
        Assert.Single(members.OfType<ConstructorDeclarationSyntax>());
        return new
        {
            handlerOrder = handlers.Select(Request),
            pipelineOrder = pipelines.Select(Request),
            privateBranchNames = names
        };
    }

    [Fact]
    public void SendAllAsync_gate_accepts_response_void_in_common_composition_and_rejects_multiple_wrapper_route_as_a_whole()
    {
        var source = SendAllAsyncSource(true);
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "AllAsyncGate_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        var folder = string.IsNullOrEmpty(evidence) ? null : Path.Combine(evidence, "sendall-async-gate");
        SaveSendAllSync(folder, source, driver, output, diagnostics);
        Assert.Equal(7, diagnostics.Length);
        Assert.All(diagnostics, d => Assert.Equal("ZEN0037", d.Id));
        var start = source.IndexOf("public static IServiceCollection RegisterRejected(", StringComparison.Ordinal);
        var end = source.IndexOf("/* rejected end */", start, StringComparison.Ordinal);
        Assert.All(diagnostics, d => Assert.InRange(d.Location.SourceSpan.Start, start, end));
        var anchors = diagnostics.Select(d => source.Substring(d.Location.SourceSpan.Start, d.Location.SourceSpan.Length)).ToArray();
        foreach (var request in new[] { "D.Ping", "E.Ping", "S.Items", "C.Ping", "R.Ping", "B.Ping", "A.Ping" })
            Assert.Single(anchors, a => a.StartsWith("c.Pipeline<" + request + ">", StringComparison.Ordinal));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        var registration = AssertSendAllAsyncComposition(output);
        Assert.Null(output.GetTypeByMetadataName("AllMix.Generated.Rejected.IZendiator"));
        Assert.Null(output.GetTypeByMetadataName("AllMix.Generated.Rejected.ZendiatorGeneratedRegistrar"));
        var files = driver.GetRunResult().Results.Single().GeneratedSources;
        Assert.Contains(files, f => f.HintName.EndsWith("Interceptors.g.cs", StringComparison.Ordinal) && f.SourceText.ToString().Contains("AllMix.Generated.Many.ZendiatorGeneratedRegistrar", StringComparison.Ordinal));
        Assert.DoesNotContain(files, f => f.HintName.EndsWith("Interceptors.g.cs", StringComparison.Ordinal) && f.SourceText.ToString().Contains("AllMix.Generated.Rejected.ZendiatorGeneratedRegistrar", StringComparison.Ordinal));
        if (folder != null)
            File.WriteAllText(Path.Combine(folder, "gate-result.json"), JsonSerializer.Serialize(new
            {
                generatorInputs = 1,
                dispatches = 0,
                acceptedRoutes = 7,
                rejectedPipelineDiagnostics = 7,
                registration
            }));
    }

    [Fact]
    public async Task SendAllAsync_nine_cases_and_two_interleaves_preserve_pending_context_status_order_and_scope_ownership()
    {
        var source = SendAllAsyncSource(false);
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "AllAsyncRuntime_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        var folder = string.IsNullOrEmpty(evidence) ? null : Path.Combine(evidence, "sendall-async-runtime");
        SaveSendAllSync(folder, source, driver, output, diagnostics);
        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        var registration = AssertSendAllAsyncComposition(output);
        Assert.Equal(7, driver.GetRunResult().Results.Single().GeneratedSources.Count(f => IsCanonicalRoute(f)));
        using var bytes = new MemoryStream();
        var emitted = output.Emit(bytes);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        if (folder != null)
        {
            File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
            File.WriteAllText(Path.Combine(folder, "registration.json"), JsonSerializer.Serialize(registration));
        }
        bytes.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var run = assembly.GetType("AllMix.Bridge")!.GetMethod("Run")!.CreateDelegate<Func<Task<string>>>();
        var result = await run().WaitAsync(TimeSpan.FromSeconds(20));
        if (folder != null)
            File.WriteAllText(Path.Combine(folder, "runtime.json"), result);
        using var observed = JsonDocument.Parse(result);
        Assert.Equal(9, observed.RootElement.GetProperty("scenarios").GetInt32());
        Assert.Equal(20, observed.RootElement.GetProperty("dispatches").GetInt32());
    }
}
