using Di.Generated;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.DiConfiguration.Tests;

[Collection("DI runtime")]
public sealed class RegressionTests
{
    [Fact]
    public async Task Mixed_handler_contracts_dispatch_through_the_correct_implementation()
    {
        using var provider = DiRuntimeTests.CreateServices().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        Assert.Equal(1, await mediator.SendAsync(new MixedFirst()));
        Assert.Equal(7, await mediator.SendAsync(new MixedSecond()));
        Assert.Equal(7, mediator.SendSync(new MixedSyncSecond()));
        await mediator.PublishAsync(new MixedNoteSecond());
        Assert.Equal(7, scope.ServiceProvider.GetRequiredService<MixedHandler>().NotificationResult);
        var items = new List<int>();
        await foreach (var item in mediator.StreamAsync(new MixedStreamSecond())) items.Add(item);
        Assert.Equal([7], items);
    }

    [Fact]
    public async Task Constructor_constrained_response_keeps_its_behavior()
    {
        NewResponseBehavior<Construct<ConstructedItem>, ConstructedItem>.Calls = 0;
        using var provider = DiRuntimeTests.CreateServices().BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.NotNull(await scope.ServiceProvider.GetRequiredService<IZendiator>().SendAsync(new Construct<ConstructedItem>()));
        Assert.Equal(1, NewResponseBehavior<Construct<ConstructedItem>, ConstructedItem>.Calls);
    }

    [Fact]
    public async Task Closed_generic_configuration_matches_reference_assembly_compilation()
    {
        ClosedBehavior<Add, int>.Calls = 0;
        using var provider = DiRuntimeTests.CreateServices(staticForm: true).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        Assert.Equal(3, await mediator.SendAsync(new Add(1, 2)));
        Assert.Equal(1, ClosedBehavior<Add, int>.Calls);
        await mediator.PublishAsync(new ClosedNote<int>());
    }

    [Fact]
    public void Generic_sync_void_and_combined_ref_constraints_dispatch()
    {
        using var provider = DiRuntimeTests.CreateServices().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        mediator.SendSync(new GenericReset<int>());
        Assert.Equal(1, scope.ServiceProvider.GetRequiredService<GenericResetHandler<int>>().Calls);
        mediator.SendAllSync(new GenericResetAll<int>());
        Assert.Equal(1, scope.ServiceProvider.GetRequiredService<GenericResetAllHandler<int>>().Calls);
        Assert.Equal(17, mediator.SendSync(new ConstrainedBox<Span<byte>>()));
    }
}
