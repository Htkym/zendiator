using Di.Generated;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Zendiator.DependencyInjection;

namespace Zendiator.DiConfiguration.Tests;

[Collection("DI runtime")]
public sealed class DiRuntimeTests
{
    internal static ServiceCollection CreateServices(ServiceLifetime lifetime = ServiceLifetime.Scoped, bool staticForm = false)
    {
        var services = new ServiceCollection();
        services.AddScoped<Trace>();
        if (staticForm)
        {
            global::Zendiator.DependencyInjection.ZendiatorServiceCollectionExtensions.AddZendiator(services, static configuration =>
            {
                configuration.Namespace = "Di.Generated";
                configuration.RegisterServicesFromAssemblyContaining<DiMarker>();
                configuration.RegisterServicesFromAssemblyContaining<DiMarker>();
                configuration.RegisterServicesFromAssembly(typeof(DiMarker).Assembly);
                configuration.AddOpenBehavior(typeof(global::Zendiator.DiConfiguration.Tests.DiBehavior<,>), order: 0);
                configuration.AddOpenBehavior(typeof(DiVoidBehavior<>), order: 1);
                configuration.AddOpenBehavior(typeof(DiSyncBehavior<,>), order: 2);
                configuration.AddOpenStreamBehavior(typeof(DiStreamBehavior<,>), order: 3);
                configuration.AddOpenBehavior(typeof(NewResponseBehavior<,>), order: 4);
                configuration.AddOpenBehavior(typeof(ClosedBehavior<Add, int>), order: 5);
                configuration.AddNotification<Lonely>();
                configuration.AddNotification<ClosedNote<int>>();
                configuration.ConfigureHandlerOrder(typeof(ChangedFirst), order: 1);
                configuration.ConfigureHandlerOrder(typeof(ChangedThird), order: 2);
            });
        }
        else
        {
            services.AddZendiator(configuration =>
            {
                configuration.Namespace = "Di.Generated";
                configuration.RegisterServicesFromAssemblyContaining<DiMarker>();
                configuration.AddOpenBehavior(typeof(DiBehavior<,>), order: 0);
                configuration.AddOpenBehavior(typeof(DiVoidBehavior<>), order: 1);
                configuration.AddOpenBehavior(typeof(DiSyncBehavior<,>), order: 2);
                configuration.AddOpenStreamBehavior(typeof(DiStreamBehavior<,>), order: 3);
                configuration.AddOpenBehavior(typeof(NewResponseBehavior<,>), order: 4);
                configuration.AddOpenBehavior(typeof(ClosedBehavior<Add, int>), order: 5);
                configuration.AddNotification<Lonely>();
                configuration.AddNotification<ClosedNote<int>>();
                configuration.ConfigureHandlerOrder(typeof(ChangedFirst), order: 1);
                configuration.ConfigureHandlerOrder(typeof(ChangedThird), order: 2);
                configuration.ServiceLifetime = lifetime;
            });
        }
        return services;
    }

    [Fact]
    public async Task Di_registers_and_dispatches_without_attributes()
    {
        DiBehavior<Add, int>.Calls = 0;
        await using var provider = CreateServices().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        Assert.Equal(7, await mediator.SendAsync(new Add(3, 4)));
        Assert.Equal(1, DiBehavior<Add, int>.Calls);
    }

    [Fact]
    public async Task Static_form_registers_through_the_same_registrar()
    {
        await using var provider = CreateServices(staticForm: true).BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        Assert.Equal(7, await scope.ServiceProvider.GetRequiredService<IZendiator>().SendAsync(new Add(3, 4)));
    }

    [Fact]
    public async Task Providers_stay_independent_with_shared_structure()
    {
        await using var first = CreateServices().BuildServiceProvider();
        await using var second = CreateServices().BuildServiceProvider();
        await using var scopeA = first.CreateAsyncScope();
        await using var scopeB = second.CreateAsyncScope();
        Assert.Equal(3, await scopeA.ServiceProvider.GetRequiredService<IZendiator>().SendAsync(new Add(1, 2)));
        Assert.Equal(3, await scopeB.ServiceProvider.GetRequiredService<IZendiator>().SendAsync(new Add(1, 2)));
        Assert.NotSame(
            scopeA.ServiceProvider.GetRequiredService<AddHandler>(),
            scopeB.ServiceProvider.GetRequiredService<AddHandler>());
    }

