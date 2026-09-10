using Microsoft.Extensions.DependencyInjection;

namespace Zendiator.Benchmarks;

/// <summary>Shared provider factories. Setup-only code, never on the hot path.</summary>
public static class ZrHost
{
    public static ServiceProvider CreateScoped()
    {
        var services = new ServiceCollection();
        services.AddZendiator();
        return services.BuildServiceProvider();
    }

    public static ServiceProvider CreateSingleton()
    {
        var services = new ServiceCollection();
        services.AddZendiator(ServiceLifetime.Singleton);
        return services.BuildServiceProvider();
    }
}
