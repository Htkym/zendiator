using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Foundatio.Mediator;
using Competitive;
using F = Foundatio.Mediator;

[assembly: F.MediatorConfiguration(HandlerDiscovery = F.HandlerDiscovery.Explicit,
    HandlerLifetime = F.MediatorLifetime.Scoped, MiddlewareLifetime = F.MediatorLifetime.Scoped,
    NotificationPublishStrategy = F.NotificationPublishStrategy.ForeachAwait)]

namespace Competitive.FoundatioRelease;

public static class FoundatioRegistration
{
    public static ServiceProvider Create(Action<IServiceCollection>? inspect = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediator(b => b.AddAssembly<FoundatioPing0Handler>().SetMediatorLifetime(ServiceLifetime.Scoped));
        inspect?.Invoke(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}

public sealed record FoundatioPing0(int Value = 41, Probe? Probe = null) : IWork;
[F.Handler]

public sealed class FoundatioPing0Handler
{
    public ValueTask<int> HandleAsync(FoundatioPing0 request, CancellationToken token) =>
        new(Work.Handle(request.Value, request.Probe, token));
}
public sealed record FoundatioPing1(int Value = 41, Probe? Probe = null) : ILevel1;
[F.Handler]
[F.UseMiddleware(typeof(FoundatioBefore1Middleware))]
public sealed class FoundatioPing1Handler
{
    public ValueTask<int> HandleAsync(FoundatioPing1 request, CancellationToken token) =>
        new(Work.Handle(request.Value, request.Probe, token));
}
public sealed record FoundatioPing3(int Value = 41, Probe? Probe = null) : ILevel3;
[F.Handler]
[F.UseMiddleware(typeof(FoundatioBefore1Middleware))]
[F.UseMiddleware(typeof(FoundatioBefore2Middleware))]
[F.UseMiddleware(typeof(FoundatioBefore3Middleware))]
public sealed class FoundatioPing3Handler
{
    public ValueTask<int> HandleAsync(FoundatioPing3 request, CancellationToken token) =>
        new(Work.Handle(request.Value, request.Probe, token));
}
public sealed record FoundatioPing5(int Value = 41, Probe? Probe = null) : ILevel5;
[F.Handler]
[F.UseMiddleware(typeof(FoundatioBefore1Middleware))]
[F.UseMiddleware(typeof(FoundatioBefore2Middleware))]
[F.UseMiddleware(typeof(FoundatioBefore3Middleware))]
[F.UseMiddleware(typeof(FoundatioBefore4Middleware))]
[F.UseMiddleware(typeof(FoundatioBefore5Middleware))]
public sealed class FoundatioPing5Handler
{
    public ValueTask<int> HandleAsync(FoundatioPing5 request, CancellationToken token) =>
        new(Work.Handle(request.Value, request.Probe, token));
}
[F.Middleware(1, ExplicitOnly = true)]
public sealed class FoundatioBefore1Middleware
{
    public void Before(ILevel1 request) => Work.Enter(request, 1);
}
[F.Middleware(2, ExplicitOnly = true)]
public sealed class FoundatioBefore2Middleware
{
    public void Before(ILevel2 request) => Work.Enter(request, 2);
}
[F.Middleware(3, ExplicitOnly = true)]
public sealed class FoundatioBefore3Middleware
{
    public void Before(ILevel3 request) => Work.Enter(request, 3);
}
[F.Middleware(4, ExplicitOnly = true)]
public sealed class FoundatioBefore4Middleware
{
    public void Before(ILevel4 request) => Work.Enter(request, 4);
}
[F.Middleware(5, ExplicitOnly = true)]
public sealed class FoundatioBefore5Middleware
{
    public void Before(ILevel5 request) => Work.Enter(request, 5);
}
public sealed record FoundatioStreamRequest(int Count, bool Asynchronous, Probe? Probe = null) : IWork;
[F.Handler]
public sealed class FoundatioStreamHandler
{
    public IAsyncEnumerable<int> HandleAsync(FoundatioStreamRequest request, CancellationToken token) =>
        Work.Stream(request.Count, request.Asynchronous, request.Probe, token);
}
public sealed record FoundatioRelayRequest1(int Count, bool Asynchronous, Probe? Probe = null) : IRelayLevel1;
[F.Handler]
[F.UseMiddleware(typeof(FoundatioRelay1Middleware))]
public sealed class FoundatioRelay1Handler
{
    public IAsyncEnumerable<int> HandleAsync(FoundatioRelayRequest1 request, CancellationToken token) =>
        Work.Stream(request.Count, request.Asynchronous, request.Probe, token);
}
public sealed record FoundatioRelayRequest5(int Count, bool Asynchronous, Probe? Probe = null) : IRelayLevel5;
[F.Handler]
[F.UseMiddleware(typeof(FoundatioRelay1Middleware))]
[F.UseMiddleware(typeof(FoundatioRelay2Middleware))]
[F.UseMiddleware(typeof(FoundatioRelay3Middleware))]
[F.UseMiddleware(typeof(FoundatioRelay4Middleware))]
[F.UseMiddleware(typeof(FoundatioRelay5Middleware))]
public sealed class FoundatioRelay5Handler
{
    public IAsyncEnumerable<int> HandleAsync(FoundatioRelayRequest5 request, CancellationToken token) =>
        Work.Stream(request.Count, request.Asynchronous, request.Probe, token);
}
[F.Middleware(1, ExplicitOnly = true)]
public sealed class FoundatioRelay1Middleware
{
    public void Before(IRelayLevel1 request) => Work.Relay(request, 1);
}
[F.Middleware(2, ExplicitOnly = true)]
public sealed class FoundatioRelay2Middleware
{
    public void Before(IRelayLevel2 request) => Work.Relay(request, 2);
}
[F.Middleware(3, ExplicitOnly = true)]
public sealed class FoundatioRelay3Middleware
{
    public void Before(IRelayLevel3 request) => Work.Relay(request, 3);
}
[F.Middleware(4, ExplicitOnly = true)]
public sealed class FoundatioRelay4Middleware
{
    public void Before(IRelayLevel4 request) => Work.Relay(request, 4);
}
[F.Middleware(5, ExplicitOnly = true)]
public sealed class FoundatioRelay5Middleware
{
    public void Before(IRelayLevel5 request) => Work.Relay(request, 5);
}
