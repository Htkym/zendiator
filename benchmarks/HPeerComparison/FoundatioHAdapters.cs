using Microsoft.Extensions.DependencyInjection;
using Foundatio.Mediator;
using F = Foundatio.Mediator;
using Z = Zendiator;
namespace Competitive;

// Include this file in HPeerComparison with the shared reviewed HWork gate overlay.
// HWork.Before preserves request identity. The parameterless Foundatio continuation
// cannot forward a replacement request; this is not a general request-transform adapter.
[F.Handler(Lifetime = F.MediatorLifetime.Transient)]
[F.UseMiddleware(typeof(HFoundatioHook1Middleware))]
[F.UseMiddleware(typeof(HFoundatioHook2Middleware))]
[F.UseMiddleware(typeof(HFoundatioHook3Middleware))]
public sealed class HFoundatioHandler : IDisposable
{
    private readonly HWork work;
    public HFoundatioHandler(HWork work)
    {
        this.work = work;
        work.Construct("Handler", this);
    }
    public ValueTask<int> HandleAsync(HRequest request, CancellationToken token) => work.Handle(this, request, token);
    public void Dispose() => work.Release("Handler", this);
}
[F.Middleware(1, ExplicitOnly = true, Lifetime = F.MediatorLifetime.Transient)]
public sealed class HFoundatioHook1Middleware : IDisposable
{
    private readonly HWork work;
    public HFoundatioHook1Middleware(HWork work)
    {
        this.work = work;
        work.Construct("Hook1", this);
    }
    public async ValueTask<object?> ExecuteAsync(HRequest request, F.HandlerExecutionDelegate next, CancellationToken token)
    {
        var saved = request;
        bool success = false;
        Exception? error = null;
        try
        {
            saved = await work.Before(1, this, saved, token).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(saved);
            token.ThrowIfCancellationRequested();
            var response = (int)(await next().ConfigureAwait(false))!;
            response = await work.After(1, this, saved, response, token).ConfigureAwait(false);
            success = true;
            return response;
        }
        catch (Exception failure)
        {
            error = failure;
            try
            {
                await work.Error(1, this, saved, failure, token).ConfigureAwait(false);
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
            await work.Finally(1, this, saved, new Z.SendExitInfo(success, error), token).ConfigureAwait(false);
        }
    }
    public void Dispose() => work.Release("Hook1", this);
}
[F.Middleware(2, ExplicitOnly = true, Lifetime = F.MediatorLifetime.Transient)]
public sealed class HFoundatioHook2Middleware : IDisposable
{
    private readonly HWork work;
    public HFoundatioHook2Middleware(HWork work)
    {
        this.work = work;
        work.Construct("Hook2", this);
    }
    public async ValueTask<object?> ExecuteAsync(HRequest request, F.HandlerExecutionDelegate next, CancellationToken token)
    {
        var saved = request;
        bool success = false;
        Exception? error = null;
        try
        {
            saved = await work.Before(2, this, saved, token).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(saved);
            token.ThrowIfCancellationRequested();
            var response = (int)(await next().ConfigureAwait(false))!;
            response = await work.After(2, this, saved, response, token).ConfigureAwait(false);
            success = true;
            return response;
        }
        catch (Exception failure)
        {
            error = failure;
            try
            {
                await work.Error(2, this, saved, failure, token).ConfigureAwait(false);
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
            await work.Finally(2, this, saved, new Z.SendExitInfo(success, error), token).ConfigureAwait(false);
        }
    }
    public void Dispose() => work.Release("Hook2", this);
}
[F.Middleware(3, ExplicitOnly = true, Lifetime = F.MediatorLifetime.Transient)]
public sealed class HFoundatioHook3Middleware : IDisposable
{
    private readonly HWork work;
    public HFoundatioHook3Middleware(HWork work)
    {
        this.work = work;
        work.Construct("Hook3", this);
    }
    public async ValueTask<object?> ExecuteAsync(HRequest request, F.HandlerExecutionDelegate next, CancellationToken token)
    {
        var saved = request;
        bool success = false;
        Exception? error = null;
        try
        {
            saved = await work.Before(3, this, saved, token).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(saved);
            token.ThrowIfCancellationRequested();
            var response = (int)(await next().ConfigureAwait(false))!;
            response = await work.After(3, this, saved, response, token).ConfigureAwait(false);
            success = true;
            return response;
        }
        catch (Exception failure)
        {
            error = failure;
            try
            {
                await work.Error(3, this, saved, failure, token).ConfigureAwait(false);
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
            await work.Finally(3, this, saved, new Z.SendExitInfo(success, error), token).ConfigureAwait(false);
        }
    }
    public void Dispose() => work.Release("Hook3", this);
}
public static class HFoundatioRegistrationCandidate
{
    public static void Add(IServiceCollection services) => F.MediatorExtensions.AddMediator(services, b =>
        b.AddAssembly<HFoundatioHandler>().SetMediatorLifetime(ServiceLifetime.Scoped));

    public static ServiceProvider Create(HWork work, Action<IServiceCollection>? inspect = null)
    {
        IServiceCollection services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(work);
        Add(services);
        inspect?.Invoke(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    public static Type[] ReceiverServiceTypes =>
        [typeof(HFoundatioHook1Middleware), typeof(HFoundatioHook2Middleware), typeof(HFoundatioHook3Middleware), typeof(HFoundatioHandler)];

    // An outside-timing gate, not a check added to every timed Hook.
    public static void ValidateBeforeIdentity()
    {
        var work = new HWork();
        var request = new HRequest(7, false, CancellationToken.None);
        for (int role = 1; role <= 3; role++)
        {
            var before = work.Before(role, work, request, CancellationToken.None);
            if (!before.IsCompletedSuccessfully)
                throw new InvalidOperationException("The fixed H Before workload must complete synchronously");
            if (!ReferenceEquals(before.GetAwaiter().GetResult(), request))
                throw new InvalidOperationException("Foundatio H comparison requires identity-preserving Before work");
        }
    }

    // Keep typed call sites in the compiled consumer, not reflection or a direct handler call.
    public static ValueTask<int> Invoke(F.IMediator entry, HRequest request, CancellationToken token) =>
        entry.InvokeAsync<int>(request, token);
}
