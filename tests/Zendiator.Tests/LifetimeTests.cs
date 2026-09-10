using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Tests;

public sealed class LifetimeTests
{
    [Fact]
    public async Task Singleton_reuses_instances_across_scopes()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Trace>();
        services.AddSingleton<AuditLog>();
        services.AddSingleton<Gate>();
        services.AddZendiator(ServiceLifetime.Singleton);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        Guid first;
        await using (var scope = provider.CreateAsyncScope())
            first = await scope.ServiceProvider.GetRequiredService<IZendiator>().SendAsync(new Identity());
        await using (var scope = provider.CreateAsyncScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
            Assert.Equal(first, await mediator.SendAsync(new Identity()));
            Assert.Same(provider.GetRequiredService<IZendiator>(), mediator);
        }
    }

    [Fact]
    public async Task Transient_creates_new_instances_per_send()
    {
        var services = new ServiceCollection();
        services.AddTransient<Trace>();
        services.AddScoped<AuditLog>();
        services.AddScoped<Gate>();
        services.AddZendiator(ServiceLifetime.Transient);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();

        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        Assert.NotEqual(await mediator.SendAsync(new Identity()), await mediator.SendAsync(new Identity()));
        Assert.NotSame(mediator, scope.ServiceProvider.GetRequiredService<IZendiator>());
    }

    [Fact]
    public void Invalid_lifetime_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ServiceCollection().AddZendiator((ServiceLifetime)42));
    }

    [Fact]
    public async Task First_registration_wins_across_lifetimes()
    {
        var singletonFirst = new ServiceCollection();
        singletonFirst.AddSingleton<Trace>();
        singletonFirst.AddScoped<AuditLog>();
        singletonFirst.AddScoped<Gate>();
        singletonFirst.AddZendiator(ServiceLifetime.Singleton);
        singletonFirst.AddZendiator(ServiceLifetime.Transient);
        await using (var provider = singletonFirst.BuildServiceProvider())
        {
            Guid first;
            await using (var scope = provider.CreateAsyncScope())
                first = await scope.ServiceProvider.GetRequiredService<IZendiator>().SendAsync(new Identity());
            await using (var scope = provider.CreateAsyncScope())
                Assert.Equal(first, await scope.ServiceProvider.GetRequiredService<IZendiator>().SendAsync(new Identity()));
        }

        var scopedFirst = new ServiceCollection();
        scopedFirst.AddScoped<Trace>();
        scopedFirst.AddScoped<AuditLog>();
        scopedFirst.AddScoped<Gate>();
        scopedFirst.AddZendiator();
        scopedFirst.AddZendiator(ServiceLifetime.Singleton);
        await using (var provider = scopedFirst.BuildServiceProvider())
        {
            Guid first;
            await using (var scope = provider.CreateAsyncScope())
                first = await scope.ServiceProvider.GetRequiredService<IZendiator>().SendAsync(new Identity());
            await using (var scope = provider.CreateAsyncScope())
                Assert.NotEqual(first, await scope.ServiceProvider.GetRequiredService<IZendiator>().SendAsync(new Identity()));
        }
    }
}
