using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks.Sources;

namespace Zendiator.Streaming;

/// <summary>Shares cursor ownership and operation guards without virtual per-item forwarding.</summary>
/// <remarks>
/// Generated sealed cursors implement MoveNextAsync and Current directly against their typed source.
/// Initialization and source operations must each be consumed once, then completed through these helpers.
/// Only cleanup is virtual. An in-flight Move must be awaited before requesting disposal.
/// </remarks>
public abstract class StreamCursorState : IAsyncDisposable
{
    private readonly CancellationToken _apiToken;
    private CancellationToken _enumerationToken;
    private CancellationTokenSource? _linked;
    private CancellationToken _token;
    // -1 reserves the first enumeration, -2 binds its token; 0/1/2 are ready/Move/Dispose.
    private int _busy;
    private bool _started;
    private bool _hasCurrent;
    private bool _closed;
    private bool _cleanupComplete;
    private Exception? _disposeError;
    private DisposeCompletion? _disposeCompletion;
    private object? _pendingMove;

    /// <summary>Creates a cursor owner without acquiring dependencies or combining tokens.</summary>
    protected StreamCursorState(CancellationToken apiToken = default, CancellationToken enumerationToken = default,
        bool reserveFirstEnumeration = false)
    {
        _apiToken = apiToken;
        _enumerationToken = enumerationToken;
        _busy = reserveFirstEnumeration ? -1 : 0;
    }

    /// <summary>The original API token, retained when creating an independent enumeration.</summary>
    protected CancellationToken OriginalApiToken => _apiToken;

    /// <summary>Binds the first enumeration token to an owner reserved at construction.</summary>
    protected bool TryClaimFirstEnumeration(CancellationToken enumerationToken)
    {
        if (Interlocked.CompareExchange(ref _busy, -2, -1) != -1)
            return false;

        _enumerationToken = enumerationToken;
        Volatile.Write(ref _busy, 0);
        return true;
    }

    /// <summary>The effective token, initialized by the first successful call to TryStart.</summary>
    protected CancellationToken Token => _token;

    /// <summary>The effective API and enumeration token after initialization begins.</summary>
    public CancellationToken CancellationToken => _token;

    /// <summary>Whether EOF, failure, or disposal has begun closing this owner.</summary>
    public bool IsClosed => Volatile.Read(ref _closed);

    /// <summary>Whether an operation or its returned completion is still pending.</summary>
    protected bool HasPendingOperation
    {
        get
        {
            var busy = Volatile.Read(ref _busy);
            return busy != 0 && (busy != 2 || !HasCompletedDisposal) || HasPendingCompletion;
        }
    }

    private bool HasCompletedDisposal => IsClosed
        && Volatile.Read(ref _disposeCompletion) is { Task.IsCompleted: true }
        && !IsPendingMoveCompletion(Volatile.Read(ref _pendingMove));

    private bool HasPendingMove => IsPendingMoveCompletion(Volatile.Read(ref _pendingMove));

    private bool HasPendingCompletion => IsPendingMoveCompletion(Volatile.Read(ref _pendingMove))
        || Volatile.Read(ref _disposeCompletion) is { Task.IsCompleted: false };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsPendingMoveCompletion(object? completion) => completion is not null
        && (completion is Task<bool> task ? !task.IsCompleted : !((MoveCompletion)completion).IsCompleted);

    /// <summary>Claims one Move, invalidates Current, and returns false for an already closed owner.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected bool TryBeginMove()
    {
        var busy = Interlocked.CompareExchange(ref _busy, 1, 0);
        if (busy != 0)
        {
            if (busy == 2 && HasCompletedDisposal)
                return false;
            throw new InvalidOperationException("Await the preceding cursor operation before starting another.");
        }

        // Check after claiming the gate: publication may finish between an earlier check and the CAS.
        var pendingMove = Volatile.Read(ref _pendingMove);
        if (IsPendingMoveCompletion(pendingMove)
            || Volatile.Read(ref _disposeCompletion) is { Task.IsCompleted: false })
        {
            Volatile.Write(ref _busy, 0);
            throw new InvalidOperationException("Await the preceding cursor operation before starting another.");
        }

        if (pendingMove is not null)
            Volatile.Write(ref _pendingMove, null);
        _hasCurrent = false;
        if (!_closed)
            return true;

        Volatile.Write(ref _busy, 0);
        return false;
    }

