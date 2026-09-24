using System.Threading.Tasks.Sources;

namespace Zendiator.Tests;

public sealed class NotificationCompletion : IValueTaskSource
{
    private ManualResetValueTaskSourceCore<bool> _source = new() { RunContinuationsAsynchronously = true };
    public short Version => _source.Version;
    public int Consumptions { get; private set; }
    public void Complete(Exception? error)
    {
        if (error is null) _source.SetResult(true);
        else _source.SetException(error);
    }
    public void GetResult(short token)
    {
        Consumptions++;
        _source.GetResult(token);
    }
    public ValueTaskSourceStatus GetStatus(short token) => _source.GetStatus(token);
    public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
        => _source.OnCompleted(continuation, state, token, flags);
}
