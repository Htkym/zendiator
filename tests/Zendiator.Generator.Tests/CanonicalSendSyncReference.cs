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

    internal static T SendTwoHooksResponse<R, T, TOuter, TInner, THandler>(IServiceProvider provider, R request, CancellationToken token)
        where R : class, ISyncRequest<T>
        where TOuter : class, ISendRequestTransform<R>, ISendResponseTransform<R, T>, ISendExceptionObserver<R>, ISendFinallyObserver<R>
        where TInner : class, ISendRequestTransform<R>, ISendResponseTransform<R, T>, ISendExceptionObserver<R>, ISendFinallyObserver<R>
        where THandler : class, ISyncRequestHandler<R, T>
    {
        ArgumentNullException.ThrowIfNull(request);
        token.ThrowIfCancellationRequested();
        var outer = provider.GetRequiredService<TOuter>();
        var saved = request;
        var success = false;
        Exception? error = null;
        try
        {
            saved = outer.Before(saved, token);
            var response = SendResponse<R, T, TInner, THandler>(provider, saved, token);
            response = outer.After(saved, response, token);
            success = true;
            return response;
        }
        catch (Exception failure)
        {
            error = failure;
            try { outer.OnException(saved, failure, token); }
            catch (Exception replacement) { error = replacement; throw; }
            throw;
        }
        finally { outer.Finally(saved, new SendExitInfo(success, error), token); }
    }

    internal static void SendTwoHooksVoid<R, TOuter, TInner, THandler>(IServiceProvider provider, R request, CancellationToken token)
        where R : class, ISyncRequest
        where TOuter : class, ISendRequestTransform<R>, ISendSuccessObserver<R>, ISendExceptionObserver<R>, ISendFinallyObserver<R>
        where TInner : class, ISendRequestTransform<R>, ISendSuccessObserver<R>, ISendExceptionObserver<R>, ISendFinallyObserver<R>
        where THandler : class, ISyncRequestHandler<R>
    {
        ArgumentNullException.ThrowIfNull(request);
        token.ThrowIfCancellationRequested();
        var outer = provider.GetRequiredService<TOuter>();
        var saved = request;
        var success = false;
        Exception? error = null;
        try
        {
            saved = outer.Before(saved, token);
            SendVoid<R, TInner, THandler>(provider, saved, token);
            outer.After(saved, token);
            success = true;
        }
        catch (Exception failure)
        {
            error = failure;
            try { outer.OnException(saved, failure, token); }
            catch (Exception replacement) { error = replacement; throw; }
            throw;
        }
        finally { outer.Finally(saved, new SendExitInfo(success, error), token); }
    }

    internal static void SendVoidWithoutHook<R, THandler>(IServiceProvider provider, R request, CancellationToken token)
        where R : class, ISyncRequest where THandler : class, ISyncRequestHandler<R>
    {
        ArgumentNullException.ThrowIfNull(request);
        token.ThrowIfCancellationRequested();
        provider.GetRequiredService<THandler>().Handle(request, token);
    }

    internal static T SendOwnedResponseWithoutHook<R, T, THandler, TFactory>(IServiceProvider provider, R request, CancellationToken token)
        where R : class, ISyncRequest<T>
        where THandler : class, ISyncRequestHandler<R, T>, IDisposable
        where TFactory : class, IOwnedSessionFactory<R, THandler>
    {
        ArgumentNullException.ThrowIfNull(request);
        token.ThrowIfCancellationRequested();
        var factory = provider.GetRequiredService<TFactory>();
        THandler? session = factory.Create(request, token)
            ?? throw new InvalidOperationException("A session factory must return a nonnull Handler session.");
        try { return session.Handle(request, token); }
        finally { var detached = session; session = null; detached.Dispose(); }
    }

    internal static void SendOwnedVoidWithoutHook<R, THandler, TFactory>(IServiceProvider provider, R request, CancellationToken token)
        where R : class, ISyncRequest
        where THandler : class, ISyncRequestHandler<R>, IDisposable
        where TFactory : class, IOwnedSessionFactory<R, THandler>
    {
        ArgumentNullException.ThrowIfNull(request);
        token.ThrowIfCancellationRequested();
        var factory = provider.GetRequiredService<TFactory>();
        THandler? session = factory.Create(request, token)
            ?? throw new InvalidOperationException("A session factory must return a nonnull Handler session.");
        try { session.Handle(request, token); }
        finally { var detached = session; session = null; detached.Dispose(); }
    }

    internal static T SendBorrowedResponseWithoutHook<R, T, THandler, TFactory>(IServiceProvider provider, R request, CancellationToken token)
        where R : class, ISyncRequest<T>
        where THandler : class, ISyncRequestHandler<R, T>
        where TFactory : class, IBorrowedSessionFactory<R, THandler>
    {
        ArgumentNullException.ThrowIfNull(request);
        token.ThrowIfCancellationRequested();
        var factory = provider.GetRequiredService<TFactory>();
        var session = factory.Create(request, token)
            ?? throw new InvalidOperationException("A session factory must return a nonnull Handler session.");
        return session.Handle(request, token);
    }

    internal static void SendBorrowedVoidWithoutHook<R, THandler, TFactory>(IServiceProvider provider, R request, CancellationToken token)
        where R : class, ISyncRequest
        where THandler : class, ISyncRequestHandler<R>
        where TFactory : class, IBorrowedSessionFactory<R, THandler>
    {
        ArgumentNullException.ThrowIfNull(request);
        token.ThrowIfCancellationRequested();
        var factory = provider.GetRequiredService<TFactory>();
        var session = factory.Create(request, token)
            ?? throw new InvalidOperationException("A session factory must return a nonnull Handler session.");
        session.Handle(request, token);
    }

    internal static T SendOwnedHookResponse<R, T, THook, THandler, TFactory>(IServiceProvider provider, R request, CancellationToken token)
        where R : class, ISyncRequest<T>
        where THook : class, ISendRequestTransform<R>, ISendResponseTransform<R, T>, ISendExceptionObserver<R>, ISendFinallyObserver<R>, IDisposable
        where THandler : class, ISyncRequestHandler<R, T>
        where TFactory : class, IOwnedSessionFactory<R, THook>
    {
        ArgumentNullException.ThrowIfNull(request); token.ThrowIfCancellationRequested();
        THook? hook = provider.GetRequiredService<TFactory>().Create(request, token)
            ?? throw new InvalidOperationException("A session factory must return a nonnull Hook session.");
        try { return InvokeResponseWithSessionHook<R, T, THook, THandler>(provider, request, token, hook); }
        finally { var detached = hook; hook = null; detached.Dispose(); }
    }

    internal static void SendOwnedHookVoid<R, THook, THandler, TFactory>(IServiceProvider provider, R request, CancellationToken token)
        where R : class, ISyncRequest
        where THook : class, ISendRequestTransform<R>, ISendSuccessObserver<R>, ISendExceptionObserver<R>, ISendFinallyObserver<R>, IDisposable
        where THandler : class, ISyncRequestHandler<R>
        where TFactory : class, IOwnedSessionFactory<R, THook>
    {
        ArgumentNullException.ThrowIfNull(request); token.ThrowIfCancellationRequested();
        THook? hook = provider.GetRequiredService<TFactory>().Create(request, token)
            ?? throw new InvalidOperationException("A session factory must return a nonnull Hook session.");
        try { InvokeVoidWithSessionHook<R, THook, THandler>(provider, request, token, hook); }
        finally { var detached = hook; hook = null; detached.Dispose(); }
    }

    internal static T SendBorrowedHookResponse<R, T, THook, THandler, TFactory>(IServiceProvider provider, R request, CancellationToken token)
        where R : class, ISyncRequest<T>
        where THook : class, ISendRequestTransform<R>, ISendResponseTransform<R, T>, ISendExceptionObserver<R>, ISendFinallyObserver<R>
        where THandler : class, ISyncRequestHandler<R, T>
        where TFactory : class, IBorrowedSessionFactory<R, THook>
    {
        ArgumentNullException.ThrowIfNull(request); token.ThrowIfCancellationRequested();
        var hook = provider.GetRequiredService<TFactory>().Create(request, token)
            ?? throw new InvalidOperationException("A session factory must return a nonnull Hook session.");
        return InvokeResponseWithSessionHook<R, T, THook, THandler>(provider, request, token, hook);
    }

    internal static void SendBorrowedHookVoid<R, THook, THandler, TFactory>(IServiceProvider provider, R request, CancellationToken token)
        where R : class, ISyncRequest
        where THook : class, ISendRequestTransform<R>, ISendSuccessObserver<R>, ISendExceptionObserver<R>, ISendFinallyObserver<R>
        where THandler : class, ISyncRequestHandler<R>
        where TFactory : class, IBorrowedSessionFactory<R, THook>
    {
        ArgumentNullException.ThrowIfNull(request); token.ThrowIfCancellationRequested();
        var hook = provider.GetRequiredService<TFactory>().Create(request, token)
            ?? throw new InvalidOperationException("A session factory must return a nonnull Hook session.");
        InvokeVoidWithSessionHook<R, THook, THandler>(provider, request, token, hook);
    }

    // The stage guard precedes acquisition. Successful factory return does not insert another entry guard.
    private static T InvokeResponseWithSessionHook<R, T, THook, THandler>(IServiceProvider provider, R request, CancellationToken token, THook hook)
        where R : class, ISyncRequest<T>
        where THook : class, ISendRequestTransform<R>, ISendResponseTransform<R, T>, ISendExceptionObserver<R>, ISendFinallyObserver<R>
        where THandler : class, ISyncRequestHandler<R, T>
    {
        var saved = request; var success = false; Exception? error = null;
        try
        {
            saved = hook.Before(saved, token); ArgumentNullException.ThrowIfNull(saved); token.ThrowIfCancellationRequested();
            var response = provider.GetRequiredService<THandler>().Handle(saved, token);
            response = hook.After(saved, response, token); success = true; return response;
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

    private static void InvokeVoidWithSessionHook<R, THook, THandler>(IServiceProvider provider, R request, CancellationToken token, THook hook)
        where R : class, ISyncRequest
        where THook : class, ISendRequestTransform<R>, ISendSuccessObserver<R>, ISendExceptionObserver<R>, ISendFinallyObserver<R>
        where THandler : class, ISyncRequestHandler<R>
    {
        var saved = request; var success = false; Exception? error = null;
        try
        {
            saved = hook.Before(saved, token); ArgumentNullException.ThrowIfNull(saved); token.ThrowIfCancellationRequested();
            provider.GetRequiredService<THandler>().Handle(saved, token); hook.After(saved, token); success = true;
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
}
