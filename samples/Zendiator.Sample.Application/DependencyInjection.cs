using Microsoft.Extensions.DependencyInjection;
using Zendiator.DependencyInjection;
using Zendiator.Sample.Contracts;

namespace Zendiator.Sample.Application;

/// <summary>Application composition root. Generation happens in this compilation.</summary>
public static class DependencyInjection
{
    /// <summary>Adds application services and generates the mediator.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddZendiator(static configuration =>
        {
            configuration.Namespace = "Zendiator.Sample.Application.Generated";
            configuration.RegisterServicesFromAssemblyContaining<ApplicationAssemblyMarker>();
            configuration.RegisterServicesFromAssemblyContaining<ContractsAssemblyMarker>();
            configuration.AddOpenBehavior(typeof(LoggingBehavior<,>), order: 0);
            configuration.AddOpenBehavior(typeof(CommandLoggingBehavior<>), order: 1);
            configuration.AddOpenStreamBehavior(typeof(StreamLoggingBehavior<,>), order: 2);
        });

        return services;
    }
}
