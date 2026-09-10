using global::Zendiator;

namespace Zendiator.Tests;

public sealed class Gate
{
    public TaskCompletionSource FirstEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public List<string> Events { get; } = [];
}
