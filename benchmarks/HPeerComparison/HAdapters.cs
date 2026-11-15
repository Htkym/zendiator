using HPeerComparison;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using DispatchR.Extensions;
using Zendiator.DependencyInjection;
using Z = Zendiator;
using M = MediatR;
using G = Mediator;
using D = DispatchR.Abstractions.Send;
using I = Immediate.Handlers.Shared;
[assembly: I.ImmediateAssemblyIdentifier("HPeerComparison")]
namespace Competitive;

[Z.ZendiatorComposition("h-peer-comparison")]
public sealed class HPeerComposition;

public sealed class HZHook1 : Z.IAsyncSendRequestTransform<HRequest>, Z.IAsyncSendResponseTransform<HRequest, int>, Z.IAsyncSendExceptionObserver<HRequest>, Z.IAsyncSendFinallyObserver<HRequest>, IDisposable
{
    private readonly HWork _work;
    public HZHook1(HWork work)
    {
        _work = work;
        work.Construct("Hook1", this);
    }
    public ValueTask<HRequest> BeforeAsync(HRequest r, CancellationToken t) => _work.Before(1, this, r, t);
    public ValueTask<int> AfterAsync(HRequest r, int value, CancellationToken t) => _work.After(1, this, r, value, t);
    public ValueTask OnExceptionAsync(HRequest r, Exception error, CancellationToken t) => _work.Error(1, this, r, error, t);
    public ValueTask FinallyAsync(HRequest r, Z.SendExitInfo exit, CancellationToken t) => _work.Finally(1, this, r, exit, t);
    public void Dispose() => _work.Release("Hook1", this);
}

public sealed class HZHook2 : Z.IAsyncSendRequestTransform<HRequest>, Z.IAsyncSendResponseTransform<HRequest, int>, Z.IAsyncSendExceptionObserver<HRequest>, Z.IAsyncSendFinallyObserver<HRequest>, IDisposable
{
    private readonly HWork _work;
    public HZHook2(HWork work)
    {
        _work = work;
        work.Construct("Hook2", this);
    }
    public ValueTask<HRequest> BeforeAsync(HRequest r, CancellationToken t) => _work.Before(2, this, r, t);
    public ValueTask<int> AfterAsync(HRequest r, int value, CancellationToken t) => _work.After(2, this, r, value, t);
    public ValueTask OnExceptionAsync(HRequest r, Exception error, CancellationToken t) => _work.Error(2, this, r, error, t);
    public ValueTask FinallyAsync(HRequest r, Z.SendExitInfo exit, CancellationToken t) => _work.Finally(2, this, r, exit, t);
    public void Dispose() => _work.Release("Hook2", this);
}

public sealed class HZHook3 : Z.IAsyncSendRequestTransform<HRequest>, Z.IAsyncSendResponseTransform<HRequest, int>, Z.IAsyncSendExceptionObserver<HRequest>, Z.IAsyncSendFinallyObserver<HRequest>, IDisposable
{
    private readonly HWork _work;
    public HZHook3(HWork work)
    {
        _work = work;
        work.Construct("Hook3", this);
    }
    public ValueTask<HRequest> BeforeAsync(HRequest r, CancellationToken t) => _work.Before(3, this, r, t);
    public ValueTask<int> AfterAsync(HRequest r, int value, CancellationToken t) => _work.After(3, this, r, value, t);
    public ValueTask OnExceptionAsync(HRequest r, Exception error, CancellationToken t) => _work.Error(3, this, r, error, t);
    public ValueTask FinallyAsync(HRequest r, Z.SendExitInfo exit, CancellationToken t) => _work.Finally(3, this, r, exit, t);
    public void Dispose() => _work.Release("Hook3", this);
}

