using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Allocation.Tests;

public sealed class AllocationTests
{
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
    public async Task Stream_creation_is_minimal_and_enumeration_has_no_per_item_DI_lookup()
    {
        var (mediator, scope) = CreateMediator();
        await using (scope)
        {
            // Creation alone allocates only the lightweight enumerable (no handler execution).
            var beforeCreate = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1024; i++)
                _ = mediator.StreamAsync(new AllocStream(16));
            var afterCreate = GC.GetAllocatedBytesForCurrentThread();
            var perCreate = (afterCreate - beforeCreate) / 1024.0;
            Assert.True(perCreate < 256, $"per-create {perCreate}B");

            // Full enumeration: async-iterator machinery is expected, but no per-item service lookup.
            var sum = 0;
            await foreach (var item in mediator.StreamAsync(new AllocStream(16)))
                sum += item;
            Assert.Equal(120, sum);
        }
    }
}
