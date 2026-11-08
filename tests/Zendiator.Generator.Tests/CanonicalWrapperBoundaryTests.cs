using System.Globalization;
using System.Runtime.Loader;
using System.Threading.Tasks.Sources;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed class WrapperBoundaryProbe
{
    public static readonly AsyncLocal<string?> Ambient = new();
    public readonly Exception Fault = new ArithmeticException("wrapper-boundary-sentinel");
    public readonly OperationCanceledException Cancellation = new(new CancellationToken(true));
    public WrapperBoundarySource? Source;
    public ValueTask<int> Invoke(int mode)
    {
        Ambient.Value = "wrapper";
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ja-JP");
        SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
        if (mode == 1)
            throw Fault;
        if (mode == 2)
            return ValueTask.FromException<int>(Cancellation);
        if (mode == 5)
            return ValueTask.FromException<int>(Fault);
        if (mode == 6)
            return ValueTask.FromCanceled<int>(Cancellation.CancellationToken);
        if (mode is 3 or 4)
        {
            Source = new WrapperBoundarySource();
            if (mode == 3)
                Source.Complete();
            return Source.Operation;
        }
        return new(42);
    }
}

public sealed class WrapperBoundarySource : IValueTaskSource<int>
{
    private ManualResetValueTaskSourceCore<int> _core;
    public int Consumptions;
    public ValueTask<int> Operation => new(this, _core.Version);
    public void Complete() => _core.SetResult(42);
    public int GetResult(short token)
    {
        Assert.Equal(1, ++Consumptions);
        return _core.GetResult(token);
    }
    public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);
    public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) =>
        _core.OnCompleted(continuation, state, token, flags);
}

public sealed partial class GeneratorTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(4, true)]
    [InlineData(5, false)]
    [InlineData(5, true)]
    [InlineData(6, false)]
    [InlineData(6, true)]
    public async Task Wrapper_only_boundary_restores_context_and_preserves_completion_and_single_consumption(int mode, bool valueWrapper)
    {
        var source = """
            using System.Threading;using System.Threading.Tasks;using Zendiator;using Zendiator.DependencyInjection;using Microsoft.Extensions.DependencyInjection;
            using Probe=global::Zendiator.Generator.Tests.WrapperBoundaryProbe;
            namespace Boundary{
            public sealed record Ping(int Mode):IRequest<int>;
            [ZendiatorHandler]public sealed class Handler:IRequestHandler<Ping,int>{public ValueTask<int> HandleAsync(Ping r,CancellationToken t)=>throw new System.InvalidOperationException("Short circuit must not call the handler.");}
            public sealed class Wrapper(Probe p):IAsyncSendWrapper<Ping,int>{public ValueTask<int> InvokeAsync<N>(Ping r,N n,CancellationToken t) where N:IAsyncSendContinuation<Ping,int> =>p.Invoke(r.Mode);}
            [ZendiatorComposition("boundary")]public sealed class App;
            public static class Bridge{
            public static IServiceCollection Register(IServiceCollection s)=>s.AddZendiator<App>(c=>{c.Handlers<Ping>(DispatchFamily.SendAsync,typeof(Handler));c.Pipeline<Ping>(DispatchFamily.SendAsync,Stage.Wrapper<Wrapper>());});
            public static ValueTask<int> Send(object m,int mode)=>((global::Boundary.Generated.App.IZendiator)m).SendAsync(new Ping(mode));
            }}
            """;
        if (valueWrapper)
            source = source.Replace("sealed class Wrapper", "struct Wrapper");
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "Boundary_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        using var bytes = new MemoryStream();
        var emitted = output.Emit(bytes);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        bytes.Position = 0;
        var assembly = new AssemblyLoadContext("Boundary_" + mode, isCollectible: true).LoadFromStream(bytes);
        var bridge = assembly.GetType("Boundary.Bridge")!;
        var register = bridge.GetMethod("Register")!.CreateDelegate<Func<IServiceCollection, IServiceCollection>>();
        var send = bridge.GetMethod("Send")!.CreateDelegate<Func<object, int, ValueTask<int>>>();
        var probe = new WrapperBoundaryProbe();
        var services = new ServiceCollection();
        services.AddSingleton(probe);
        register(services);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService(assembly.GetType("Boundary.Generated.App.IZendiator")!);
        var ambient = WrapperBoundaryProbe.Ambient.Value;
        var culture = CultureInfo.CurrentCulture;
        var context = SynchronizationContext.Current;
        Task<int> operation;
        try
        {
            WrapperBoundaryProbe.Ambient.Value = "caller";
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var callerContext = new SynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(callerContext);
            operation = send(mediator, mode).AsTask();
            Assert.Equal("caller", WrapperBoundaryProbe.Ambient.Value);
            Assert.Equal("en-US", CultureInfo.CurrentCulture.Name);
            Assert.Same(callerContext, SynchronizationContext.Current);
            if (mode == 4)
            {
                Assert.False(operation.IsCompleted);
                probe.Source!.Complete();
            }
        }
        finally
        {
            WrapperBoundaryProbe.Ambient.Value = ambient;
            CultureInfo.CurrentCulture = culture;
            SynchronizationContext.SetSynchronizationContext(context);
        }
        if (mode is 1 or 5)
            Assert.Same(probe.Fault, await Assert.ThrowsAsync<ArithmeticException>(() => operation));
        else if (mode is 2 or 6)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
            Assert.True(operation.IsCanceled);
        }
        else
            Assert.Equal(42, await operation);
        if (probe.Source != null)
            Assert.Equal(1, probe.Source.Consumptions);
    }
}

