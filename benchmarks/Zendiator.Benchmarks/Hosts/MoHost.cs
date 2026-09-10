using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace Zendiator.Benchmarks;

public static class MoHost
{
    public static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddMediator(static options =>
        {
            options.PipelineBehaviors = [typeof(MoB1<,>), typeof(MoB2<,>), typeof(MoB3<,>), typeof(MoB4<,>), typeof(MoB5<,>), typeof(MoOb1<,>), typeof(MoOb2<,>), typeof(MoOb3<,>), typeof(MoOb4<,>), typeof(MoOb5<,>)];
        });
        return services.BuildServiceProvider();
    }
}
