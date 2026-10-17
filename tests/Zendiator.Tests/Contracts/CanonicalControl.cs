using System.Runtime.ExceptionServices;

namespace Zendiator.Tests.Contracts;

internal readonly record struct ContractExit(bool Success, Exception? Error);
internal sealed record ContractStage<R, T>(string Id, Func<ContractCallbacks<R, T>> Resolve);
internal sealed class ContractCallbacks<R, T>
{
    public Func<R, CancellationToken, ValueTask<R>> Before = (r, _) => new(r);
    public Func<R, T, CancellationToken, ValueTask<T>> After = (_, value, _) => new(value);
    public Func<R, Exception, CancellationToken, ValueTask> Observe = (_, _, _) => default;
    public Func<R, ContractExit, CancellationToken, ValueTask> Finally = (_, _, _) => default;
}
internal sealed record ContractSyncStage<R, T>(string Id, Func<ContractSyncCallbacks<R, T>> Resolve);
internal sealed class ContractSyncCallbacks<R, T>
{
    public Func<R, R> Before = r => r;
    public Func<R, T, T> After = (_, value) => value;
    public Action<R, Exception> Observe = (_, _) => { };
    public Action<R, ContractExit> Finally = (_, _) => { };
}

// Executable test oracle for the selected contract, never a production runtime fusion engine.
// State belongs to this invocation; callback user async boundaries retain normal .NET semantics.
internal static class CanonicalControl
{
    public static T SendSync<R, T>(R request, ContractSyncStage<R, T>[] stages, Func<R, T> handler,
        ContractEvents events, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        events.Add("send", "enter", request, token);
        try { var result = Invoke(0, request); events.Add("send", "return", request, token); return result; }
        catch (Exception error) { events.Add("send", "throw", request, token, error); throw; }
        T Invoke(int index, R input)
        {
            if (index == stages.Length)
            {
                events.Add("handler", "guard", input, token); token.ThrowIfCancellationRequested();
                events.Add("handler", "enter", input, token);
                var result = handler(input); events.Add("handler", "return", input, token); return result;
            }
            var stage = stages[index]; events.Add(stage.Id, "guard", input, token); token.ThrowIfCancellationRequested();
            events.Add(stage.Id, "resolve", input, token); var callbacks = stage.Resolve(); var saved = input;
            events.Add(stage.Id, "enter", input, token); ContractExit exit = default;
            try
            {
                saved = callbacks.Before(saved); var result = Invoke(index + 1, saved);
                result = callbacks.After(saved, result); exit = new(true, null); return result;
            }
            catch (Exception error)
            {
                exit = new(false, error); events.Add(stage.Id, "observe", saved, token, error);
                try { callbacks.Observe(saved, error); }
                catch (Exception replacement) { exit = new(false, replacement); throw; }
                throw;
            }
            finally { events.Add(stage.Id, "finally", saved, token, exit.Error, exit.Success.ToString()); callbacks.Finally(saved, exit); }
        }
    }

    public static void SendVoid<R>(R request, Action<R> handler)
    { ArgumentNullException.ThrowIfNull(request); handler(request); }
    public static ValueTask SendVoidAsync<R>(R request, Func<R, ValueTask> handler)
    { ArgumentNullException.ThrowIfNull(request); return VoidCore(); async ValueTask VoidCore() => await handler(request).ConfigureAwait(false); }
    public static ValueTask<T> Send<R, T>(R request, ContractStage<R, T>[] stages,
        Func<R, CancellationToken, ValueTask<T>> handler, ContractEvents events, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Core(request, stages, handler, events, token);
    }
    private static async ValueTask<T> Core<R, T>(R request, ContractStage<R, T>[] stages,
        Func<R, CancellationToken, ValueTask<T>> handler, ContractEvents events, CancellationToken token)
    {
        var callbacks = new ContractCallbacks<R, T>[stages.Length]; var saved = new R[stages.Length];
        var original = request;
        var entered = 0; T response = default!; Exception? failure = null;
        events.Add("send", "enter", request, token);
        try
        {
            for (var i = 0; i < stages.Length; i++)
            {
                events.Add(stages[i].Id, "guard", request, token); token.ThrowIfCancellationRequested();
                events.Add(stages[i].Id, "resolve", request, token);
                callbacks[i] = stages[i].Resolve(); saved[i] = request; entered++;
                events.Add(stages[i].Id, "enter", request, token);
                request = await callbacks[i].Before(request, token).ConfigureAwait(false); saved[i] = request;
            }
            events.Add("handler", "guard", request, token); token.ThrowIfCancellationRequested();
            events.Add("handler", "enter", request, token);
            response = await handler(request, token).ConfigureAwait(false);
            events.Add("handler", "return", request, token);
        }
        catch (Exception error) { failure = error; }
        for (var i = entered - 1; i >= 0; i--)
        {
            if (failure is null)
            {
                try { response = await callbacks[i].After(saved[i], response, token).ConfigureAwait(false); }
                catch (Exception error) { failure = error; }
            }
            if (failure is not null)
            {
                events.Add(stages[i].Id, "observe", saved[i], token, failure);
                try { await callbacks[i].Observe(saved[i], failure, token).ConfigureAwait(false); }
                catch (Exception error) { failure = error; }
            }
            var exit = new ContractExit(failure is null, failure);
            events.Add(stages[i].Id, "finally", saved[i], token, failure, exit.Success.ToString());
            try { await callbacks[i].Finally(saved[i], exit, token).ConfigureAwait(false); }
            catch (Exception error) { failure = error; }
        }
        events.Add("send", failure is null ? "return" : "throw", original, token, failure);
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        return response;
    }

