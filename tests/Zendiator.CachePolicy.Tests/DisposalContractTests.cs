using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Zendiator.CachePolicy.Tests;

public sealed class DisposalContractTests
{
    [Fact]
    public async Task Async_dispose_owns_single_handler_dispose()
    {
        TestCounters.ResetAll();
        var services = new ServiceCollection();
        services.AddZendiator();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        Assert.DoesNotContain(typeof(IDisposable), mediator.GetType().GetInterfaces());
        Assert.Equal(5, await mediator.SendAsync(new DispReq()));
        Assert.Equal(5, await mediator.SendAsync(new DispReq()));
        await scope.DisposeAsync();
        Assert.Equal(1, DispHandler.DisposeCount);
        await provider.DisposeAsync();
        Assert.Equal(1, DispHandler.DisposeCount);
    }
}
