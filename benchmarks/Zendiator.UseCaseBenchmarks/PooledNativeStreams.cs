using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Zendiator;
using Zendiator.DependencyInjection;

namespace Competitive;

public sealed record InputOrdinary0(int Count, bool Asynchronous, Probe? Probe = null) : IStreamRequest<int>, IWork;
public sealed record InputPooled0(int Count, bool Asynchronous, Probe? Probe = null) : IStreamRequest<int>, IWork;
public sealed record InputOrdinary3(int Count, bool Asynchronous, Probe? Probe = null) : IStreamRequest<int>, ILevel3;
public sealed record InputPooled3(int Count, bool Asynchronous, Probe? Probe = null) : IStreamRequest<int>, ILevel3;
public sealed record InputOrdinary5(int Count, bool Asynchronous, Probe? Probe = null) : IStreamRequest<int>, ILevel5;
public sealed record InputPooled5(int Count, bool Asynchronous, Probe? Probe = null) : IStreamRequest<int>, ILevel5;

[ZendiatorHandler]
public sealed class OrdinaryInput0 : IStreamSourceHandler<InputOrdinary0, int, WorkStreamCursor>
{
    public ValueTask<WorkStreamCursor> OpenAsync(InputOrdinary0 request, CancellationToken token) =>
        new(new WorkStreamCursor(request.Count, request.Asynchronous, request.Probe, token));
}

[ZendiatorHandler]
public sealed class PooledInput0 : IStreamSourceHandler<InputPooled0, int, PooledWorkStreamCursor>
{
    public ValueTask<PooledWorkStreamCursor> OpenAsync(InputPooled0 request, CancellationToken token) =>
        new(new PooledWorkStreamCursor(request.Count, request.Asynchronous, request.Probe, token));
}

[ZendiatorHandler]
public sealed class OrdinaryInput3 : IStreamSourceHandler<InputOrdinary3, int, WorkStreamCursor>
{
    public ValueTask<WorkStreamCursor> OpenAsync(InputOrdinary3 request, CancellationToken token) =>
        new(new WorkStreamCursor(request.Count, request.Asynchronous, request.Probe, token));
}

[ZendiatorHandler]
public sealed class PooledInput3 : IStreamSourceHandler<InputPooled3, int, PooledWorkStreamCursor>
{
    public ValueTask<PooledWorkStreamCursor> OpenAsync(InputPooled3 request, CancellationToken token) =>
        new(new PooledWorkStreamCursor(request.Count, request.Asynchronous, request.Probe, token));
}

[ZendiatorHandler]
public sealed class OrdinaryInput5 : IStreamSourceHandler<InputOrdinary5, int, WorkStreamCursor>
{
    public ValueTask<WorkStreamCursor> OpenAsync(InputOrdinary5 request, CancellationToken token) =>
        new(new WorkStreamCursor(request.Count, request.Asynchronous, request.Probe, token));
}

[ZendiatorHandler]
public sealed class PooledInput5 : IStreamSourceHandler<InputPooled5, int, PooledWorkStreamCursor>
{
    public ValueTask<PooledWorkStreamCursor> OpenAsync(InputPooled5 request, CancellationToken token) =>
        new(new PooledWorkStreamCursor(request.Count, request.Asynchronous, request.Probe, token));
}

[ZendiatorComposition("nativePoolingStreams")]
public sealed class BenchmarkPooledNativeStreams;

internal static class NativePoolingRegistration
{
    internal static ServiceProvider Create()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddZendiator<BenchmarkPooledNativeStreams>(configuration =>
        {
            configuration.Handlers<InputOrdinary0>(DispatchFamily.Stream, typeof(OrdinaryInput0));
            configuration.Pipeline<InputOrdinary0>(DispatchFamily.Stream);
            configuration.Handlers<InputPooled0>(DispatchFamily.Stream, typeof(PooledInput0));
            configuration.Pipeline<InputPooled0>(DispatchFamily.Stream);
            configuration.Handlers<InputOrdinary3>(DispatchFamily.Stream, typeof(OrdinaryInput3));
            configuration.Pipeline<InputOrdinary3>(DispatchFamily.Stream,
                Stage.Wrapper<ZSBehavior3<InputOrdinary3, int>>(),
                Stage.Wrapper<ZSBehavior2<InputOrdinary3, int>>(),
                Stage.Wrapper<ZSBehavior1<InputOrdinary3, int>>());
            configuration.Handlers<InputPooled3>(DispatchFamily.Stream, typeof(PooledInput3));
            configuration.Pipeline<InputPooled3>(DispatchFamily.Stream,
                Stage.Wrapper<ZSBehavior3<InputPooled3, int>>(),
                Stage.Wrapper<ZSBehavior2<InputPooled3, int>>(),
                Stage.Wrapper<ZSBehavior1<InputPooled3, int>>());
            configuration.Handlers<InputOrdinary5>(DispatchFamily.Stream, typeof(OrdinaryInput5));
            configuration.Pipeline<InputOrdinary5>(DispatchFamily.Stream,
                Stage.Wrapper<ZSBehavior5<InputOrdinary5, int>>(),
                Stage.Wrapper<ZSBehavior4<InputOrdinary5, int>>(),
                Stage.Wrapper<ZSBehavior3<InputOrdinary5, int>>(),
                Stage.Wrapper<ZSBehavior2<InputOrdinary5, int>>(),
                Stage.Wrapper<ZSBehavior1<InputOrdinary5, int>>());
            configuration.Handlers<InputPooled5>(DispatchFamily.Stream, typeof(PooledInput5));
            configuration.Pipeline<InputPooled5>(DispatchFamily.Stream,
                Stage.Wrapper<ZSBehavior5<InputPooled5, int>>(),
                Stage.Wrapper<ZSBehavior4<InputPooled5, int>>(),
                Stage.Wrapper<ZSBehavior3<InputPooled5, int>>(),
                Stage.Wrapper<ZSBehavior2<InputPooled5, int>>(),
                Stage.Wrapper<ZSBehavior1<InputPooled5, int>>());
        });
        RegistrationAudit.Record(services, "ZendiatorNativePooling", "Default", "before-provider");
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}

