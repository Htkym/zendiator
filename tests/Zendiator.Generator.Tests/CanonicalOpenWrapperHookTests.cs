using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using Zendiator.SourceGenerator;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private static string OpenWrapperHookSource(bool isVoid = false)
    {
        var ns = isVoid ? "OpenVoidWrapperHook" : "OpenWrapperHook";
        var result = isVoid ? "ValueTask" : "ValueTask<int>";
        var response = isVoid ? "" : ",int";
        return $$"""
        using System;using System.Collections.Generic;using System.Linq;using System.Text.Json;using System.Threading;using System.Threading.Tasks;
        using Microsoft.Extensions.DependencyInjection;using Zendiator;using Zendiator.DependencyInjection;using Zendiator.Generator.Tests;
        namespace {{ns}} {
        public sealed class Marker {} public sealed class OtherMarker {}
        public sealed record Query<T>(int Value):IRequest{{(isVoid ? "" : "<int>")}};
        public sealed class Probe {
            public static readonly AsyncLocal<string?> Ambient=new();
            public string Mode="";public CancellationToken Token;public object Original=null!;
            public readonly Exception Failure=new FormatException("inner finally");public int HandlerCalls,NextCalls;
            public readonly List<string> Trace=new();public readonly List<string?> AmbientSeen=new();
            public readonly List<object> Created=new(),Disposed=new(),Saved=new();public readonly List<IAsyncOnceGate> Gates=new();
            public readonly TaskCompletionSource<IAsyncOnceGate>[] Ready={new(TaskCreationOptions.RunContinuationsAsynchronously),new(TaskCreationOptions.RunContinuationsAsynchronously)};
            public readonly TaskCompletionSource<IAsyncOnceGate> FinallyReady=new(TaskCreationOptions.RunContinuationsAsynchronously);
            public static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
            public void Resolve(object receiver,string role){Created.Add(receiver);Trace.Add("resolve:"+role);}
            public void Hit(string phase,object request,CancellationToken token){
                Check(token==Token&&ReferenceEquals(request,phase.StartsWith("W",StringComparison.Ordinal)||phase=="K.before"?Original:Saved.Last()),"saved request/token");
                Trace.Add(phase);AmbientSeen.Add(Ambient.Value);Ambient.Value=phase;
            }
            public ValueTask<T> Value<T>(T value){var gate=new AsyncOnceGate<T>(value);Gates.Add(gate);gate.Complete();return gate.Task;}
            public ValueTask Done(){var gate=new AsyncOnceGate<int>(0);Gates.Add(gate);gate.Complete();return gate.Void;}
        }
        [ZendiatorHandler]public sealed class Handler<T>:IRequestHandler<Query<T>{{response}}>,IDisposable where T:class,new() {
            private readonly Probe p;public Handler(Probe p){this.p=p;p.Resolve(this,"H");}
            public {{result}} HandleAsync(Query<T> r,CancellationToken token){p.Hit("H",r,token);var gate=new AsyncOnceGate<int>(r.Value);p.Gates.Add(gate);p.Ready[p.HandlerCalls++].SetResult(gate);return gate.{{(isVoid ? "Void" : "Task")}};}
            public void Dispose(){p.Disposed.Add(this);}
        }
        public class Hook<T>:IAsyncSendRequestTransform<Query<T>>,{{(isVoid ? "IAsyncSendSuccessObserver<Query<T>>" : "IAsyncSendResponseTransform<Query<T>,int>")}},IAsyncSendExceptionObserver<Query<T>>,IAsyncSendFinallyObserver<Query<T>>,IDisposable where T:class,new() {
            private readonly Probe p;public Hook(Probe p){this.p=p;p.Resolve(this,"K");}
            public ValueTask<Query<T>> BeforeAsync(Query<T> r,CancellationToken token){p.Hit("K.before",r,token);var saved=r with{Value=r.Value+1};p.Saved.Add(saved);return p.Value(saved);}
            public {{result}} AfterAsync(Query<T> r,{{(isVoid ? "" : "int value,")}}CancellationToken token){p.Hit("K.after",r,token);return {{(isVoid ? "p.Done()" : "p.Value(value+10)")}};}
            public ValueTask OnExceptionAsync(Query<T> r,Exception error,CancellationToken token){p.Hit("K.exception",r,token);return p.Done();}
            public ValueTask FinallyAsync(Query<T> r,SendExitInfo exit,CancellationToken token){
                p.Hit("K.finally",r,token);Probe.Check(exit.Success&&exit.Error==null,"successful inner work before finally");
                if(p.Mode!="finally")return p.Done();var gate=new AsyncOnceGate<int>(0);p.Gates.Add(gate);p.FinallyReady.SetResult(gate);return gate.Void;
            }
            public void Dispose(){p.Disposed.Add(this);}
        }
        public sealed class Unbound<T,Unused>(Probe p):Hook<T>(p) where T:class,new();
        public sealed class Logging<R{{(isVoid ? "" : ",S")}}>:IAsyncSendWrapper<R{{(isVoid ? "" : ",S")}}>,IDisposable where R:class,IRequest{{(isVoid ? "" : "<S>")}} {
            private readonly Probe p;public Logging(Probe p){this.p=p;p.Resolve(this,"W");}
            public async ValueTask{{(isVoid ? "" : "<S>")}} InvokeAsync<N>(R r,N next,CancellationToken token) where N:IAsyncSendContinuation<R{{(isVoid ? "" : ",S")}}>{
                p.Hit("W.before",r,token);try{ {{(isVoid ? "" : "S value=default!;")}}for(var i=0;i<(p.Mode=="repeat"?2:1);i++){p.NextCalls++;{{(isVoid ? "" : "value=")}}await next.InvokeAsync(r,token).ConfigureAwait(false);}{{(isVoid ? "" : "return value;")}} }
                finally{p.Hit("W.finally",r,token);}
            }
            public void Dispose(){p.Disposed.Add(this);}
        }
        [ZendiatorComposition("{{ns}}")]public sealed class Mixed;
        public static class Register {
            public static IServiceCollection Add(IServiceCollection s)=>s.AddZendiator<Mixed>(c=>{
                c.Handlers(typeof(Query<>),DispatchFamily.SendAsync,typeof(Handler<>));
                c.Pipeline(typeof(Query<>),DispatchFamily.SendAsync,Stage.Wrapper(typeof(Logging<{{(isVoid ? "" : ",")}}>)),Stage.Hook(typeof(Hook<>)));
            });
        }
        }
        """;
    }

    private static string OpenWrapperHookConsumer(bool isVoid = false)
    {
        var ns = isVoid ? "OpenVoidWrapperHook" : "OpenWrapperHook";
        var result = isVoid ? "ValueTask" : "ValueTask<int>";
        var response = isVoid ? "" : ",int";
        return $$"""
        namespace {{ns}} {
        public static class Entry {
            private static {{result}} Reference<T>(IServiceProvider p,Query<T> r,CancellationToken token) where T:class,new(){ArgumentNullException.ThrowIfNull(r);return Outer(p,r,token);}
            private static async {{result}} Outer<T>(IServiceProvider p,Query<T> r,CancellationToken token) where T:class,new(){
                token.ThrowIfCancellationRequested();{{(isVoid ? "" : "return ")}}await p.GetRequiredService<Logging<Query<T>{{response}}>>().InvokeAsync(r,new Next<T>(p),token).ConfigureAwait(false);
            }
            private readonly struct Next<T>(IServiceProvider p):IAsyncSendContinuation<Query<T>{{response}}> where T:class,new(){
                public {{result}} InvokeAsync(Query<T> r,CancellationToken token){ArgumentNullException.ThrowIfNull(r);return Inner(p,r,token);}
            }
            // Independent orchestration of the same user callbacks.
            private static async {{result}} Inner<T>(IServiceProvider p,Query<T> r,CancellationToken token) where T:class,new(){
                token.ThrowIfCancellationRequested();var hook=p.GetRequiredService<Hook<T>>();var saved=r;var success=false;Exception? error=null;
                try{
                    saved=await hook.BeforeAsync(saved,token).ConfigureAwait(false);ArgumentNullException.ThrowIfNull(saved);token.ThrowIfCancellationRequested();
                    {{(isVoid ? "" : "var value=")}}await p.GetRequiredService<Handler<T>>().HandleAsync(saved,token).ConfigureAwait(false);
                    {{(isVoid ? "" : "value=")}}await hook.AfterAsync(saved,{{(isVoid ? "" : "value,")}}token).ConfigureAwait(false);success=true;{{(isVoid ? "" : "return value;")}}
                }catch(Exception failure){error=failure;try{await hook.OnExceptionAsync(saved,failure,token).ConfigureAwait(false);}catch(Exception replacement){error=replacement;throw;}throw;}
                finally{await hook.FinallyAsync(saved,new(success,error),token).ConfigureAwait(false);}
            }
            public static {{result}} OtherMarkerCompileSentinel(Generated.Mixed.IZendiator m)=>m.SendAsync(new Query<OtherMarker>(42));
            private static async Task<string> Observe(bool generated,string mode){
                using var cancellation=new CancellationTokenSource();var p=new Probe{Mode=mode,Token=cancellation.Token};IServiceCollection services=new ServiceCollection();services.AddSingleton(p);
                services.AddScoped(typeof(Hook<>));var existing=services.Last();Register.Add(services);var count=services.Count;Register.Add(services);
                Probe.Check(services.Count==count&&services.Contains(existing),"TryAdd/lifetime");
                foreach(var type in new[]{typeof(Logging<{{(isVoid ? "" : ",")}}>),typeof(Hook<>),typeof(Handler<>),typeof(Generated.Mixed.IZendiator),typeof(Generated.Mixed.Zendiator)})Probe.Check(services.Single(d=>d.ServiceType==type).Lifetime==ServiceLifetime.Scoped,"scoped registrations");
                var mismatch=new ZendiatorConfiguration();mismatch.Handlers(typeof(Query<>),DispatchFamily.SendAsync,typeof(Handler<>));mismatch.Pipeline(typeof(Query<>),DispatchFamily.SendAsync,Stage.Hook(typeof(Hook<>)),Stage.Wrapper(typeof(Logging<{{(isVoid ? "" : ",")}}>)));
                IServiceCollection rejected=new ServiceCollection();var mismatchRejected=false;try{Generated.Mixed.ZendiatorGeneratedRegistrar.Add(rejected,mismatch.Snapshot());}catch(InvalidOperationException){mismatchRejected=true;}
                Probe.Check(mismatchRejected&&rejected.Count==0,"snapshot order checked before registration");
                using var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});using var scope=provider.CreateScope();var sp=scope.ServiceProvider;
                var mediator=sp.GetRequiredService<Generated.Mixed.IZendiator>();Probe.Check(ReferenceEquals(mediator,sp.GetRequiredService<Generated.Mixed.Zendiator>()),"one facade");
                var request=new Query<Marker>(42);p.Original=request;Probe.Ambient.Value="caller";
                var task=(generated?mediator.SendAsync(request,p.Token):Reference(sp,request,p.Token)).AsTask();
                for(var i=0;i<(mode=="repeat"?2:1);i++){
                    var gate=await p.Ready[i].Task.WaitAsync(TimeSpan.FromSeconds(5));Probe.Check(!task.IsCompleted&&gate.Consumptions==0&&Probe.Ambient.Value=="caller","real pending/caller context");gate.Complete();
                }
                if(mode=="finally"){var gate=await p.FinallyReady.Task.WaitAsync(TimeSpan.FromSeconds(5));Probe.Check(!task.IsCompleted&&gate.Consumptions==0,"pending finally");gate.Fail(p.Failure);}
                int? value=null;Exception? caught=null;try{ {{(isVoid ? "" : "value=")}}await task.WaitAsync(TimeSpan.FromSeconds(5));}catch(Exception error)when(ReferenceEquals(error,p.Failure)){caught=error;}
                Probe.Check(mode=="repeat"?{{(isVoid ? "" : "value==53&&")}}task.IsCompletedSuccessfully:caught==p.Failure&&task.IsFaulted,"value/error/status");
                var expected=new List<string>{"resolve:W","W.before","resolve:K","K.before","resolve:H","H","K.after","K.finally"};
                if(mode=="repeat")expected.AddRange(new[]{"K.before","H","K.after","K.finally"});expected.Add("W.finally");
                Probe.Check(p.Trace.SequenceEqual(expected)&&Probe.Ambient.Value=="caller","entry/unwind/caller context");
                Probe.Check(p.HandlerCalls==(mode=="repeat"?2:1)&&p.NextCalls==p.HandlerCalls&&p.Saved.Count==p.HandlerCalls,"continuation multiplicity");
                Probe.Check(p.Saved.All(r=>!ReferenceEquals(r,request))&&(p.Saved.Count==1||!ReferenceEquals(p.Saved[0],p.Saved[1])),"separate request slots");
                Probe.Check(p.Gates.Count==p.HandlerCalls*4&&p.Gates.All(g=>g.Consumptions==1),"single consumption of every callback and handler");
                Probe.Check(p.Created.Select(x=>x.GetType()).SequenceEqual(new[]{typeof(Logging<Query<Marker>{{response}}>),typeof(Hook<Marker>),typeof(Handler<Marker>)}),"inferred DI keys");
                foreach(var receiver in p.Created)Probe.Check(ReferenceEquals(receiver,sp.GetRequiredService(receiver.GetType())),"borrowed scoped receiver");
                Probe.Check(p.Disposed.Count==0,"no dispatch disposal");scope.Dispose();Probe.Check(p.Disposed.SequenceEqual(p.Created.AsEnumerable().Reverse()),"one reverse scope cleanup");
                return JsonSerializer.Serialize(new{mode,value,status=task.Status.ToString(),error=caught==null?null:"sentinel",p.Trace,p.AmbientSeen,p.HandlerCalls,p.NextCalls,consumptions=p.Gates.Select(g=>g.Consumptions),created=p.Created.Select(x=>x.GetType().ToString()),disposed=p.Disposed.Select(x=>x.GetType().ToString()),registrations=count,mismatchRejected});
            }
            public static async Task<string> Run(){var rows=new List<object>();foreach(var mode in new[]{"repeat","finally"}){
                var reference=await Observe(false,mode);var generated=await Observe(true,mode);Probe.Check(reference==generated,"reference/generated mismatch\n"+reference+"\n"+generated);rows.Add(JsonDocument.Parse(generated).RootElement.Clone());
            }return JsonSerializer.Serialize(new{pairs=2,dispatches=4,rows});}
        }
        }
        """;
    }

    [Fact]
    public Task OpenSendAsync_wrapper_then_hook_preserves_pending_repeat_finally_and_registration() => VerifyOpenWrapperHook(false);

    [Fact]
    public Task OpenSendAsync_void_wrapper_then_hook_preserves_pending_repeat_finally_and_registration() => VerifyOpenWrapperHook(true);

    private static async Task VerifyOpenWrapperHook(bool isVoid)
    {
        var ns = isVoid ? "OpenVoidWrapperHook" : "OpenWrapperHook";
        var source = OpenWrapperHookSource(isVoid) + OpenWrapperHookConsumer(isVoid);
        var wrapperSentinels = OpenSendWrapperPairSource(false, false) + "namespace OpenSendWrapper {" + OpenSendWrapperConfiguration("SingleSentinel", "Stage.Wrapper(typeof(Logging<,>))") + "}";
        var compilation = SyncCompilation(source, "OpenWrapperHook_" + Guid.NewGuid().ToString("N"))
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(wrapperSentinels, SyncParseOptions), CSharpSyntaxTree.ParseText(OpenSendAllAsyncSource() + OpenSendAsyncTwoHookConsumer, SyncParseOptions));
        if (isVoid) compilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(OpenWrapperHookSource() + OpenWrapperHookConsumer(), SyncParseOptions));
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        var folder = OpenSendAsyncFolder(isVoid ? "void-wrapper-hook" : "wrapper-hook"); SaveSendAllSync(folder, source, driver, output, diagnostics);
        Assert.Empty(diagnostics); Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        foreach (var name in new[] { ns + ".Generated.Mixed", "OpenWrapperHook.Generated.Mixed", "OpenSendWrapper.Generated.SingleSentinel", "OpenSendWrapper.Generated.Pair", "OpenAsyncTwoHooks.Generated.Composition" }.Distinct())
            Assert.NotNull(output.GetTypeByMetadataName(name + ".IZendiator"));
        var api = output.GetTypeByMetadataName(ns + ".Generated.Mixed.IZendiator")!;
        Assert.False(api.IsGenericType);
        var method = Assert.Single(api.GetMembers("SendAsync").OfType<IMethodSymbol>());
        var returnType = Assert.IsAssignableFrom<INamedTypeSymbol>(method.ReturnType);
        Assert.Equal("ValueTask", returnType.Name); Assert.Equal(isVoid ? 0 : 1, returnType.Arity);
        var parameter = Assert.Single(method.TypeParameters); Assert.True(parameter.HasReferenceTypeConstraint); Assert.True(parameter.HasConstructorConstraint);
        var concrete = output.GetTypeByMetadataName(ns + ".Generated.Mixed.Zendiator")!;
        foreach (var name in new[] { "ExecuteOuter", "ExecuteInner" })
        {
            var execute = Assert.Single(concrete.GetMembers().OfType<IMethodSymbol>(), m => m.Name.StartsWith(name + "__", StringComparison.Ordinal));
            Assert.True(SymbolEqualityComparer.Default.Equals(method.ReturnType, execute.ReturnType));
            var argument = Assert.Single(execute.TypeParameters); Assert.True(argument.HasReferenceTypeConstraint); Assert.True(argument.HasConstructorConstraint);
        }
        Assert.Equal(isVoid ? 1 : 2, Assert.Single(Assert.Single(concrete.GetTypeMembers(), t => t.Name.StartsWith("WrapperNext__", StringComparison.Ordinal)).Interfaces).Arity);
        var catalog = Assert.IsType<CompositionSelectionCatalog>(Assert.Single(driver.GetRunResult().Results.Single().TrackedSteps["CompositionSelectionModel"].SelectMany(s => s.Outputs)).Value);
        var route = Assert.Single(Assert.Single(catalog.Compositions, c => c.Key == ns).Pipelines);
        var emission = Assert.Single(CanonicalWrapperEmissionNormalizer.Create(catalog, CanonicalExecutionGraphNormalizer.Create(catalog)).Compositions, c => c.Registration.Namespace == ns + ".Generated.Mixed");
        Assert.Equal(route.SendOpenRegistration, emission.Registration.OpenRegistration); Assert.Equal(2, emission.Registration.OpenRegistration!.Stages.Count);
        Assert.NotNull(emission.AsyncInner); Assert.Empty(emission.Regions);
        using var bytes = new MemoryStream(); var emitted = output.Emit(bytes); Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        if (folder != null)
        {
            File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
            for (var i = 0; i < compilation.SyntaxTrees.Count(); i++) File.WriteAllText(Path.Combine(folder, "input-" + i + ".cs"), compilation.SyntaxTrees.ElementAt(i).ToString());
        }
        bytes.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var runtime = await assembly.GetType(ns + ".Entry")!.GetMethod("Run")!.CreateDelegate<Func<Task<string>>>()();
        if (folder != null) File.WriteAllText(Path.Combine(folder, "runtime.json"), runtime);
        using var json = JsonDocument.Parse(runtime); Assert.Equal(4, json.RootElement.GetProperty("dispatches").GetInt32());
    }

    [Fact]
    public void OpenSendAsync_wrapper_then_hook_rejects_undetermined_hook_without_partial_generation()
    {
        var source = OpenWrapperHookSource().Replace("Stage.Hook(typeof(Hook<>))", "Stage.Hook(typeof(Unbound<,>))");
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "OpenWrapperHookUnbound_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        SaveSendAllSync(OpenSendAsyncFolder("wrapper-hook-unbound"), source, driver, output, diagnostics);
        Assert.All(diagnostics, d => Assert.Equal("ZEN0035", d.Id));
        var error = Assert.Single(diagnostics, d => source.Substring(d.Location.SourceSpan.Start, d.Location.SourceSpan.Length) == "Stage.Hook(typeof(Unbound<,>))");
        Assert.Equal("Stage.Hook(typeof(Unbound<,>))", source.Substring(error.Location.SourceSpan.Start, error.Location.SourceSpan.Length)); Assert.Empty(error.AdditionalLocations);
        Assert.Null(output.GetTypeByMetadataName("OpenWrapperHook.Generated.Mixed.IZendiator")); Assert.Null(output.GetTypeByMetadataName("OpenWrapperHook.Generated.Mixed.ZendiatorGeneratedRegistrar"));
        Assert.DoesNotContain(driver.GetRunResult().Results.Single().GeneratedSources, s => s.HintName.EndsWith("Interceptors.g.cs", StringComparison.Ordinal));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
    }

    [Fact]
    public void OpenSendAsync_void_wrapper_then_hook_rejects_unbound_reverse_session_and_wrapper_only()
    {
        const string wrapper = "Stage.Wrapper(typeof(Logging<>))", hook = "Stage.Hook(typeof(Hook<>))", unbound = "Stage.Wrapper(typeof(UnboundLogging<,>))";
        var source = OpenWrapperHookSource(true) + """
            namespace OpenVoidWrapperHook {
            public sealed class UnboundLogging<R,Unused>:IAsyncSendWrapper<R> where R:class,IRequest {
                public ValueTask InvokeAsync<N>(R r,N next,CancellationToken token) where N:IAsyncSendContinuation<R> => next.InvokeAsync(r,token);
            }
            public sealed class Factory<R>:IAsyncBorrowedSessionFactory<R,Logging<R>> where R:class,IRequest {
                public ValueTask<Logging<R>> CreateAsync(R request,CancellationToken token)=>throw new NotSupportedException();
            }
            }
            """;
        foreach (var (name, invalid) in new[]
        {
            ("unbound", source.Replace(wrapper, unbound)),
            ("reverse", source.Replace(wrapper + "," + hook, hook + "," + wrapper)),
            ("session", source.Replace(wrapper, "Stage.Wrapper(typeof(Logging<>),typeof(Factory<>))")),
            ("wrapper-only", source.Replace(wrapper + "," + hook, wrapper))
        })
        {
            var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(invalid, "OpenVoidWrapperHookRejected_" + name + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
            SaveSendAllSync(OpenSendAsyncFolder("void-wrapper-hook-" + name), invalid, driver, output, diagnostics);
            Assert.NotEmpty(diagnostics); Assert.All(diagnostics, d => Assert.Equal("ZEN0035", d.Id));
            if (name == "unbound")
            {
                var error = Assert.Single(diagnostics, d => invalid.Substring(d.Location.SourceSpan.Start, d.Location.SourceSpan.Length) == unbound);
                Assert.Empty(error.AdditionalLocations);
            }
            Assert.Null(output.GetTypeByMetadataName("OpenVoidWrapperHook.Generated.Mixed.IZendiator")); Assert.Null(output.GetTypeByMetadataName("OpenVoidWrapperHook.Generated.Mixed.ZendiatorGeneratedRegistrar"));
            Assert.DoesNotContain(driver.GetRunResult().Results.Single().GeneratedSources, s => s.HintName.EndsWith("Interceptors.g.cs", StringComparison.Ordinal));
            Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        }
    }
}
