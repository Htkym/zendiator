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

public static class MrHost
{
    public static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddMediatR(static cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(MrPing).Assembly);
            cfg.AddOpenBehavior(typeof(MrB1<,>));
            cfg.AddOpenBehavior(typeof(MrB2<,>));
            cfg.AddOpenBehavior(typeof(MrB3<,>));
            cfg.AddOpenBehavior(typeof(MrB4<,>));
            cfg.AddOpenBehavior(typeof(MrB5<,>));
        });
        return services.BuildServiceProvider();
    }
}
