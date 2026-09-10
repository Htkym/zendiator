using Microsoft.Extensions.DependencyInjection;

namespace Zendiator.DSoftObs;

// Isolated host: the open behavior must not touch the main suite's behavior-free rows.
public static class DsObsHost
{
    public static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        // Explicit interface registration plus PrecompilePipelines() applies the behavior.
        services.AddTransient(typeof(global::DSoftStudio.Mediator.Abstractions.IPipelineBehavior<,>), typeof(DsObsBehavior<,>));
        DSoftStudio.Mediator.ServiceCollectionExtensions.AddMediator(services).RegisterMediatorHandlers().PrecompilePipelines();
        return services.BuildServiceProvider();
    }
}
