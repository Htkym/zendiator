using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Competitive;
using F = Foundatio.Mediator;
namespace Competitive.FoundatioRelease;

public abstract class FoundatioState
{
    protected ServiceProvider Provider = null!;
    protected IServiceScope Scope = null!;
    protected F.IMediator Entry = null!;
    // Semantic input controls. Timing leaves these null/None; not BDN parameters.
    public Probe? Probe
    {
        get; set;
    }
    public CancellationToken Token
    {
        get; set;
    }
    public RegistrationDescriptorSnapshot[] RegistrationDescriptors { get; private set; } = [];
    [GlobalSetup]
    public void Setup()
    {
        Provider = Environment.GetEnvironmentVariable("ROOT_V8_HOST_MODE") == "semantic-only"
            ? FoundatioRegistration.Create(services => RegistrationDescriptors = services.Select(RegistrationDescriptorSnapshot.From).ToArray())
            : FoundatioRegistration.Create();
        Scope = Provider.CreateScope();
        Entry = Scope.ServiceProvider.GetRequiredService<F.IMediator>();
        InitializeRequest();
        FoundatioRuntimeEvidence.Begin(this);
    }
    protected virtual void InitializeRequest()
    {
    }
    protected virtual void VerifyLifecycle()
    {
    }
    [GlobalCleanup]
    public void Cleanup()
    {
        try
        {
            FoundatioRuntimeEvidence.End(this);
        }
        finally
        {
            Scope.Dispose();
            Provider.Dispose();
        }
        VerifyLifecycle();
    }
}
public sealed record RegistrationDescriptorSnapshot(string ServiceType, string? ImplementationType,
    bool Keyed, string? Key, string Lifetime, bool Factory, bool Instance)
{
    public static RegistrationDescriptorSnapshot From(ServiceDescriptor descriptor)
    {
        bool keyed = descriptor.IsKeyedService;
        return new(descriptor.ServiceType.FullName ?? descriptor.ServiceType.Name,
            (keyed ? descriptor.KeyedImplementationType : descriptor.ImplementationType)?.FullName,
            keyed, keyed ? descriptor.ServiceKey?.ToString() : null, descriptor.Lifetime.ToString(),
            keyed ? descriptor.KeyedImplementationFactory is not null : descriptor.ImplementationFactory is not null,
            keyed ? descriptor.KeyedImplementationInstance is not null : descriptor.ImplementationInstance is not null);
    }
}
public interface IFoundatioSendProbe
{
    ValueTask<int> Typed(); ValueTask<int> ResolveSend(); ValueTask<int> ScopeK1();
    ValueTask<int> ScopeK10(); ValueTask<int> ScopeK100(); ValueTask<int> FirstSend();
    int ScopeOnly(); int ScopeResolve(); void CreateFresh(); void DisposeFresh();
}
public interface IFoundatioStreamProbe
{
    int Count
    {
        get; set;
    }
    bool Asynchronous
    {
        get; set;
    }
    IAsyncEnumerable<int> Creation(); ValueTask<int> Full(); ValueTask<bool> First();
    ValueTask<int> EarlyBreak(); ValueTask<bool> Cancellation();
}
[MemoryDiagnoser]
public class FoundatioSend0 : FoundatioState, IFoundatioSendProbe
{
    private FoundatioPing0 request = null!;
    private readonly FoundatioFreshScopes fresh = new();
    [Params("Scoped")] public string Lifetime { get; set; } = "Scoped";
    protected override void InitializeRequest()
    {
        if (Lifetime != "Scoped")
            throw new InvalidOperationException("Only the matched Scoped entry profile is defined");
        request = new(41, Probe);
    }
    protected override void VerifyLifecycle() => fresh.VerifyCompleted();
    [Benchmark] public ValueTask<int> Typed() => Entry.InvokeAsync<int>(request, Token);
    [Benchmark]
    public ValueTask<int> ResolveSend()
    {
        Entry = Scope.ServiceProvider.GetRequiredService<F.IMediator>();
        return Entry.InvokeAsync<int>(request, Token);
    }
    [Benchmark]
    public async ValueTask<int> ScopeK1()
    {
        using var local = Provider.CreateScope();
        var entry = local.ServiceProvider.GetRequiredService<F.IMediator>();
        return await entry.InvokeAsync<int>(request, Token).ConfigureAwait(false);
    }
    [Benchmark]
    public async ValueTask<int> ScopeK10()
    {
        using var local = Provider.CreateScope();
        var entry = local.ServiceProvider.GetRequiredService<F.IMediator>();
        int sum = 0;
        for (int i = 0; i < 10; i++)
            sum += await entry.InvokeAsync<int>(request, Token).ConfigureAwait(false);
        return sum;
    }
    [Benchmark]
    public async ValueTask<int> ScopeK100()
    {
        using var local = Provider.CreateScope();
        var entry = local.ServiceProvider.GetRequiredService<F.IMediator>();
        int sum = 0;
        for (int i = 0; i < 100; i++)
            sum += await entry.InvokeAsync<int>(request, Token).ConfigureAwait(false);
        return sum;
    }
    [Benchmark]
    public int ScopeOnly()
    {
        using var local = Provider.CreateScope();
        return 1;
    }
    [Benchmark]
    public int ScopeResolve()
    {
        using var local = Provider.CreateScope();
        GC.KeepAlive(local.ServiceProvider.GetRequiredService<F.IMediator>());
        return 1;
    }
    [IterationSetup(Target = nameof(FirstSend))] public void CreateFresh() => fresh.Prepare(Provider);
    [IterationCleanup(Target = nameof(FirstSend))] public void DisposeFresh() => fresh.Release();
    [Benchmark, InvocationCount(FoundatioFreshScopes.Count)]
    public ValueTask<int> FirstSend()
    {
        var entry = fresh.Take().ServiceProvider.GetRequiredService<F.IMediator>();
        return entry.InvokeAsync<int>(request, Token);
    }
}
[MemoryDiagnoser]
public class FoundatioSend1 : FoundatioState, IFoundatioSendProbe
{
    private FoundatioPing1 request = null!;
    private readonly FoundatioFreshScopes fresh = new();
    [Params("Scoped")] public string Lifetime { get; set; } = "Scoped";
    protected override void InitializeRequest()
    {
        if (Lifetime != "Scoped")
            throw new InvalidOperationException("Only the matched Scoped entry profile is defined");
        request = new(41, Probe);
    }
    protected override void VerifyLifecycle() => fresh.VerifyCompleted();
    [Benchmark] public ValueTask<int> Typed() => Entry.InvokeAsync<int>(request, Token);
    [Benchmark]
    public ValueTask<int> ResolveSend()
    {
        Entry = Scope.ServiceProvider.GetRequiredService<F.IMediator>();
        return Entry.InvokeAsync<int>(request, Token);
    }
    [Benchmark]
    public async ValueTask<int> ScopeK1()
    {
        using var local = Provider.CreateScope();
        var entry = local.ServiceProvider.GetRequiredService<F.IMediator>();
        return await entry.InvokeAsync<int>(request, Token).ConfigureAwait(false);
    }
    [Benchmark]
    public async ValueTask<int> ScopeK10()
    {
        using var local = Provider.CreateScope();
        var entry = local.ServiceProvider.GetRequiredService<F.IMediator>();
        int sum = 0;
        for (int i = 0; i < 10; i++)
            sum += await entry.InvokeAsync<int>(request, Token).ConfigureAwait(false);
        return sum;
    }
    [Benchmark]
    public async ValueTask<int> ScopeK100()
    {
        using var local = Provider.CreateScope();
        var entry = local.ServiceProvider.GetRequiredService<F.IMediator>();
        int sum = 0;
        for (int i = 0; i < 100; i++)
            sum += await entry.InvokeAsync<int>(request, Token).ConfigureAwait(false);
        return sum;
    }
    [Benchmark]
    public int ScopeOnly()
    {
        using var local = Provider.CreateScope();
        return 1;
    }
    [Benchmark]
    public int ScopeResolve()
    {
        using var local = Provider.CreateScope();
        GC.KeepAlive(local.ServiceProvider.GetRequiredService<F.IMediator>());
        return 1;
    }
    [IterationSetup(Target = nameof(FirstSend))] public void CreateFresh() => fresh.Prepare(Provider);
    [IterationCleanup(Target = nameof(FirstSend))] public void DisposeFresh() => fresh.Release();
    [Benchmark, InvocationCount(FoundatioFreshScopes.Count)]
    public ValueTask<int> FirstSend()
    {
        var entry = fresh.Take().ServiceProvider.GetRequiredService<F.IMediator>();
        return entry.InvokeAsync<int>(request, Token);
    }
}
[MemoryDiagnoser]
public class FoundatioSend3 : FoundatioState, IFoundatioSendProbe
{
    private FoundatioPing3 request = null!;
    private readonly FoundatioFreshScopes fresh = new();
    [Params("Scoped")] public string Lifetime { get; set; } = "Scoped";
    protected override void InitializeRequest()
    {
        if (Lifetime != "Scoped")
            throw new InvalidOperationException("Only the matched Scoped entry profile is defined");
        request = new(41, Probe);
    }
    protected override void VerifyLifecycle() => fresh.VerifyCompleted();
    [Benchmark] public ValueTask<int> Typed() => Entry.InvokeAsync<int>(request, Token);
    [Benchmark]
    public ValueTask<int> ResolveSend()
    {
        Entry = Scope.ServiceProvider.GetRequiredService<F.IMediator>();
        return Entry.InvokeAsync<int>(request, Token);
    }
    [Benchmark]
    public async ValueTask<int> ScopeK1()
    {
        using var local = Provider.CreateScope();
        var entry = local.ServiceProvider.GetRequiredService<F.IMediator>();
        return await entry.InvokeAsync<int>(request, Token).ConfigureAwait(false);
    }
    [Benchmark]
    public async ValueTask<int> ScopeK10()
    {
        using var local = Provider.CreateScope();
        var entry = local.ServiceProvider.GetRequiredService<F.IMediator>();
        int sum = 0;
        for (int i = 0; i < 10; i++)
            sum += await entry.InvokeAsync<int>(request, Token).ConfigureAwait(false);
        return sum;
    }
    [Benchmark]
    public async ValueTask<int> ScopeK100()
    {
        using var local = Provider.CreateScope();
        var entry = local.ServiceProvider.GetRequiredService<F.IMediator>();
        int sum = 0;
        for (int i = 0; i < 100; i++)
            sum += await entry.InvokeAsync<int>(request, Token).ConfigureAwait(false);
        return sum;
    }
    [Benchmark]
    public int ScopeOnly()
    {
        using var local = Provider.CreateScope();
        return 1;
    }
    [Benchmark]
    public int ScopeResolve()
    {
        using var local = Provider.CreateScope();
        GC.KeepAlive(local.ServiceProvider.GetRequiredService<F.IMediator>());
        return 1;
    }
    [IterationSetup(Target = nameof(FirstSend))] public void CreateFresh() => fresh.Prepare(Provider);
    [IterationCleanup(Target = nameof(FirstSend))] public void DisposeFresh() => fresh.Release();
    [Benchmark, InvocationCount(FoundatioFreshScopes.Count)]
    public ValueTask<int> FirstSend()
    {
        var entry = fresh.Take().ServiceProvider.GetRequiredService<F.IMediator>();
        return entry.InvokeAsync<int>(request, Token);
    }
}
[MemoryDiagnoser]
public class FoundatioSend5 : FoundatioState, IFoundatioSendProbe
{
    private FoundatioPing5 request = null!;
    private readonly FoundatioFreshScopes fresh = new();
    [Params("Scoped")] public string Lifetime { get; set; } = "Scoped";
    protected override void InitializeRequest()
    {
        if (Lifetime != "Scoped")
            throw new InvalidOperationException("Only the matched Scoped entry profile is defined");
        request = new(41, Probe);
    }
    protected override void VerifyLifecycle() => fresh.VerifyCompleted();
    [Benchmark] public ValueTask<int> Typed() => Entry.InvokeAsync<int>(request, Token);
    [Benchmark]
    public ValueTask<int> ResolveSend()
    {
        Entry = Scope.ServiceProvider.GetRequiredService<F.IMediator>();
        return Entry.InvokeAsync<int>(request, Token);
    }
    [Benchmark]
    public async ValueTask<int> ScopeK1()
    {
        using var local = Provider.CreateScope();
        var entry = local.ServiceProvider.GetRequiredService<F.IMediator>();
        return await entry.InvokeAsync<int>(request, Token).ConfigureAwait(false);
    }
    [Benchmark]
    public async ValueTask<int> ScopeK10()
    {
        using var local = Provider.CreateScope();
        var entry = local.ServiceProvider.GetRequiredService<F.IMediator>();
        int sum = 0;
        for (int i = 0; i < 10; i++)
            sum += await entry.InvokeAsync<int>(request, Token).ConfigureAwait(false);
        return sum;
    }
    [Benchmark]
    public async ValueTask<int> ScopeK100()
    {
        using var local = Provider.CreateScope();
        var entry = local.ServiceProvider.GetRequiredService<F.IMediator>();
        int sum = 0;
        for (int i = 0; i < 100; i++)
            sum += await entry.InvokeAsync<int>(request, Token).ConfigureAwait(false);
        return sum;
    }
    [Benchmark]
    public int ScopeOnly()
    {
        using var local = Provider.CreateScope();
        return 1;
    }
    [Benchmark]
    public int ScopeResolve()
    {
        using var local = Provider.CreateScope();
        GC.KeepAlive(local.ServiceProvider.GetRequiredService<F.IMediator>());
        return 1;
    }
    [IterationSetup(Target = nameof(FirstSend))] public void CreateFresh() => fresh.Prepare(Provider);
    [IterationCleanup(Target = nameof(FirstSend))] public void DisposeFresh() => fresh.Release();
    [Benchmark, InvocationCount(FoundatioFreshScopes.Count)]
    public ValueTask<int> FirstSend()
    {
        var entry = fresh.Take().ServiceProvider.GetRequiredService<F.IMediator>();
        return entry.InvokeAsync<int>(request, Token);
    }
}
[MemoryDiagnoser]
public class FoundatioStream0 : FoundatioState, IFoundatioStreamProbe
{
    private FoundatioStreamRequest request = null!;
    [Params(16, 1024)]
    public int Count
    {
        get; set;
    }
    [Params(false, true)]
    public bool Asynchronous
    {
        get; set;
    }
    protected override void InitializeRequest() => request = new(Count, Asynchronous, Probe);
    public IAsyncEnumerable<int> Invoke(FoundatioStreamRequest request, CancellationToken token = default) => Entry.Invoke<IAsyncEnumerable<int>>(request, token);
    [Benchmark] public IAsyncEnumerable<int> Creation() => Invoke(request, Token);
    [Benchmark]
    public async ValueTask<int> Full()
    {
        int sum = 0;
        await foreach (int item in Invoke(request, Token).ConfigureAwait(false))
            sum += item;
        return sum;
    }
    [Benchmark]
    public async ValueTask<bool> First()
    {
        await using var e = Invoke(request, Token).GetAsyncEnumerator();
        return await e.MoveNextAsync();
    }
    [Benchmark]
    public async ValueTask<int> EarlyBreak()
    {
        await foreach (int item in Invoke(request, Token).ConfigureAwait(false))
            return item;
        return -1;
    }
    [Benchmark]
    public async ValueTask<bool> Cancellation()
    {
        using var cts = new CancellationTokenSource();
        await using var e = Invoke(request, cts.Token).GetAsyncEnumerator();
        await e.MoveNextAsync();
        cts.Cancel();
        try
        {
            await e.MoveNextAsync();
            return false;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
    }
}
[MemoryDiagnoser]
public class FoundatioStream1 : FoundatioState, IFoundatioStreamProbe
{
    private FoundatioStreamRequest1 request = null!;
    [Params(16, 1024)]
    public int Count
    {
        get; set;
    }
    [Params(false, true)]
    public bool Asynchronous
    {
        get; set;
    }
    protected override void InitializeRequest() => request = new(Count, Asynchronous, Probe);
    public IAsyncEnumerable<int> Invoke(FoundatioStreamRequest1 request, CancellationToken token = default) => Deferred(request, token);
    private async IAsyncEnumerable<int> Deferred(FoundatioStreamRequest1 request, [EnumeratorCancellation] CancellationToken token)
    {
        var sequence = await Entry.InvokeAsync<IAsyncEnumerable<int>>(request, token).ConfigureAwait(false);
        await foreach (int item in sequence.WithCancellation(token).ConfigureAwait(false))
            yield return item;
    }
    [Benchmark] public IAsyncEnumerable<int> Creation() => Invoke(request, Token);
    [Benchmark]
    public async ValueTask<int> Full()
    {
        int sum = 0;
        await foreach (int item in Invoke(request, Token).ConfigureAwait(false))
            sum += item;
        return sum;
    }
    [Benchmark]
    public async ValueTask<bool> First()
    {
        await using var e = Invoke(request, Token).GetAsyncEnumerator();
        return await e.MoveNextAsync();
    }
    [Benchmark]
    public async ValueTask<int> EarlyBreak()
    {
        await foreach (int item in Invoke(request, Token).ConfigureAwait(false))
            return item;
        return -1;
    }
    [Benchmark]
    public async ValueTask<bool> Cancellation()
    {
        using var cts = new CancellationTokenSource();
        await using var e = Invoke(request, cts.Token).GetAsyncEnumerator();
        await e.MoveNextAsync();
        cts.Cancel();
        try
        {
            await e.MoveNextAsync();
            return false;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
    }
}
[MemoryDiagnoser]
public class FoundatioStream3 : FoundatioState, IFoundatioStreamProbe
{
    private FoundatioStreamRequest3 request = null!;
    [Params(16, 1024)]
    public int Count
    {
        get; set;
    }
    [Params(false, true)]
    public bool Asynchronous
    {
        get; set;
    }
    protected override void InitializeRequest() => request = new(Count, Asynchronous, Probe);
    public IAsyncEnumerable<int> Invoke(FoundatioStreamRequest3 request, CancellationToken token = default) => Deferred(request, token);
    private async IAsyncEnumerable<int> Deferred(FoundatioStreamRequest3 request, [EnumeratorCancellation] CancellationToken token)
    {
        var sequence = await Entry.InvokeAsync<IAsyncEnumerable<int>>(request, token).ConfigureAwait(false);
        await foreach (int item in sequence.WithCancellation(token).ConfigureAwait(false))
            yield return item;
    }
    [Benchmark] public IAsyncEnumerable<int> Creation() => Invoke(request, Token);
    [Benchmark]
    public async ValueTask<int> Full()
    {
        int sum = 0;
        await foreach (int item in Invoke(request, Token).ConfigureAwait(false))
            sum += item;
        return sum;
    }
    [Benchmark]
    public async ValueTask<bool> First()
    {
        await using var e = Invoke(request, Token).GetAsyncEnumerator();
        return await e.MoveNextAsync();
    }
    [Benchmark]
    public async ValueTask<int> EarlyBreak()
    {
        await foreach (int item in Invoke(request, Token).ConfigureAwait(false))
            return item;
        return -1;
    }
    [Benchmark]
    public async ValueTask<bool> Cancellation()
    {
        using var cts = new CancellationTokenSource();
        await using var e = Invoke(request, cts.Token).GetAsyncEnumerator();
        await e.MoveNextAsync();
        cts.Cancel();
        try
        {
            await e.MoveNextAsync();
            return false;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
    }
}
[MemoryDiagnoser]
public class FoundatioStream5 : FoundatioState, IFoundatioStreamProbe
{
    private FoundatioStreamRequest5 request = null!;
    [Params(16, 1024)]
    public int Count
    {
        get; set;
    }
    [Params(false, true)]
    public bool Asynchronous
    {
        get; set;
    }
    protected override void InitializeRequest() => request = new(Count, Asynchronous, Probe);
    public IAsyncEnumerable<int> Invoke(FoundatioStreamRequest5 request, CancellationToken token = default) => Deferred(request, token);
    private async IAsyncEnumerable<int> Deferred(FoundatioStreamRequest5 request, [EnumeratorCancellation] CancellationToken token)
    {
        var sequence = await Entry.InvokeAsync<IAsyncEnumerable<int>>(request, token).ConfigureAwait(false);
        await foreach (int item in sequence.WithCancellation(token).ConfigureAwait(false))
            yield return item;
    }
    [Benchmark] public IAsyncEnumerable<int> Creation() => Invoke(request, Token);
    [Benchmark]
    public async ValueTask<int> Full()
    {
        int sum = 0;
        await foreach (int item in Invoke(request, Token).ConfigureAwait(false))
            sum += item;
        return sum;
    }
    [Benchmark]
    public async ValueTask<bool> First()
    {
        await using var e = Invoke(request, Token).GetAsyncEnumerator();
        return await e.MoveNextAsync();
    }
    [Benchmark]
    public async ValueTask<int> EarlyBreak()
    {
        await foreach (int item in Invoke(request, Token).ConfigureAwait(false))
            return item;
        return -1;
    }
    [Benchmark]
    public async ValueTask<bool> Cancellation()
    {
        using var cts = new CancellationTokenSource();
        await using var e = Invoke(request, cts.Token).GetAsyncEnumerator();
        await e.MoveNextAsync();
        cts.Cancel();
        try
        {
            await e.MoveNextAsync();
            return false;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
    }
}
[MemoryDiagnoser]
public class FoundatioRelay1 : FoundatioState, IFoundatioStreamProbe
{
    private FoundatioRelayRequest1 request = null!;
    [Params(16, 1024)]
    public int Count
    {
        get; set;
    }
    [Params(false, true)]
    public bool Asynchronous
    {
        get; set;
    }
    protected override void InitializeRequest() => request = new(Count, Asynchronous, Probe);
    public IAsyncEnumerable<int> Invoke(FoundatioRelayRequest1 request, CancellationToken token = default) => Entry.Invoke<IAsyncEnumerable<int>>(request, token);
    [Benchmark] public IAsyncEnumerable<int> Creation() => Invoke(request, Token);
    [Benchmark]
    public async ValueTask<int> Full()
    {
        int sum = 0;
        await foreach (int item in Invoke(request, Token).ConfigureAwait(false))
            sum += item;
        return sum;
    }
    [Benchmark]
    public async ValueTask<bool> First()
    {
        await using var e = Invoke(request, Token).GetAsyncEnumerator();
        return await e.MoveNextAsync();
    }
    [Benchmark]
    public async ValueTask<int> EarlyBreak()
    {
        await foreach (int item in Invoke(request, Token).ConfigureAwait(false))
            return item;
        return -1;
    }
    [Benchmark]
    public async ValueTask<bool> Cancellation()
    {
        using var cts = new CancellationTokenSource();
        await using var e = Invoke(request, cts.Token).GetAsyncEnumerator();
        await e.MoveNextAsync();
        cts.Cancel();
        try
        {
            await e.MoveNextAsync();
            return false;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
    }
}
[MemoryDiagnoser]
public class FoundatioRelay5 : FoundatioState, IFoundatioStreamProbe
{
    private FoundatioRelayRequest5 request = null!;
    [Params(16, 1024)]
    public int Count
    {
        get; set;
    }
    [Params(false, true)]
    public bool Asynchronous
    {
        get; set;
    }
    protected override void InitializeRequest() => request = new(Count, Asynchronous, Probe);
    public IAsyncEnumerable<int> Invoke(FoundatioRelayRequest5 request, CancellationToken token = default) => Entry.Invoke<IAsyncEnumerable<int>>(request, token);
    [Benchmark] public IAsyncEnumerable<int> Creation() => Invoke(request, Token);
    [Benchmark]
    public async ValueTask<int> Full()
    {
        int sum = 0;
        await foreach (int item in Invoke(request, Token).ConfigureAwait(false))
            sum += item;
        return sum;
    }
    [Benchmark]
    public async ValueTask<bool> First()
    {
        await using var e = Invoke(request, Token).GetAsyncEnumerator();
        return await e.MoveNextAsync();
    }
    [Benchmark]
    public async ValueTask<int> EarlyBreak()
    {
        await foreach (int item in Invoke(request, Token).ConfigureAwait(false))
            return item;
        return -1;
    }
    [Benchmark]
    public async ValueTask<bool> Cancellation()
    {
        using var cts = new CancellationTokenSource();
        await using var e = Invoke(request, cts.Token).GetAsyncEnumerator();
        await e.MoveNextAsync();
        cts.Cancel();
        try
        {
            await e.MoveNextAsync();
            return false;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
    }
}
[MemoryDiagnoser]
public class FoundatioSameProducerStream0 : FoundatioState, IFoundatioStreamProbe
{
    private FoundatioStreamRequest request = null!;
    [Params(16, 1024)]
    public int Count
    {
        get; set;
    }
    [Params(false, true)]
    public bool Asynchronous
    {
        get; set;
    }
    protected override void InitializeRequest() => request = new(Count, Asynchronous, Probe);
    public IAsyncEnumerable<int> Invoke(FoundatioStreamRequest request, CancellationToken token = default) => Entry.Invoke<IAsyncEnumerable<int>>(request, token);
    [Benchmark] public IAsyncEnumerable<int> Creation() => Invoke(request, Token);
    [Benchmark]
    public async ValueTask<int> Full()
    {
        int sum = 0;
        await foreach (int item in Invoke(request, Token).ConfigureAwait(false))
            sum += item;
        return sum;
    }
    [Benchmark]
    public async ValueTask<bool> First()
    {
        await using var e = Invoke(request, Token).GetAsyncEnumerator();
        return await e.MoveNextAsync();
    }
    [Benchmark]
    public async ValueTask<int> EarlyBreak()
    {
        await foreach (int item in Invoke(request, Token).ConfigureAwait(false))
            return item;
        return -1;
    }
    [Benchmark]
    public async ValueTask<bool> Cancellation()
    {
        using var cts = new CancellationTokenSource();
        await using var e = Invoke(request, cts.Token).GetAsyncEnumerator();
        await e.MoveNextAsync();
        cts.Cancel();
        try
        {
            await e.MoveNextAsync();
            return false;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
    }
}
