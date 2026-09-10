using Microsoft.Extensions.DependencyInjection;

namespace Zendiator.Benchmarks;

public static class DsHost
{
    public static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        DSoftStudio.Mediator.ServiceCollectionExtensions.AddMediator(services).RegisterMediatorHandlers();
        return services.BuildServiceProvider();
    }
}
