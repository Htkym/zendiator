namespace Zendiator;

/// <summary>Synchronously observes a scoped request before invoking the inner scope.</summary>
/// <typeparam name="R">The request, which must not be retained by the callback.</typeparam>
public interface IScopedSendBeforeObserver<R> where R : allows ref struct
{
    void Before(scoped R request, CancellationToken token);
}

/// <summary>Synchronously observes successful response completion without replacing the response.</summary>
/// <typeparam name="R">The request, which must not escape the callback.</typeparam>
/// <typeparam name="T">The response.</typeparam>
public interface IScopedSendResponseObserver<R, T> where R : allows ref struct
{
    void After(scoped R request, T response, CancellationToken token);
}

/// <summary>Synchronously observes successful void completion with a scoped request.</summary>
/// <typeparam name="R">The request, which must not escape the callback.</typeparam>
public interface IScopedSendSuccessObserver<R> where R : allows ref struct
{
    void After(scoped R request, CancellationToken token);
}

/// <summary>Synchronously observes the original exception with a scoped request.</summary>
/// <typeparam name="R">The request, which must not escape the callback.</typeparam>
public interface IScopedSendExceptionObserver<R> where R : allows ref struct
{
    /// <summary>Observes a failure without recovering on normal return.</summary>
    void OnException(scoped R request, Exception error, CancellationToken token);
}

/// <summary>Synchronously observes an entered stage's exit with a scoped request.</summary>
/// <typeparam name="R">The request, which must not escape the callback.</typeparam>
public interface IScopedSendFinallyObserver<R> where R : allows ref struct
{
    void Finally(scoped R request, SendExitInfo exit, CancellationToken token);
}
