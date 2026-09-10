using Microsoft.Extensions.DependencyInjection;

namespace Zendiator.Benchmarks;

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
