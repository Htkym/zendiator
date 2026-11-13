using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
namespace Competitive;

public abstract class HPeerBenchState
{
    public const int FreshCount = 16384;
    protected ServiceProvider Provider = null!;
    protected IServiceScope WarmScope = null!;
    protected CancellationTokenSource TokenSource = null!;
    protected HRequest Success = null!, Failure = null!;
    protected IServiceScope[] Fresh = [];
    protected int Used;
    public HWork Workload { get; } = new();
    private int _fullIterations;
    private bool _freshStarted;

    protected void Initialize(string peer)
    {
        TokenSource = new CancellationTokenSource();
        Success = new(7, false, TokenSource.Token);
        Failure = new(7, true, TokenSource.Token);
        Provider = HPeerRegistration.Create(peer, Workload);
        WarmScope = Provider.CreateScope();
    }
    protected static void CheckWarm(ValueTask<int> operation)
    {
        if (!operation.IsCompletedSuccessfully)
            throw new InvalidOperationException("This workload must complete synchronously");
        if (operation.GetAwaiter().GetResult() != 7 + HWork.Expected)
            throw new InvalidOperationException("Warm work mismatch");
    }
    protected void Capture()
    {
        ChildEvidence.Record(this, GetType().Name, "HFullAsyncHook3-Transient");
    }
    [IterationSetup(Targets = new[] { "FirstSend", "ColdOuterFailure" })]
    public void PrepareFresh()
    {
        if (Fresh.Length != FreshCount)
            Fresh = new IServiceScope[FreshCount];
        for (var i = 0; i < Fresh.Length; i++)
            Fresh[i] = Provider.CreateScope();
        Used = 0;
        _freshStarted = true;
    }
    [IterationCleanup(Targets = new[] { "FirstSend", "ColdOuterFailure" })]
    public void ReleaseFresh()
    {
        var count = Used;
        foreach (var scope in Fresh)
            scope.Dispose();
        Array.Clear(Fresh);
        if (count == FreshCount)
        {
            _fullIterations++;
            return;
        }
        if ((count == 1 || count == 16) && _fullIterations == 0 && IsJitProbe())
            return;
        throw new InvalidOperationException($"Cold iteration used {count} of {FreshCount} scopes");
    }
    private static bool IsJitProbe() => (new System.Diagnostics.StackTrace().GetFrames() ?? [])
        .Select(frame => frame.GetMethod()).Any(method => method?.DeclaringType?.FullName == "BenchmarkDotNet.Engines.EngineFactory" && method.Name == "Jit");
    [GlobalCleanup]
    public void Cleanup()
    {
        try
        {
            Competitive.FoundatioRelease.FoundatioRuntimeEvidence.End(this);
        }
        finally
        {
            WarmScope.Dispose();
            Provider.Dispose();
            TokenSource.Dispose();
            if (_freshStarted && _fullIterations == 0)
                throw new InvalidOperationException("No complete cold iteration");
        }
    }
}

[MemoryDiagnoser]
public class HZendiatorBenchmarks : HPeerBenchState
{
    private Competitive.Generated.HPeerComposition.IZendiator _entry = null!;
    [GlobalSetup]
    public void Setup()
    {
        Initialize("Zendiator");
        _entry = WarmScope.ServiceProvider.GetRequiredService<Competitive.Generated.HPeerComposition.IZendiator>();
        CheckWarm(_entry.SendAsync(Success, TokenSource.Token));
        Capture();
    }
    [Benchmark, InvocationCount(FreshCount)]
    public ValueTask<int> FirstSend() => Fresh[Used++].ServiceProvider.GetRequiredService<Competitive.Generated.HPeerComposition.IZendiator>().SendAsync(Success, TokenSource.Token);
    [Benchmark]
    public ValueTask<int> WarmSend() => _entry.SendAsync(Success, TokenSource.Token);
    [Benchmark, InvocationCount(FreshCount)]
    public async ValueTask<int> ColdOuterFailure()
    {
        var entry = Fresh[Used++].ServiceProvider.GetRequiredService<Competitive.Generated.HPeerComposition.IZendiator>();
        try
        {
            await entry.SendAsync(Failure, TokenSource.Token).ConfigureAwait(false);
        }
        catch (HOuterFailure)
        {
            return 1;
        }
        throw new InvalidOperationException("Expected outer Hook failure");
    }
}

