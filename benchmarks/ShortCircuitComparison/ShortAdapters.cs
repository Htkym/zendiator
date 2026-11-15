using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using DispatchR.Extensions;
using Zendiator.DependencyInjection;
using Z = Zendiator;
using M = MediatR;
using G = Mediator;
using D = DispatchR.Abstractions.Send;
using I = Immediate.Handlers.Shared;
using F = Foundatio.Mediator;

[assembly: I.ImmediateAssemblyIdentifier("ShortCircuit")]
[assembly: F.MediatorConfiguration(HandlerDiscovery = F.HandlerDiscovery.Explicit,
    HandlerLifetime = F.MediatorLifetime.Transient, MiddlewareLifetime = F.MediatorLifetime.Transient,
    EndpointDiscovery = F.EndpointDiscovery.None)]

namespace Competitive.ShortCircuit;

[Z.ZendiatorComposition("short-circuit-comparison")]
public sealed class ShortCircuitApp;

[Z.Handler]
public sealed class ZShortHandler : Z.IRequestHandler<ShortRequest, int>
{
    public ZShortHandler(ConstructionLog log) => log.Construct("handler", this);
    public ValueTask<int> HandleAsync(ShortRequest request, CancellationToken token) => new(ShortWork.Handle(request, token));
}

public sealed class ZShort1 : Z.IAsyncSendWrapper<ShortRequest, int>
{
    public ZShort1(ConstructionLog log) => log.Construct("stage1", this);
    public ValueTask<int> InvokeAsync<TNext>(ShortRequest request, TNext next, CancellationToken token)
        where TNext : Z.IAsyncSendContinuation<ShortRequest, int>
    {
        return new(ShortWork.Short(request, token));
    }
}

public sealed class ZShort2 : Z.IAsyncSendWrapper<ShortRequest, int>
{
    public ZShort2(ConstructionLog log) => log.Construct("stage2", this);
    public ValueTask<int> InvokeAsync<TNext>(ShortRequest request, TNext next, CancellationToken token)
        where TNext : Z.IAsyncSendContinuation<ShortRequest, int>
    {
        ShortWork.Enter(request, 2, token);
        return next.InvokeAsync(request, token);
    }
}

public sealed class ZShort3 : Z.IAsyncSendWrapper<ShortRequest, int>
{
    public ZShort3(ConstructionLog log) => log.Construct("stage3", this);
    public ValueTask<int> InvokeAsync<TNext>(ShortRequest request, TNext next, CancellationToken token)
        where TNext : Z.IAsyncSendContinuation<ShortRequest, int>
    {
        ShortWork.Enter(request, 3, token);
        return next.InvokeAsync(request, token);
    }
}

public sealed class MShortHandler : M.IRequestHandler<ShortRequest, int>
{
    public MShortHandler(ConstructionLog log) => log.Construct("handler", this);
    public Task<int> Handle(ShortRequest request, CancellationToken token) => Task.FromResult(ShortWork.Handle(request, token));
}

public sealed class MShort1 : M.IPipelineBehavior<ShortRequest, int>
{
    public MShort1(ConstructionLog log) => log.Construct("stage1", this);
    public Task<int> Handle(ShortRequest request, M.RequestHandlerDelegate<int> next, CancellationToken token)
    {
        return Task.FromResult(ShortWork.Short(request, token));
    }
}

public sealed class MShort2 : M.IPipelineBehavior<ShortRequest, int>
{
    public MShort2(ConstructionLog log) => log.Construct("stage2", this);
    public Task<int> Handle(ShortRequest request, M.RequestHandlerDelegate<int> next, CancellationToken token)
    {
        ShortWork.Enter(request, 2, token);
        return next(token);
    }
}

public sealed class MShort3 : M.IPipelineBehavior<ShortRequest, int>
{
    public MShort3(ConstructionLog log) => log.Construct("stage3", this);
    public Task<int> Handle(ShortRequest request, M.RequestHandlerDelegate<int> next, CancellationToken token)
    {
        ShortWork.Enter(request, 3, token);
        return next(token);
    }
}

