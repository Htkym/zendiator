using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Zendiator.DependencyInjection;

namespace Zendiator.Configuration.Tests;

public sealed class ConfigurationTests
{
    private sealed class MarkerA;
    private sealed class MarkerB;
    private sealed class BehaviorA;
    private sealed class BehaviorB;
    private sealed class Note;
    private sealed class HandlerA;

    [Fact]
    public void Defaults_match_assembly_mode()
    {
        var snapshot = new ZendiatorConfiguration().Snapshot();
        Assert.Null(snapshot.Namespace);
        Assert.Equal(ServiceLifetime.Scoped, snapshot.ServiceLifetime);
        Assert.Empty(snapshot.AssemblyMarkers);
        Assert.Empty(snapshot.Behaviors);
        Assert.Empty(snapshot.Notifications);
        Assert.Empty(snapshot.HandlerOrders);
    }

    [Fact]
    public void Recording_normalizes_for_comparison()
    {
        ZendiatorConfigurationSnapshot First()
        {
            var configuration = new ZendiatorConfiguration
            {
                Namespace = "MyApp.Generated",
                ServiceLifetime = ServiceLifetime.Singleton,
            };
            configuration.RegisterServicesFromAssemblyContaining<MarkerB>();
            configuration.RegisterServicesFromAssemblyContaining<MarkerA>();
            configuration.AddOpenBehavior(typeof(BehaviorB), order: 1);
            configuration.AddOpenBehavior(typeof(BehaviorA), order: 0);
            configuration.AddNotification<Note>();
            configuration.ConfigureHandlerOrder(typeof(HandlerA), order: 3);
            return configuration.Snapshot();
        }

        ZendiatorConfigurationSnapshot Second()
        {
            var configuration = new ZendiatorConfiguration();
            configuration.ConfigureHandlerOrder(typeof(HandlerA), order: 3);
            configuration.AddNotification<Note>();
            configuration.AddOpenBehavior(typeof(BehaviorA), order: 0);
            configuration.AddOpenBehavior(typeof(BehaviorB), order: 1);
            configuration.RegisterServicesFromAssemblyContaining<MarkerA>();
            configuration.RegisterServicesFromAssemblyContaining<MarkerB>();
            configuration.Namespace = "MyApp.Generated";
            configuration.ServiceLifetime = ServiceLifetime.Singleton;
            return configuration.Snapshot();
        }

        Assert.Equal(First().GetFingerprint(), Second().GetFingerprint());
    }

    [Fact]
    public void Snapshot_is_single_use_and_freezes()
    {
        var configuration = new ZendiatorConfiguration();
        _ = configuration.Snapshot();
        Assert.Throws<InvalidOperationException>(() => configuration.Snapshot());
        Assert.Throws<InvalidOperationException>(() => configuration.Namespace = "X");
        Assert.Throws<InvalidOperationException>(() => configuration.RegisterServicesFromAssemblyContaining<MarkerA>());
    }

    [Theory]
    [InlineData(typeof(BehaviorA), 0, typeof(BehaviorA), 1)]
    [InlineData(typeof(BehaviorA), 0, typeof(BehaviorB), 0)]
    public void Duplicate_behaviors_and_orders_fail(Type first, int firstOrder, Type second, int secondOrder)
    {
        var configuration = new ZendiatorConfiguration();
        configuration.AddOpenBehavior(first, order: firstOrder);
        configuration.AddOpenBehavior(second, order: secondOrder);
        Assert.Throws<InvalidOperationException>(() => configuration.Snapshot());
    }

    [Fact]
    public void Duplicate_notifications_and_handler_orders_fail()
    {
        var notifications = new ZendiatorConfiguration();
        notifications.AddNotification<Note>();
        notifications.AddNotification<Note>();
        Assert.Throws<InvalidOperationException>(() => notifications.Snapshot());

        var orders = new ZendiatorConfiguration();
        orders.ConfigureHandlerOrder(typeof(HandlerA), order: 0);
        orders.ConfigureHandlerOrder(typeof(HandlerA), order: 1);
        Assert.Throws<InvalidOperationException>(() => orders.Snapshot());
    }

    [Fact]
    public void Null_arguments_fail_fast()
    {
        var configuration = new ZendiatorConfiguration();
        Assert.Throws<ArgumentNullException>(() => configuration.AddOpenBehavior(null!, order: 0));
        Assert.Throws<ArgumentNullException>(() => configuration.ConfigureHandlerOrder(null!, order: 0));
        Assert.Throws<ArgumentNullException>(() => configuration.RegisterServicesFromAssembly(null!));
    }

    [Fact]
    public void Bootstrap_without_generation_fails_fast()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentNullException>(() => ZendiatorServiceCollectionExtensions.AddZendiator(null!));
        var error = Assert.Throws<InvalidOperationException>(() => services.AddZendiator());
        Assert.Contains("source generation", error.Message);
        Assert.Throws<InvalidOperationException>(() => services.AddZendiator(static _ => { }));
    }
}