public sealed class HMHook1 : M.IPipelineBehavior<HRequest, int>, IDisposable
{
    private readonly HWork _work;
    public HMHook1(HWork work)
    {
        _work = work;
        work.Construct("Hook1", this);
    }
    public async Task<int> Handle(HRequest r, M.RequestHandlerDelegate<int> next, CancellationToken t)
    {
        var saved = r;
        var success = false;
        Exception? error = null;
        try
        {
            saved = await _work.Before(1, this, saved, t).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(saved);
            t.ThrowIfCancellationRequested();
            var response = await next(t).ConfigureAwait(false);
            response = await _work.After(1, this, saved, response, t).ConfigureAwait(false);
            success = true;
            return response;
        }
        catch (Exception failure)
        {
            error = failure;
            try
            {
                await _work.Error(1, this, saved, failure, t).ConfigureAwait(false);
            }
            catch (Exception replacement)
            {
                error = replacement;
                throw;
            }
            throw;
        }
        finally
        {
            await _work.Finally(1, this, saved, new Z.SendExitInfo(success, error), t).ConfigureAwait(false);
        }
    }
    public void Dispose() => _work.Release("Hook1", this);
}

public sealed class HMHook2 : M.IPipelineBehavior<HRequest, int>, IDisposable
{
    private readonly HWork _work;
    public HMHook2(HWork work)
    {
        _work = work;
        work.Construct("Hook2", this);
    }
    public async Task<int> Handle(HRequest r, M.RequestHandlerDelegate<int> next, CancellationToken t)
    {
        var saved = r;
        var success = false;
        Exception? error = null;
        try
        {
            saved = await _work.Before(2, this, saved, t).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(saved);
            t.ThrowIfCancellationRequested();
            var response = await next(t).ConfigureAwait(false);
            response = await _work.After(2, this, saved, response, t).ConfigureAwait(false);
            success = true;
            return response;
        }
        catch (Exception failure)
        {
            error = failure;
            try
            {
                await _work.Error(2, this, saved, failure, t).ConfigureAwait(false);
            }
            catch (Exception replacement)
            {
                error = replacement;
                throw;
            }
            throw;
        }
        finally
        {
            await _work.Finally(2, this, saved, new Z.SendExitInfo(success, error), t).ConfigureAwait(false);
        }
    }
    public void Dispose() => _work.Release("Hook2", this);
}

public sealed class HMHook3 : M.IPipelineBehavior<HRequest, int>, IDisposable
{
    private readonly HWork _work;
    public HMHook3(HWork work)
    {
        _work = work;
        work.Construct("Hook3", this);
    }
    public async Task<int> Handle(HRequest r, M.RequestHandlerDelegate<int> next, CancellationToken t)
    {
        var saved = r;
        var success = false;
        Exception? error = null;
        try
        {
            saved = await _work.Before(3, this, saved, t).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(saved);
            t.ThrowIfCancellationRequested();
            var response = await next(t).ConfigureAwait(false);
            response = await _work.After(3, this, saved, response, t).ConfigureAwait(false);
            success = true;
            return response;
        }
        catch (Exception failure)
        {
            error = failure;
            try
            {
                await _work.Error(3, this, saved, failure, t).ConfigureAwait(false);
            }
            catch (Exception replacement)
            {
                error = replacement;
                throw;
            }
            throw;
        }
        finally
        {
            await _work.Finally(3, this, saved, new Z.SendExitInfo(success, error), t).ConfigureAwait(false);
        }
    }
    public void Dispose() => _work.Release("Hook3", this);
}

public sealed class HGHook1 : G.IPipelineBehavior<HRequest, int>, IDisposable
{
    private readonly HWork _work;
    public HGHook1(HWork work)
    {
        _work = work;
        work.Construct("Hook1", this);
    }
    public async ValueTask<int> Handle(HRequest r, G.MessageHandlerDelegate<HRequest, int> next, CancellationToken t)
    {
        var saved = r;
        var success = false;
        Exception? error = null;
        try
        {
            saved = await _work.Before(1, this, saved, t).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(saved);
            t.ThrowIfCancellationRequested();
            var response = await next(saved, t).ConfigureAwait(false);
            response = await _work.After(1, this, saved, response, t).ConfigureAwait(false);
            success = true;
            return response;
        }
        catch (Exception failure)
        {
            error = failure;
            try
            {
                await _work.Error(1, this, saved, failure, t).ConfigureAwait(false);
            }
            catch (Exception replacement)
            {
                error = replacement;
                throw;
            }
            throw;
        }
        finally
        {
            await _work.Finally(1, this, saved, new Z.SendExitInfo(success, error), t).ConfigureAwait(false);
        }
    }
    public void Dispose() => _work.Release("Hook1", this);
}

