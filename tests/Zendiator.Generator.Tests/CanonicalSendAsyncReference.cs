using Microsoft.Extensions.DependencyInjection;

namespace Zendiator.Generator.Tests;

// Typed handwritten asynchronous contract; no graph, emitted text, or blocking conversion.
internal static class CanonicalSendAsyncReference
{
    internal static ValueTask<T> SendDirectResponse<R, T, H>(IServiceProvider provider, R request, CancellationToken token) where R : class, IRequest<T> where H : class, IRequestHandler<R, T>
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteDirectResponse<R, T, H>(provider, request, token);
    }
    private static async ValueTask<T> ExecuteDirectResponse<R, T, H>(IServiceProvider provider, R request, CancellationToken token) where R : class, IRequest<T> where H : class, IRequestHandler<R, T>
    {
        token.ThrowIfCancellationRequested();
        var handler = provider.GetRequiredService<H>();
        return await handler.HandleAsync(request, token).ConfigureAwait(false);
    }

    internal static ValueTask SendDirectVoid<R, H>(IServiceProvider provider, R request, CancellationToken token) where R : class, IRequest where H : class, IRequestHandler<R>
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteDirectVoid<R, H>(provider, request, token);
    }
    private static async ValueTask ExecuteDirectVoid<R, H>(IServiceProvider provider, R request, CancellationToken token) where R : class, IRequest where H : class, IRequestHandler<R>
    {
        token.ThrowIfCancellationRequested();
        var handler = provider.GetRequiredService<H>();
        await handler.HandleAsync(request, token).ConfigureAwait(false);
    }

    internal static ValueTask<T> SendOwnedResponse<R, T, H, F>(IServiceProvider provider, R request, CancellationToken token) where R : class, IRequest<T> where H : class, IRequestHandler<R, T>, IAsyncDisposable where F : class, IOwnedSessionFactory<R, H>
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteOwnedResponse<R, T, H, F>(provider, request, token);
    }
    private static async ValueTask<T> ExecuteOwnedResponse<R, T, H, F>(IServiceProvider provider, R request, CancellationToken token) where R : class, IRequest<T> where H : class, IRequestHandler<R, T>, IAsyncDisposable where F : class, IOwnedSessionFactory<R, H>
    {
        token.ThrowIfCancellationRequested();
        H? handler = provider.GetRequiredService<F>().Create(request, token) ?? throw new InvalidOperationException("A session factory must return a nonnull Handler session.");
        try { return await handler.HandleAsync(request, token).ConfigureAwait(false); }
        finally { var detached = handler; handler = null; await detached.DisposeAsync().ConfigureAwait(false); }
    }

    internal static ValueTask SendOwnedVoid<R, H, F>(IServiceProvider provider, R request, CancellationToken token) where R : class, IRequest where H : class, IRequestHandler<R>, IAsyncDisposable where F : class, IOwnedSessionFactory<R, H>
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteOwnedVoid<R, H, F>(provider, request, token);
    }
    private static async ValueTask ExecuteOwnedVoid<R, H, F>(IServiceProvider provider, R request, CancellationToken token) where R : class, IRequest where H : class, IRequestHandler<R>, IAsyncDisposable where F : class, IOwnedSessionFactory<R, H>
    {
        token.ThrowIfCancellationRequested();
        H? handler = provider.GetRequiredService<F>().Create(request, token) ?? throw new InvalidOperationException("A session factory must return a nonnull Handler session.");
        try { await handler.HandleAsync(request, token).ConfigureAwait(false); }
        finally { var detached = handler; handler = null; await detached.DisposeAsync().ConfigureAwait(false); }
    }

    internal static ValueTask<T> SendBorrowedResponse<R, T, H, F>(IServiceProvider provider, R request, CancellationToken token) where R : class, IRequest<T> where H : class, IRequestHandler<R, T> where F : class, IBorrowedSessionFactory<R, H>
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteBorrowedResponse<R, T, H, F>(provider, request, token);
    }
    private static async ValueTask<T> ExecuteBorrowedResponse<R, T, H, F>(IServiceProvider provider, R request, CancellationToken token) where R : class, IRequest<T> where H : class, IRequestHandler<R, T> where F : class, IBorrowedSessionFactory<R, H>
    {
        token.ThrowIfCancellationRequested();
        H? handler = provider.GetRequiredService<F>().Create(request, token) ?? throw new InvalidOperationException("A session factory must return a nonnull Handler session.");
        return await handler.HandleAsync(request, token).ConfigureAwait(false);
    }

    internal static ValueTask SendBorrowedVoid<R, H, F>(IServiceProvider provider, R request, CancellationToken token) where R : class, IRequest where H : class, IRequestHandler<R> where F : class, IBorrowedSessionFactory<R, H>
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteBorrowedVoid<R, H, F>(provider, request, token);
    }
    private static async ValueTask ExecuteBorrowedVoid<R, H, F>(IServiceProvider provider, R request, CancellationToken token) where R : class, IRequest where H : class, IRequestHandler<R> where F : class, IBorrowedSessionFactory<R, H>
    {
        token.ThrowIfCancellationRequested();
        H? handler = provider.GetRequiredService<F>().Create(request, token) ?? throw new InvalidOperationException("A session factory must return a nonnull Handler session.");
        await handler.HandleAsync(request, token).ConfigureAwait(false);
    }

    internal static ValueTask<T> SendAsyncOwnedResponse<R, T, H, F>(IServiceProvider provider, R request, CancellationToken token) where R : class, IRequest<T> where H : class, IRequestHandler<R, T>, IAsyncDisposable where F : class, IAsyncOwnedSessionFactory<R, H>
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteAsyncOwnedResponse<R, T, H, F>(provider, request, token);
    }
    private static async ValueTask<T> ExecuteAsyncOwnedResponse<R, T, H, F>(IServiceProvider provider, R request, CancellationToken token) where R : class, IRequest<T> where H : class, IRequestHandler<R, T>, IAsyncDisposable where F : class, IAsyncOwnedSessionFactory<R, H>
    {
        token.ThrowIfCancellationRequested();
        H? handler = await provider.GetRequiredService<F>().CreateAsync(request, token).ConfigureAwait(false) ?? throw new InvalidOperationException("A session factory must return a nonnull Handler session.");
        try { return await handler.HandleAsync(request, token).ConfigureAwait(false); }
        finally { var detached = handler; handler = null; await detached.DisposeAsync().ConfigureAwait(false); }
    }

    internal static ValueTask SendAsyncOwnedVoid<R, H, F>(IServiceProvider provider, R request, CancellationToken token) where R : class, IRequest where H : class, IRequestHandler<R>, IAsyncDisposable where F : class, IAsyncOwnedSessionFactory<R, H>
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteAsyncOwnedVoid<R, H, F>(provider, request, token);
    }
    private static async ValueTask ExecuteAsyncOwnedVoid<R, H, F>(IServiceProvider provider, R request, CancellationToken token) where R : class, IRequest where H : class, IRequestHandler<R>, IAsyncDisposable where F : class, IAsyncOwnedSessionFactory<R, H>
    {
        token.ThrowIfCancellationRequested();
        H? handler = await provider.GetRequiredService<F>().CreateAsync(request, token).ConfigureAwait(false) ?? throw new InvalidOperationException("A session factory must return a nonnull Handler session.");
        try { await handler.HandleAsync(request, token).ConfigureAwait(false); }
        finally { var detached = handler; handler = null; await detached.DisposeAsync().ConfigureAwait(false); }
    }

    internal static ValueTask<T> SendAsyncBorrowedResponse<R, T, H, F>(IServiceProvider provider, R request, CancellationToken token) where R : class, IRequest<T> where H : class, IRequestHandler<R, T> where F : class, IAsyncBorrowedSessionFactory<R, H>
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteAsyncBorrowedResponse<R, T, H, F>(provider, request, token);
    }
    private static async ValueTask<T> ExecuteAsyncBorrowedResponse<R, T, H, F>(IServiceProvider provider, R request, CancellationToken token) where R : class, IRequest<T> where H : class, IRequestHandler<R, T> where F : class, IAsyncBorrowedSessionFactory<R, H>
    {
        token.ThrowIfCancellationRequested();
        H? handler = await provider.GetRequiredService<F>().CreateAsync(request, token).ConfigureAwait(false) ?? throw new InvalidOperationException("A session factory must return a nonnull Handler session.");
        return await handler.HandleAsync(request, token).ConfigureAwait(false);
    }

    internal static ValueTask SendAsyncBorrowedVoid<R, H, F>(IServiceProvider provider, R request, CancellationToken token) where R : class, IRequest where H : class, IRequestHandler<R> where F : class, IAsyncBorrowedSessionFactory<R, H>
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteAsyncBorrowedVoid<R, H, F>(provider, request, token);
    }
    private static async ValueTask ExecuteAsyncBorrowedVoid<R, H, F>(IServiceProvider provider, R request, CancellationToken token) where R : class, IRequest where H : class, IRequestHandler<R> where F : class, IAsyncBorrowedSessionFactory<R, H>
    {
        token.ThrowIfCancellationRequested();
        H? handler = await provider.GetRequiredService<F>().CreateAsync(request, token).ConfigureAwait(false) ?? throw new InvalidOperationException("A session factory must return a nonnull Handler session.");
        await handler.HandleAsync(request, token).ConfigureAwait(false);
    }
}