public sealed class GShortHandler : G.IRequestHandler<ShortRequest, int>
{
    public GShortHandler(ConstructionLog log) => log.Construct("handler", this);
    public ValueTask<int> Handle(ShortRequest request, CancellationToken token) => new(ShortWork.Handle(request, token));
}

public sealed class GShort1 : G.IPipelineBehavior<ShortRequest, int>
{
    public GShort1(ConstructionLog log) => log.Construct("stage1", this);
    public ValueTask<int> Handle(ShortRequest request, G.MessageHandlerDelegate<ShortRequest, int> next, CancellationToken token)
    {
        return new(ShortWork.Short(request, token));
    }
}

public sealed class GShort2 : G.IPipelineBehavior<ShortRequest, int>
{
    public GShort2(ConstructionLog log) => log.Construct("stage2", this);
    public ValueTask<int> Handle(ShortRequest request, G.MessageHandlerDelegate<ShortRequest, int> next, CancellationToken token)
    {
        ShortWork.Enter(request, 2, token);
        return next(request, token);
    }
}

public sealed class GShort3 : G.IPipelineBehavior<ShortRequest, int>
{
    public GShort3(ConstructionLog log) => log.Construct("stage3", this);
    public ValueTask<int> Handle(ShortRequest request, G.MessageHandlerDelegate<ShortRequest, int> next, CancellationToken token)
    {
        ShortWork.Enter(request, 3, token);
        return next(request, token);
    }
}

public sealed class DShortHandler : D.IRequestHandler<ShortRequest, ValueTask<int>>
{
    public DShortHandler(ConstructionLog log) => log.Construct("handler", this);
    public ValueTask<int> Handle(ShortRequest request, CancellationToken token) => new(ShortWork.Handle(request, token));
}

public sealed class DShort1 : D.IPipelineBehavior<ShortRequest, ValueTask<int>>
{
    public DShort1(ConstructionLog log) => log.Construct("stage1", this);
    public required D.IRequestHandler<ShortRequest, ValueTask<int>> NextPipeline
    {
        get; set;
    }
    public ValueTask<int> Handle(ShortRequest request, CancellationToken token)
    {
        return new(ShortWork.Short(request, token));
    }
}

public sealed class DShort2 : D.IPipelineBehavior<ShortRequest, ValueTask<int>>
{
    public DShort2(ConstructionLog log) => log.Construct("stage2", this);
    public required D.IRequestHandler<ShortRequest, ValueTask<int>> NextPipeline
    {
        get; set;
    }
    public ValueTask<int> Handle(ShortRequest request, CancellationToken token)
    {
        ShortWork.Enter(request, 2, token);
        return NextPipeline.Handle(request, token);
    }
}

public sealed class DShort3 : D.IPipelineBehavior<ShortRequest, ValueTask<int>>
{
    public DShort3(ConstructionLog log) => log.Construct("stage3", this);
    public required D.IRequestHandler<ShortRequest, ValueTask<int>> NextPipeline
    {
        get; set;
    }
    public ValueTask<int> Handle(ShortRequest request, CancellationToken token)
    {
        ShortWork.Enter(request, 3, token);
        return NextPipeline.Handle(request, token);
    }
}

[I.Handler]
[I.Behaviors(typeof(IShort1), typeof(IShort2), typeof(IShort3))]
public sealed partial class IShortHandler
{
    public IShortHandler(ConstructionLog log) => log.Construct("handler", this);
    private ValueTask<int> HandleAsync(ShortRequest request, CancellationToken token) => new(ShortWork.Handle(request, token));
}

public sealed class IShort1 : I.Behavior<ShortRequest, int>
{
    public IShort1(ConstructionLog log) => log.Construct("stage1", this);
    public override ValueTask<int> HandleAsync(ShortRequest request, CancellationToken token)
    {
        return new(ShortWork.Short(request, token));
    }
}

