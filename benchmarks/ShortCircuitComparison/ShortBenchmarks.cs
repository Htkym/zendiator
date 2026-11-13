using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Evidence = Competitive.FoundatioRelease.FoundatioRuntimeEvidence;
using F = Foundatio.Mediator;
using M = MediatR;
using G = Mediator;

namespace Competitive.ShortCircuit;

public interface IShortBenchmark : IDisposable
{
    Task Setup();
    ValueTask<int> GateDispatch(ShortRequest request, CancellationToken token);
    Dictionary<string, int> ConstructionSnapshot();
}

public abstract class ShortState : IShortBenchmark
{
    protected ServiceProvider Provider = null!;
    protected IServiceScope Scope = null!;
    protected readonly CancellationTokenSource TokenSource = new();
    protected readonly ShortRequest WarmRequest = new();
    protected readonly ConstructionLog Constructions = new() { Enabled = true };
    public List<object> RegistrationDescriptors { get; } = [];
    public bool ShortConstructionDiagnosticsEnabled => ConstructionLog.Instrumented;
    protected abstract string Peer
    {
        get;
    }
    protected abstract void BindEntry();
    public abstract ValueTask<int> GateDispatch(ShortRequest request, CancellationToken token);

    [GlobalSetup]
    public async Task Setup()
    {
        if (Environment.GetEnvironmentVariable("ROOT_V8_HOST_MODE") == "timing-child" && ConstructionLog.Instrumented)
            throw new InvalidOperationException("Constructor-diagnostics assembly cannot enter a timed child");
        Initialize(Peer);
        BindEntry();
        int value = await GateDispatch(WarmRequest, TokenSource.Token).ConfigureAwait(false);
        if (value != 42)
            throw new InvalidOperationException("Warm short-circuit returned the wrong value");
        Evidence.Begin(this);
    }

    private void Initialize(string peer)
    {
        Provider = ShortRegistration.Create(peer, Constructions, services =>
        {
            foreach (var d in services)
            {
                if (d.IsKeyedService)
                {
                    RegistrationDescriptors.Add(new
                    {
                        service = d.ServiceType.FullName,
                        key = d.ServiceKey?.ToString(),
                        implementation = d.KeyedImplementationType?.FullName,
                        lifetime = d.Lifetime.ToString()
                    });
                }
                else
                {
                    RegistrationDescriptors.Add(new
                    {
                        service = d.ServiceType.FullName,
                        implementation = d.ImplementationType?.FullName,
                        factory = d.ImplementationFactory is not null,
                        lifetime = d.Lifetime.ToString()
                    });
                }
            }
        });
        Scope = Provider.CreateScope();
    }

    public Dictionary<string, int> ConstructionSnapshot() => Constructions.Snapshot();

    [GlobalCleanup]
    public void Dispose()
    {
        Scope?.Dispose();
        Provider?.Dispose();
        TokenSource.Dispose();
        Evidence.End(this);
    }
}

[MemoryDiagnoser]
public class ZendiatorShortCircuit : ShortState
{
    private Competitive.ShortCircuit.Generated.ShortCircuitApp.IZendiator entry = null!;
    protected override string Peer => "Zendiator";
    protected override void BindEntry() => entry = Scope.ServiceProvider.GetRequiredService<Competitive.ShortCircuit.Generated.ShortCircuitApp.IZendiator>();
    public ValueTask<int> Dispatch(ShortRequest request, CancellationToken token) => entry.SendAsync(request, token);
    public override ValueTask<int> GateDispatch(ShortRequest request, CancellationToken token) => Dispatch(request, token);
    [Benchmark]
    public ValueTask<int> WarmDispatch() => Dispatch(WarmRequest, TokenSource.Token);
}

[MemoryDiagnoser]
public class MediatRShortCircuit : ShortState
{
    private M.IMediator entry = null!;
    protected override string Peer => "MediatR";
    protected override void BindEntry() => entry = Scope.ServiceProvider.GetRequiredService<M.IMediator>();
    public Task<int> Dispatch(ShortRequest request, CancellationToken token) => entry.Send(request, token);
    public override ValueTask<int> GateDispatch(ShortRequest request, CancellationToken token) => new(Dispatch(request, token));
    [Benchmark]
    public Task<int> WarmDispatch() => Dispatch(WarmRequest, TokenSource.Token);
}

[MemoryDiagnoser]
public class MediatorShortCircuit : ShortState
{
    private G.Mediator entry = null!;
    protected override string Peer => "Mediator";
    protected override void BindEntry() => entry = Scope.ServiceProvider.GetRequiredService<G.Mediator>();
    public ValueTask<int> Dispatch(ShortRequest request, CancellationToken token) => entry.Send(request, token);
    public override ValueTask<int> GateDispatch(ShortRequest request, CancellationToken token) => Dispatch(request, token);
    [Benchmark]
    public ValueTask<int> WarmDispatch() => Dispatch(WarmRequest, TokenSource.Token);
}

[MemoryDiagnoser]
public class DispatchRShortCircuit : ShortState
{
    private DispatchR.IMediator entry = null!;
    protected override string Peer => "DispatchR";
    protected override void BindEntry() => entry = Scope.ServiceProvider.GetRequiredService<DispatchR.IMediator>();
    public ValueTask<int> Dispatch(ShortRequest request, CancellationToken token) => entry.Send(request, token);
    public override ValueTask<int> GateDispatch(ShortRequest request, CancellationToken token) => Dispatch(request, token);
    [Benchmark]
    public ValueTask<int> WarmDispatch() => Dispatch(WarmRequest, TokenSource.Token);
}

[MemoryDiagnoser]
public class ImmediateShortCircuit : ShortState
{
    private IShortHandler.Handler entry = null!;
    protected override string Peer => "Immediate";
    protected override void BindEntry() => entry = Scope.ServiceProvider.GetRequiredService<IShortHandler.Handler>();
    public ValueTask<int> Dispatch(ShortRequest request, CancellationToken token) => entry.HandleAsync(request, token);
    public override ValueTask<int> GateDispatch(ShortRequest request, CancellationToken token) => Dispatch(request, token);
    [Benchmark]
    public ValueTask<int> WarmDispatch() => Dispatch(WarmRequest, TokenSource.Token);
}

[MemoryDiagnoser]
public class FoundatioShortCircuit : ShortState
{
    private F.IMediator entry = null!;
    protected override string Peer => "Foundatio";
    protected override void BindEntry() => entry = Scope.ServiceProvider.GetRequiredService<F.IMediator>();
    public ValueTask<int> Dispatch(ShortRequest request, CancellationToken token) => entry.InvokeAsync<int>(request, token);
    public override ValueTask<int> GateDispatch(ShortRequest request, CancellationToken token) => Dispatch(request, token);
    [Benchmark]
    public ValueTask<int> WarmDispatch() => Dispatch(WarmRequest, TokenSource.Token);
}