public sealed class HGHook2 : G.IPipelineBehavior<HRequest, int>, IDisposable
{
    private readonly HWork _work;
    public HGHook2(HWork work)
    {
        _work = work;
        work.Construct("Hook2", this);
    }
    public async ValueTask<int> Handle(HRequest r, G.MessageHandlerDelegate<HRequest, int> next, CancellationToken t)
    {
        var saved = r;
        var success = false;
        Exception? error = null;
        try
        {
            saved = await _work.Before(2, this, saved, t).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(saved);
            t.ThrowIfCancellationRequested();
            var response = await next(saved, t).ConfigureAwait(false);
            response = await _work.After(2, this, saved, response, t).ConfigureAwait(false);
            success = true;
            return response;
        }
        catch (Exception failure)
        {
            error = failure;
            try
            {
                await _work.Error(2, this, saved, failure, t).ConfigureAwait(false);
            }
            catch (Exception replacement)
            {
                error = replacement;
                throw;
            }
            throw;
        }
        finally
        {
            await _work.Finally(2, this, saved, new Z.SendExitInfo(success, error), t).ConfigureAwait(false);
        }
    }
    public void Dispose() => _work.Release("Hook2", this);
}

public sealed class HGHook3 : G.IPipelineBehavior<HRequest, int>, IDisposable
{
    private readonly HWork _work;
    public HGHook3(HWork work)
    {
        _work = work;
        work.Construct("Hook3", this);
    }
    public async ValueTask<int> Handle(HRequest r, G.MessageHandlerDelegate<HRequest, int> next, CancellationToken t)
    {
        var saved = r;
        var success = false;
        Exception? error = null;
        try
        {
            saved = await _work.Before(3, this, saved, t).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(saved);
            t.ThrowIfCancellationRequested();
            var response = await next(saved, t).ConfigureAwait(false);
            response = await _work.After(3, this, saved, response, t).ConfigureAwait(false);
            success = true;
            return response;
        }
        catch (Exception failure)
        {
            error = failure;
            try
            {
                await _work.Error(3, this, saved, failure, t).ConfigureAwait(false);
            }
            catch (Exception replacement)
            {
                error = replacement;
                throw;
            }
            throw;
        }
        finally
        {
            await _work.Finally(3, this, saved, new Z.SendExitInfo(success, error), t).ConfigureAwait(false);
        }
    }
    public void Dispose() => _work.Release("Hook3", this);
}

public sealed class HDHook1 : D.IPipelineBehavior<HRequest, ValueTask<int>>, IDisposable
{
    private readonly HWork _work;
    public HDHook1(HWork work)
    {
        _work = work;
        work.Construct("Hook1", this);
    }
    public required D.IRequestHandler<HRequest, ValueTask<int>> NextPipeline
    {
        get; set;
    }
    public async ValueTask<int> Handle(HRequest r, CancellationToken t)
    {
        var saved = r;
        var success = false;
        Exception? error = null;
        try
        {
            saved = await _work.Before(1, this, saved, t).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(saved);
            t.ThrowIfCancellationRequested();
            var response = await NextPipeline.Handle(saved, t).ConfigureAwait(false);
            response = await _work.After(1, this, saved, response, t).ConfigureAwait(false);
            success = true;
            return response;
        }
        catch (Exception failure)
        {
            error = failure;
            try
            {
                await _work.Error(1, this, saved, failure, t).ConfigureAwait(false);
            }
            catch (Exception replacement)
            {
                error = replacement;
                throw;
            }
            throw;
        }
        finally
        {
            await _work.Finally(1, this, saved, new Z.SendExitInfo(success, error), t).ConfigureAwait(false);
        }
    }
    public void Dispose() => _work.Release("Hook1", this);
}

