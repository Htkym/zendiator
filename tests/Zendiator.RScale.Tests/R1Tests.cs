using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.RScale.Tests;

public sealed class R1Tests
{
    [Fact]
    public async Task Routes_return_expected_values()
    {
        var services = new ServiceCollection();
        R1.ZendiatorServiceCollectionExtensions.AddZendiator(services);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<R1.IZendiator>();
        Assert.Equal(1, await mediator.SendAsync(new R1.Q0(0)));
        Assert.Equal(43, await mediator.SendAsync(new R1.Q0(42)));
    }
}
