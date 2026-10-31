using Microsoft.Extensions.DependencyInjection;
namespace Zendiator.Generator.Tests;

internal interface ISyncReferenceLease<R, H> where R : class where H : class
{
    static abstract bool Owned
    {
        get;
    }
    static abstract H Acquire(IServiceProvider p, R r, CancellationToken t); static abstract void Release(H h);
}
internal readonly struct SyncDiLease<R, H> : ISyncReferenceLease<R, H> where R : class where H : class
{
    public static bool Owned => false; public static H Acquire(IServiceProvider p, R r, CancellationToken t) => p.GetRequiredService<H>(); public static void Release(H h)
    {
    }
}
internal readonly struct SyncOwnedLease<R, H, F> : ISyncReferenceLease<R, H> where R : class where H : class, IDisposable where F : class, IOwnedSessionFactory<R, H>
{
    public static bool Owned => true; public static H Acquire(IServiceProvider p, R r, CancellationToken t) => p.GetRequiredService<F>().Create(r, t); public static void Release(H h) => h.Dispose();
}
internal readonly struct SyncBorrowedLease<R, H, F> : ISyncReferenceLease<R, H> where R : class where H : class where F : class, IBorrowedSessionFactory<R, H>
{
    public static bool Owned => false; public static H Acquire(IServiceProvider p, R r, CancellationToken t) => p.GetRequiredService<F>().Create(r, t); public static void Release(H h)
    {
    }
}
// Independent handwritten opaque boundary: one orchestration per region, fresh inner state per next call.
internal static class CanonicalWrapperReference
{
    internal static T SendSyncResponse<R, T, W, K0, K1, H, L>(IServiceProvider p, R r, CancellationToken t, bool outer, bool inner) where R : class, ISyncRequest<T> where W : class, ISendWrapper<R, T> where K0 : class, ISendRequestTransform<R>, ISendResponseTransform<R, T>, ISendExceptionObserver<R>, ISendFinallyObserver<R> where K1 : class, ISendRequestTransform<R>, ISendResponseTransform<R, T>, ISendExceptionObserver<R>, ISendFinallyObserver<R> where H : class, ISyncRequestHandler<R, T> where L : struct, ISyncReferenceLease<R, H>
    {
        ArgumentNullException.ThrowIfNull(r);
        return OuterSyncResponse<R, T, W, K0, K1, H, L>(p, r, t, outer, inner);
    }
    private static T OuterSyncResponse<R, T, W, K0, K1, H, L>(IServiceProvider p, R request, CancellationToken token, bool outer, bool inner) where R : class, ISyncRequest<T> where W : class, ISendWrapper<R, T> where K0 : class, ISendRequestTransform<R>, ISendResponseTransform<R, T>, ISendExceptionObserver<R>, ISendFinallyObserver<R> where K1 : class, ISendRequestTransform<R>, ISendResponseTransform<R, T>, ISendExceptionObserver<R>, ISendFinallyObserver<R> where H : class, ISyncRequestHandler<R, T> where L : struct, ISyncReferenceLease<R, H>
    {
        token.ThrowIfCancellationRequested();
        K0? hook = outer ? p.GetRequiredService<K0>() : null;
        var saved = request;
        var success = false;
        Exception? error = null;
        try
        {
            if (outer)
            {
                saved = hook!.Before(saved, token);
                ArgumentNullException.ThrowIfNull(saved, "request");
                token.ThrowIfCancellationRequested();
            }
            T response = p.GetRequiredService<W>().Invoke(saved, new NextSyncResponse<R, T, W, K0, K1, H, L>(p, inner), token);
            if (outer)
            {
                response = hook!.After(saved, response, token);
            }
            success = true;
            return response;
        }
        catch (Exception failure)
        {
            error = failure;
            if (outer)
            {
                try
                {
                    hook!.OnException(saved, failure, token);
                }
                catch (Exception replacement)
                {
                    error = replacement;
                    throw;
                }
            }

            throw;
        }
        finally
        {
            if (outer)
                hook!.Finally(saved, new(success, error), token);
        }
    }
    private readonly struct NextSyncResponse<R, T, W, K0, K1, H, L> : ISendContinuation<R, T> where R : class, ISyncRequest<T> where W : class, ISendWrapper<R, T> where K0 : class, ISendRequestTransform<R>, ISendResponseTransform<R, T>, ISendExceptionObserver<R>, ISendFinallyObserver<R> where K1 : class, ISendRequestTransform<R>, ISendResponseTransform<R, T>, ISendExceptionObserver<R>, ISendFinallyObserver<R> where H : class, ISyncRequestHandler<R, T> where L : struct, ISyncReferenceLease<R, H>
    {
        private readonly IServiceProvider _p; private readonly bool _inner; public NextSyncResponse(IServiceProvider p, bool inner)
        {
            _p = p;
            _inner = inner;
        }
        public T Invoke(R request, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(request);
            return InnerSyncResponse<R, T, W, K0, K1, H, L>(_p, request, token, _inner);
        }
    }
    private static T InnerSyncResponse<R, T, W, K0, K1, H, L>(IServiceProvider p, R request, CancellationToken token, bool inner) where R : class, ISyncRequest<T> where W : class, ISendWrapper<R, T> where K0 : class, ISendRequestTransform<R>, ISendResponseTransform<R, T>, ISendExceptionObserver<R>, ISendFinallyObserver<R> where K1 : class, ISendRequestTransform<R>, ISendResponseTransform<R, T>, ISendExceptionObserver<R>, ISendFinallyObserver<R> where H : class, ISyncRequestHandler<R, T> where L : struct, ISyncReferenceLease<R, H>
    {
        token.ThrowIfCancellationRequested();
        K1? hook = inner ? p.GetRequiredService<K1>() : null;
        var saved = request;
        var success = false;
        Exception? error = null;
        try
        {
            if (inner)
            {
                saved = hook!.Before(saved, token);
                ArgumentNullException.ThrowIfNull(saved, "request");
                token.ThrowIfCancellationRequested();
            }
            H? handler = L.Acquire(p, saved, token) ?? throw new InvalidOperationException("A session factory must return a nonnull Handler session.");
            T response;
            try
            {
                response = handler.Handle(saved, token);
            }
            finally
            {
                if (L.Owned)
                {
                    var detached = handler;
                    handler = null;
                    L.Release(detached);
                }
            }
            if (inner)
            {
                response = hook!.After(saved, response, token);
            }
            success = true;
            return response;
        }
        catch (Exception failure)
        {
            error = failure;
            if (inner)
            {
                try
                {
                    hook!.OnException(saved, failure, token);
                }
                catch (Exception replacement)
                {
                    error = replacement;
                    throw;
                }
            }

            throw;
        }
        finally
        {
            if (inner)
                hook!.Finally(saved, new(success, error), token);
        }
    }
    internal static void SendSyncVoid<R, W, K0, K1, H, L>(IServiceProvider p, R r, CancellationToken t, bool outer, bool inner) where R : class, ISyncRequest where W : class, ISendWrapper<R> where K0 : class, ISendRequestTransform<R>, ISendSuccessObserver<R>, ISendExceptionObserver<R>, ISendFinallyObserver<R> where K1 : class, ISendRequestTransform<R>, ISendSuccessObserver<R>, ISendExceptionObserver<R>, ISendFinallyObserver<R> where H : class, ISyncRequestHandler<R> where L : struct, ISyncReferenceLease<R, H>
    {
        ArgumentNullException.ThrowIfNull(r);
        OuterSyncVoid<R, W, K0, K1, H, L>(p, r, t, outer, inner);
    }
    private static void OuterSyncVoid<R, W, K0, K1, H, L>(IServiceProvider p, R request, CancellationToken token, bool outer, bool inner) where R : class, ISyncRequest where W : class, ISendWrapper<R> where K0 : class, ISendRequestTransform<R>, ISendSuccessObserver<R>, ISendExceptionObserver<R>, ISendFinallyObserver<R> where K1 : class, ISendRequestTransform<R>, ISendSuccessObserver<R>, ISendExceptionObserver<R>, ISendFinallyObserver<R> where H : class, ISyncRequestHandler<R> where L : struct, ISyncReferenceLease<R, H>
    {
        token.ThrowIfCancellationRequested();
        K0? hook = outer ? p.GetRequiredService<K0>() : null;
        var saved = request;
        var success = false;
        Exception? error = null;
        try
        {
            if (outer)
            {
                saved = hook!.Before(saved, token);
                ArgumentNullException.ThrowIfNull(saved, "request");
                token.ThrowIfCancellationRequested();
            }
            p.GetRequiredService<W>().Invoke(saved, new NextSyncVoid<R, W, K0, K1, H, L>(p, inner), token);
            if (outer)
            {
                hook!.After(saved, token);
            }
            success = true;
        }
        catch (Exception failure)
        {
            error = failure;
            if (outer)
            {
                try
                {
                    hook!.OnException(saved, failure, token);
                }
                catch (Exception replacement)
                {
                    error = replacement;
                    throw;
                }
            }

            throw;
        }
        finally
        {
            if (outer)
                hook!.Finally(saved, new(success, error), token);
        }
    }
    private readonly struct NextSyncVoid<R, W, K0, K1, H, L> : ISendContinuation<R> where R : class, ISyncRequest where W : class, ISendWrapper<R> where K0 : class, ISendRequestTransform<R>, ISendSuccessObserver<R>, ISendExceptionObserver<R>, ISendFinallyObserver<R> where K1 : class, ISendRequestTransform<R>, ISendSuccessObserver<R>, ISendExceptionObserver<R>, ISendFinallyObserver<R> where H : class, ISyncRequestHandler<R> where L : struct, ISyncReferenceLease<R, H>
    {
        private readonly IServiceProvider _p; private readonly bool _inner; public NextSyncVoid(IServiceProvider p, bool inner)
        {
            _p = p;
            _inner = inner;
        }
        public void Invoke(R request, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(request);
            InnerSyncVoid<R, W, K0, K1, H, L>(_p, request, token, _inner);
        }
    }
    private static void InnerSyncVoid<R, W, K0, K1, H, L>(IServiceProvider p, R request, CancellationToken token, bool inner) where R : class, ISyncRequest where W : class, ISendWrapper<R> where K0 : class, ISendRequestTransform<R>, ISendSuccessObserver<R>, ISendExceptionObserver<R>, ISendFinallyObserver<R> where K1 : class, ISendRequestTransform<R>, ISendSuccessObserver<R>, ISendExceptionObserver<R>, ISendFinallyObserver<R> where H : class, ISyncRequestHandler<R> where L : struct, ISyncReferenceLease<R, H>
    {
        token.ThrowIfCancellationRequested();
        K1? hook = inner ? p.GetRequiredService<K1>() : null;
        var saved = request;
        var success = false;
        Exception? error = null;
        try
        {
            if (inner)
            {
                saved = hook!.Before(saved, token);
                ArgumentNullException.ThrowIfNull(saved, "request");
                token.ThrowIfCancellationRequested();
            }
            H? handler = L.Acquire(p, saved, token) ?? throw new InvalidOperationException("A session factory must return a nonnull Handler session.");
            try
            {
                handler.Handle(saved, token);
            }
            finally
            {
                if (L.Owned)
                {
                    var detached = handler;
                    handler = null;
                    L.Release(detached);
                }
            }
            if (inner)
            {
                hook!.After(saved, token);
            }
            success = true;
        }
        catch (Exception failure)
        {
            error = failure;
            if (inner)
            {
                try
                {
                    hook!.OnException(saved, failure, token);
                }
                catch (Exception replacement)
                {
                    error = replacement;
                    throw;
                }
            }

            throw;
        }
        finally
        {
            if (inner)
                hook!.Finally(saved, new(success, error), token);
        }
    }
    internal static ValueTask<T> SendAsyncResponse<R, T, W, K0, K1, H, L>(IServiceProvider p, R r, CancellationToken t, bool outer, bool inner) where R : class, IRequest<T> where W : class, IAsyncSendWrapper<R, T> where K0 : class, IAsyncSendRequestTransform<R>, IAsyncSendResponseTransform<R, T>, IAsyncSendExceptionObserver<R>, IAsyncSendFinallyObserver<R> where K1 : class, IAsyncSendRequestTransform<R>, IAsyncSendResponseTransform<R, T>, IAsyncSendExceptionObserver<R>, IAsyncSendFinallyObserver<R> where H : class, IRequestHandler<R, T> where L : struct, IReferenceLease<R, H>
    {
        ArgumentNullException.ThrowIfNull(r);
        return OuterAsyncResponse<R, T, W, K0, K1, H, L>(p, r, t, outer, inner);
    }
    private static async ValueTask<T> OuterAsyncResponse<R, T, W, K0, K1, H, L>(IServiceProvider p, R request, CancellationToken token, bool outer, bool inner) where R : class, IRequest<T> where W : class, IAsyncSendWrapper<R, T> where K0 : class, IAsyncSendRequestTransform<R>, IAsyncSendResponseTransform<R, T>, IAsyncSendExceptionObserver<R>, IAsyncSendFinallyObserver<R> where K1 : class, IAsyncSendRequestTransform<R>, IAsyncSendResponseTransform<R, T>, IAsyncSendExceptionObserver<R>, IAsyncSendFinallyObserver<R> where H : class, IRequestHandler<R, T> where L : struct, IReferenceLease<R, H>
    {
        token.ThrowIfCancellationRequested();
        K0? hook = outer ? p.GetRequiredService<K0>() : null;
        var saved = request;
        var success = false;
        Exception? error = null;
        try
        {
            if (outer)
            {
                saved = await hook!.BeforeAsync(saved, token).ConfigureAwait(false);
                ArgumentNullException.ThrowIfNull(saved, "request");
                token.ThrowIfCancellationRequested();
            }
            T response = await p.GetRequiredService<W>().InvokeAsync(saved, new NextAsyncResponse<R, T, W, K0, K1, H, L>(p, inner), token).ConfigureAwait(false);
            if (outer)
            {
                response = await hook!.AfterAsync(saved, response, token).ConfigureAwait(false);
            }
            success = true;
            return response;
        }
        catch (Exception failure)
        {
            error = failure;
            if (outer)
            {
                try
                {
                    await hook!.OnExceptionAsync(saved, failure, token).ConfigureAwait(false);
                }
                catch (Exception replacement)
                {
                    error = replacement;
                    throw;
                }
            }

            throw;
        }
        finally
        {
            if (outer)
                await hook!.FinallyAsync(saved, new(success, error), token).ConfigureAwait(false);
        }
    }
    private readonly struct NextAsyncResponse<R, T, W, K0, K1, H, L> : IAsyncSendContinuation<R, T> where R : class, IRequest<T> where W : class, IAsyncSendWrapper<R, T> where K0 : class, IAsyncSendRequestTransform<R>, IAsyncSendResponseTransform<R, T>, IAsyncSendExceptionObserver<R>, IAsyncSendFinallyObserver<R> where K1 : class, IAsyncSendRequestTransform<R>, IAsyncSendResponseTransform<R, T>, IAsyncSendExceptionObserver<R>, IAsyncSendFinallyObserver<R> where H : class, IRequestHandler<R, T> where L : struct, IReferenceLease<R, H>
    {
        private readonly IServiceProvider _p; private readonly bool _inner; public NextAsyncResponse(IServiceProvider p, bool inner)
        {
            _p = p;
            _inner = inner;
        }
        public ValueTask<T> InvokeAsync(R request, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(request);
            return InnerAsyncResponse<R, T, W, K0, K1, H, L>(_p, request, token, _inner);
        }
    }
    private static async ValueTask<T> InnerAsyncResponse<R, T, W, K0, K1, H, L>(IServiceProvider p, R request, CancellationToken token, bool inner) where R : class, IRequest<T> where W : class, IAsyncSendWrapper<R, T> where K0 : class, IAsyncSendRequestTransform<R>, IAsyncSendResponseTransform<R, T>, IAsyncSendExceptionObserver<R>, IAsyncSendFinallyObserver<R> where K1 : class, IAsyncSendRequestTransform<R>, IAsyncSendResponseTransform<R, T>, IAsyncSendExceptionObserver<R>, IAsyncSendFinallyObserver<R> where H : class, IRequestHandler<R, T> where L : struct, IReferenceLease<R, H>
    {
        token.ThrowIfCancellationRequested();
        K1? hook = inner ? p.GetRequiredService<K1>() : null;
        var saved = request;
        var success = false;
        Exception? error = null;
        try
        {
            if (inner)
            {
                saved = await hook!.BeforeAsync(saved, token).ConfigureAwait(false);
                ArgumentNullException.ThrowIfNull(saved, "request");
                token.ThrowIfCancellationRequested();
            }
            H? handler = await L.Acquire(p, saved, token).ConfigureAwait(false) ?? throw new InvalidOperationException("A session factory must return a nonnull Handler session.");
            T response;
            try
            {
                response = await handler.HandleAsync(saved, token).ConfigureAwait(false);
            }
            finally
            {
                if (L.Owned)
                {
                    var detached = handler;
                    handler = null;
                    await L.Release(detached).ConfigureAwait(false);
                }
            }
            if (inner)
            {
                response = await hook!.AfterAsync(saved, response, token).ConfigureAwait(false);
            }
            success = true;
            return response;
        }
        catch (Exception failure)
        {
            error = failure;
            if (inner)
            {
                try
                {
                    await hook!.OnExceptionAsync(saved, failure, token).ConfigureAwait(false);
                }
                catch (Exception replacement)
                {
                    error = replacement;
                    throw;
                }
            }

            throw;
        }
        finally
        {
            if (inner)
                await hook!.FinallyAsync(saved, new(success, error), token).ConfigureAwait(false);
        }
    }
    internal static ValueTask SendAsyncVoid<R, W, K0, K1, H, L>(IServiceProvider p, R r, CancellationToken t, bool outer, bool inner) where R : class, IRequest where W : class, IAsyncSendWrapper<R> where K0 : class, IAsyncSendRequestTransform<R>, IAsyncSendSuccessObserver<R>, IAsyncSendExceptionObserver<R>, IAsyncSendFinallyObserver<R> where K1 : class, IAsyncSendRequestTransform<R>, IAsyncSendSuccessObserver<R>, IAsyncSendExceptionObserver<R>, IAsyncSendFinallyObserver<R> where H : class, IRequestHandler<R> where L : struct, IReferenceLease<R, H>
    {
        ArgumentNullException.ThrowIfNull(r);
        return OuterAsyncVoid<R, W, K0, K1, H, L>(p, r, t, outer, inner);
    }
    private static async ValueTask OuterAsyncVoid<R, W, K0, K1, H, L>(IServiceProvider p, R request, CancellationToken token, bool outer, bool inner) where R : class, IRequest where W : class, IAsyncSendWrapper<R> where K0 : class, IAsyncSendRequestTransform<R>, IAsyncSendSuccessObserver<R>, IAsyncSendExceptionObserver<R>, IAsyncSendFinallyObserver<R> where K1 : class, IAsyncSendRequestTransform<R>, IAsyncSendSuccessObserver<R>, IAsyncSendExceptionObserver<R>, IAsyncSendFinallyObserver<R> where H : class, IRequestHandler<R> where L : struct, IReferenceLease<R, H>
    {
        token.ThrowIfCancellationRequested();
        K0? hook = outer ? p.GetRequiredService<K0>() : null;
        var saved = request;
        var success = false;
        Exception? error = null;
        try
        {
            if (outer)
            {
                saved = await hook!.BeforeAsync(saved, token).ConfigureAwait(false);
                ArgumentNullException.ThrowIfNull(saved, "request");
                token.ThrowIfCancellationRequested();
            }
            await p.GetRequiredService<W>().InvokeAsync(saved, new NextAsyncVoid<R, W, K0, K1, H, L>(p, inner), token).ConfigureAwait(false);
            if (outer)
            {
                await hook!.AfterAsync(saved, token).ConfigureAwait(false);
            }
            success = true;
        }
        catch (Exception failure)
        {
            error = failure;
            if (outer)
            {
                try
                {
                    await hook!.OnExceptionAsync(saved, failure, token).ConfigureAwait(false);
                }
                catch (Exception replacement)
                {
                    error = replacement;
                    throw;
                }
            }

            throw;
        }
        finally
        {
            if (outer)
                await hook!.FinallyAsync(saved, new(success, error), token).ConfigureAwait(false);
        }
    }
    private readonly struct NextAsyncVoid<R, W, K0, K1, H, L> : IAsyncSendContinuation<R> where R : class, IRequest where W : class, IAsyncSendWrapper<R> where K0 : class, IAsyncSendRequestTransform<R>, IAsyncSendSuccessObserver<R>, IAsyncSendExceptionObserver<R>, IAsyncSendFinallyObserver<R> where K1 : class, IAsyncSendRequestTransform<R>, IAsyncSendSuccessObserver<R>, IAsyncSendExceptionObserver<R>, IAsyncSendFinallyObserver<R> where H : class, IRequestHandler<R> where L : struct, IReferenceLease<R, H>
    {
        private readonly IServiceProvider _p; private readonly bool _inner; public NextAsyncVoid(IServiceProvider p, bool inner)
        {
            _p = p;
            _inner = inner;
        }
        public ValueTask InvokeAsync(R request, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(request);
            return InnerAsyncVoid<R, W, K0, K1, H, L>(_p, request, token, _inner);
        }
    }
    private static async ValueTask InnerAsyncVoid<R, W, K0, K1, H, L>(IServiceProvider p, R request, CancellationToken token, bool inner) where R : class, IRequest where W : class, IAsyncSendWrapper<R> where K0 : class, IAsyncSendRequestTransform<R>, IAsyncSendSuccessObserver<R>, IAsyncSendExceptionObserver<R>, IAsyncSendFinallyObserver<R> where K1 : class, IAsyncSendRequestTransform<R>, IAsyncSendSuccessObserver<R>, IAsyncSendExceptionObserver<R>, IAsyncSendFinallyObserver<R> where H : class, IRequestHandler<R> where L : struct, IReferenceLease<R, H>
    {
        token.ThrowIfCancellationRequested();
        K1? hook = inner ? p.GetRequiredService<K1>() : null;
        var saved = request;
        var success = false;
        Exception? error = null;
        try
        {
            if (inner)
            {
                saved = await hook!.BeforeAsync(saved, token).ConfigureAwait(false);
                ArgumentNullException.ThrowIfNull(saved, "request");
                token.ThrowIfCancellationRequested();
            }
            H? handler = await L.Acquire(p, saved, token).ConfigureAwait(false) ?? throw new InvalidOperationException("A session factory must return a nonnull Handler session.");
            try
            {
                await handler.HandleAsync(saved, token).ConfigureAwait(false);
            }
            finally
            {
                if (L.Owned)
                {
                    var detached = handler;
                    handler = null;
                    await L.Release(detached).ConfigureAwait(false);
                }
            }
            if (inner)
            {
                await hook!.AfterAsync(saved, token).ConfigureAwait(false);
            }
            success = true;
        }
        catch (Exception failure)
        {
            error = failure;
            if (inner)
            {
                try
                {
                    await hook!.OnExceptionAsync(saved, failure, token).ConfigureAwait(false);
                }
                catch (Exception replacement)
                {
                    error = replacement;
                    throw;
                }
            }

            throw;
        }
        finally
        {
            if (inner)
                await hook!.FinallyAsync(saved, new(success, error), token).ConfigureAwait(false);
        }
    }
}
