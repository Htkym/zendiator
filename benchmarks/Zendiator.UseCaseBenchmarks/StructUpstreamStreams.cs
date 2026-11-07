using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Zendiator;
using Zendiator.DependencyInjection;
using Z = Zendiator;

namespace Competitive;

// Separate opt-in series. WorkStreamCursor and the original request/consumer bodies are reused.
[ZendiatorComposition("structUpstreamStreams")]
public sealed class BenchmarkStructUpstreamStreams;

internal static class StructUpstreamRegistration
{
    internal static ServiceProvider Create()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddZendiator<BenchmarkStructUpstreamStreams>(configuration =>
        {
            configuration.Handlers<Stream0>(DispatchFamily.Stream, typeof(ZStream0));
            configuration.Pipeline<Stream0>(DispatchFamily.Stream);
            configuration.Handlers<Stream1>(DispatchFamily.Stream, typeof(ZStream1));
            configuration.Pipeline<Stream1>(DispatchFamily.Stream, Stage.Wrapper<StructZSBehavior1<Stream1, int>>());
            configuration.Handlers<Stream3>(DispatchFamily.Stream, typeof(ZStream3));
            configuration.Pipeline<Stream3>(DispatchFamily.Stream, Stage.Wrapper<StructZSBehavior3<Stream3, int>>(), Stage.Wrapper<StructZSBehavior2<Stream3, int>>(), Stage.Wrapper<StructZSBehavior1<Stream3, int>>());
            configuration.Handlers<Stream5>(DispatchFamily.Stream, typeof(ZStream5));
            configuration.Pipeline<Stream5>(DispatchFamily.Stream, Stage.Wrapper<StructZSBehavior5<Stream5, int>>(), Stage.Wrapper<StructZSBehavior4<Stream5, int>>(), Stage.Wrapper<StructZSBehavior3<Stream5, int>>(), Stage.Wrapper<StructZSBehavior2<Stream5, int>>(), Stage.Wrapper<StructZSBehavior1<Stream5, int>>());
            configuration.Handlers<Relay1>(DispatchFamily.Stream, typeof(ZRelay1));
            configuration.Pipeline<Relay1>(DispatchFamily.Stream, Stage.Wrapper<StructZRBehavior1<Relay1, int>>());
            configuration.Handlers<Relay5>(DispatchFamily.Stream, typeof(ZRelay5));
            configuration.Pipeline<Relay5>(DispatchFamily.Stream, Stage.Wrapper<StructZRBehavior5<Relay5, int>>(), Stage.Wrapper<StructZRBehavior4<Relay5, int>>(), Stage.Wrapper<StructZRBehavior3<Relay5, int>>(), Stage.Wrapper<StructZRBehavior2<Relay5, int>>(), Stage.Wrapper<StructZRBehavior1<Relay5, int>>());
        });
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}

public sealed class StructZSBehavior1<T, R> : IStructStreamMiddleware<T, R> where T : IStreamRequest<R>, ILevel1
{
    public ValueTask<IAsyncEnumerator<R>> OpenAsync(T r, IStreamUpstream<T, R> next, CancellationToken t)
    {
        Work.Enter(r, 1);
        return next.OpenAsync(r, t);
    }
    public ValueTask<IAsyncEnumerator<R>> OpenAsync<TNext>(T r, TNext next, CancellationToken t)
        where TNext : struct, IStreamUpstream<T, R>
    {
        Work.Enter(r, 1);
        return next.OpenAsync(r, t);
    }
}

public sealed class StructZSBehavior2<T, R> : IStructStreamMiddleware<T, R> where T : IStreamRequest<R>, ILevel2
{
    public ValueTask<IAsyncEnumerator<R>> OpenAsync(T r, IStreamUpstream<T, R> next, CancellationToken t)
    {
        Work.Enter(r, 2);
        return next.OpenAsync(r, t);
    }
    public ValueTask<IAsyncEnumerator<R>> OpenAsync<TNext>(T r, TNext next, CancellationToken t)
        where TNext : struct, IStreamUpstream<T, R>
    {
        Work.Enter(r, 2);
        return next.OpenAsync(r, t);
    }
}

