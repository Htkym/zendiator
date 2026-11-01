using Microsoft.Extensions.DependencyInjection;

namespace Zendiator.Generator.Tests;

// Stateless, typed acquisition/ownership choices. None introduces an async builder or
// consumes/rewrites a factory ValueTask; async factories and cleanup are forwarded intact.
internal interface IReferenceLease<R, S> where R : class where S : class
{
    static abstract bool Owned
    {
        get;
    }
    static abstract ValueTask<S> Acquire(IServiceProvider provider, R request, CancellationToken token);
    static abstract ValueTask Release(S resource);
}
internal readonly struct DiReferenceLease<R, S> : IReferenceLease<R, S> where R : class where S : class
{
    public static bool Owned => false;
    public static ValueTask<S> Acquire(IServiceProvider p, R r, CancellationToken t) => new(p.GetRequiredService<S>());
    public static ValueTask Release(S s) => default;
}
internal readonly struct OwnedReferenceLease<R, S, F> : IReferenceLease<R, S> where R : class where S : class, IAsyncDisposable where F : class, IOwnedSessionFactory<R, S>
{
    public static bool Owned => true;
    public static ValueTask<S> Acquire(IServiceProvider p, R r, CancellationToken t) => new(p.GetRequiredService<F>().Create(r, t));
    public static ValueTask Release(S s) => s.DisposeAsync();
}
internal readonly struct BorrowedReferenceLease<R, S, F> : IReferenceLease<R, S> where R : class where S : class where F : class, IBorrowedSessionFactory<R, S>
{
    public static bool Owned => false;
    public static ValueTask<S> Acquire(IServiceProvider p, R r, CancellationToken t) => new(p.GetRequiredService<F>().Create(r, t));
    public static ValueTask Release(S s) => default;
}
internal readonly struct AsyncOwnedReferenceLease<R, S, F> : IReferenceLease<R, S> where R : class where S : class, IAsyncDisposable where F : class, IAsyncOwnedSessionFactory<R, S>
{
    public static bool Owned => true;
    public static ValueTask<S> Acquire(IServiceProvider p, R r, CancellationToken t) => p.GetRequiredService<F>().CreateAsync(r, t);
    public static ValueTask Release(S s) => s.DisposeAsync();
}
internal readonly struct AsyncBorrowedReferenceLease<R, S, F> : IReferenceLease<R, S> where R : class where S : class where F : class, IAsyncBorrowedSessionFactory<R, S>
{
    public static bool Owned => false;
    public static ValueTask<S> Acquire(IServiceProvider p, R r, CancellationToken t) => p.GetRequiredService<F>().CreateAsync(r, t);
    public static ValueTask Release(S s) => default;
}