// This separate producer family requires sequential, single consumption of every issued ValueTask.
[MemoryDiagnoser]
public class NativePoolingBenchmarks
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private Competitive.Generated.BenchmarkPooledNativeStreams.IZendiator entry = null!;
    private InputOrdinary0 ordinary0 = null!;
    private InputPooled0 pooled0 = null!;
    private InputOrdinary5 ordinary5 = null!;
    private InputPooled5 pooled5 = null!;

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
    [Params(false, true)]
    public bool Early
    {
        get; set;
    }
    internal Type EntryType => entry.GetType();

    [GlobalSetup]
    public void Setup()
    {
        provider = NativePoolingRegistration.Create();
        scope = provider.CreateScope();
        entry = scope.ServiceProvider.GetRequiredService<Competitive.Generated.BenchmarkPooledNativeStreams.IZendiator>();
        ordinary0 = new(Count, Asynchronous);
        pooled0 = new(Count, Asynchronous);
        ordinary5 = new(Count, Asynchronous);
        pooled5 = new(Count, Asynchronous);
        ChildEvidence.Record(nameof(NativePoolingBenchmarks), "Default");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        scope.Dispose();
        provider.Dispose();
    }

    [Benchmark] public ValueTask<int> RawOrdinary() => Consume(new WorkStreamCursor(Count, Asynchronous, null, default), Early);
    [Benchmark] public ValueTask<int> RawPooled() => Consume(new PooledWorkStreamCursor(Count, Asynchronous, null, default), Early);
    [Benchmark] public ValueTask<int> ZenOrdinary0() => Consume(entry.StreamAsync(ordinary0).GetAsyncEnumerator(), Early);
    [Benchmark] public ValueTask<int> ZenPooled0() => Consume(entry.StreamAsync(pooled0).GetAsyncEnumerator(), Early);
    [Benchmark] public ValueTask<int> ZenOrdinary5() => Consume(entry.StreamAsync(ordinary5).GetAsyncEnumerator(), Early);
    [Benchmark] public ValueTask<int> ZenPooled5() => Consume(entry.StreamAsync(pooled5).GetAsyncEnumerator(), Early);

    internal static async ValueTask<int> Consume(IAsyncEnumerator<int> cursor, bool early)
    {
        try
        {
            var sum = 0;
            while (await cursor.MoveNextAsync().ConfigureAwait(false))
            {
                sum += cursor.Current;
                if (early)
                    return sum;
            }
            return sum;
        }
        finally
        {
            await cursor.DisposeAsync().ConfigureAwait(false);
        }
    }

    internal IAsyncEnumerable<int> Stream(bool pooled, int depth, int count, bool asynchronous, Probe probe, CancellationToken token) =>
        (pooled, depth) switch
        {
            (false, 0) => entry.StreamAsync(new InputOrdinary0(count, asynchronous, probe), token),
            (true, 0) => entry.StreamAsync(new InputPooled0(count, asynchronous, probe), token),
            (false, 3) => entry.StreamAsync(new InputOrdinary3(count, asynchronous, probe), token),
            (true, 3) => entry.StreamAsync(new InputPooled3(count, asynchronous, probe), token),
            (false, 5) => entry.StreamAsync(new InputOrdinary5(count, asynchronous, probe), token),
            (true, 5) => entry.StreamAsync(new InputPooled5(count, asynchronous, probe), token),
            _ => throw new ArgumentOutOfRangeException(nameof(depth))
        };
}