    /// <summary>Initializes cancellation on the first Move and reports whether acquisition is needed.</summary>
    /// <remarks>Call inside the generated Move's failure-handling boundary after TryBeginMove.</remarks>
    protected bool TryStart()
    {
        if (_started)
            return false;

        _started = true;
        _token = !_apiToken.CanBeCanceled ? _enumerationToken
            : !_enumerationToken.CanBeCanceled || _apiToken == _enumerationToken ? _apiToken
            : (_linked = CancellationTokenSource.CreateLinkedTokenSource(_apiToken, _enumerationToken)).Token;
        _token.ThrowIfCancellationRequested();
        return true;
    }

    /// <summary>Releases an opening operation without making Current available.</summary>
    protected void InitializationSucceeded()
    {
        _hasCurrent = false;
        Volatile.Write(ref _busy, 0);
    }

    /// <summary>Releases a privately consumed Move after its result and cleanup have settled.</summary>
    protected void ReleasePrivateMoveClaim() => Volatile.Write(ref _busy, 0);

    /// <summary>Checks Current validity before the generated getter reads its typed source.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void ValidateCurrent()
    {
        if (!_hasCurrent || _closed || HasPendingOperation)
            throw new InvalidOperationException("Current is unavailable.");
    }

    /// <summary>Completes a Move, cleaning owned resources before reporting EOF.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected ValueTask<bool> MoveSucceeded(bool hasValue)
    {
        if (hasValue)
        {
            _hasCurrent = true;
            return new(true);
        }

        return CloseMove(null);
    }

    /// <summary>Publishes a synchronous item directly from the outermost public Move.</summary>
    /// <remarks>Use only after consuming a successful raw Move once, never inside an asynchronous operation.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected ValueTask<bool> CompleteSynchronousMove()
    {
        var result = new ValueTask<bool>(true);
        _hasCurrent = true;
        Volatile.Write(ref _busy, 0);
        return result;
    }

    /// <summary>Closes a failed Move; a cleanup failure takes precedence over the original error.</summary>
    protected ValueTask<bool> MoveFailed(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return CloseMove(error);
    }

    /// <summary>Publishes the complete generated Move before releasing its operation gate.</summary>
    /// <remarks>
    /// Call once around the whole operation, including asynchronous initialization and cleanup.
    /// Synchronous success stays inline. Other completions are consumed once through AsTask and the
    /// returned Task also guards the interval after internal state changes but before publication.
    /// </remarks>
    protected ValueTask<bool> PublishMove(ValueTask<bool> operation)
    {
        if (operation.IsCompletedSuccessfully)
        {
            var result = operation.GetAwaiter().GetResult();
            Volatile.Write(ref _busy, 0);
            return new(result);
        }

        return PublishPendingMove(operation);
    }

    /// <summary>Publishes an operation backed by the supplied generation completion.</summary>
    /// <remarks>The operation must belong to this completion; do not use it for an unrelated ValueTask.</remarks>
    protected ValueTask<bool> PublishMove(ValueTask<bool> operation, MoveCompletion completion)
    {
        Volatile.Write(ref _pendingMove, completion);
        Volatile.Write(ref _busy, 0);
        return operation;
    }