public sealed class StructZSBehavior3<T, R> : IStructStreamMiddleware<T, R> where T : IStreamRequest<R>, ILevel3
{
    public ValueTask<IAsyncEnumerator<R>> OpenAsync(T r, IStreamUpstream<T, R> next, CancellationToken t)
    {
        Work.Enter(r, 3);
        return next.OpenAsync(r, t);
    }
    public ValueTask<IAsyncEnumerator<R>> OpenAsync<TNext>(T r, TNext next, CancellationToken t)
        where TNext : struct, IStreamUpstream<T, R>
    {
        Work.Enter(r, 3);
        return next.OpenAsync(r, t);
    }
}

public sealed class StructZSBehavior4<T, R> : IStructStreamMiddleware<T, R> where T : IStreamRequest<R>, ILevel4
{
    public ValueTask<IAsyncEnumerator<R>> OpenAsync(T r, IStreamUpstream<T, R> next, CancellationToken t)
    {
        Work.Enter(r, 4);
        return next.OpenAsync(r, t);
    }
    public ValueTask<IAsyncEnumerator<R>> OpenAsync<TNext>(T r, TNext next, CancellationToken t)
        where TNext : struct, IStreamUpstream<T, R>
    {
        Work.Enter(r, 4);
        return next.OpenAsync(r, t);
    }
}

public sealed class StructZSBehavior5<T, R> : IStructStreamMiddleware<T, R> where T : IStreamRequest<R>, ILevel5
{
    public ValueTask<IAsyncEnumerator<R>> OpenAsync(T r, IStreamUpstream<T, R> next, CancellationToken t)
    {
        Work.Enter(r, 5);
        return next.OpenAsync(r, t);
    }
    public ValueTask<IAsyncEnumerator<R>> OpenAsync<TNext>(T r, TNext next, CancellationToken t)
        where TNext : struct, IStreamUpstream<T, R>
    {
        Work.Enter(r, 5);
        return next.OpenAsync(r, t);
    }
}

public sealed class StructZRBehavior1<T, R> : IStructStreamMiddleware<T, R> where T : IStreamRequest<R>, IRelayLevel1
{
    public ValueTask<IAsyncEnumerator<R>> OpenAsync(T r, IStreamUpstream<T, R> next, CancellationToken t)
    {
        Work.Relay(r, 1);
        return next.OpenAsync(r, t);
    }
    public ValueTask<IAsyncEnumerator<R>> OpenAsync<TNext>(T r, TNext next, CancellationToken t)
        where TNext : struct, IStreamUpstream<T, R>
    {
        Work.Relay(r, 1);
        return next.OpenAsync(r, t);
    }
}

public sealed class StructZRBehavior2<T, R> : IStructStreamMiddleware<T, R> where T : IStreamRequest<R>, IRelayLevel2
{
    public ValueTask<IAsyncEnumerator<R>> OpenAsync(T r, IStreamUpstream<T, R> next, CancellationToken t)
    {
        Work.Relay(r, 2);
        return next.OpenAsync(r, t);
    }
    public ValueTask<IAsyncEnumerator<R>> OpenAsync<TNext>(T r, TNext next, CancellationToken t)
        where TNext : struct, IStreamUpstream<T, R>
    {
        Work.Relay(r, 2);
        return next.OpenAsync(r, t);
    }
}

public sealed class StructZRBehavior3<T, R> : IStructStreamMiddleware<T, R> where T : IStreamRequest<R>, IRelayLevel3
{
    public ValueTask<IAsyncEnumerator<R>> OpenAsync(T r, IStreamUpstream<T, R> next, CancellationToken t)
    {
        Work.Relay(r, 3);
        return next.OpenAsync(r, t);
    }
    public ValueTask<IAsyncEnumerator<R>> OpenAsync<TNext>(T r, TNext next, CancellationToken t)
        where TNext : struct, IStreamUpstream<T, R>
    {
        Work.Relay(r, 3);
        return next.OpenAsync(r, t);
    }
}

