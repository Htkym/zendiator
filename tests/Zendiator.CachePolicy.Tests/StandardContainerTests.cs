using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.CachePolicy.Tests;

public sealed class StandardContainerTests
{
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
            scope.Dispose();
            await Assert.ThrowsAnyAsync<ObjectDisposedException>(async () => await mediator.SendAsync(new Guid0()));
        }
        using (var scope = factory.CreateScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
            Assert.NotEqual(first, await mediator.SendAsync(new Guid0()));
            Assert.Equal(2, calls);
        }
    }

    [Fact]
    public async Task Custom_mediator_factory_keeps_the_cached_path_and_di_ownership()
    {
        var services = new ServiceCollection();
        services.AddScoped<Zendiator>(static provider => new Zendiator(provider));
        services.AddZendiator();
        var calls = 0;
        services.AddTransient<Guid0Handler>(_ => { calls++; return new(); });
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        Assert.Same(mediator, scope.ServiceProvider.GetRequiredService<Zendiator>());
        Assert.Equal(await mediator.SendAsync(new Guid0()), await mediator.SendAsync(new Guid0()));
        Assert.Equal(1, calls);
        scope.Dispose();
        await Assert.ThrowsAnyAsync<ObjectDisposedException>(async () => await mediator.SendAsync(new Guid0()));
    }

    [Fact]
    public async Task Root_disposal_invalidates_a_mediator_in_an_undisposed_scope()
    {
        var services = new ServiceCollection();
        services.AddZendiator();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        await mediator.SendAsync(new Guid0());
        provider.Dispose();
        await Assert.ThrowsAnyAsync<ObjectDisposedException>(async () => await mediator.SendAsync(new Guid0()));
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
