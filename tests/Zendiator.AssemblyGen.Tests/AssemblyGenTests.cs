using AsmGen.Generated;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Zendiator.DependencyInjection;
using NativeMediator = global::Zendiator.AssemblyGen.Tests.Generated.AssemblyStreams.IZendiator;

namespace Zendiator.AssemblyGen.Tests;

public sealed class AssemblyGenTests
{
    [Fact]
    public async Task Assembly_mode_dispatches_without_a_handwritten_class()
    {
        var services = new ServiceCollection();
        services.AddScoped<Trace>();
        global::AsmGen.Generated.ZendiatorServiceCollectionExtensions.AddZendiator(services);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        Assert.Equal(84, await mediator.SendAsync(new Ping(42)));
        Assert.Equal(["before", "after"], scope.ServiceProvider.GetRequiredService<Trace>().Events);
    }

    [Fact]
    public async Task Native_stream_uses_explicit_composition_alongside_assembly_Send_registration()
    {
        var services = new ServiceCollection();
        services.AddScoped<Trace>();
        global::AsmGen.Generated.ZendiatorServiceCollectionExtensions.AddZendiator(services);
        services.AddZendiator<AssemblyStreams>(configuration =>
        {
            configuration.Handlers<AsmNumbers>(DispatchFamily.Stream, typeof(AsmNumbersHandler));
            configuration.Pipeline<AsmNumbers>(DispatchFamily.Stream, Stage.Wrapper<AsmStreamTrace>());
        });
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<NativeMediator>();
        var items = new List<int>();
        await foreach (var i in mediator.StreamAsync(new AsmNumbers(3)))
            items.Add(i);
        Assert.Equal([0, 1, 2], items);
        Assert.Equal(["s-before", "s-after"], scope.ServiceProvider.GetRequiredService<Trace>().Events);
    }
}

[ZendiatorComposition("AssemblyStreams")]
public sealed class AssemblyStreams;
