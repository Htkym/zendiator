namespace Zendiator;

/// <summary>Creates a session whose ownership passes to the operation on a successful, nonnull return.</summary>
/// <remarks>
/// The factory service belongs to its DI owner. The factory must release partial construction before
/// throwing. Each owned result must be new or exclusively transferred; returning a DI-owned object or
/// the same owned object to concurrent operations violates this contract. The session implements the
/// declared Hook or Wrapper callbacks. Synchronous routes require IDisposable; asynchronous routes
/// and Stream require IAsyncDisposable. Callbacks borrow the session until the operation completes.
/// </remarks>
public interface IOwnedSessionFactory<TRequest, TSession> where TSession : class
{
    TSession Create(TRequest request, CancellationToken cancellationToken);
}

/// <summary>Asynchronously creates a session with the ownership rules of IOwnedSessionFactory.</summary>
public interface IAsyncOwnedSessionFactory<TRequest, TSession> where TSession : class
{
    /// <summary>Transfers a nonnull session only after successful completion. The returned ValueTask is consumed once.</summary>
    ValueTask<TSession> CreateAsync(TRequest request, CancellationToken cancellationToken);
}

/// <summary>Returns a borrowed session. The operation never disposes it, including when it is disposable.</summary>
/// <remarks>
/// The factory and session retain their external owners. Their lifetime must cover the operation and,
/// for Stream, enumerator disposal. Shared mutable sessions require the owner's concurrency control.
/// </remarks>
public interface IBorrowedSessionFactory<TRequest, TSession> where TSession : class
{
    TSession Create(TRequest request, CancellationToken cancellationToken);
}

/// <summary>Asynchronously returns a session with the ownership rules of IBorrowedSessionFactory.</summary>
public interface IAsyncBorrowedSessionFactory<TRequest, TSession> where TSession : class
{
    /// <summary>Returns a nonnull session after successful completion. The returned ValueTask is consumed once.</summary>
    ValueTask<TSession> CreateAsync(TRequest request, CancellationToken cancellationToken);
}
