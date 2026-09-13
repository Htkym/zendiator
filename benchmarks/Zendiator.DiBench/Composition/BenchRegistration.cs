using Microsoft.Extensions.DependencyInjection;
using Zendiator;
using Zendiator.DependencyInjection;

namespace Zendiator.DiBench;

public static class BenchRegistration
{
    public static IServiceCollection AddBench(this IServiceCollection services)
    {
        services.AddZendiator(static configuration =>
        {
            configuration.Namespace = "Zendiator.DiBench.Generated";
            configuration.RegisterServicesFromAssemblyContaining<BenchMarker>();
        });
        return services;
    }
}
