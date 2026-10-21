using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;
using Zendiator.SourceGenerator;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private const string OpenSendAsyncTwoHookConsumer = """
        namespace OpenAsyncTwoHooks {
        public sealed record Request<TLeft,TRight>(int Value):IRequest<int>;
        public sealed class Probe {
            public static readonly AsyncLocal<string?> Ambient=new();
            public readonly Exception Failure=new FormatException("inner finally");public CancellationToken Token;public string Mode="";
            public object Original=null!,SavedA=null!,SavedB=null!;public int Calls;public IAsyncOnceGate? Pending;
            public readonly List<string> Trace=new();public readonly List<string?> AmbientSeen=new();
            public readonly List<Type> Created=new(),Disposed=new();public readonly List<(string Phase,IAsyncOnceGate Gate)> Gates=new();
            public static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
            public void Resolve(string role,Type type){Created.Add(type);Trace.Add("resolve:"+role);}
            public void Hit(string phase,object request,CancellationToken token){
                var expected=phase=="A.before"?Original:phase=="B.before"||phase.StartsWith("A.",StringComparison.Ordinal)?SavedA:SavedB;
                Check(token==Token&&ReferenceEquals(request,expected),"per-stage saved request/token");Trace.Add(phase);AmbientSeen.Add(Ambient.Value);Ambient.Value=phase;
            }
            public ValueTask<T> Value<T>(string phase,T value){var g=new AsyncOnceGate<T>(value);Gates.Add((phase,g));if(Mode=="pending"&&phase=="handler")Pending=g;else g.Complete();return g.Task;}
            public ValueTask Done(string phase){var g=new AsyncOnceGate<int>(0);Gates.Add((phase,g));if(Mode=="finally"&&phase=="B.finally")Pending=g;else g.Complete();return g.Void;}
        }
        public abstract class Hook<R>:IAsyncSendRequestTransform<R>,IAsyncSendResponseTransform<R,int>,IAsyncSendExceptionObserver<R>,IAsyncSendFinallyObserver<R>,IDisposable {
            protected readonly Probe P;private readonly string role;private readonly int delta;
            protected Hook(Probe p,string role,int delta){P=p;this.role=role;this.delta=delta;p.Resolve(role,GetType());}
            public abstract ValueTask<R> BeforeAsync(R request,CancellationToken token);
            public ValueTask<int> AfterAsync(R request,int value,CancellationToken token){P.Hit(role+".after",request!,token);return P.Value(role+".after",value+delta);}
            public ValueTask OnExceptionAsync(R request,Exception error,CancellationToken token){Probe.Check(ReferenceEquals(error,P.Failure),"exception identity");P.Hit(role+".exception",request!,token);return P.Done(role+".exception");}
            public ValueTask FinallyAsync(R request,SendExitInfo exit,CancellationToken token){
                var failed=role=="A"&&P.Mode=="finally";Probe.Check(exit.Success==!failed&&ReferenceEquals(exit.Error,failed?P.Failure:null),"entered exit info");
                P.Hit(role+".finally",request!,token);return P.Done(role+".finally");
            }
            public void Dispose(){P.Disposed.Add(GetType());}
        }
        public sealed class First<X,Y>:Hook<Request<Y,X>> where X:class,new() where Y:class {
            public First(Probe p):base(p,"A",5){}
            public override ValueTask<Request<Y,X>> BeforeAsync(Request<Y,X> request,CancellationToken token){P.Hit("A.before",request,token);var next=new Request<Y,X>(request.Value+1);P.SavedA=next;return P.Value("A.before",next);}
        }
        public class Second<X,Y>:Hook<Request<X,Y>> where X:class,new() where Y:class {
            public Second(Probe p):base(p,"B",3){}
            public override ValueTask<Request<X,Y>> BeforeAsync(Request<X,Y> request,CancellationToken token){P.Hit("B.before",request,token);var next=new Request<X,Y>(request.Value+10);P.SavedB=next;return P.Value("B.before",next);}
        }
        [ZendiatorHandler]public sealed class Handler<X,Y>:IRequestHandler<Request<Y,X>,int>,IDisposable where X:class,new() where Y:class {
            private readonly Probe p;public Handler(Probe p){this.p=p;p.Resolve("H",GetType());}
            public ValueTask<int> HandleAsync(Request<Y,X> request,CancellationToken token){p.Hit("handler",request,token);p.Calls++;return p.Value("handler",request.Value*2);}
            public void Dispose(){p.Disposed.Add(GetType());}
        }
        [ZendiatorComposition("OpenAsyncTwoHooks")]public sealed class Composition;
        public static class Entry {
            public static IServiceCollection Register(IServiceCollection services)=>services.AddZendiator<Composition>(c=>{
                c.Handlers(typeof(Request<,>),DispatchFamily.SendAsync,typeof(Handler<,>));
                c.Pipeline(typeof(Request<,>),DispatchFamily.SendAsync,Stage.Hook(typeof(First<,>)),Stage.Hook(typeof(Second<,>)));
            });
            private static ValueTask<int> Reference<L,R>(IServiceProvider sp,Request<L,R> request,CancellationToken token) where L:class,new() where R:class,new(){ArgumentNullException.ThrowIfNull(request);return Execute(sp,request,token);}
            // Independent two-stage orchestration; uses the same user callbacks, not generated planning/emission.
            private static async ValueTask<int> Execute<L,R>(IServiceProvider sp,Request<L,R> request,CancellationToken token) where L:class,new() where R:class,new(){
                token.ThrowIfCancellationRequested();var a=sp.GetRequiredService<First<R,L>>();var savedA=request;var successA=false;Exception? errorA=null;
                try{
                    savedA=await a.BeforeAsync(savedA,token).ConfigureAwait(false);ArgumentNullException.ThrowIfNull(savedA);token.ThrowIfCancellationRequested();
                    var b=sp.GetRequiredService<Second<L,R>>();var savedB=savedA;var successB=false;Exception? errorB=null;int value;
                    try{
                        savedB=await b.BeforeAsync(savedB,token).ConfigureAwait(false);ArgumentNullException.ThrowIfNull(savedB);token.ThrowIfCancellationRequested();
                        var handler=sp.GetRequiredService<Handler<R,L>>();value=await handler.HandleAsync(savedB,token).ConfigureAwait(false);
                        value=await b.AfterAsync(savedB,value,token).ConfigureAwait(false);successB=true;
                    }catch(Exception error){errorB=error;try{await b.OnExceptionAsync(savedB,error,token).ConfigureAwait(false);}catch(Exception replacement){errorB=replacement;throw;}throw;}
                    finally{await b.FinallyAsync(savedB,new(successB,errorB),token).ConfigureAwait(false);}
                    value=await a.AfterAsync(savedA,value,token).ConfigureAwait(false);successA=true;return value;
                }catch(Exception error){errorA=error;try{await a.OnExceptionAsync(savedA,error,token).ConfigureAwait(false);}catch(Exception replacement){errorA=replacement;throw;}throw;}
                finally{await a.FinallyAsync(savedA,new(successA,errorA),token).ConfigureAwait(false);}
            }
            private static async Task<string> Observe<L,R>(bool generated,string mode) where L:class,new() where R:class,new(){
                using var cancellation=new CancellationTokenSource();var p=new Probe{Mode=mode,Token=cancellation.Token};IServiceCollection services=new ServiceCollection();services.AddSingleton(p);
                services.AddScoped(typeof(Handler<,>));var existing=services.Last();Register(services);var count=services.Count;Register(services);
                Probe.Check(services.Count==count&&services.Contains(existing),"TryAdd and repeated registration");
                foreach(var definition in new[]{typeof(Handler<,>),typeof(First<,>),typeof(Second<,>)})Probe.Check(services.Count(d=>d.ServiceType==definition&&d.ImplementationType==definition&&d.Lifetime==(definition==typeof(Handler<,>)?ServiceLifetime.Scoped:ServiceLifetime.Transient))==1,"open transient definitions");
                using var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});using var scope=provider.CreateScope();var sp=scope.ServiceProvider;
                var mediator=sp.GetRequiredService<OpenAsyncTwoHooks.Generated.Composition.IZendiator>();var request=new Request<L,R>(7);p.Original=request;Probe.Ambient.Value="caller";
                var dispatch=generated?mediator.SendAsync(request,cancellation.Token):Reference(sp,request,cancellation.Token);var task=dispatch.AsTask();
                Probe.Check(!task.IsCompleted&&p.Pending!=null&&p.Pending.Consumptions==0&&p.Calls==1&&Probe.Ambient.Value=="caller","real pending boundary and caller context");
                var prefix=new[]{"resolve:A","A.before","resolve:B","B.before","resolve:H","handler"};
                Probe.Check(p.Trace.SequenceEqual(mode=="pending"?prefix:prefix.Concat(new[]{"B.after","B.finally"})),"nothing crosses pending boundary");
                if(mode=="pending")p.Pending!.Complete();else p.Pending!.Fail(p.Failure);
                int? value=null;Exception? caught=null;try{value=await task.WaitAsync(TimeSpan.FromSeconds(5));}catch(Exception error)when(ReferenceEquals(error,p.Failure)){caught=error;}
                Probe.Check(Probe.Ambient.Value=="caller","caller context after await");
                Probe.Check(mode=="pending"?value==44&&caught==null&&task.IsCompletedSuccessfully:value==null&&ReferenceEquals(caught,p.Failure)&&task.IsFaulted,"response and failure status/identity");
                Probe.Check(p.Trace.SequenceEqual(prefix.Concat(mode=="pending"?new[]{"B.after","B.finally","A.after","A.finally"}:new[]{"B.after","B.finally","A.exception","A.finally"})),"entered unwind and skipped outer success");
                Probe.Check(p.AmbientSeen.SequenceEqual(new[]{"caller","A.before","B.before","handler","B.after","B.finally",mode=="pending"?"A.after":"A.exception"}),"native callback context order");
                Probe.Check(p.Calls==1&&p.Gates.Count==7&&p.Gates.All(g=>g.Gate.Consumptions==1),"single handler and ValueTask consumption");
                Probe.Check(!ReferenceEquals(p.Original,p.SavedA)&&!ReferenceEquals(p.SavedA,p.SavedB)&&((Request<L,R>)p.SavedA).Value==8&&((Request<L,R>)p.SavedB).Value==18,"distinct per-stage requests");
                Probe.Check(p.Created.SequenceEqual(new[]{typeof(First<R,L>),typeof(Second<L,R>),typeof(Handler<R,L>)}),"each receiver closes by its contract");
                Probe.Check(p.Disposed.Count==0,"dispatch borrows receivers");scope.Dispose();Probe.Check(p.Disposed.SequenceEqual(p.Created.AsEnumerable().Reverse()),"scope disposes each receiver once");
                return JsonSerializer.Serialize(new{mode,value,status=task.Status.ToString(),error=caught==null?null:"sentinel",p.Calls,p.Trace,p.AmbientSeen,gates=p.Gates.Select(g=>new{g.Phase,g.Gate.Consumptions}),created=p.Created.Select(t=>t.ToString()),disposed=p.Disposed.Select(t=>t.ToString()),registrations=count});
            }
            public static async Task<string> Run(){var rows=new List<object>();foreach(var mode in new[]{"pending","finally"}){
                var reference=await Observe<OpenFixture.A,OpenFixture.B>(false,mode);var generated=await Observe<OpenFixture.A,OpenFixture.B>(true,mode);
                Probe.Check(reference==generated,"reference/generated mismatch\n"+reference+"\n"+generated);rows.Add(new{reference=JsonDocument.Parse(reference).RootElement.Clone(),generated=JsonDocument.Parse(generated).RootElement.Clone()});
            }return JsonSerializer.Serialize(new{pairs=2,dispatches=4,rows});}
        } }
        """;

    private static (Compilation Output, string? Folder) CompileOpenSendAsyncTwoHooks(bool negative)
    {
        var source = OpenSendAllAsyncSource() + OpenSendAsyncTwoHookConsumer + (negative ? """
            namespace OpenAsyncTwoHooks {
            public sealed class Unused<X,Y,Z>:Second<X,Y> where X:class,new() where Y:class{public Unused(Probe p):base(p){}}
            [ZendiatorComposition("OpenAsyncTwoHooksUndetermined")]public sealed class Undetermined;
            public static class Rejected{public static IServiceCollection Add(IServiceCollection s)=>s.AddZendiator<Undetermined>(c=>{
                c.Handlers(typeof(Request<,>),DispatchFamily.SendAsync,typeof(Handler<,>));
                c.Pipeline(typeof(Request<,>),DispatchFamily.SendAsync,Stage.Hook(typeof(First<,>)),Stage.Hook(typeof(Unused<,,>)));
            });} }
            """ : "");
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "OpenSendAsyncTwoHooks_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        var folder = OpenSendAsyncFolder(negative ? "two-hooks-negative" : "two-hooks-runtime"); SaveSendAllSync(folder, source, driver, output, diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        AssertOpenSendAllAsyncOutput(output, driver); // Existing single-hook, void, SendAll and Stream entries are compile sentinels only.
        var catalog = Assert.IsType<CompositionSelectionCatalog>(Assert.Single(driver.GetRunResult().Results.Single().TrackedSteps["CompositionSelectionModel"].SelectMany(s => s.Outputs)).Value);
        var route = Assert.Single(Assert.Single(catalog.Compositions, c => c.Key == "OpenAsyncTwoHooks").Pipelines);
        var emission = Assert.Single(CanonicalAsyncEmissionNormalizer.Create(catalog, CanonicalExecutionGraphNormalizer.Create(catalog)).Compositions, c => c.Namespace == "OpenAsyncTwoHooks.Generated.Composition");
        Assert.Equal(BoundRequestShape.OpenAsyncReference, route.RequestShape); Assert.Equal(FusionResultKind.Response, route.ResultKind); Assert.Equal(2, route.Stages.Count); Assert.Equal(2, emission.Hooks.Count);
        Assert.Equal(route.SendOpenRegistration, emission.OpenRegistration); Assert.Equal(2, emission.OpenRegistration!.Stages.Count);
        Assert.Equal(new[] { "global::OpenAsyncTwoHooks.First<,>", "global::OpenAsyncTwoHooks.Second<,>" }, emission.OpenRegistration.Stages.Select(s => s.TypeName));
        var api = output.GetTypeByMetadataName("OpenAsyncTwoHooks.Generated.Composition.IZendiator")!; Assert.NotNull(api); Assert.False(api.IsGenericType);
        var mediator = output.GetTypeByMetadataName("OpenAsyncTwoHooks.Generated.Composition.Zendiator")!; Assert.False(mediator.IsGenericType);
        var methods = new[] { Assert.Single(api.GetMembers("SendAsync").OfType<IMethodSymbol>()), Assert.Single(mediator.GetMembers("SendAsync").OfType<IMethodSymbol>()) };
        Assert.All(methods, m => { Assert.Equal(2, m.Arity); Assert.All(m.TypeParameters, p => { Assert.True(p.HasReferenceTypeConstraint); Assert.True(p.HasConstructorConstraint); }); Assert.Equal(SpecialType.System_Int32, ((INamedTypeSymbol)m.ReturnType).TypeArguments.Single().SpecialType); });
        var declarations = methods.Select(m => (MethodDeclarationSyntax)m.DeclaringSyntaxReferences.Single().GetSyntax()).ToArray();
        Assert.All(declarations, d => Assert.Equal(declarations[0].ConstraintClauses.ToFullString().Trim(), d.ConstraintClauses.ToFullString().Trim()));
        var invoke = AssertOpenAsyncInvoker(output, mediator, methods[1]);
        Assert.Equal(SpecialType.System_Int32, ((INamedTypeSymbol)invoke.ReturnType).TypeArguments.Single().SpecialType);
        if (negative)
        {
            var error = Assert.Single(diagnostics, d => d.Id == "ZEN0035" && source.Substring(d.Location.SourceSpan.Start, d.Location.SourceSpan.Length) == "Stage.Hook(typeof(Unused<,,>))"); Assert.Empty(error.AdditionalLocations);
            Assert.All(diagnostics, d => Assert.Equal("ZEN0035", d.Id));
            Assert.Null(output.GetTypeByMetadataName("OpenAsyncTwoHooks.Generated.Undetermined.IZendiator")); Assert.Null(output.GetTypeByMetadataName("OpenAsyncTwoHooks.Generated.Undetermined.ZendiatorGeneratedRegistrar"));
            foreach (var interceptor in driver.GetRunResult().Results.Single().GeneratedSources.Where(f => f.SourceText.ToString().Contains("InterceptsLocation", StringComparison.Ordinal))) Assert.DoesNotContain("typeof(global::OpenAsyncTwoHooks.Undetermined)", interceptor.SourceText.ToString(), StringComparison.Ordinal);
        }
        else Assert.Empty(diagnostics);
        if (folder != null) File.WriteAllText(Path.Combine(folder, "binding.json"), JsonSerializer.Serialize(new { logicalInputs = 1, dispatches = 0, requestShape = route.RequestShape.ToString(), generic = emission.Generic, openRegistration = emission.OpenRegistration, hooks = emission.Hooks, negative, diagnostics = diagnostics.Select(d => new { d.Id, message = d.GetMessage(), anchor = source.Substring(d.Location.SourceSpan.Start, d.Location.SourceSpan.Length) }) }));
        return (output, folder);
    }

    [Fact]
    public async Task OpenSendAsync_two_hooks_pending_and_inner_finally_failure_match_reference()
    {
        var compiled = CompileOpenSendAsyncTwoHooks(false); using var bytes = new MemoryStream(); var emitted = compiled.Output.Emit(bytes); Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        if (compiled.Folder != null) File.WriteAllBytes(Path.Combine(compiled.Folder, "generated.dll"), bytes.ToArray()); bytes.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes); var result = await assembly.GetType("OpenAsyncTwoHooks.Entry")!.GetMethod("Run")!.CreateDelegate<Func<Task<string>>>()();
        if (compiled.Folder != null) File.WriteAllText(Path.Combine(compiled.Folder, "runtime.json"), result);
        using var json = JsonDocument.Parse(result); Assert.Equal(4, json.RootElement.GetProperty("dispatches").GetInt32());
    }

    [Fact]
    public void OpenSendAsync_two_hooks_reject_undetermined_second_hook_preserve_compile_sentinels() => CompileOpenSendAsyncTwoHooks(true);
}
