using Microsoft.Extensions.DependencyInjection;
using Zendiator;
using Zendiator.DependencyInjection;

namespace Competitive;

[ZendiatorComposition("Competitive")]
public sealed class App;

public sealed class CanonicalBefore1<T> : ISendRequestTransform<T> where T : IWork
{
    public T Before(T request, CancellationToken token)
    {
        Work.Enter(request, 1);
        return request;
    }
}

public sealed class CanonicalBefore2<T> : ISendRequestTransform<T> where T : IWork
{
    public T Before(T request, CancellationToken token)
    {
        Work.Enter(request, 2);
        return request;
    }
}

public sealed class CanonicalBefore3<T> : ISendRequestTransform<T> where T : IWork
{
    public T Before(T request, CancellationToken token)
    {
        Work.Enter(request, 3);
        return request;
    }
}

public sealed class CanonicalBefore4<T> : ISendRequestTransform<T> where T : IWork
{
    public T Before(T request, CancellationToken token)
    {
        Work.Enter(request, 4);
        return request;
    }
}

public sealed class CanonicalBefore5<T> : ISendRequestTransform<T> where T : IWork
{
    public T Before(T request, CancellationToken token)
    {
        Work.Enter(request, 5);
        return request;
    }
}

public static class NotificationRegistration
{
    public static ServiceProvider Create()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddZendiator(configuration =>
        {
            configuration.Namespace = "Competitive.Generated.Notifications";
            configuration.ServiceLifetime = ServiceLifetime.Scoped;
            configuration.RegisterServicesFromAssemblyContaining<Notice0>();
            configuration.AddOpenBehavior(typeof(ZBehavior1<,>), order: 1);
            configuration.AddOpenBehavior(typeof(ZVBehavior1<>), order: 21);
            configuration.AddOpenBehavior(typeof(ZBehavior2<,>), order: 2);
            configuration.AddOpenBehavior(typeof(ZVBehavior2<>), order: 22);
            configuration.AddOpenBehavior(typeof(ZBehavior3<,>), order: 3);
            configuration.AddOpenBehavior(typeof(ZVBehavior3<>), order: 23);
            configuration.AddOpenBehavior(typeof(ZBehavior4<,>), order: 4);
            configuration.AddOpenBehavior(typeof(ZVBehavior4<>), order: 24);
            configuration.AddOpenBehavior(typeof(ZBehavior5<,>), order: 5);
            configuration.AddOpenBehavior(typeof(ZVBehavior5<>), order: 25);
        });
        RegistrationAudit.Record(services, "Zendiator", "Default", "before-notification-provider");
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}