public sealed class StructZRBehavior4<T, R> : IStructStreamMiddleware<T, R> where T : IStreamRequest<R>, IRelayLevel4
{
    public ValueTask<IAsyncEnumerator<R>> OpenAsync(T r, IStreamUpstream<T, R> next, CancellationToken t)
    {
        Work.Relay(r, 4);
        return next.OpenAsync(r, t);
    }
    public ValueTask<IAsyncEnumerator<R>> OpenAsync<TNext>(T r, TNext next, CancellationToken t)
        where TNext : struct, IStreamUpstream<T, R>
    {
        Work.Relay(r, 4);
        return next.OpenAsync(r, t);
    }
}

public sealed class StructZRBehavior5<T, R> : IStructStreamMiddleware<T, R> where T : IStreamRequest<R>, IRelayLevel5
{
    public ValueTask<IAsyncEnumerator<R>> OpenAsync(T r, IStreamUpstream<T, R> next, CancellationToken t)
    {
        Work.Relay(r, 5);
        return next.OpenAsync(r, t);
    }
    public ValueTask<IAsyncEnumerator<R>> OpenAsync<TNext>(T r, TNext next, CancellationToken t)
        where TNext : struct, IStreamUpstream<T, R>
    {
        Work.Relay(r, 5);
        return next.OpenAsync(r, t);
    }
}