[MemoryDiagnoser]
public class HMediatRHistoricalBenchmarks : HPeerBenchState
{
    private MediatR.IMediator _entry = null!;
    [GlobalSetup]
    public void Setup()
    {
        Initialize("MediatRHistorical");
        _entry = WarmScope.ServiceProvider.GetRequiredService<MediatR.IMediator>();
        CheckWarm(new ValueTask<int>(_entry.Send(Success, TokenSource.Token)));
        Capture();
    }
    [Benchmark, InvocationCount(FreshCount)]
    public Task<int> FirstSend() => Fresh[Used++].ServiceProvider.GetRequiredService<MediatR.IMediator>().Send(Success, TokenSource.Token);
    [Benchmark]
    public Task<int> WarmSend() => _entry.Send(Success, TokenSource.Token);
    [Benchmark, InvocationCount(FreshCount)]
    public async ValueTask<int> ColdOuterFailure()
    {
        var entry = Fresh[Used++].ServiceProvider.GetRequiredService<MediatR.IMediator>();
        try
        {
            await entry.Send(Failure, TokenSource.Token).ConfigureAwait(false);
        }
        catch (HOuterFailure)
        {
            return 1;
        }
        throw new InvalidOperationException("Expected outer Hook failure");
    }
}

[MemoryDiagnoser]
public class HMediatorBenchmarks : HPeerBenchState
{
    private Mediator.Mediator _entry = null!;
    [GlobalSetup]
    public void Setup()
    {
        Initialize("Mediator");
        _entry = WarmScope.ServiceProvider.GetRequiredService<Mediator.Mediator>();
        CheckWarm(_entry.Send(Success, TokenSource.Token));
        Capture();
    }
    [Benchmark, InvocationCount(FreshCount)]
    public ValueTask<int> FirstSend() => Fresh[Used++].ServiceProvider.GetRequiredService<Mediator.Mediator>().Send(Success, TokenSource.Token);
    [Benchmark]
    public ValueTask<int> WarmSend() => _entry.Send(Success, TokenSource.Token);
    [Benchmark, InvocationCount(FreshCount)]
    public async ValueTask<int> ColdOuterFailure()
    {
        var entry = Fresh[Used++].ServiceProvider.GetRequiredService<Mediator.Mediator>();
        try
        {
            await entry.Send(Failure, TokenSource.Token).ConfigureAwait(false);
        }
        catch (HOuterFailure)
        {
            return 1;
        }
        throw new InvalidOperationException("Expected outer Hook failure");
    }
}

[MemoryDiagnoser]
public class HDispatchRBenchmarks : HPeerBenchState
{
    private DispatchR.IMediator _entry = null!;
    [GlobalSetup]
    public void Setup()
    {
        Initialize("DispatchR");
        _entry = WarmScope.ServiceProvider.GetRequiredService<DispatchR.IMediator>();
        CheckWarm(_entry.Send(Success, TokenSource.Token));
        Capture();
    }
    [Benchmark, InvocationCount(FreshCount)]
    public ValueTask<int> FirstSend() => Fresh[Used++].ServiceProvider.GetRequiredService<DispatchR.IMediator>().Send(Success, TokenSource.Token);
    [Benchmark]
    public ValueTask<int> WarmSend() => _entry.Send(Success, TokenSource.Token);
    [Benchmark, InvocationCount(FreshCount)]
    public async ValueTask<int> ColdOuterFailure()
    {
        var entry = Fresh[Used++].ServiceProvider.GetRequiredService<DispatchR.IMediator>();
        try
        {
            await entry.Send(Failure, TokenSource.Token).ConfigureAwait(false);
        }
        catch (HOuterFailure)
        {
            return 1;
        }
        throw new InvalidOperationException("Expected outer Hook failure");
    }
}

[MemoryDiagnoser]
public class HImmediateBenchmarks : HPeerBenchState
{
    private HImmediate.Handler _entry = null!;
    [GlobalSetup]
    public void Setup()
    {
        Initialize("Immediate");
        _entry = WarmScope.ServiceProvider.GetRequiredService<HImmediate.Handler>();
        CheckWarm(_entry.HandleAsync(Success, TokenSource.Token));
        Capture();
    }
    [Benchmark, InvocationCount(FreshCount)]
    public ValueTask<int> FirstSend() => Fresh[Used++].ServiceProvider.GetRequiredService<HImmediate.Handler>().HandleAsync(Success, TokenSource.Token);
    [Benchmark]
    public ValueTask<int> WarmSend() => _entry.HandleAsync(Success, TokenSource.Token);
    [Benchmark, InvocationCount(FreshCount)]
    public async ValueTask<int> ColdOuterFailure()
    {
        var entry = Fresh[Used++].ServiceProvider.GetRequiredService<HImmediate.Handler>();
        try
        {
            await entry.HandleAsync(Failure, TokenSource.Token).ConfigureAwait(false);
        }
        catch (HOuterFailure)
        {
            return 1;
        }
        throw new InvalidOperationException("Expected outer Hook failure");
    }
}