public sealed class IShort2 : I.Behavior<ShortRequest, int>
{
    public IShort2(ConstructionLog log) => log.Construct("stage2", this);
    public override ValueTask<int> HandleAsync(ShortRequest request, CancellationToken token)
    {
        ShortWork.Enter(request, 2, token);
        return Next(request, token);
    }
}

public sealed class IShort3 : I.Behavior<ShortRequest, int>
{
    public IShort3(ConstructionLog log) => log.Construct("stage3", this);
    public override ValueTask<int> HandleAsync(ShortRequest request, CancellationToken token)
    {
        ShortWork.Enter(request, 3, token);
        return Next(request, token);
    }
}

[F.Handler(Lifetime = F.MediatorLifetime.Transient)]
[F.UseMiddleware(typeof(FShort1))]
[F.UseMiddleware(typeof(FShort2))]
[F.UseMiddleware(typeof(FShort3))]
public sealed class FShortHandler
{
    public FShortHandler(ConstructionLog log) => log.Construct("handler", this);
    public ValueTask<int> HandleAsync(ShortRequest request, CancellationToken token) => new(ShortWork.Handle(request, token));
}

[F.Middleware(1, ExplicitOnly = true, Lifetime = F.MediatorLifetime.Transient)]
public sealed class FShort1
{
    public FShort1(ConstructionLog log) => log.Construct("stage1", this);
    public ValueTask<object?> ExecuteAsync(ShortRequest request, F.HandlerExecutionDelegate next, CancellationToken token)
    {
        return new((object)ShortWork.Short(request, token));
    }
}

[F.Middleware(2, ExplicitOnly = true, Lifetime = F.MediatorLifetime.Transient)]
public sealed class FShort2
{
    public FShort2(ConstructionLog log) => log.Construct("stage2", this);
    public ValueTask<object?> ExecuteAsync(ShortRequest request, F.HandlerExecutionDelegate next, CancellationToken token)
    {
        ShortWork.Enter(request, 2, token);
        return next();
    }
}

[F.Middleware(3, ExplicitOnly = true, Lifetime = F.MediatorLifetime.Transient)]
public sealed class FShort3
{
    public FShort3(ConstructionLog log) => log.Construct("stage3", this);
    public ValueTask<object?> ExecuteAsync(ShortRequest request, F.HandlerExecutionDelegate next, CancellationToken token)
    {
        ShortWork.Enter(request, 3, token);
        return next();
    }
}

public static class ShortRegistration
{
    public static readonly string[] Peers = ["Zendiator", "MediatR", "Mediator", "DispatchR", "Immediate", "Foundatio"];
    public static Type EntryType(string peer) => peer switch
    {
        "Zendiator" => typeof(Competitive.ShortCircuit.Generated.ShortCircuitApp.IZendiator),
        "MediatR" => typeof(M.IMediator),
        "Mediator" => typeof(G.Mediator),
        "DispatchR" => typeof(DispatchR.IMediator),
        "Immediate" => typeof(IShortHandler.Handler),
        "Foundatio" => typeof(F.IMediator),
        _ => throw new ArgumentOutOfRangeException(nameof(peer))
    };

    public static Type[] ReceiverTypes(string peer) => peer switch
    {
        "Zendiator" => [typeof(ZShort1), typeof(ZShort2), typeof(ZShort3), typeof(ZShortHandler)],
        "MediatR" => [typeof(MShort1), typeof(MShort2), typeof(MShort3), typeof(MShortHandler)],
        "Mediator" => [typeof(GShort1), typeof(GShort2), typeof(GShort3), typeof(GShortHandler)],
        "DispatchR" => [typeof(DShort1), typeof(DShort2), typeof(DShort3), typeof(DShortHandler)],
        "Immediate" => [typeof(IShort1), typeof(IShort2), typeof(IShort3), typeof(IShortHandler)],
        "Foundatio" => [typeof(FShort1), typeof(FShort2), typeof(FShort3), typeof(FShortHandler)],
        _ => throw new ArgumentOutOfRangeException(nameof(peer))
    };

