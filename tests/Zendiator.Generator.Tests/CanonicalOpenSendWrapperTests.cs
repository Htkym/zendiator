using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Xunit;
using Zendiator.SourceGenerator;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private const string OpenSendWrapperModule = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Text.Json;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.Extensions.DependencyInjection;
        using Zendiator;
        using Zendiator.DependencyInjection;
        using Zendiator.Generator.Tests;
        namespace OpenSendWrapper {
        public sealed record Query<TMarker>(int Value):IRequest<int>;
        [ZendiatorHandler]public sealed class Handler<TMarker>:IRequestHandler<Query<TMarker>,int>,IDisposable {
            private readonly Probe p;
            public Handler(Probe p){this.p=p;p.Created.Add(this);p.Events.Add("resolve:H");}
            public ValueTask<int> HandleAsync(Query<TMarker> r,CancellationToken token){
                Probe.Check(token==p.Outer.Token,"handler token");p.Events.Add("handler");p.Gate=new(r.Value);
                if(p.Mode=="fault")p.Gate.Fail(p.Failure);else if(p.Mode!="pending")p.Gate.Complete();
                return p.Gate.Task;
            }
            public void Dispose(){p.Disposed.Add(this);}
        }
        public class Logging<TRequest,TResponse>:IAsyncSendWrapper<TRequest,TResponse>,IDisposable where TRequest:class,IRequest<TResponse> {
            private readonly Probe p;
            public Logging(Probe p){this.p=p;p.Created.Add(this);p.Events.Add("resolve:W");}
            public async ValueTask<TResponse> InvokeAsync<TNext>(TRequest r,TNext next,CancellationToken token) where TNext:IAsyncSendContinuation<TRequest,TResponse> {
                Probe.Check(token==p.Outer.Token,"wrapper token");p.Events.Add("wrapper.before");
                if(p.Mode=="before")throw p.Failure;
                try{return await next.InvokeAsync(r,p.Mode=="replace-cancel"?p.Inner.Token:token).ConfigureAwait(false);}
                finally{p.Events.Add("wrapper.finally");}
            }
            public void Dispose(){p.Disposed.Add(this);}
        }
        public sealed class Probe {
            public string Mode="";public readonly CancellationTokenSource Outer=new(),Inner=new();
            public readonly Exception Failure=new InvalidOperationException("user failure");public AsyncOnceGate<int>? Gate;
            public readonly List<string> Events=new();public readonly List<object> Created=new(),Disposed=new();
            public static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        }
        __CONFIGURATION__
        }
        """;

    private static string OpenSendWrapperConfiguration(string name, string stages) => $$"""
        [ZendiatorComposition("{{name}}")]public sealed class {{name}};
        public static class Register{{name}} {
            public static IServiceCollection Add(IServiceCollection s)=>s.AddZendiator<{{name}}>(c=>{
                c.Handlers(typeof(Query<>),DispatchFamily.SendAsync,typeof(Handler<>));
                c.Pipeline(typeof(Query<>),DispatchFamily.SendAsync,{{stages}});
            });
        }
        """;

    private const string OpenSendWrapperConsumer = """
        namespace OpenSendWrapper {
        public static class Entry {
            private static ValueTask<int> Reference<T>(IServiceProvider p,Query<T> r,CancellationToken token){
                ArgumentNullException.ThrowIfNull(r);return Outer(p,r,token);
            }
            private static async ValueTask<int> Outer<T>(IServiceProvider p,Query<T> r,CancellationToken token){
                token.ThrowIfCancellationRequested();return await p.GetRequiredService<Logging<Query<T>,int>>().InvokeAsync(r,new Next<T>(p),token).ConfigureAwait(false);
            }
            private readonly struct Next<T>(IServiceProvider p):IAsyncSendContinuation<Query<T>,int> {
                public ValueTask<int> InvokeAsync(Query<T> r,CancellationToken token){ArgumentNullException.ThrowIfNull(r);return Inner(p,r,token);}
            }
            private static async ValueTask<int> Inner<T>(IServiceProvider p,Query<T> r,CancellationToken token){
                token.ThrowIfCancellationRequested();return await p.GetRequiredService<Handler<T>>().HandleAsync(r,token).ConfigureAwait(false);
            }
            private static async Task<string> Observe<T>(bool generated,string mode){
                var p=new Probe{Mode=mode};using var outer=p.Outer;using var inner=p.Inner;
                if(mode=="pre-cancel")outer.Cancel();if(mode=="replace-cancel")inner.Cancel();
                IServiceCollection services=new ServiceCollection();services.AddSingleton(p);RegisterComposition.Add(services);
                using var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});
                using var scope=provider.CreateScope();var request=new Query<T>(42);
                var mediator=scope.ServiceProvider.GetRequiredService<Generated.Composition.IZendiator>();
                var task=(generated?mediator.SendAsync(request,outer.Token):Reference(scope.ServiceProvider,request,outer.Token)).AsTask();
                if(mode=="pending"){Probe.Check(!task.IsCompleted&&p.Gate!=null&&p.Gate.Consumptions==0,"pending handler");p.Gate!.Complete();}
                Exception? caught=null;int? value=null;try{value=await task;}catch(Exception error){caught=error;}
                var canceled=mode is "pre-cancel" or "replace-cancel";
                Probe.Check(canceled?task.IsCanceled&&caught is OperationCanceledException oce&&oce.CancellationToken==(mode=="pre-cancel"?outer.Token:inner.Token):
                    mode is "before" or "fault"?task.IsFaulted&&ReferenceEquals(caught,p.Failure):task.IsCompletedSuccessfully&&value==42,"result/exception/token identity");
                var expected=mode switch{
                    "pre-cancel"=>Array.Empty<string>(),
                    "before"=>new[]{"resolve:W","wrapper.before"},
                    "replace-cancel"=>new[]{"resolve:W","wrapper.before","wrapper.finally"},
                    _=>new[]{"resolve:W","wrapper.before","resolve:H","handler","wrapper.finally"}};
                Probe.Check(p.Events.SequenceEqual(expected),"opaque try/finally and guard order");
                var expectedTypes=mode=="pre-cancel"?Array.Empty<Type>():mode is "before" or "replace-cancel"?
                    new[]{typeof(Logging<Query<T>,int>)}:new[]{typeof(Logging<Query<T>,int>),typeof(Handler<T>)};
                Probe.Check(p.Created.Select(x=>x.GetType()).SequenceEqual(expectedTypes),"inferred closed DI keys");
                Probe.Check(p.Disposed.Count==0,"pipeline must not dispose borrowed services");
                Probe.Check(p.Gate==null||p.Gate.Consumptions==1,"single handler ValueTask consumption");
                scope.Dispose();Probe.Check(p.Disposed.SequenceEqual(p.Created.AsEnumerable().Reverse()),"DI scope owns cleanup");
                return JsonSerializer.Serialize(new{marker=typeof(T).Name,mode,value,status=task.Status.ToString(),events=p.Events,
                    created=p.Created.Select(x=>x.GetType().ToString()),disposed=p.Disposed.Select(x=>x.GetType().ToString()),consumptions=p.Gate?.Consumptions??0});
            }
            public static async Task<string> Run(){
                var rows=new List<object>();foreach(var mode in new[]{"pending","before","fault","replace-cancel","pre-cancel"}){
                    var generated=await Observe<int>(true,mode);var reference=await Observe<int>(false,mode);Probe.Check(generated==reference,"generated/reference difference");rows.Add(JsonDocument.Parse(generated).RootElement.Clone());
                }
                var other=await Observe<string>(true,"pending");Probe.Check(other==await Observe<string>(false,"pending"),"second closed marker");rows.Add(JsonDocument.Parse(other).RootElement.Clone());
                return JsonSerializer.Serialize(new{pairs=rows.Count,dispatches=rows.Count*2,rows});
            }
        }
        }
        """;

    [Fact]
    public async Task OpenSendAsync_borrowed_opaque_wrapper_preserves_generic_DI_and_try_finally_boundaries()
    {
        var root = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        var folder = string.IsNullOrEmpty(root) ? null : Path.Combine(root, "open-send-async-wrapper");
        var configuration = OpenSendWrapperConfiguration("Composition", "Stage.Wrapper(typeof(Logging<,>))");
        var source = OpenSendWrapperModule.Replace("__CONFIGURATION__", configuration) + OpenSendWrapperConsumer;
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "OpenSendWrapper_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        SaveSendAllSync(folder, source, driver, output, diagnostics);
        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        var catalog = Assert.IsType<CompositionSelectionCatalog>(Assert.Single(driver.GetRunResult().Results.Single().TrackedSteps["CompositionSelectionModel"].SelectMany(s => s.Outputs)).Value);
        var route = Assert.Single(Assert.Single(catalog.Compositions).Pipelines);
        var wrapper = Assert.Single(CanonicalWrapperEmissionNormalizer.Create(catalog, CanonicalExecutionGraphNormalizer.Create(catalog)).Compositions);
        Assert.Equal(route.SendGeneric, wrapper.Registration.Generic);
        Assert.Equal(route.SendOpenRegistration, wrapper.Registration.OpenRegistration);
        var api = output.GetTypeByMetadataName("OpenSendWrapper.Generated.Composition.IZendiator")!;
        Assert.False(api.IsGenericType);
        Assert.Equal(1, Assert.Single(api.GetMembers("SendAsync").OfType<IMethodSymbol>()).Arity);
        using var bytes = new MemoryStream();
        var emitted = output.Emit(bytes);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        if (folder != null)
            File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
        bytes.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var result = await assembly.GetType("OpenSendWrapper.Entry")!.GetMethod("Run")!.CreateDelegate<Func<Task<string>>>()();
        if (folder != null)
            File.WriteAllText(Path.Combine(folder, "runtime.json"), result);
        using var json = JsonDocument.Parse(result);
        Assert.Equal(6, json.RootElement.GetProperty("pairs").GetInt32());
    }

    [Fact]
    public void OpenSendAsync_wrapper_rejects_uninferred_sessions_and_reversed_hook_order()
    {
        var folder = OpenSendAsyncFolder("wrapper-rejected");
        var rejected = string.Join("\n", new[] {
            OpenSendWrapperConfiguration("Undetermined", "Stage.Wrapper(typeof(UninferredWrapper<,,>))"),
            OpenSendWrapperConfiguration("Session", "Stage.Wrapper(typeof(Logging<,>),typeof(Factory<>))"),
            OpenSendWrapperConfiguration("Reversed", "Stage.Hook(typeof(Hook<>)),Stage.Wrapper(typeof(Logging<,>))") });
        var negative = OpenSendWrapperModule.Replace("__CONFIGURATION__", rejected) + """
            namespace OpenSendWrapper {
            public sealed class UninferredWrapper<R,S,Unused>(Probe p):Logging<R,S>(p) where R:class,IRequest<S>;
            public sealed class Factory<T>;
            public sealed class Hook<T>:IAsyncSendRequestTransform<Query<T>>,IAsyncSendResponseTransform<Query<T>,int>,IAsyncSendExceptionObserver<Query<T>>,IAsyncSendFinallyObserver<Query<T>> {
                public ValueTask<Query<T>> BeforeAsync(Query<T> r,CancellationToken t)=>new(r);
                public ValueTask<int> AfterAsync(Query<T> r,int value,CancellationToken t)=>new(value);
                public ValueTask OnExceptionAsync(Query<T> r,Exception error,CancellationToken t)=>default;
                public ValueTask FinallyAsync(Query<T> r,SendExitInfo exit,CancellationToken t)=>default;
            }
            }
            """;
        var negativeDriver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(negative, "OpenSendWrapperRejected_" + Guid.NewGuid().ToString("N")), out var rejectedOutput, out var refused);
        SaveSendAllSync(folder == null ? null : Path.Combine(folder, "rejected"), negative, negativeDriver, rejectedOutput, refused);
        Assert.NotEmpty(refused);
        Assert.All(refused, d =>
        {
            Assert.Contains(d.Id, new[] { "ZEN0035", "ZEN0037" });
            Assert.True(d.Location.IsInSource);
        });
        foreach (var name in new[] { "Undetermined", "Session", "Reversed" })
        {
            Assert.Null(rejectedOutput.GetTypeByMetadataName("OpenSendWrapper.Generated." + name + ".IZendiator"));
            Assert.Null(rejectedOutput.GetTypeByMetadataName("OpenSendWrapper.Generated." + name + ".ZendiatorGeneratedRegistrar"));
        }
        Assert.Empty(rejectedOutput.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
    }
}
