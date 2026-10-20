using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;
using Zendiator.SourceGenerator;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private static string OpenAllAsyncModule(bool isVoid)
    {
        var ns = isVoid ? "OpenAllAsyncVoid" : "OpenAllAsyncResponse";
        var request = "Request<TRight,TLeft>";
        var contract = isVoid ? "" : ",int";
        var result = isVoid ? "ValueTask" : "ValueTask<int>";
        var outer = isVoid ? "ValueTask" : "ValueTask<int[]>";
        return $$"""
            namespace {{ns}} {
            public sealed record Request<TLeft,TRight>(int Value):IRequest{{(isVoid ? "" : "<int>")}};
            [ZendiatorHandler]public sealed class ZHandler<TLeft,TRight>:IRequestHandler<{{request}}{{contract}}>,IDisposable where TLeft:class,new() where TRight:class {
                private readonly AllAsyncFixture.Probe p;public ZHandler(AllAsyncFixture.Probe p){this.p=p;p.Created.Add(GetType());}
                public {{result}} HandleAsync({{request}} r,CancellationToken t){p.Body("Z",r,r.Value,t);return p.{{(isVoid ? "Done(\"handler\")" : "Value(\"handler\",r.Value+20)")}};}
                public void Dispose(){p.Disposed.Add(GetType());}
            }
            [ZendiatorHandler]public sealed class AHandler<TLeft,TRight>:IRequestHandler<Request<TLeft,TRight>{{contract}}>,IDisposable where TLeft:class,OpenAllFixture.ISecond,new() where TRight:class {
                private readonly AllAsyncFixture.Probe p;public AHandler(AllAsyncFixture.Probe p){this.p=p;p.Created.Add(GetType());}
                public {{result}} HandleAsync(Request<TLeft,TRight> r,CancellationToken t){p.Body("A",r,r.Value,t);return p.{{(isVoid ? "Done(\"handler\")" : "Value(\"handler\",r.Value+10)")}};}
                public void Dispose(){p.Disposed.Add(GetType());}
            }
            public sealed class Hook<TLeft,TRight>:IAsyncSendRequestTransform<{{request}}>,{{(isVoid ? "IAsyncSendSuccessObserver<" + request + ">" : "IAsyncSendResponseTransform<" + request + ",int>")}},IAsyncSendExceptionObserver<{{request}}>,IAsyncSendFinallyObserver<{{request}}>,IDisposable where TLeft:class,OpenAllFixture.IHook where TRight:class {
                private readonly AllAsyncFixture.Probe p;public Hook(AllAsyncFixture.Probe p){this.p=p;p.Created.Add(GetType());}
                public ValueTask<{{request}}> BeforeAsync({{request}} r,CancellationToken t){p.Branch++;p.Hit("before",r,r.Value,t);AllAsyncFixture.Probe.Check(r.Value==7,"original request for each branch");var saved=new {{request}}(r.Value+1);p.Saved=saved;return p.Value("before",saved);}
                public {{result}} AfterAsync({{request}} r,{{(isVoid ? "" : "int value,")}}CancellationToken t){p.Hit("after",r,r.Value,t);return p.{{(isVoid ? "Done(\"after\")" : "Value(\"after\",value+100)")}};}
                public ValueTask OnExceptionAsync({{request}} r,Exception e,CancellationToken t){AllAsyncFixture.Probe.Check(ReferenceEquals(e,p.Failure),"Exception identity");p.Hit("exception",r,r.Value,t);return p.Done("exception");}
                public ValueTask FinallyAsync({{request}} r,SendExitInfo e,CancellationToken t){var failed=p.Mode=="oce"&&p.Branch==2;AllAsyncFixture.Probe.Check(e.Success!=failed&&ReferenceEquals(e.Error,failed?p.Failure:null),"Finally identity/outcome");p.Hit("finally",r,r.Value,t);return p.Done("finally");}
                public void Dispose(){p.Disposed.Add(GetType());}
            }
            [ZendiatorComposition("{{ns}}")]public sealed class Composition;
            public static class Bridge {
                public static IServiceCollection Register(IServiceCollection s)=>s.AddZendiator<Composition>(c=>{
                    c.Handlers(typeof(Request<,>),DispatchFamily.SendAllAsync,typeof(ZHandler<,>),typeof(AHandler<,>));
                    c.Pipeline(typeof(Request<,>),DispatchFamily.SendAllAsync,Stage.Hook(typeof(Hook<,>)));
                });
                private static {{outer}} Reference<TLeft,TRight>(IServiceProvider sp,Request<TLeft,TRight> request,CancellationToken t) where TLeft:class,OpenAllFixture.ISecond,new() where TRight:class,OpenAllFixture.IHook,new(){ArgumentNullException.ThrowIfNull(request);return Execute(sp,request,t);}
                private static async {{outer}} Execute<TLeft,TRight>(IServiceProvider sp,Request<TLeft,TRight> request,CancellationToken t) where TLeft:class,OpenAllFixture.ISecond,new() where TRight:class,OpenAllFixture.IHook,new(){
                    t.ThrowIfCancellationRequested();{{(isVoid ? "await Branch(sp,request,0,t).ConfigureAwait(false);await Branch(sp,request,1,t).ConfigureAwait(false);" : "var first=await Branch(sp,request,0,t).ConfigureAwait(false);var second=await Branch(sp,request,1,t).ConfigureAwait(false);return new[]{first,second};")}}
                }
                // Separate async boundary, matching the user-visible context contract independently of generator IR.
                private static async {{result}} Branch<TLeft,TRight>(IServiceProvider sp,Request<TLeft,TRight> request,int branch,CancellationToken t) where TLeft:class,OpenAllFixture.ISecond,new() where TRight:class,OpenAllFixture.IHook,new(){
                    ArgumentNullException.ThrowIfNull(request);t.ThrowIfCancellationRequested();var f=sp.GetRequiredService<Hook<TRight,TLeft>>();var saved=request;bool success=false;Exception? error=null;
                    try{saved=await f.BeforeAsync(saved,t).ConfigureAwait(false);ArgumentNullException.ThrowIfNull(saved);t.ThrowIfCancellationRequested();
                        {{(isVoid ? "if(branch==0)await sp.GetRequiredService<ZHandler<TRight,TLeft>>().HandleAsync(saved,t).ConfigureAwait(false);else await sp.GetRequiredService<AHandler<TLeft,TRight>>().HandleAsync(saved,t).ConfigureAwait(false);await f.AfterAsync(saved,t).ConfigureAwait(false);success=true;" : "var value=branch==0?await sp.GetRequiredService<ZHandler<TRight,TLeft>>().HandleAsync(saved,t).ConfigureAwait(false):await sp.GetRequiredService<AHandler<TLeft,TRight>>().HandleAsync(saved,t).ConfigureAwait(false);value=await f.AfterAsync(saved,value,t).ConfigureAwait(false);success=true;return value;")}}
                    }catch(Exception e){error=e;try{await f.OnExceptionAsync(saved,e,t).ConfigureAwait(false);}catch(Exception replacement){error=replacement;throw;}throw;}
                    finally{await f.FinallyAsync(saved,new(success,error),t).ConfigureAwait(false);}
                }
                public static async Task<string> Observe<TLeft,TRight>(bool generated,string mode) where TLeft:class,OpenAllFixture.ISecond,new() where TRight:class,OpenAllFixture.IHook,new(){
                    var p=new AllAsyncFixture.Probe{Mode=mode};using var token=p.Cancellation;IServiceCollection s=new ServiceCollection();s.AddSingleton(p);s.AddScoped(typeof(ZHandler<,>));var existing=s.Last();Register(s);var count=s.Count;Register(s);
                    AllAsyncFixture.Probe.Check(s.Count==count&&s.Contains(existing),"TryAdd/repeat snapshot");
                    foreach(var definition in new[]{typeof(ZHandler<,>),typeof(AHandler<,>),typeof(Hook<,>)})AllAsyncFixture.Probe.Check(s.Count(d=>d.ServiceType==definition&&d.ImplementationType==definition&&d.Lifetime==ServiceLifetime.Scoped)==1,"all ordered open definitions registered");
                    using var provider=s.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});using var scope=provider.CreateScope();var sp=scope.ServiceProvider;var m=sp.GetRequiredService<{{ns}}.Generated.Composition.IZendiator>();var request=new Request<TLeft,TRight>(7);p.Original=request;AllAsyncFixture.Probe.Ambient.Value="caller";
                    var operation=generated?m.SendAllAsync(request,token.Token):Reference(sp,request,token.Token);var sync=operation.IsCompletedSuccessfully;
                    AllAsyncFixture.Probe.Check(AllAsyncFixture.Probe.Ambient.Value=="caller","public context boundary");
                    var publicConsumptions=1;var task=operation.AsTask();
                    if(mode=="pending"){
                        AllAsyncFixture.Probe.Check(!sync&&!task.IsCompleted&&p.Branch==1&&p.Handlers.SequenceEqual(new[]{"Z"})&&p.Created.SequenceEqual(new[]{typeof(Hook<TRight,TLeft>),typeof(ZHandler<TRight,TLeft>)})&&p.Pending!=null&&p.Pending.Consumptions==0,"first Finally await blocks second branch entry/resolution");
                        AllAsyncFixture.Probe.Ambient.Value="completer";p.Pending!.Complete();AllAsyncFixture.Probe.Ambient.Value="caller";
                    }else if(mode=="sync")AllAsyncFixture.Probe.Check(sync,"native void IsCompletedSuccessfully");
                    int[]? result=null;OperationCanceledException? caught=null;
                    try{ {{(isVoid ? "await task.WaitAsync(TimeSpan.FromSeconds(5));" : "result=await task.WaitAsync(TimeSpan.FromSeconds(5));")}} }catch(OperationCanceledException e){caught=e;}
                    AllAsyncFixture.Probe.Check(mode=="oce"?task.IsCanceled&&caught?.CancellationToken==token.Token&&result==null:task.IsCompletedSuccessfully&&caught==null,"Canceled/token/no partial result");
                    AllAsyncFixture.Probe.Check(AllAsyncFixture.Probe.Ambient.Value=="caller","caller context after completion");
                    AllAsyncFixture.Probe.Check(p.Branch==2&&p.Handlers.SequenceEqual(new[]{"Z","A"}),"ordered branches and single handler calls");
                    var trace=new[]{"before:1:7","handler:Z:1:8","after:1:8","finally:1:8","before:2:7","handler:A:2:8",(mode=="oce"?"exception:2:8":"after:2:8"),"finally:2:8"};AllAsyncFixture.Probe.Check(p.Trace.SequenceEqual(trace),"branch-local unwind and original request");
                    AllAsyncFixture.Probe.Check(p.AmbientSeen.SequenceEqual(new[]{"caller","before","handler","after","caller","before","handler",mode=="oce"?"exception":"after"}),"Before context from caller,not prior branch or completer");
                    AllAsyncFixture.Probe.Check(p.Gates.Count==8&&p.Gates.All(g=>g.Gate.Consumptions==1),"each callback GetResult exactly once");
                    AllAsyncFixture.Probe.Check(p.Created.SequenceEqual(new[]{typeof(Hook<TRight,TLeft>),typeof(ZHandler<TRight,TLeft>),typeof(AHandler<TLeft,TRight>)}),"distinct closed DI receiver keys");
                    AllAsyncFixture.Probe.Check(p.Disposed.Count==0,"dispatch borrows services");scope.Dispose();AllAsyncFixture.Probe.Check(p.Disposed.SequenceEqual(p.Created.AsEnumerable().Reverse()),"scope owns all receivers");
                    {{(isVoid ? "" : "AllAsyncFixture.Probe.Check(mode==\"oce\"?result==null:result!.SequenceEqual(new[]{128,118}),\"ordered response array\");")}}
                    return JsonSerializer.Serialize(new{profile="{{ns}}",mode,result,sync,status=task.Status.ToString(),publicConsumptions,p.Branch,p.Handlers,p.Trace,p.AmbientSeen,gates=p.Gates.Select(g=>new{g.Phase,g.Gate.Consumptions}),created=p.Created.Select(t=>t.ToString()),disposed=p.Disposed.Select(t=>t.ToString()),registrations=count,oceToken=caught?.CancellationToken==token.Token});
                }
            } }
            """;
    }

    private static string OpenSendAllAsyncSource() => OpenSendAllSyncSource() + """
        namespace AllAsyncFixture {
        public sealed class Probe {
            public static readonly AsyncLocal<string?> Ambient=new();public readonly CancellationTokenSource Cancellation=new();public string Mode="";public int Branch;public object? Original,Saved;public Exception? Failure;public IAsyncOnceGate? Pending;
            public readonly List<string> Trace=new(),Handlers=new();public readonly List<string?> AmbientSeen=new();public readonly List<Type> Created=new(),Disposed=new();public readonly List<(string Phase,IAsyncOnceGate Gate)> Gates=new();
            public static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
            public void Hit(string phase,object r,int value,CancellationToken t){Check(t==Cancellation.Token&&ReferenceEquals(r,phase=="before"?Original:Saved),"token/saved request");Trace.Add(phase+":"+Branch+":"+value);AmbientSeen.Add(Ambient.Value);Ambient.Value=phase.StartsWith("handler",StringComparison.Ordinal)?"handler":phase;}
            public void Body(string name,object r,int value,CancellationToken t){Hit("handler:"+name,r,value,t);Handlers.Add(name);}
            public ValueTask<T> Value<T>(string phase,T value){var g=new AsyncOnceGate<T>(value);Gates.Add((phase+":"+Branch,g));if(Mode=="oce"&&Branch==2&&phase=="handler"){Cancellation.Cancel();Failure=new OperationCanceledException("second handler",Cancellation.Token);g.Fail(Failure);}else g.Complete();return g.Task;}
            public ValueTask Done(string phase){var g=new AsyncOnceGate<int>(0);Gates.Add((phase+":"+Branch,g));if(Mode=="pending"&&Branch==1&&phase=="finally")Pending=g;else g.Complete();return g.Void;}
        }
        public static class Entry {
            public static async Task<string> Run(){var rows=new[]{(await OpenAllAsyncResponse.Bridge.Observe<OpenAllFixture.A,OpenAllFixture.B>(true,"pending"),await OpenAllAsyncResponse.Bridge.Observe<OpenAllFixture.A,OpenAllFixture.B>(false,"pending")),(await OpenAllAsyncResponse.Bridge.Observe<OpenAllFixture.A,OpenAllFixture.B>(true,"oce"),await OpenAllAsyncResponse.Bridge.Observe<OpenAllFixture.A,OpenAllFixture.B>(false,"oce")),(await OpenAllAsyncVoid.Bridge.Observe<OpenAllFixture.A,OpenAllFixture.B>(true,"sync"),await OpenAllAsyncVoid.Bridge.Observe<OpenAllFixture.A,OpenAllFixture.B>(false,"sync"))};
                foreach(var row in rows)Probe.Check(row.Item1==row.Item2,"generated/reference mismatch\n"+row.Item1+"\n"+row.Item2);
                return JsonSerializer.Serialize(new{pairs=3,dispatches=6,rows=rows.Select(r=>new{generated=JsonDocument.Parse(r.Item1).RootElement.Clone(),reference=JsonDocument.Parse(r.Item2).RootElement.Clone()})});}
        } }
        """ + OpenAllAsyncModule(false) + OpenAllAsyncModule(true) + """
        namespace AllAsyncClosedSentinel {
        [ZendiatorComposition("closedAllAsyncResponse")]public sealed class Response;
        [ZendiatorComposition("closedAllAsyncVoid")]public sealed class Command;
        public static class Registration {
            public static IServiceCollection R(IServiceCollection s)=>s.AddZendiator<Response>(c=>{c.Handlers<OpenAllAsyncResponse.Request<OpenAllFixture.A,OpenAllFixture.B>>(DispatchFamily.SendAllAsync,typeof(OpenAllAsyncResponse.ZHandler<OpenAllFixture.B,OpenAllFixture.A>),typeof(OpenAllAsyncResponse.AHandler<OpenAllFixture.A,OpenAllFixture.B>));c.Pipeline<OpenAllAsyncResponse.Request<OpenAllFixture.A,OpenAllFixture.B>>(DispatchFamily.SendAllAsync,Stage.Hook<OpenAllAsyncResponse.Hook<OpenAllFixture.B,OpenAllFixture.A>>());});
            public static IServiceCollection V(IServiceCollection s)=>s.AddZendiator<Command>(c=>{c.Handlers<OpenAllAsyncVoid.Request<OpenAllFixture.A,OpenAllFixture.B>>(DispatchFamily.SendAllAsync,typeof(OpenAllAsyncVoid.ZHandler<OpenAllFixture.B,OpenAllFixture.A>),typeof(OpenAllAsyncVoid.AHandler<OpenAllFixture.A,OpenAllFixture.B>));c.Pipeline<OpenAllAsyncVoid.Request<OpenAllFixture.A,OpenAllFixture.B>>(DispatchFamily.SendAllAsync,Stage.Hook<OpenAllAsyncVoid.Hook<OpenAllFixture.B,OpenAllFixture.A>>());});
        } }
        """;

    private static string OpenSendAllAsyncNegative()
    {
        const string request = "OpenAllAsyncResponse.Request<,>"; const string first = "OpenAllAsyncResponse.ZHandler<,>"; const string second = "OpenAllAsyncResponse.AHandler<,>"; const string hook = "OpenAllAsyncResponse.Hook<,>";
        var declarations = new List<string>(); var registrations = new List<string>();
        void Reject(string name, string handlers, string stage, string extra = "", string r = request)
        {
            declarations.Add($"[ZendiatorComposition(\"{name}\")]public sealed class {name};");
            registrations.Add($"public static IServiceCollection {name}(IServiceCollection s)=>s.AddZendiator<{name}>(c=>{{c.Handlers(typeof({r}),DispatchFamily.SendAllAsync,{handlers});c.Pipeline(typeof({r}),DispatchFamily.SendAllAsync,{stage});{extra}}});");
        }
        var normal = $"typeof({first}),typeof({second})";
        Reject("Undetermined", $"typeof({first}),typeof(Unused<,,>)", $"Stage.Hook(typeof({hook}))");
        Reject("ConstraintConflict", $"typeof({first}),typeof(StructHandler<,>)", $"Stage.Hook(typeof({hook}))");
        Reject("PatternMismatch", $"typeof({first}),typeof(OtherHandler<,>)", $"Stage.Hook(typeof({hook}))");
        Reject("ResponseVoid", "typeof(DualVoid<,>),typeof(DualResult<,>)", "Stage.Hook(typeof(DualHook<,>))", r: "DualRequest<,>");
        Reject("Wrapper", normal, $"Stage.Wrapper(typeof({hook}))");
        Reject("HookSession", normal, $"Stage.Hook(typeof({hook}),typeof(Factory<,>))");
        Reject("HandlerSession", $"typeof({first}),HandlerEntry.Session(typeof({second}),typeof(Factory<,>))", $"Stage.Hook(typeof({hook}))");
        Reject("Overlap", normal, $"Stage.Hook(typeof({hook}))", "c.Handlers<OpenAllAsyncResponse.Request<OpenAllFixture.A,OpenAllFixture.B>>(DispatchFamily.SendAllAsync,typeof(OpenAllAsyncResponse.ZHandler<OpenAllFixture.B,OpenAllFixture.A>),typeof(OpenAllAsyncResponse.AHandler<OpenAllFixture.A,OpenAllFixture.B>));c.Pipeline<OpenAllAsyncResponse.Request<OpenAllFixture.A,OpenAllFixture.B>>(DispatchFamily.SendAllAsync,Stage.Hook<OpenAllAsyncResponse.Hook<OpenAllFixture.B,OpenAllFixture.A>>());");
        Reject("Multiple", normal, $"Stage.Hook(typeof({hook}))", "c.Handlers<AsyncSentinel.Request>(DispatchFamily.SendAsync,typeof(AsyncSentinel.Handler));c.Pipeline<AsyncSentinel.Request>(DispatchFamily.SendAsync);");
        Reject("Three", normal + ",typeof(Third<,>)", $"Stage.Hook(typeof({hook}))");
        return OpenSendAllAsyncSource() + "namespace AllAsyncRejected {" + string.Join("\n", declarations) + "public static class Registration {" + string.Join("\n", registrations) + "}" + """
            [ZendiatorHandler]public sealed class Unused<X,Y,Z>:IRequestHandler<OpenAllAsyncResponse.Request<X,Y>,int>{public ValueTask<int> HandleAsync(OpenAllAsyncResponse.Request<X,Y> r,CancellationToken t)=>new(0);}
            [ZendiatorHandler]public sealed class StructHandler<X,Y>:IRequestHandler<OpenAllAsyncResponse.Request<X,Y>,int> where X:struct{public ValueTask<int> HandleAsync(OpenAllAsyncResponse.Request<X,Y> r,CancellationToken t)=>new(0);}
            public sealed record OtherRequest<X,Y>:IRequest<int>;
            [ZendiatorHandler]public sealed class OtherHandler<X,Y>:IRequestHandler<OtherRequest<X,Y>,int>{public ValueTask<int> HandleAsync(OtherRequest<X,Y> r,CancellationToken t)=>new(0);}
            [ZendiatorHandler]public sealed class Third<X,Y>:IRequestHandler<OpenAllAsyncResponse.Request<X,Y>,int>{public ValueTask<int> HandleAsync(OpenAllAsyncResponse.Request<X,Y> r,CancellationToken t)=>new(0);}
            public sealed record DualRequest<X,Y>:IRequest,IRequest<int>;
            [ZendiatorHandler]public sealed class DualVoid<X,Y>:IRequestHandler<DualRequest<X,Y>>{public ValueTask HandleAsync(DualRequest<X,Y> r,CancellationToken t)=>default;}
            [ZendiatorHandler]public sealed class DualResult<X,Y>:IRequestHandler<DualRequest<X,Y>,int>{public ValueTask<int> HandleAsync(DualRequest<X,Y> r,CancellationToken t)=>new(0);}
            public sealed class DualHook<X,Y>:IAsyncSendRequestTransform<DualRequest<X,Y>>,IAsyncSendSuccessObserver<DualRequest<X,Y>>,IAsyncSendExceptionObserver<DualRequest<X,Y>>,IAsyncSendFinallyObserver<DualRequest<X,Y>>{
                public ValueTask<DualRequest<X,Y>> BeforeAsync(DualRequest<X,Y> r,CancellationToken t)=>new(r);public ValueTask AfterAsync(DualRequest<X,Y> r,CancellationToken t)=>default;
                public ValueTask OnExceptionAsync(DualRequest<X,Y> r,Exception e,CancellationToken t)=>default;public ValueTask FinallyAsync(DualRequest<X,Y> r,SendExitInfo e,CancellationToken t)=>default;
            }
            public sealed class Factory<X,Y>;
            public sealed class NoDefault:OpenAllFixture.ISecond,OpenAllFixture.IHook{public NoDefault(int value){}}
            public static class Consumer{public static ValueTask<int[]> Invalid(OpenAllAsyncResponse.Generated.Composition.IZendiator m)=>m.SendAllAsync(new OpenAllAsyncResponse.Request<NoDefault,OpenAllFixture.B>(7));}
            }
            """;
    }

    private static string? OpenSendAllAsyncFolder(string name)
    {
        var root = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        if (string.IsNullOrEmpty(root)) return null;
        var folder = Path.Combine(root, "open-sendall-async-" + name); Directory.CreateDirectory(folder); return folder;
    }

    private static void AssertOpenSendAllAsyncOutput(Compilation output, GeneratorDriver driver)
    {
        AssertOpenSendAllSyncOutput(output, driver);
        var catalog = Assert.IsType<CompositionSelectionCatalog>(Assert.Single(driver.GetRunResult().Results.Single().TrackedSteps["CompositionSelectionModel"].SelectMany(s => s.Outputs)).Value);
        var model = CanonicalSendAllAsyncEmissionNormalizer.Create(catalog, CanonicalExecutionGraphNormalizer.Create(catalog));
        foreach (var ns in new[] { "OpenAllAsyncResponse", "OpenAllAsyncVoid" })
        {
            var isVoid = ns == "OpenAllAsyncVoid"; var route = Assert.Single(Assert.Single(catalog.Compositions, c => c.Key == ns).Pipelines); Assert.Equal(BoundRequestShape.OpenAllAsyncReference, route.RequestShape);
            Assert.Equal(isVoid ? FusionResultKind.Void : FusionResultKind.Response, route.ResultKind); Assert.Equal(2, route.Handlers.Count);
            var emission = Assert.Single(model.Compositions, c => c.Registration.Namespace == ns + ".Generated.Composition"); Assert.All(emission.Branches, b => { Assert.Equal(route.SendGeneric, b.Generic); Assert.Equal(route.SendOpenRegistration, b.OpenRegistration); Assert.Equal(isVoid, b.Handler.ResultName == null); });
            var registration = Assert.Single(CanonicalRegistrationPlan.SendAllAsync(emission).Routes); var open = Assert.IsType<BoundStreamOpenRegistration>(registration.Open);
            Assert.Equal(new[] { "global::" + ns + ".ZHandler<,>", "global::" + ns + ".AHandler<,>" }, open.HandlerDefinitionNames); Assert.Equal(new[] { "global::" + ns + ".Hook<,>", "global::" + ns + ".ZHandler<,>", "global::" + ns + ".AHandler<,>" }, registration.ServiceTypes);
            var api = output.GetTypeByMetadataName(ns + ".Generated.Composition.IZendiator")!; Assert.NotNull(api); Assert.False(api.IsGenericType); var mediator = output.GetTypeByMetadataName(ns + ".Generated.Composition.Zendiator")!; Assert.False(mediator.IsGenericType);
            var publicMethod = Assert.Single(mediator.GetMembers("SendAllAsync").OfType<IMethodSymbol>()); var execute = Assert.Single(mediator.GetMembers().OfType<IMethodSymbol>(), m => m.Name.StartsWith("ExecuteAllAsync", StringComparison.Ordinal)); var branches = mediator.GetMembers().OfType<IMethodSymbol>().Where(m => m.Name.StartsWith("Branch", StringComparison.Ordinal)).ToArray(); Assert.Equal(2, branches.Length);
            var methods = new[] { Assert.Single(api.GetMembers("SendAllAsync").OfType<IMethodSymbol>()), publicMethod, execute }.Concat(branches).ToArray();
            Assert.All(methods, m => { Assert.Equal(2, m.Arity); Assert.All(m.TypeParameters, p => { Assert.True(p.HasReferenceTypeConstraint); Assert.True(p.HasConstructorConstraint); }); Assert.Contains(m.TypeParameters, p => p.ConstraintTypes.Any(t => t.Name == "ISecond")); Assert.Contains(m.TypeParameters, p => p.ConstraintTypes.Any(t => t.Name == "IHook")); Assert.Equal(isVoid ? 0 : 1, ((INamedTypeSymbol)m.ReturnType).Arity); });
            var declarations = methods.Select(m => (MethodDeclarationSyntax)m.DeclaringSyntaxReferences.Single().GetSyntax()).ToArray(); Assert.All(declarations, d => Assert.Equal(declarations[0].ConstraintClauses.ToFullString().Trim(), d.ConstraintClauses.ToFullString().Trim()));
            foreach (var method in new[] { publicMethod, execute })
            {
                var declaration = (MethodDeclarationSyntax)method.DeclaringSyntaxReferences.Single().GetSyntax(); var calls = declaration.DescendantNodes().OfType<InvocationExpressionSyntax>().Where(i => i.Expression is GenericNameSyntax).ToArray(); Assert.Equal(SymbolEqualityComparer.Default.Equals(method, publicMethod) ? 1 : 2, calls.Length);
                foreach (var call in calls) { var target = Assert.IsAssignableFrom<IMethodSymbol>(output.GetSemanticModel(call.SyntaxTree).GetSymbolInfo(call).Symbol); Assert.Equal(method.TypeParameters.Cast<ITypeSymbol>(), target.TypeArguments, SymbolEqualityComparer.Default); Assert.Contains(methods, m => SymbolEqualityComparer.Default.Equals(target.OriginalDefinition, m)); }
            }
        }
        foreach (var marker in new[] { "Response", "Command" })
        {
            var api = output.GetTypeByMetadataName("AllAsyncClosedSentinel.Generated." + marker + ".IZendiator")!; Assert.NotNull(api); var method = Assert.Single(api.GetMembers("SendAllAsync").OfType<IMethodSymbol>()); Assert.Equal(0, method.Arity); Assert.Equal(marker == "Command" ? 0 : 1, ((INamedTypeSymbol)method.ReturnType).Arity);
        }
    }

    [Fact]
    public async Task OpenSendAllAsync_response_Finally_pending_second_OCE_and_native_void_match_reference()
    {
        var source = OpenSendAllAsyncSource(); var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "OpenSendAllAsync_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics); var folder = OpenSendAllAsyncFolder("runtime"); SaveSendAllSync(folder, source, driver, output, diagnostics);
        Assert.Empty(diagnostics); Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)); AssertOpenSendAllAsyncOutput(output, driver);
        using var bytes = new MemoryStream(); var emitted = output.Emit(bytes); Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics)); if (folder != null) File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray()); bytes.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes); var result = await assembly.GetType("AllAsyncFixture.Entry")!.GetMethod("Run")!.CreateDelegate<Func<Task<string>>>()(); if (folder != null) File.WriteAllText(Path.Combine(folder, "runtime.json"), result); using var json = JsonDocument.Parse(result); Assert.Equal(6, json.RootElement.GetProperty("dispatches").GetInt32());
    }

    [Fact]
    public void OpenSendAllAsync_rejects_independent_family_constraints_shapes_and_preserves_compiler_where()
    {
        var source = OpenSendAllAsyncNegative(); var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "OpenSendAllAsyncNegative_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics); var folder = OpenSendAllAsyncFolder("negative"); SaveSendAllSync(folder, source, driver, output, diagnostics); AssertOpenSendAllAsyncOutput(output, driver);
        AssertComposedProfile(output, "AllAsyncRejected.Generated.Multiple", ("SendAllAsync", 1), ("SendAsync", 1));
        foreach (var name in new[] { "Undetermined", "ConstraintConflict", "PatternMismatch", "ResponseVoid", "Wrapper", "HookSession", "HandlerSession", "Overlap", "Three" })
        {
            var ns = "AllAsyncRejected.Generated." + name; Assert.Null(output.GetTypeByMetadataName(ns + ".IZendiator")); Assert.Null(output.GetTypeByMetadataName(ns + ".ZendiatorGeneratedRegistrar")); var start = source.IndexOf("public static IServiceCollection " + name + "(", StringComparison.Ordinal); var end = source.IndexOf("});", start, StringComparison.Ordinal) + 3;
            Assert.Contains(diagnostics, d => d.Location.IsInSource && d.Location.SourceSpan.Start >= start && d.Location.SourceSpan.End <= end && d.Id == (name == "Overlap" ? "ZEN0028" : "ZEN0035")); foreach (var f in driver.GetRunResult().Results.Single().GeneratedSources.Where(f => f.SourceText.ToString().Contains("InterceptsLocation", StringComparison.Ordinal))) Assert.DoesNotContain("typeof(global::AllAsyncRejected." + name + ")", f.SourceText.ToString(), StringComparison.Ordinal);
        }
        foreach (var name in new[] { "Undetermined", "ConstraintConflict", "PatternMismatch", "HandlerSession" })
        {
            var start = source.IndexOf("public static IServiceCollection " + name + "(", StringComparison.Ordinal); var end = source.IndexOf("});", start, StringComparison.Ordinal) + 3; var d = Assert.Single(diagnostics, d => d.Id == "ZEN0035" && d.Location.SourceSpan.Start >= start && d.Location.SourceSpan.End <= end && (source.Substring(d.Location.SourceSpan.Start, d.Location.SourceSpan.Length).StartsWith("typeof(", StringComparison.Ordinal) || source.Substring(d.Location.SourceSpan.Start, d.Location.SourceSpan.Length).StartsWith("HandlerEntry.Session", StringComparison.Ordinal))); Assert.Empty(d.AdditionalLocations);
        }
        var mismatchStart = source.IndexOf("public static IServiceCollection ResponseVoid(", StringComparison.Ordinal); var mismatchEnd = source.IndexOf("});", mismatchStart, StringComparison.Ordinal) + 3;
        var mismatch = Assert.Single(diagnostics, d => d.Id == "ZEN0035" && d.Location.SourceSpan.Start >= mismatchStart && d.Location.SourceSpan.End <= mismatchEnd);
        Assert.Contains("int response or void contract", mismatch.GetMessage()); Assert.Contains("Pipeline(typeof(DualRequest<,>)", source.Substring(mismatch.Location.SourceSpan.Start, mismatch.Location.SourceSpan.Length)); Assert.Empty(mismatch.AdditionalLocations);
        var overlap = Assert.Single(diagnostics, d => d.Id == "ZEN0028"); Assert.Contains("Pipeline<OpenAllAsyncResponse.Request<", source.Substring(overlap.Location.SourceSpan.Start, overlap.Location.SourceSpan.Length)); var additional = Assert.Single(overlap.AdditionalLocations); Assert.Contains("Pipeline(typeof(OpenAllAsyncResponse.Request<,>)", source.Substring(additional.SourceSpan.Start, additional.SourceSpan.Length)); Assert.True(additional.SourceSpan.Start < overlap.Location.SourceSpan.Start);
        var errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray(); Assert.NotEmpty(errors); Assert.All(errors, d => { Assert.Equal("CS0310", d.Id); Assert.Contains("SendAllAsync", source.Substring(d.Location.SourceSpan.Start, d.Location.SourceSpan.Length)); Assert.Contains("NoDefault", d.GetMessage()); });
        if (folder != null) File.WriteAllText(Path.Combine(folder, "negative-result.json"), JsonSerializer.Serialize(new { logicalInputs = 1, drivers = 1, dispatches = 0, validGeneratedCompositions = 16, newlyComposedProfiles = new[] { "Multiple" }, rejectedCompositions = 9, compiler = errors.Select(d => new { d.Id, message = d.GetMessage(), span = d.Location.SourceSpan.ToString() }), generator = diagnostics.Select(d => new { d.Id, message = d.GetMessage(), primary = d.Location.SourceSpan.ToString(), additional = d.AdditionalLocations.Select(l => l.SourceSpan.ToString()) }) }));
    }
}
