namespace Zendiator;

/// <summary>The outcome immediately before an entered stage's Finally callback.</summary>
/// <param name="Success">Whether the inner scope completed successfully before this callback.</param>
/// <param name="Error">The original exception object, if the inner scope failed.</param>
public readonly record struct SendExitInfo(bool Success, Exception? Error);

/// <summary>Transforms the request before the inner scope starts.</summary>
public interface ISendRequestTransform<R>
{
    R Before(R request, CancellationToken token);
}
/// <summary>Transforms the response after the inner scope succeeds.</summary>
public interface ISendResponseTransform<R, T>
{
    T After(R stageRequest, T response, CancellationToken token);
}
/// <summary>Observes successful completion of a void inner scope.</summary>
public interface ISendSuccessObserver<R>
{
    /// <summary>Observes successful inner completion using this stage's request.</summary>
    void After(R stageRequest, CancellationToken token);
}
/// <summary>Observes an exception without recovering the inner operation.</summary>
public interface ISendExceptionObserver<R>
{
    /// <summary>Observes the original inner exception without replacing it on normal return.</summary>
    void OnException(R stageRequest, Exception error, CancellationToken token);
}
/// <summary>Runs once when an entered stage exits.</summary>
public interface ISendFinallyObserver<R>
{
    void Finally(R stageRequest, SendExitInfo exit, CancellationToken token);
}
/// <summary>Asynchronously transforms the request before the inner scope starts.</summary>
public interface IAsyncSendRequestTransform<R>
{
    /// <summary>Returns the request for the inner scope in a ValueTask consumed once.</summary>
    ValueTask<R> BeforeAsync(R request, CancellationToken token);
}
/// <summary>Asynchronously transforms the response after the inner scope succeeds.</summary>
public interface IAsyncSendResponseTransform<R, T>
{
    /// <summary>Returns the transformed successful response in a ValueTask consumed once.</summary>
    ValueTask<T> AfterAsync(R stageRequest, T response, CancellationToken token);
}
/// <summary>Asynchronously observes successful completion of a void inner scope.</summary>
public interface IAsyncSendSuccessObserver<R>
{
    /// <summary>Observes successful void completion in a ValueTask consumed once.</summary>
    ValueTask AfterAsync(R stageRequest, CancellationToken token);
}
/// <summary>Asynchronously observes an exception without recovering the inner operation.</summary>
public interface IAsyncSendExceptionObserver<R>
{
    /// <summary>Observes the original inner exception in a ValueTask consumed once.</summary>
    ValueTask OnExceptionAsync(R stageRequest, Exception error, CancellationToken token);
}
/// <summary>Asynchronously runs once when an entered stage exits.</summary>
public interface IAsyncSendFinallyObserver<R>
{
    /// <summary>Observes the outcome before this stage exits in a ValueTask consumed once.</summary>
    ValueTask FinallyAsync(R stageRequest, SendExitInfo exit, CancellationToken token);
}

/// <summary>A typed inner invocation. Every call starts independent inner state.</summary>
public interface ISendContinuation<R, T>
{
    T Invoke(R request, CancellationToken token);
}
/// <summary>A typed void inner invocation. Every call starts independent inner state.</summary>
public interface ISendContinuation<R>
{
    void Invoke(R request, CancellationToken token);
}
/// <summary>A typed asynchronous inner invocation, consumed once per call.</summary>
public interface IAsyncSendContinuation<R, T>
{
    ValueTask<T> InvokeAsync(R request, CancellationToken token);
}
/// <summary>A typed asynchronous void inner invocation, consumed once per call.</summary>
public interface IAsyncSendContinuation<R>
{
    ValueTask InvokeAsync(R request, CancellationToken token);
}
/// <summary>An opaque synchronous wrapper that may invoke the inner scope zero or multiple times.</summary>
public interface ISendWrapper<R, T>
{
    T Invoke<TNext>(R request, TNext next, CancellationToken token) where TNext : ISendContinuation<R, T>;
}
public interface ISendWrapper<R>
{
    /// <summary>Wraps the void inner scope and may invoke next zero or multiple times.</summary>
    void Invoke<TNext>(R request, TNext next, CancellationToken token) where TNext : ISendContinuation<R>;
}
public interface IAsyncSendWrapper<R, T>
{
    /// <summary>Wraps the inner scope. Each next call has independent state and a ValueTask consumed once.</summary>
    ValueTask<T> InvokeAsync<TNext>(R request, TNext next, CancellationToken token) where TNext : IAsyncSendContinuation<R, T>;
}
public interface IAsyncSendWrapper<R>
{
    /// <summary>Wraps the void inner scope. Each next call has independent state and a ValueTask consumed once.</summary>
    ValueTask InvokeAsync<TNext>(R request, TNext next, CancellationToken token) where TNext : IAsyncSendContinuation<R>;
}
