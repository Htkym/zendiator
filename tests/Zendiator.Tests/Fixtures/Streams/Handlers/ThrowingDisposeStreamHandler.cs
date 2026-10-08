using global::Zendiator;

namespace Zendiator.Tests;

public sealed class ThrowingDisposeStreamHandler : IStreamRequestHandler<ThrowingDisposeStream, int>
{
    public CancellationToken Token { get; private set; }
    public InvalidOperationException Failure { get; } = new("dispose failed");
    public IAsyncEnumerable<int> HandleAsync(ThrowingDisposeStream request, CancellationToken cancellationToken)
    {
        Token = cancellationToken;
        if (request.FailOnStart) throw Failure;
        return new Enumerator(Failure, request);
    }
    private sealed class Enumerator(Exception failure, ThrowingDisposeStream request) : IAsyncEnumerable<int>, IAsyncEnumerator<int>
    {
        public int Current => 1;
        public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            request.FailOnGetEnumerator ? throw failure : this;
        public ValueTask<bool> MoveNextAsync() => request.MoveNextCallback is null ? new(true) : request.MoveNextCallback();
        public ValueTask DisposeAsync() => request.DisposeCallback is null ? ValueTask.FromException(failure) : request.DisposeCallback();
    }
}