public sealed class HDHook2 : D.IPipelineBehavior<HRequest, ValueTask<int>>, IDisposable
{
    private readonly HWork _work;
    public HDHook2(HWork work)
    {
        _work = work;
        work.Construct("Hook2", this);
    }
    public required D.IRequestHandler<HRequest, ValueTask<int>> NextPipeline
    {
        get; set;
    }
    public async ValueTask<int> Handle(HRequest r, CancellationToken t)
    {
        var saved = r;
        var success = false;
        Exception? error = null;
        try
        {
            saved = await _work.Before(2, this, saved, t).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(saved);
            t.ThrowIfCancellationRequested();
            var response = await NextPipeline.Handle(saved, t).ConfigureAwait(false);
            response = await _work.After(2, this, saved, response, t).ConfigureAwait(false);
            success = true;
            return response;
        }
        catch (Exception failure)
        {
            error = failure;
            try
            {
                await _work.Error(2, this, saved, failure, t).ConfigureAwait(false);
            }
            catch (Exception replacement)
            {
                error = replacement;
                throw;
            }
            throw;
        }
        finally
        {
            await _work.Finally(2, this, saved, new Z.SendExitInfo(success, error), t).ConfigureAwait(false);
        }
    }
    public void Dispose() => _work.Release("Hook2", this);
}

public sealed class HDHook3 : D.IPipelineBehavior<HRequest, ValueTask<int>>, IDisposable
{
    private readonly HWork _work;
    public HDHook3(HWork work)
    {
        _work = work;
        work.Construct("Hook3", this);
    }
    public required D.IRequestHandler<HRequest, ValueTask<int>> NextPipeline
    {
        get; set;
    }
    public async ValueTask<int> Handle(HRequest r, CancellationToken t)
    {
        var saved = r;
        var success = false;
        Exception? error = null;
        try
        {
            saved = await _work.Before(3, this, saved, t).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(saved);
            t.ThrowIfCancellationRequested();
            var response = await NextPipeline.Handle(saved, t).ConfigureAwait(false);
            response = await _work.After(3, this, saved, response, t).ConfigureAwait(false);
            success = true;
            return response;
        }
        catch (Exception failure)
        {
            error = failure;
            try
            {
                await _work.Error(3, this, saved, failure, t).ConfigureAwait(false);
            }
            catch (Exception replacement)
            {
                error = replacement;
                throw;
            }
            throw;
        }
        finally
        {
            await _work.Finally(3, this, saved, new Z.SendExitInfo(success, error), t).ConfigureAwait(false);
        }
    }
    public void Dispose() => _work.Release("Hook3", this);
}

public sealed class HIHook1 : I.Behavior<HRequest, int>, IDisposable
{
    private readonly HWork _work;
    public HIHook1(HWork work)
    {
        _work = work;
        work.Construct("Hook1", this);
    }
    public override async ValueTask<int> HandleAsync(HRequest r, CancellationToken t)
    {
        var saved = r;
        var success = false;
        Exception? error = null;
        try
        {
            saved = await _work.Before(1, this, saved, t).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(saved);
            t.ThrowIfCancellationRequested();
            var response = await Next(saved, t).ConfigureAwait(false);
            response = await _work.After(1, this, saved, response, t).ConfigureAwait(false);
            success = true;
            return response;
        }
        catch (Exception failure)
        {
            error = failure;
            try
            {
                await _work.Error(1, this, saved, failure, t).ConfigureAwait(false);
            }
            catch (Exception replacement)
            {
                error = replacement;
                throw;
            }
            throw;
        }
        finally
        {
            await _work.Finally(1, this, saved, new Z.SendExitInfo(success, error), t).ConfigureAwait(false);
        }
    }
    public void Dispose() => _work.Release("Hook1", this);
}

public sealed class HIHook2 : I.Behavior<HRequest, int>, IDisposable
{
    private readonly HWork _work;
    public HIHook2(HWork work)
    {
        _work = work;
        work.Construct("Hook2", this);
    }
    public override async ValueTask<int> HandleAsync(HRequest r, CancellationToken t)
    {
        var saved = r;
        var success = false;
        Exception? error = null;
        try
        {
            saved = await _work.Before(2, this, saved, t).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(saved);
            t.ThrowIfCancellationRequested();
            var response = await Next(saved, t).ConfigureAwait(false);
            response = await _work.After(2, this, saved, response, t).ConfigureAwait(false);
            success = true;
            return response;
        }
        catch (Exception failure)
        {
            error = failure;
            try
            {
                await _work.Error(2, this, saved, failure, t).ConfigureAwait(false);
            }
            catch (Exception replacement)
            {
                error = replacement;
                throw;
            }
            throw;
        }
        finally
        {
            await _work.Finally(2, this, saved, new Z.SendExitInfo(success, error), t).ConfigureAwait(false);
        }
    }
    public void Dispose() => _work.Release("Hook2", this);
}

