using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Zendiator.DependencyInjection;
using NativeMediator = global::Zendiator.Allocation.Tests.Generated.AllocationStreams.IZendiator;

namespace Zendiator.Allocation.Tests;

public sealed class AllocationTests
{
    [Fact]
    public async Task Warm_sync_multi_span_does_not_allocate_a_result_container()
    {
        var (mediator, scope) = CreateMediator();
        await using (scope)
        {
            var sum = 0;
            var allocated = Measure(() =>
            {
                Span<int> results = stackalloc int[2];
                mediator.SendAllSync(new SyncMulti(10), results, default);
                sum += results[0] + results[1];
            });
            Assert.Equal(0, allocated);
            Assert.Equal(21 * 1025, sum);
        }
    }

    private static (IZendiator Mediator, AsyncServiceScope Scope) CreateMediator()
    {
        var services = new ServiceCollection();
        services.AddZendiator();
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var scope = provider.CreateAsyncScope();
        return (scope.ServiceProvider.GetRequiredService<IZendiator>(), scope);
    }

    private static long Measure(Action action)
    {
        action();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1024; i++)
            action();
        var after = GC.GetAllocatedBytesForCurrentThread();
        return after - before;
    }

    [Fact]
    public async Task Zero_behavior_sync_dispatch_allocates_nothing()
    {
        var (mediator, scope) = CreateMediator();
        await using (scope)
        {
            Assert.Equal(41, await mediator.SendAsync(new Direct(41)));

            var allocated = Measure(() => _ = mediator.SendAsync(new Direct(41)).GetAwaiter().GetResult());
            Assert.Equal(0, allocated);
        }
    }

    [Fact]
    public async Task Single_passthrough_behavior_sync_dispatch_allocates_nothing()
    {
        var (mediator, scope) = CreateMediator();
        await using (scope)
        {
            Assert.Equal(7, await mediator.SendAsync(new Ping(7)));

            var allocated = Measure(() => _ = mediator.SendAsync(new Ping(7)).GetAwaiter().GetResult());
            Assert.Equal(0, allocated);
        }
    }

    [Fact]
    public async Task Native_stream_completion_stays_below_256_bytes_without_per_item_containers()
    {
        var services = new ServiceCollection();
        services.AddZendiator<AllocationStreams>(configuration =>
        {
            configuration.Handlers<AllocStream>(DispatchFamily.Stream, typeof(AllocStreamHandler));
            configuration.Pipeline<AllocStream>(DispatchFamily.Stream);
        });
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<NativeMediator>();
        var request = new AllocStream(16);
        var sink = new SumSink();
        var allocated = Measure(() => mediator.StreamSource(request).ConsumeAsync(sink).GetAwaiter().GetResult());
        Assert.True(allocated / 1024d < 256, $"per-operation {allocated / 1024d}B");
        Assert.Equal(120 * 1025, sink.Sum);
        Assert.Equal(16 * 1025, sink.Calls);
    }

    private sealed class SumSink : IStreamSink<int>
    {
        public int Sum, Calls;
        public ValueTask<bool> OnNextAsync(int item, CancellationToken cancellationToken)
        {
            if (item != Calls++ % 16)
                throw new InvalidOperationException("The mutable cursor lost its iteration state.");
            Sum += item;
            return new(true);
        }
    }
}

[ZendiatorComposition("AllocationStreams")]
public sealed class AllocationStreams;
