using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed class RouteDisposalGateProbe
{
    public bool Pause;
    public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public readonly TaskCompletionSource Resume = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ValueTask<T> BeforeAsync<T>(T request)
    {
        if (!Pause) return new(request);
        Entered.TrySetResult();
        return ContinueAsync(request);
    }

    private async ValueTask<T> ContinueAsync<T>(T request)
    {
        await Resume.Task.ConfigureAwait(false);
        return request;
    }
}

public sealed partial class GeneratorTests
{
    private sealed class RouteDisposalBlocker : IAsyncDisposable
    {
        public RouteDisposalBlocker() { }
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Resume = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask DisposeAsync()
        {
            Entered.TrySetResult();
            await Resume.Task.ConfigureAwait(false);
        }
    }

    private sealed record OpenRouteDisposalFixture(Type Mediator, Type Handler, MethodInfo Register, Func<object, int, Task<int>> Dispatch);
    private static readonly Lazy<OpenRouteDisposalFixture> OpenRouteDisposal = new(() =>
    {
        const string source = """
            using System;using System.Threading;using System.Threading.Tasks;
            using Zendiator;using Zendiator.DependencyInjection;using Microsoft.Extensions.DependencyInjection;
            using Probe=global::Zendiator.Generator.Tests.MultiLifetimeProbe;
            using Gate=global::Zendiator.Generator.Tests.RouteDisposalGateProbe;
            namespace DisposalOpen {
                public sealed class Item;
                public sealed record Request<T>(int Value):IRequest<int> where T:class,new();
                public sealed class Before<T>:IAsyncSendRequestTransform<Request<T>>,IAsyncSendResponseTransform<Request<T>,int>,
                    IAsyncSendExceptionObserver<Request<T>>,IAsyncSendFinallyObserver<Request<T>> where T:class,new() {
                    private readonly Gate gate;public Before(Gate gate){this.gate=gate;}
                    public ValueTask<Request<T>> BeforeAsync(Request<T> request,CancellationToken token)=>gate.BeforeAsync(request);
                    public ValueTask<int> AfterAsync(Request<T> request,int value,CancellationToken token)=>new(value);
                    public ValueTask OnExceptionAsync(Request<T> request,Exception error,CancellationToken token)=>default;
                    public ValueTask FinallyAsync(Request<T> request,SendExitInfo exit,CancellationToken token)=>default;
                }
                [ZendiatorHandler]public sealed class Handler<T>:IRequestHandler<Request<T>,int>,IDisposable where T:class,new() {
                    private readonly Probe probe;public Handler(Probe probe){this.probe=probe;probe.Construct(this);}
                    public ValueTask<int> HandleAsync(Request<T> request,CancellationToken token)=>new(probe.Hit(this,request.Value));
                    public void Dispose()=>probe.Release(this);
                }
                [ZendiatorComposition("disposal-open")]public sealed class Composition;
                public static class Bridge {
                    public static IServiceCollection Register(IServiceCollection services)=>services.AddZendiator<Composition>(c=>{
                        c.Handlers(typeof(Request<>),DispatchFamily.SendAsync,typeof(Handler<>));
                        c.Pipeline(typeof(Request<>),DispatchFamily.SendAsync,Stage.Hook(typeof(Before<>)));
                    });
                    public static Task<int> Dispatch(object mediator,int value)=>
                        ((global::DisposalOpen.Generated.Composition.IZendiator)mediator).SendAsync(new Request<Item>(value)).AsTask();
                }
            }
            """;
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "RouteDisposal_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        using var bytes = new MemoryStream();
        var emitted = output.Emit(bytes);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR")))
        {
            var folder = Path.Combine(MultiArtifactRoot, "generated-artifacts", "route-disposal");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "input.cs"), source);
            foreach (var generated in driver.GetRunResult().Results.Single().GeneratedSources)
                File.WriteAllText(Path.Combine(folder, generated.HintName), generated.SourceText.ToString());
            File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
        }
        bytes.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var bridge = assembly.GetType("DisposalOpen.Bridge")!;
        return new(assembly.GetType("DisposalOpen.Generated.Composition.Zendiator")!,
            assembly.GetType("DisposalOpen.Handler`1")!.MakeGenericType(assembly.GetType("DisposalOpen.Item")!),
            bridge.GetMethod("Register")!, bridge.GetMethod("Dispatch")!.CreateDelegate<Func<object, int, Task<int>>>());
    });

    [Fact]
    public async Task Route_closed_cache_rejects_disposal_before_its_own_Dispose_runs()
    {
        var fixture = LifetimeMulti.Value;
        Task<int> Invoke(object mediator, int value) => (Task<int>)fixture.Dispatch.Invoke(null,
            [mediator, Activator.CreateInstance(fixture.Request1, [value])!])!;
        await AssertRouteRejectsDisposalAsync(fixture.Mediator, fixture.Handler, fixture.Register, Invoke);
    }

    [Fact]
    public async Task Route_open_cache_rejects_disposal_at_a_lazy_stage_after_a_pending_Before()
    {
        var fixture = OpenRouteDisposal.Value;
        await AssertRouteRejectsDisposalAsync(fixture.Mediator, fixture.Handler, fixture.Register, fixture.Dispatch, new RouteDisposalGateProbe());
    }

    private static async Task AssertRouteRejectsDisposalAsync(Type mediatorType, Type handlerType, MethodInfo register,
        Func<object, int, Task<int>> invoke, RouteDisposalGateProbe? gate = null)
    {
        var probe = new MultiLifetimeProbe();
        IServiceCollection services = new ServiceCollection();
        services.AddSingleton(probe);
        services.AddScoped<RouteDisposalBlocker>();
        if (gate is not null) services.AddSingleton(gate);
        register.Invoke(null, [services]);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        var scope = provider.CreateAsyncScope();
        RouteDisposalBlocker? blocker = null;
        Task? disposal = null;
        Task<int>? dispatch = null;
        try
        {
            // DI captures Route first, blocker next, handler last. Reverse disposal releases the
            // handler, then waits in blocker while Route.Dispose has not yet been reached.
            var mediator = scope.ServiceProvider.GetRequiredService(mediatorType);
            blocker = scope.ServiceProvider.GetRequiredService<RouteDisposalBlocker>();
            Assert.Equal(11, await invoke(mediator, 11).WaitAsync(TimeSpan.FromSeconds(10)));
            var handler = Assert.Single(probe.Created);
            if (gate is not null)
            {
                gate.Pause = true;
                dispatch = invoke(mediator, 12);
                await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.False(dispatch.IsCompleted);
            }

            disposal = scope.DisposeAsync().AsTask();
            await blocker.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(disposal.IsCompleted);
            Assert.Same(handler, Assert.Single(probe.Released));
            Assert.Throws<ObjectDisposedException>(() => scope.ServiceProvider.GetService(handlerType));

            if (gate is null) dispatch = invoke(mediator, 12);
            else gate.Resume.TrySetResult();
            await Assert.ThrowsAsync<ObjectDisposedException>(() => dispatch!.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Same(handler, Assert.Single(probe.Calls));
        }
        finally
        {
            gate?.Resume.TrySetResult();
            blocker?.Resume.TrySetResult();
            try
            {
                if (dispatch is not null)
                {
                    try { await dispatch.WaitAsync(TimeSpan.FromSeconds(10)); }
                    catch (ObjectDisposedException) { }
                }
            }
            finally
            {
                if (disposal is null) await scope.DisposeAsync();
                else await disposal.WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
    }
}
