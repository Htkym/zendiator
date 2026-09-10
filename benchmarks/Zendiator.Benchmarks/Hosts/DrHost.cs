using DispatchR.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Zendiator.Benchmarks;

public static class DrHost
{
    public static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddDispatchR(typeof(DrPing).Assembly, withPipelines: true, withNotifications: true);
        return services.BuildServiceProvider();
    }
}