public sealed class HIHook3 : I.Behavior<HRequest, int>, IDisposable
{
    private readonly HWork _work;
    public HIHook3(HWork work)
    {
        _work = work;
        work.Construct("Hook3", this);
    }
    public override async ValueTask<int> HandleAsync(HRequest r, CancellationToken t)
    {
        var saved = r;
        var success = false;
        Exception? error = null;
        try
        {
            saved = await _work.Before(3, this, saved, t).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(saved);
            t.ThrowIfCancellationRequested();
            var response = await Next(saved, t).ConfigureAwait(false);
            response = await _work.After(3, this, saved, response, t).ConfigureAwait(false);
            success = true;
            return response;
        }
        catch (Exception failure)
        {
            error = failure;
            try
            {
                await _work.Error(3, this, saved, failure, t).ConfigureAwait(false);
            }
            catch (Exception replacement)
            {
                error = replacement;
                throw;
            }
            throw;
        }
        finally
        {
            await _work.Finally(3, this, saved, new Z.SendExitInfo(success, error), t).ConfigureAwait(false);
        }
    }
    public void Dispose() => _work.Release("Hook3", this);
}

[Z.Handler]
public sealed class HZHandler : Z.IRequestHandler<HRequest, int>, IDisposable
{
    private readonly HWork _work;
    public HZHandler(HWork work)
    {
        _work = work;
        work.Construct("Handler", this);
    }
    public ValueTask<int> HandleAsync(HRequest r, CancellationToken t) => _work.Handle(this, r, t);
    public void Dispose() => _work.Release("Handler", this);
}

public sealed class HMHandler : M.IRequestHandler<HRequest, int>, IDisposable
{
    private readonly HWork _work;
    public HMHandler(HWork work)
    {
        _work = work;
        work.Construct("Handler", this);
    }
    public Task<int> Handle(HRequest r, CancellationToken t) => _work.Handle(this, r, t).AsTask();
    public void Dispose() => _work.Release("Handler", this);
}

public sealed class HGHandler : G.IRequestHandler<HRequest, int>, IDisposable
{
    private readonly HWork _work;
    public HGHandler(HWork work)
    {
        _work = work;
        work.Construct("Handler", this);
    }
    public ValueTask<int> Handle(HRequest r, CancellationToken t) => _work.Handle(this, r, t);
    public void Dispose() => _work.Release("Handler", this);
}

public sealed class HDHandler : D.IRequestHandler<HRequest, ValueTask<int>>, IDisposable
{
    private readonly HWork _work;
    public HDHandler(HWork work)
    {
        _work = work;
        work.Construct("Handler", this);
    }
    public ValueTask<int> Handle(HRequest r, CancellationToken t) => _work.Handle(this, r, t);
    public void Dispose() => _work.Release("Handler", this);
}

[I.Handler]
[I.Behaviors(typeof(HIHook1), typeof(HIHook2), typeof(HIHook3))]
public sealed partial class HImmediate : IDisposable
{
    private readonly HWork _work;
    public HImmediate(HWork work)
    {
        _work = work;
        work.Construct("Handler", this);
    }
    private ValueTask<int> HandleAsync(HRequest r, CancellationToken t) => _work.Handle(this, r, t);
    void IDisposable.Dispose() => _work.Release("Handler", this);
}

