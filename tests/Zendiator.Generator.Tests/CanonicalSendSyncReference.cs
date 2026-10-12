using Microsoft.Extensions.DependencyInjection;

namespace Zendiator.Generator.Tests;

// Handwritten S02 reference. No planner, graph, lowering, or emitted implementation is consulted.
internal static class CanonicalSendSyncReference
{
    internal static T SendResponse<R, T, THook, THandler>(IServiceProvider provider, R request, CancellationToken token)
        where R : class, ISyncRequest<T>
        where THook : class, ISendRequestTransform<R>, ISendResponseTransform<R, T>, ISendExceptionObserver<R>, ISendFinallyObserver<R>
        where THandler : class, ISyncRequestHandler<R, T>
    {
        ArgumentNullException.ThrowIfNull(request);
        token.ThrowIfCancellationRequested();
        var hook = provider.GetRequiredService<THook>();
        var saved = request;
        var success = false;
        Exception? error = null;
        try
        {
            saved = hook.Before(saved, token);
            ArgumentNullException.ThrowIfNull(saved);
            token.ThrowIfCancellationRequested();
            var response = provider.GetRequiredService<THandler>().Handle(saved, token);
            response = hook.After(saved, response, token);
            success = true;
            return response;
        }
        catch (Exception failure)
        {
            error = failure;
            try { hook.OnException(saved, failure, token); }
            catch (Exception replacement) { error = replacement; throw; }
            throw;
        }
        finally { hook.Finally(saved, new SendExitInfo(success, error), token); }
    }

    internal static void SendVoid<R, THook, THandler>(IServiceProvider provider, R request, CancellationToken token)
        where R : class, ISyncRequest
        where THook : class, ISendRequestTransform<R>, ISendSuccessObserver<R>, ISendExceptionObserver<R>, ISendFinallyObserver<R>
        where THandler : class, ISyncRequestHandler<R>
    {
        ArgumentNullException.ThrowIfNull(request);
        token.ThrowIfCancellationRequested();
        var hook = provider.GetRequiredService<THook>();
        var saved = request;
        var success = false;
        Exception? error = null;
        try
        {
            saved = hook.Before(saved, token);
            ArgumentNullException.ThrowIfNull(saved);
            token.ThrowIfCancellationRequested();
            provider.GetRequiredService<THandler>().Handle(saved, token);
            hook.After(saved, token);
            success = true;
        }
        catch (Exception failure)
        {
            error = failure;
            try { hook.OnException(saved, failure, token); }
            catch (Exception replacement) { error = replacement; throw; }
            throw;
        }
        finally { hook.Finally(saved, new SendExitInfo(success, error), token); }
    }

    internal static T SendResponseWithoutHook<R, T, THandler>(IServiceProvider provider, R request, CancellationToken token)
        where R : class, ISyncRequest<T> where THandler : class, ISyncRequestHandler<R, T>
    {
        ArgumentNullException.ThrowIfNull(request);
        token.ThrowIfCancellationRequested();
        return provider.GetRequiredService<THandler>().Handle(request, token);
    }

    internal static void SendVoidWithoutHook<R, THandler>(IServiceProvider provider, R request, CancellationToken token)
        where R : class, ISyncRequest where THandler : class, ISyncRequestHandler<R>
    {
        ArgumentNullException.ThrowIfNull(request);
        token.ThrowIfCancellationRequested();
        provider.GetRequiredService<THandler>().Handle(request, token);
    }
}