    public static void Notify<R>(R request, Action<R>[] handlers)
    { foreach (var handler in handlers) handler(request); }
    public static async ValueTask NotifyAsync<R>(R request, Func<R, ValueTask>[] handlers)
    { foreach (var handler in handlers) await handler(request).ConfigureAwait(false); }

    public static void SendAllSync<R, T>(R original, Func<R, T>[] branches, Span<T> destination)
    {
        if (destination.Length < branches.Length) throw new ArgumentException("Destination is too short.", nameof(destination));
        var results = new T[branches.Length];
        for (var i = 0; i < branches.Length; i++) results[i] = branches[i](original);
        results.AsSpan().CopyTo(destination);
    }

    public static IAsyncEnumerable<T> Stream<R, T>(R request, Func<R, CancellationToken, IAsyncEnumerable<T>> start,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(start);
        return new Replay<R, T>(request, start, token);
    }
    private sealed class Replay<R, T>(R request, Func<R, CancellationToken, IAsyncEnumerable<T>> start, CancellationToken api)
        : IAsyncEnumerable<T>
    {
        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken token = default) => new Cursor<R, T>(request, start, api, token);
    }
    private sealed class Cursor<R, T>(R request, Func<R, CancellationToken, IAsyncEnumerable<T>> start,
        CancellationToken api, CancellationToken enumeration) : IAsyncEnumerator<T>
    {
        private IAsyncEnumerator<T>? _inner;
        private CancellationTokenSource? _linked;
        private bool _ended, _disposed, _hasCurrent;
        private int _busy;
        private T _current = default!;
        public T Current => _hasCurrent ? _current : throw new InvalidOperationException("Current is unavailable.");
        private void Enter()
        { if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) throw new InvalidOperationException("Await the preceding operation."); }
        public ValueTask<bool> MoveNextAsync()
        {
            Enter(); _hasCurrent = false;
            if (_ended || _disposed) { Volatile.Write(ref _busy, 0); return new(false); }
            return Move();
        }
        private async ValueTask<bool> Move()
        {
            try
            {
                if (_inner is null)
                {
                    var token = !api.CanBeCanceled ? enumeration : !enumeration.CanBeCanceled || api == enumeration ? api
                        : (_linked = CancellationTokenSource.CreateLinkedTokenSource(api, enumeration)).Token;
                    token.ThrowIfCancellationRequested();
                    _inner = start(request, token).GetAsyncEnumerator(token);
                }
                var moved = await _inner.MoveNextAsync().ConfigureAwait(false);
                if (moved) { _current = _inner.Current; _hasCurrent = true; }
                else _ended = true;
                return moved;
            }
            catch { _ended = true; throw; }
            finally { Volatile.Write(ref _busy, 0); }
        }
        public ValueTask DisposeAsync()
        {
            Enter();
            if (_disposed) { Volatile.Write(ref _busy, 0); return default; }
            _disposed = _ended = true; _hasCurrent = false;
            var inner = _inner; var linked = _linked; _inner = null; _linked = null;
            return Cleanup(inner, linked);
        }
        private async ValueTask Cleanup(IAsyncEnumerator<T>? inner, CancellationTokenSource? linked)
        {
            try { if (inner is not null) await inner.DisposeAsync().ConfigureAwait(false); }
            finally { linked?.Dispose(); Volatile.Write(ref _busy, 0); }
        }
    }
}