public static class HPeerRegistration
{
    public static readonly string[] Peers = ["Zendiator", "MediatRHistorical", "Mediator", "DispatchR", "Immediate", "Foundatio"];
    public static Type EntryType(string peer) => peer switch
    {
        "Zendiator" => typeof(Competitive.Generated.HPeerComposition.IZendiator),
        "MediatRHistorical" => typeof(M.IMediator),
        "Mediator" => typeof(G.Mediator),
        "DispatchR" => typeof(DispatchR.IMediator),
        "Immediate" => typeof(HImmediate.Handler),
        "Foundatio" => typeof(global::Foundatio.Mediator.IMediator),
        _ => throw new ArgumentOutOfRangeException(nameof(peer))
    };
    public static Type[] ReceiverServiceTypes(string peer) => peer switch
    {
        "Zendiator" => [typeof(HZHook1), typeof(HZHook2), typeof(HZHook3), typeof(HZHandler)],
        "MediatRHistorical" => [typeof(HMHook1), typeof(HMHook2), typeof(HMHook3), typeof(HMHandler)],
        "Mediator" => [typeof(HGHook1), typeof(HGHook2), typeof(HGHook3), typeof(HGHandler)],
        "DispatchR" => [typeof(HDHook1), typeof(HDHook2), typeof(HDHook3), typeof(HDHandler)],
        "Immediate" => [typeof(HIHook1), typeof(HIHook2), typeof(HIHook3), typeof(HImmediate)],
        "Foundatio" => HFoundatioRegistrationCandidate.ReceiverServiceTypes,
        _ => throw new ArgumentOutOfRangeException(nameof(peer))
    };
    public static ServiceProvider Create(string peer, HWork work, Action<IServiceCollection>? inspect = null)
    {
        IServiceCollection services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(work);
        switch (peer)
        {
            case "Zendiator":
                services.AddZendiator<HPeerComposition>(c =>
                {
                    c.Handlers<HRequest>(Z.DispatchFamily.SendAsync, typeof(HZHandler));
                    c.Pipeline<HRequest>(Z.DispatchFamily.SendAsync, Z.Stage.Hook<HZHook1>(), Z.Stage.Hook<HZHook2>(), Z.Stage.Hook<HZHook3>());
                });
                break;
            case "MediatRHistorical":
                services.AddMediatR(c =>
                {
                    c.RegisterServicesFromAssemblyContaining<HRequest>();
                    c.Lifetime = ServiceLifetime.Scoped;
                });
                services.Replace(ServiceDescriptor.Transient<M.IRequestHandler<HRequest, int>, HMHandler>());
                services.AddTransient<M.IPipelineBehavior<HRequest, int>, HMHook1>();
                services.AddTransient<M.IPipelineBehavior<HRequest, int>, HMHook2>();
                services.AddTransient<M.IPipelineBehavior<HRequest, int>, HMHook3>();
                break;
            case "Mediator":
                services.AddMediator(c => { c.ServiceLifetime = ServiceLifetime.Scoped; });
                services.Replace(ServiceDescriptor.Transient<G.IRequestHandler<HRequest, int>, HGHandler>());
                services.AddTransient<G.IPipelineBehavior<HRequest, int>, HGHook1>();
                services.AddTransient<G.IPipelineBehavior<HRequest, int>, HGHook2>();
                services.AddTransient<G.IPipelineBehavior<HRequest, int>, HGHook3>();
                break;
            case "DispatchR":
                services.AddDispatchR(c =>
                {
                    c.Assemblies.Add(typeof(HRequest).Assembly);
                    c.PipelineOrder = [typeof(HDHook1), typeof(HDHook2), typeof(HDHook3)];
                });
                break;
            case "Immediate":
                services.AddTransient<HImmediate>();
                services.AddHPeerComparisonHandlers(ServiceLifetime.Scoped);
                break;
            case "Foundatio":
                HFoundatioRegistrationCandidate.Add(services);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(peer));
        }
        var selected = ReceiverServiceTypes(peer);
        for (var i = 0; i < services.Count; i++)
        {
            var d = services[i];
            if (d.IsKeyedService)
            {
                if (d.KeyedImplementationType is { } implementation && selected.Contains(implementation))
                    services[i] = ServiceDescriptor.DescribeKeyed(d.ServiceType, d.ServiceKey, implementation, ServiceLifetime.Transient);
                continue;
            }
            if (d.ImplementationType is { } type && (selected.Contains(type) || selected.Contains(d.ServiceType)))
                services[i] = ServiceDescriptor.Describe(d.ServiceType, type, ServiceLifetime.Transient);
            else if (selected.Contains(d.ServiceType) && d.ImplementationFactory is { } factory)
                services[i] = ServiceDescriptor.Transient(d.ServiceType, factory);
            else if (selected.Contains(d.ServiceType) && d.ImplementationInstance is not null)
                throw new InvalidOperationException("A logical H receiver cannot be a preconstructed singleton");
        }
        inspect?.Invoke(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}