    private ValueTask<bool> PublishPendingMove(ValueTask<bool> operation)
    {
        try
        {
            var pending = operation.AsTask();
            Volatile.Write(ref _pendingMove, pending);
            return new(pending);
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }

    /// <summary>Disposes acquired cursor and owned sessions in reverse acquisition order.</summary>
    /// <remarks>
    /// This method runs at most once, and only after TryStart. Implementations must continue their
    /// reverse cleanup after an individual failure and propagate the final cleanup failure.
    /// Never resolve new dependencies here or dispose borrowed handlers, providers, or sinks.
    /// </remarks>
    protected abstract ValueTask DisposeCoreAsync();

    /// <summary>Closes this owner; repeated pending disposal shares one Task-backed completion.</summary>
    public ValueTask DisposeAsync()
    {
        if (HasPendingMove)
            throw new InvalidOperationException("Await the pending Move before disposing the cursor.");

        int busy;
        while (true)
        {
            busy = Interlocked.CompareExchange(ref _busy, 2, 0);
            if (busy != -1)
                break;
            if (Interlocked.CompareExchange(ref _busy, 2, -1) == -1)
            {
                busy = 0;
                break;
            }
            // A lost cold claim must reacquire the ordinary gate, never release the binding winner.
        }

        if (busy == -2)
            throw new InvalidOperationException("Await the first enumeration binding before disposing the cursor.");
        if (busy == 1)
            throw new InvalidOperationException("Await the pending Move before disposing the cursor.");
        if (busy == 2)
        {
            if (HasPendingMove)
                throw new InvalidOperationException("Await the pending Move before disposing the cursor.");
            return new(GetDisposeCompletion().Task);
        }

        if (HasPendingMove)
        {
            Volatile.Write(ref _busy, 0);
            throw new InvalidOperationException("Await the pending Move before disposing the cursor.");
        }

        _hasCurrent = false;
        if (_closed)
        {
            Volatile.Write(ref _busy, 0);
            var completed = Volatile.Read(ref _disposeCompletion);
            return completed is null ? default : new(completed.Task);
        }

        var cleanupTransferred = false;
        try
        {
            try
            {
                var cleanup = BeginClose();
                if (!cleanup.IsCompleted)
                {
                    var completion = GetDisposeCompletion();
                    cleanupTransferred = true;
                    _ = CompleteDisposeAsync(cleanup);
                    return new(completion.Task);
                }

                cleanup.GetAwaiter().GetResult();
            }
            finally
            {
                if (!cleanupTransferred)
                    DisposeLinkedToken();
            }

            FinishClose(null, disposing: true);
            var shared = Volatile.Read(ref _disposeCompletion);
            return shared is null ? default : new(shared.Task);
        }
        catch (Exception error)
        {
            FinishClose(error, disposing: true);
            var shared = Volatile.Read(ref _disposeCompletion);
            return shared is null ? ThrowDisposeAsync(error) : new(shared.Task);
        }
    }

    private ValueTask<bool> CloseMove(Exception? error)
    {
        var cleanupTransferred = false;
        try
        {
            try
            {
                var cleanup = BeginClose();
                if (!cleanup.IsCompleted)
                {
                    cleanupTransferred = true;
                    return CompleteMoveAsync(cleanup, error);
                }

                cleanup.GetAwaiter().GetResult();
            }
            finally
            {
                if (!cleanupTransferred)
                    DisposeLinkedToken();
            }
        }
        catch (Exception cleanupError)
        {
            error = cleanupError;
        }

        FinishClose(error, disposing: false);
        return error is null ? new(false) : ThrowMoveAsync(error);
    }

    private ValueTask BeginClose()
    {
        var wasClosed = _closed;
        Volatile.Write(ref _closed, true);
        _hasCurrent = false;
        if (wasClosed)
            return default;

        return _started ? DisposeCoreAsync() : default;
    }

    private async ValueTask<bool> CompleteMoveAsync(ValueTask cleanup, Exception? error)
    {
        try
        {
            try
            {
                await cleanup.ConfigureAwait(false);
            }
            finally
            {
                DisposeLinkedToken();
            }
        }
        catch (Exception cleanupError)
        {
            error = cleanupError;
        }

        FinishClose(error, disposing: false);
        if (error is not null)
            ExceptionDispatchInfo.Capture(error).Throw();
        return false;
    }

    private async Task CompleteDisposeAsync(ValueTask cleanup)
    {
        Exception? error = null;
        try
        {
            try
            {
                await cleanup.ConfigureAwait(false);
            }
            finally
            {
                DisposeLinkedToken();
            }
        }
        catch (Exception cleanupError)
        {
            error = cleanupError;
        }

        FinishClose(error, disposing: true);
    }

    private void FinishClose(Exception? error, bool disposing)
    {
        if (disposing)
            _disposeError = error;
        Volatile.Write(ref _cleanupComplete, true);
        if (disposing)
        {
            // Store-load ordering prevents the closer and a Dispose joiner from missing each other.
            Interlocked.Exchange(ref _busy, 0);
        }
        Volatile.Read(ref _disposeCompletion)?.Complete(_disposeError);
    }

    private DisposeCompletion GetDisposeCompletion()
    {
        var completion = Volatile.Read(ref _disposeCompletion);
        if (completion is null)
        {
            var created = new DisposeCompletion();
            completion = Interlocked.CompareExchange(ref _disposeCompletion, created, null) ?? created;
        }

        if (Volatile.Read(ref _cleanupComplete))
            completion.Complete(_disposeError);
        return completion;
    }

    private void DisposeLinkedToken()
    {
        _linked?.Dispose();
        _linked = null;
    }

    private static async ValueTask<bool> ThrowMoveAsync(Exception error)
    {
        await ValueTask.CompletedTask.ConfigureAwait(false);
        ExceptionDispatchInfo.Capture(error).Throw();
        return false;
    }

    private static async ValueTask ThrowDisposeAsync(Exception error)
    {
        await ValueTask.CompletedTask.ConfigureAwait(false);
        ExceptionDispatchInfo.Capture(error).Throw();
    }

    /// <summary>A single-consumption generation whose result survives until its caller consumes it.</summary>
    protected sealed class MoveCompletion : IValueTaskSource<bool>
    {
        private const int Consumed = 1, Retired = 2, Ready = 4;
        private ManualResetValueTaskSourceCore<bool> _core;
        private int _lifecycle = Ready;

        public MoveCompletion() { }

        /// <summary>Claims an unused or fully consumed and retired generation.</summary>
        public bool TryPrepare(out ValueTask<bool> operation)
        {
            if (Interlocked.CompareExchange(ref _lifecycle, 0, Ready) != Ready)
            {
                operation = default;
                return false;
            }

            operation = new(this, _core.Version);
            return true;
        }

        internal bool IsCompleted
        {
            get
            {
                var lifecycle = Volatile.Read(ref _lifecycle);
                if (lifecycle != 0)
                    return true;
                try
                {
                    return _core.GetStatus(_core.Version) != ValueTaskSourceStatus.Pending
                        || Volatile.Read(ref _lifecycle) != 0;
                }
                catch (InvalidOperationException) { return Volatile.Read(ref _lifecycle) != 0; }
            }
        }

        /// <summary>Publishes after owner state and cleanup have settled.</summary>
        public void Complete(bool result, Exception? error)
        {
            try
            {
                if (error is null) _core.SetResult(result);
                else _core.SetException(error);
            }
            finally
            {
                if ((Interlocked.Or(ref _lifecycle, Retired) & Consumed) != 0)
                    Recycle();
            }
        }

        bool IValueTaskSource<bool>.GetResult(short token)
        {
            if (_core.GetStatus(token) == ValueTaskSourceStatus.Pending)
                throw new InvalidOperationException("Await the pending Stream completion before consuming it.");
            try { return _core.GetResult(token); }
            finally
            {
                if ((Interlocked.Or(ref _lifecycle, Consumed) & Retired) != 0)
                    Recycle();
            }
        }

        ValueTaskSourceStatus IValueTaskSource<bool>.GetStatus(short token) => _core.GetStatus(token);

        void IValueTaskSource<bool>.OnCompleted(Action<object?> continuation, object? state, short token,
            ValueTaskSourceOnCompletedFlags flags) => _core.OnCompleted(continuation, state, token, flags);

        private void Recycle()
        {
            _core.Reset();
            Volatile.Write(ref _lifecycle, Ready);
        }
    }

    private sealed class DisposeCompletion
    {
        private readonly TaskCompletionSource<Exception?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal DisposeCompletion() => Task = AwaitCompletionAsync(_completion.Task);

        internal Task Task { get; }

        internal void Complete(Exception? error) => _completion.TrySetResult(error);

        private static async Task AwaitCompletionAsync(Task<Exception?> completion)
        {
            var error = await completion.ConfigureAwait(false);
            if (error is not null)
                ExceptionDispatchInfo.Capture(error).Throw();
        }
    }
}
