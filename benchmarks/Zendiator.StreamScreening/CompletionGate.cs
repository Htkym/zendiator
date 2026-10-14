using System.Runtime.ExceptionServices;
using System.Threading.Tasks.Sources;

namespace Zendiator.StreamScreening;

// The worker completes only after the await continuation has been registered.
// Its final action is idle.Set(); no cleanup or allocation follows that signal.
public class CompletionGate<T> : IValueTaskSource<T>, IDisposable
{
    private ManualResetValueTaskSourceCore<T> _core = new() { RunContinuationsAsynchronously = true };
    private readonly AutoResetEvent _work = new(false);
    private readonly ManualResetEventSlim _idle = new(true);
    private readonly Thread? _worker;
    private readonly bool _pending;
    private bool _stop;
    private T _value = default!;
    private ExceptionDispatchInfo? _failure;

    public CompletionGate(bool pending)
    {
        _pending = pending;
        if (pending)
        {
            _worker = new Thread(Work) { IsBackground = true, Name = "Zendiator screening completion" };
            _worker.Start();
        }
    }

    public ValueTask<T> Begin(T value)
    {
        WaitIdle();
        _core.Reset();
        _value = value;
        if (_pending) _idle.Reset();
        else _core.SetResult(value);
        return new(this, _core.Version);
    }

    public virtual T GetResult(short token) => _core.GetResult(token);
    public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);
    public virtual void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
    {
        _core.OnCompleted(continuation, state, token, flags);
        if (_pending) _work.Set();
    }

    private void Work()
    {
        while (true)
        {
            _work.WaitOne();
            if (_stop) return;
            try { _core.SetResult(_value); }
            catch (Exception error) { _failure = ExceptionDispatchInfo.Capture(error); }
            finally { _idle.Set(); }
        }
    }

    public void WaitIdle()
    {
        _idle.Wait();
        _failure?.Throw();
    }

    public void Dispose()
    {
        _idle.Wait();
        _stop = true;
        _work.Set();
        _worker?.Join();
        _work.Dispose();
        _idle.Dispose();
        _failure?.Throw(); // Join/cleanup also complete when the worker failed.
    }
}

// Used only by the separately compiled preflight callback copy.
public sealed class CountedCompletionGate<T>(bool pending) : CompletionGate<T>(pending)
{
    public int Consumptions { get; private set; }
    public int Registrations { get; private set; }
    public override T GetResult(short token) { Consumptions++; return base.GetResult(token); }
    public override void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
    { Registrations++; base.OnCompleted(continuation, state, token, flags); }
}
