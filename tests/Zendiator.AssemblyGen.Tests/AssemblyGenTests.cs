using AsmGen.Generated;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.AssemblyGen.Tests;

public sealed class AssemblyGenTests
{
    [Fact]
    public async Task Assembly_mode_dispatches_without_a_handwritten_class()
    {
        var services = new ServiceCollection();
        services.AddScoped<Trace>();
        services.AddZendiator();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        Assert.Equal(84, await mediator.SendAsync(new Ping(42)));
        Assert.Equal(["before", "after"], scope.ServiceProvider.GetRequiredService<Trace>().Events);
    }

    [Fact]
    public async Task Assembly_mode_streams_through_typed_pipeline()
    {
        var services = new ServiceCollection();
        services.AddScoped<Trace>();
        services.AddZendiator();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var items = new List<int>();
        await foreach (var i in mediator.StreamAsync(new AsmNumbers(3))) items.Add(i);
        Assert.Equal([0, 1, 2], items);
        Assert.Equal(["s-before", "s-after"], scope.ServiceProvider.GetRequiredService<Trace>().Events);
    }
}
