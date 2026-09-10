using Foundatio.Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace Zendiator.Benchmarks;

public static class FoHost
{
    public static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        // Explicit assembly: auto-discovery does not cross into this assembly
        // from a test or benchmark host process.
        services.AddMediator(static b => b.AddAssembly(typeof(FoPing).Assembly));
        return services.BuildServiceProvider();
    }
}
