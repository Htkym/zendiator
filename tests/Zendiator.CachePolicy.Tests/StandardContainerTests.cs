using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.CachePolicy.Tests;

public sealed class StandardContainerTests
{

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

}