[MemoryDiagnoser]
public class ZendiatorStructUpstreamStream0
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private Competitive.Generated.BenchmarkStructUpstreamStreams.IZendiator entry = null!;
    private Stream0 request = null!;
    private readonly StreamBenchmarkSink typedSink = new();
    [Params(0, 1, 16, 1024)]
    public int Count
    {
        get; set;
    }
    [Params(false, true)]
    public bool Asynchronous
    {
        get; set;
    }
    [GlobalSetup]
    public void Setup()
    {
        provider = StructUpstreamRegistration.Create();
        scope = provider.CreateScope();
        entry = scope.ServiceProvider.GetRequiredService<Competitive.Generated.BenchmarkStructUpstreamStreams.IZendiator>();
        request = new(Count, Asynchronous);
        ChildEvidence.Record("ZendiatorStructUpstreamStream0", "Default");
    }
    [GlobalCleanup]
    public void Cleanup()
    {
        scope.Dispose();
        provider.Dispose();
    }
    public IAsyncEnumerable<int> Invoke(Stream0 r, CancellationToken t = default) => entry.StreamAsync(r, t);
    public ValueTask InvokeTyped<TSink>(Stream0 r, TSink sink, CancellationToken t = default) where TSink : Z.IStreamSink<int> => entry.StreamSource(r, t).ConsumeAsync(sink);
    [Benchmark]
    public async ValueTask<int> FullTyped()
    {
        typedSink.Reset(false);
        await InvokeTyped(request, typedSink).ConfigureAwait(false);
        return typedSink.Sum;
    }
    [Benchmark]
    public async ValueTask<int> EarlyBreakTyped()
    {
        typedSink.Reset(true);
        await InvokeTyped(request, typedSink).ConfigureAwait(false);
        return typedSink.First;
    }
    [Benchmark] public IAsyncEnumerable<int> Creation() => Invoke(request);
    [Benchmark]
    public async ValueTask<int> Full()
    {
        int sum = 0;
        await foreach (var item in Invoke(request).ConfigureAwait(false))
            sum += item;
        return sum;
    }
    [Benchmark]
    public async ValueTask<bool> First()
    {
        await using var e = Invoke(request).GetAsyncEnumerator();
        return await e.MoveNextAsync();
    }
    [Benchmark]
    public async ValueTask<int> EarlyBreak()
    {
        await foreach (var item in Invoke(request).ConfigureAwait(false))
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
public class ZendiatorStructUpstreamStream5
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private Competitive.Generated.BenchmarkStructUpstreamStreams.IZendiator entry = null!;
    private Stream5 request = null!;
    private readonly StreamBenchmarkSink typedSink = new();
    [Params(0, 1, 16, 1024)]
    public int Count
    {
        get; set;
    }
    [Params(false, true)]
    public bool Asynchronous
    {
        get; set;
    }
    [GlobalSetup]
    public void Setup()
    {
        provider = StructUpstreamRegistration.Create();
        scope = provider.CreateScope();
        entry = scope.ServiceProvider.GetRequiredService<Competitive.Generated.BenchmarkStructUpstreamStreams.IZendiator>();
        request = new(Count, Asynchronous);
        ChildEvidence.Record("ZendiatorStructUpstreamStream5", "Default");
    }
    [GlobalCleanup]
    public void Cleanup()
    {
        scope.Dispose();
        provider.Dispose();
    }
    public IAsyncEnumerable<int> Invoke(Stream5 r, CancellationToken t = default) => entry.StreamAsync(r, t);
    public ValueTask InvokeTyped<TSink>(Stream5 r, TSink sink, CancellationToken t = default) where TSink : Z.IStreamSink<int> => entry.StreamSource(r, t).ConsumeAsync(sink);
    [Benchmark]
    public async ValueTask<int> FullTyped()
    {
        typedSink.Reset(false);
        await InvokeTyped(request, typedSink).ConfigureAwait(false);
        return typedSink.Sum;
    }
    [Benchmark]
    public async ValueTask<int> EarlyBreakTyped()
    {
        typedSink.Reset(true);
        await InvokeTyped(request, typedSink).ConfigureAwait(false);
        return typedSink.First;
    }
    [Benchmark] public IAsyncEnumerable<int> Creation() => Invoke(request);
    [Benchmark]
    public async ValueTask<int> Full()
    {
        int sum = 0;
        await foreach (var item in Invoke(request).ConfigureAwait(false))
            sum += item;
        return sum;
    }
    [Benchmark]
    public async ValueTask<bool> First()
    {
        await using var e = Invoke(request).GetAsyncEnumerator();
        return await e.MoveNextAsync();
    }
    [Benchmark]
    public async ValueTask<int> EarlyBreak()
    {
        await foreach (var item in Invoke(request).ConfigureAwait(false))
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
public class ZendiatorStructUpstreamRelay5
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private Competitive.Generated.BenchmarkStructUpstreamStreams.IZendiator entry = null!;
    private Relay5 request = null!;
    private readonly StreamBenchmarkSink typedSink = new();
    [Params(0, 1, 16, 1024)]
    public int Count
    {
        get; set;
    }
    [Params(false, true)]
    public bool Asynchronous
    {
        get; set;
    }
    [GlobalSetup]
    public void Setup()
    {
        provider = StructUpstreamRegistration.Create();
        scope = provider.CreateScope();
        entry = scope.ServiceProvider.GetRequiredService<Competitive.Generated.BenchmarkStructUpstreamStreams.IZendiator>();
        request = new(Count, Asynchronous);
        ChildEvidence.Record("ZendiatorStructUpstreamRelay5", "Default");
    }
    [GlobalCleanup]
    public void Cleanup()
    {
        scope.Dispose();
        provider.Dispose();
    }
    public IAsyncEnumerable<int> Invoke(Relay5 r, CancellationToken t = default) => entry.StreamAsync(r, t);
    public ValueTask InvokeTyped<TSink>(Relay5 r, TSink sink, CancellationToken t = default) where TSink : Z.IStreamSink<int> => entry.StreamSource(r, t).ConsumeAsync(sink);
    [Benchmark]
    public async ValueTask<int> FullTyped()
    {
        typedSink.Reset(false);
        await InvokeTyped(request, typedSink).ConfigureAwait(false);
        return typedSink.Sum;
    }
    [Benchmark]
    public async ValueTask<int> EarlyBreakTyped()
    {
        typedSink.Reset(true);
        await InvokeTyped(request, typedSink).ConfigureAwait(false);
        return typedSink.First;
    }
    [Benchmark] public IAsyncEnumerable<int> Creation() => Invoke(request);
    [Benchmark]
    public async ValueTask<int> Full()
    {
        int sum = 0;
        await foreach (var item in Invoke(request).ConfigureAwait(false))
            sum += item;
        return sum;
    }
    [Benchmark]
    public async ValueTask<bool> First()
    {
        await using var e = Invoke(request).GetAsyncEnumerator();
        return await e.MoveNextAsync();
    }
    [Benchmark]
    public async ValueTask<int> EarlyBreak()
    {
        await foreach (var item in Invoke(request).ConfigureAwait(false))
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