    public static ServiceProvider Create(string peer, ConstructionLog log, Action<IServiceCollection>? inspect = null)
    {
        IServiceCollection services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        switch (peer)
        {
            case "Zendiator":
                services.AddZendiator<ShortCircuitApp>(c =>
                {
                    c.Handlers<ShortRequest>(Z.DispatchFamily.SendAsync, typeof(ZShortHandler));
                    c.Pipeline<ShortRequest>(Z.DispatchFamily.SendAsync,
                        Z.Stage.Wrapper<ZShort1>(), Z.Stage.Wrapper<ZShort2>(), Z.Stage.Wrapper<ZShort3>());
                });
                break;
            case "MediatR":
                services.AddMediatR(c =>
                {
                    c.RegisterServicesFromAssemblyContaining<ShortRequest>();
                    c.Lifetime = ServiceLifetime.Scoped;
                });
                services.Replace(ServiceDescriptor.Transient<M.IRequestHandler<ShortRequest, int>, MShortHandler>());
                services.AddTransient<M.IPipelineBehavior<ShortRequest, int>, MShort1>();
                services.AddTransient<M.IPipelineBehavior<ShortRequest, int>, MShort2>();
                services.AddTransient<M.IPipelineBehavior<ShortRequest, int>, MShort3>();
                break;
            case "Mediator":
                services.AddMediator(c => { c.ServiceLifetime = ServiceLifetime.Scoped; });
                services.Replace(ServiceDescriptor.Transient<G.IRequestHandler<ShortRequest, int>, GShortHandler>());
                services.AddTransient<G.IPipelineBehavior<ShortRequest, int>, GShort1>();
                services.AddTransient<G.IPipelineBehavior<ShortRequest, int>, GShort2>();
                services.AddTransient<G.IPipelineBehavior<ShortRequest, int>, GShort3>();
                break;
            case "DispatchR":
                services.AddDispatchR(c =>
                {
                    c.Assemblies.Add(typeof(ShortRequest).Assembly);
                    c.PipelineOrder = [typeof(DShort1), typeof(DShort2), typeof(DShort3)];
                });
                break;
            case "Immediate":
                services.AddTransient<IShortHandler>();
                services.AddShortCircuitHandlers(ServiceLifetime.Scoped);
                break;
            case "Foundatio":
                services.AddLogging();
                F.MediatorExtensions.AddMediator(services, b =>
                    b.AddAssembly<FShortHandler>().SetMediatorLifetime(ServiceLifetime.Scoped));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(peer));
        }
        NormalizeReceiverLifetimes(services, ReceiverTypes(peer));
        inspect?.Invoke(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private static void NormalizeReceiverLifetimes(IServiceCollection services, Type[] selected)
    {
        // Preserve the H comparison's native registration/factory graph, changing only receiver lifetime.
        for (var i = 0; i < services.Count; i++)
        {
            var d = services[i];
            if (d.IsKeyedService)
            {
                if (d.KeyedImplementationType is { } keyed && selected.Contains(keyed))
                    services[i] = ServiceDescriptor.DescribeKeyed(d.ServiceType, d.ServiceKey, keyed, ServiceLifetime.Transient);
                continue;
            }
            if (d.ImplementationType is { } type && (selected.Contains(type) || selected.Contains(d.ServiceType)))
                services[i] = ServiceDescriptor.Describe(d.ServiceType, type, ServiceLifetime.Transient);
            else if (selected.Contains(d.ServiceType) && d.ImplementationFactory is { } factory)
                services[i] = ServiceDescriptor.Transient(d.ServiceType, factory);
            else if (selected.Contains(d.ServiceType) && d.ImplementationInstance is not null)
                throw new InvalidOperationException("A selected short-circuit receiver cannot be a singleton instance");
        }
    }
}
