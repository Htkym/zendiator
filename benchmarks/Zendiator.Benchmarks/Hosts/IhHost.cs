using Microsoft.Extensions.DependencyInjection;

[assembly: global::Immediate.Handlers.Shared.ImmediateAssemblyIdentifier("Bench")]

namespace Zendiator.Benchmarks;

public static class IhHost
{
    public static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddBenchHandlers();
        return services.BuildServiceProvider();
    }
}
