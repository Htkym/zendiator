using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.CachePolicy.Tests;

public sealed class StandardContainerTests
{
    [Theory]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Singleton)]
    [InlineData(ServiceLifetime.Transient)]
    public void Registration_adds_one_interface_type_descriptor_and_keeps_the_first_lifetime(ServiceLifetime lifetime)
    {
        var services = new ServiceCollection();
        services.AddZendiator(lifetime);
        var count = services.Count;
        services.AddZendiator(lifetime == ServiceLifetime.Singleton ? ServiceLifetime.Transient : ServiceLifetime.Singleton);
        Assert.Equal(count, services.Count);
        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IZendiator));
        Assert.Equal(typeof(Zendiator), descriptor.ImplementationType);
        Assert.Null(descriptor.ImplementationFactory);
        Assert.Equal(lifetime, descriptor.Lifetime);
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(Zendiator));
    }

    [Fact]
    public async Task Web_host_and_injected_scope_factory_use_the_same_cached_path()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddZendiator();
        var calls = 0;
        builder.Services.AddTransient<Guid0Handler>(_ => { Interlocked.Increment(ref calls); return new(); });
        builder.Services.AddSingleton<NativeScopes>();
        await using var app = builder.Build();
        var factory = app.Services.GetRequiredService<NativeScopes>().Factory;
        Guid first;
        using (var scope = factory.CreateScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
            first = await mediator.SendAsync(new Guid0());
            Assert.Equal(first, await mediator.SendAsync(new Guid0()));
            Assert.Equal(1, calls);
        }
        using (var scope = factory.CreateScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
            Assert.NotEqual(first, await mediator.SendAsync(new Guid0()));
            Assert.Equal(2, calls);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Custom_mediator_factory_keeps_the_cached_path_and_di_ownership(bool registerAfter)
    {
        var services = new ServiceCollection();
        var factoryCalls = 0;
        IZendiator Factory(IServiceProvider provider) { factoryCalls++; return new Zendiator(provider); }
        if (!registerAfter) services.AddScoped<IZendiator>(Factory);
        services.AddZendiator();
        if (registerAfter) services.AddScoped<IZendiator>(Factory);
        var calls = 0;
        services.AddTransient<Guid0Handler>(_ => { calls++; return new(); });
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        Assert.Same(mediator, scope.ServiceProvider.GetRequiredService<IZendiator>());
        Assert.Null(scope.ServiceProvider.GetService<Zendiator>());
        Assert.Equal(1, factoryCalls);
        Assert.Equal(0, calls);
        Assert.Equal(await mediator.SendAsync(new Guid0()), await mediator.SendAsync(new Guid0()));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Concrete_factory_is_independent_of_interface_registration_and_owned_by_di()
    {
        var services = new ServiceCollection();
        var factoryCalls = 0;
        services.AddScoped<Guid0Handler>();
        services.AddScoped<Zendiator>(provider => { factoryCalls++; return new Zendiator(provider); });
        services.AddZendiator();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        Assert.Equal(0, factoryCalls);
        var concrete = scope.ServiceProvider.GetRequiredService<Zendiator>();
        Assert.Equal(1, factoryCalls);
        Assert.NotSame(mediator, concrete);
        Assert.Same(concrete, scope.ServiceProvider.GetRequiredService<Zendiator>());
        Assert.Equal(1, factoryCalls);
        Assert.Equal(await mediator.SendAsync(new Guid0()), await concrete.SendAsync(new Guid0()));
    }

    [Fact]
    public void Cleanup_exceptions_propagate_from_the_standard_container()
    {
        var services = new ServiceCollection();
        services.AddZendiator();
        services.AddScoped<ThrowingCleanup>();
        using var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<ThrowingCleanup>();
        Assert.Throws<InvalidOperationException>(scope.Dispose);
        // Failed scopes must not be reused; standard DI can stop disposal at the first exception.
    }

    public sealed class ThrowingCleanup : IDisposable
    {
        public void Dispose() => throw new InvalidOperationException("cleanup");
    }

    public sealed class NativeScopes(IServiceScopeFactory factory)
    {
        public IServiceScopeFactory Factory { get; } = factory;
    }
}