    [Fact]
    public async Task Reregistration_keeps_single_execution()
    {
        ClearHandler.Cleared.Clear();
        var services = CreateServices();
        services.AddZendiator(static configuration =>
        {
            configuration.Namespace = "Di.Generated";
            configuration.RegisterServicesFromAssemblyContaining<DiMarker>();
            configuration.AddOpenBehavior(typeof(DiBehavior<,>), order: 0);
            configuration.AddOpenBehavior(typeof(DiVoidBehavior<>), order: 1);
            configuration.AddOpenBehavior(typeof(DiSyncBehavior<,>), order: 2);
            configuration.AddOpenStreamBehavior(typeof(DiStreamBehavior<,>), order: 3);
            configuration.AddOpenBehavior(typeof(NewResponseBehavior<,>), order: 4);
            configuration.AddOpenBehavior(typeof(ClosedBehavior<Add, int>), order: 5);
            configuration.AddNotification<Lonely>();
            configuration.AddNotification<ClosedNote<int>>();
            configuration.ConfigureHandlerOrder(typeof(ChangedFirst), order: 1);
            configuration.ConfigureHandlerOrder(typeof(ChangedThird), order: 2);
        });
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IZendiator>().SendAsync(new Clear(9));
        Assert.Equal([9], ClearHandler.Cleared);
    }

    [Fact]
    public async Task Void_generic_notifications_multi_and_sync_match()
    {
        ClearHandler.Cleared.Clear();
        ChangedFirst.Seen.Clear();
        ChangedSecond.Seen.Clear();
        ChangedThird.Seen.Clear();
        ChangedOrder.Events.Clear();
        PingAllHandlerA.Got.Clear();
        PingAllHandlerB.Got.Clear();
        WipeHandler.Got.Clear();
        await using var provider = CreateServices().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();

        await mediator.SendAsync(new Clear(1));
        Assert.Equal([1], ClearHandler.Cleared);

        var item = await mediator.SendAsync(new GetById<Item>(2));
        Assert.Equal(new Item(2), item);

        await mediator.PublishAsync(new Changed(3));
        Assert.Equal([3], ChangedFirst.Seen);
        Assert.Equal([30], ChangedSecond.Seen);
        Assert.Equal([300], ChangedThird.Seen);
        Assert.Equal(["second:3", "first:3", "third:3"], ChangedOrder.Events);

        await mediator.PublishAsync(new Lonely());
        INotification erased = new Changed(4);
        await mediator.PublishAsync(erased);
        Assert.Equal([3, 4], ChangedFirst.Seen);

        Assert.Equal([6, 50], await mediator.SendAllAsync(new Pair(5)));
        await mediator.SendAllAsync(new PingAll("hi"));
        Assert.Equal(["a:hi"], PingAllHandlerA.Got);
        Assert.Equal(["b:hi"], PingAllHandlerB.Got);

        Span<byte> bytes = stackalloc byte[6];
        Assert.Equal(6, mediator.SendSync(new Parse(bytes)));
        mediator.SendSync(new Wipe(7));
        Assert.Equal([7], WipeHandler.Got);
        Assert.Equal([4, 5], mediator.SendAllSync(new AddSync(3)));

        Assert.Equal(global::Zendiator.Unit.Value, await mediator.SendAsync(new LegacyWork()));
    }

    [Fact]
    public async Task Singleton_lifetime_flows_per_call()
    {
        await using var provider = CreateServices(ServiceLifetime.Singleton).BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        AddHandler first;
        await using (var scope = provider.CreateAsyncScope())
        {
            Assert.Equal(5, await scope.ServiceProvider.GetRequiredService<IZendiator>().SendAsync(new Add(2, 3)));
            first = scope.ServiceProvider.GetRequiredService<AddHandler>();
        }
        await using (var scope = provider.CreateAsyncScope())
            Assert.Same(first, scope.ServiceProvider.GetRequiredService<AddHandler>());
    }

    [Fact]
    public async Task Di_stream_is_lazy_scope_safe_and_pipelined()
    {
        DiNumbersHandler.Calls = 0;
        DiStreamBehavior<DiNumbers, int>.Calls = 0;
        await using var provider = CreateServices().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var stream = mediator.StreamAsync(new DiNumbers(4));
        Assert.Equal(0, DiNumbersHandler.Calls);
        var items = new List<int>();
        await foreach (var i in stream) items.Add(i);
        Assert.Equal([0, 1, 2, 3], items);
        Assert.Equal(1, DiNumbersHandler.Calls);
        Assert.Equal(1, DiStreamBehavior<DiNumbers, int>.Calls);
        // Second enumeration re-resolves (no static leakage, per-enumeration pipeline).
        await foreach (var _ in mediator.StreamAsync(new DiNumbers(1))) { }
        Assert.Equal(2, DiNumbersHandler.Calls);
    }
}