// Independent handwritten mixed contract. One native orchestration; no graph,
// emitter text, reflection callback, runtime policy interpreter, or per-callback builder.
internal static class CanonicalAsyncMixedReference
{
    internal static ValueTask<T> SendResponse<R, T, K, H, KL, HL>(IServiceProvider p, R r, CancellationToken t)
        where R : class, IRequest<T> where K : class, IAsyncSendRequestTransform<R>, IAsyncSendResponseTransform<R, T>, IAsyncSendExceptionObserver<R>, IAsyncSendFinallyObserver<R>
        where H : class, IRequestHandler<R, T> where KL : struct, IReferenceLease<R, K> where HL : struct, IReferenceLease<R, H>
    {
        ArgumentNullException.ThrowIfNull(r);
        return ExecuteResponse<R, T, K, H, KL, HL>(p, r, t);
    }
    private static async ValueTask<T> ExecuteResponse<R, T, K, H, KL, HL>(IServiceProvider p, R request, CancellationToken token)
        where R : class, IRequest<T> where K : class, IAsyncSendRequestTransform<R>, IAsyncSendResponseTransform<R, T>, IAsyncSendExceptionObserver<R>, IAsyncSendFinallyObserver<R>
        where H : class, IRequestHandler<R, T> where KL : struct, IReferenceLease<R, K> where HL : struct, IReferenceLease<R, H>
    {
        token.ThrowIfCancellationRequested();
        K? hook = await KL.Acquire(p, request, token).ConfigureAwait(false) ?? throw new InvalidOperationException("A session factory must return a nonnull Hook session.");
        try
        {
            var saved = request;
            var success = false;
            Exception? error = null;
            try
            {
                saved = await hook.BeforeAsync(saved, token).ConfigureAwait(false);
                ArgumentNullException.ThrowIfNull(saved, "request");
                token.ThrowIfCancellationRequested();
                H? handler = await HL.Acquire(p, saved, token).ConfigureAwait(false) ?? throw new InvalidOperationException("A session factory must return a nonnull Handler session.");
                T response;
                try
                {
                    response = await handler.HandleAsync(saved, token).ConfigureAwait(false);
                }
                finally
                {
                    if (HL.Owned)
                    {
                        var detached = handler;
                        handler = null;
                        await HL.Release(detached).ConfigureAwait(false);
                    }
                }
                response = await hook.AfterAsync(saved, response, token).ConfigureAwait(false);
                success = true;
                return response;
            }
            catch (Exception failure)
            {
                error = failure;
                try
                {
                    await hook.OnExceptionAsync(saved, failure, token).ConfigureAwait(false);
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
                await hook.FinallyAsync(saved, new(success, error), token).ConfigureAwait(false);
            }
        }
        finally
        {
            if (KL.Owned)
            {
                var detached = hook;
                hook = null;
                await KL.Release(detached).ConfigureAwait(false);
            }
        }
    }
    internal static ValueTask SendVoid<R, K, H, KL, HL>(IServiceProvider p, R r, CancellationToken t)
        where R : class, IRequest where K : class, IAsyncSendRequestTransform<R>, IAsyncSendSuccessObserver<R>, IAsyncSendExceptionObserver<R>, IAsyncSendFinallyObserver<R>
        where H : class, IRequestHandler<R> where KL : struct, IReferenceLease<R, K> where HL : struct, IReferenceLease<R, H>
    {
        ArgumentNullException.ThrowIfNull(r);
        return ExecuteVoid<R, K, H, KL, HL>(p, r, t);
    }
    private static async ValueTask ExecuteVoid<R, K, H, KL, HL>(IServiceProvider p, R request, CancellationToken token)
        where R : class, IRequest where K : class, IAsyncSendRequestTransform<R>, IAsyncSendSuccessObserver<R>, IAsyncSendExceptionObserver<R>, IAsyncSendFinallyObserver<R>
        where H : class, IRequestHandler<R> where KL : struct, IReferenceLease<R, K> where HL : struct, IReferenceLease<R, H>
    {
        token.ThrowIfCancellationRequested();
        K? hook = await KL.Acquire(p, request, token).ConfigureAwait(false) ?? throw new InvalidOperationException("A session factory must return a nonnull Hook session.");
        try
        {
            var saved = request;
            var success = false;
            Exception? error = null;
            try
            {
                saved = await hook.BeforeAsync(saved, token).ConfigureAwait(false);
                ArgumentNullException.ThrowIfNull(saved, "request");
                token.ThrowIfCancellationRequested();
                H? handler = await HL.Acquire(p, saved, token).ConfigureAwait(false) ?? throw new InvalidOperationException("A session factory must return a nonnull Handler session.");
                try
                {
                    await handler.HandleAsync(saved, token).ConfigureAwait(false);
                }
                finally
                {
                    if (HL.Owned)
                    {
                        var detached = handler;
                        handler = null;
                        await HL.Release(detached).ConfigureAwait(false);
                    }
                }
                await hook.AfterAsync(saved, token).ConfigureAwait(false);
                success = true;
            }
            catch (Exception failure)
            {
                error = failure;
                try
                {
                    await hook.OnExceptionAsync(saved, failure, token).ConfigureAwait(false);
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
                await hook.FinallyAsync(saved, new(success, error), token).ConfigureAwait(false);
            }
        }
        finally
        {
            if (KL.Owned)
            {
                var detached = hook;
                hook = null;
                await KL.Release(detached).ConfigureAwait(false);
            }
        }
    }
}
