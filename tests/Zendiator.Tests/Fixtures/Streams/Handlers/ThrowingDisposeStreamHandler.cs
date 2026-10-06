using global::Zendiator;

namespace Zendiator.Tests;

public sealed class ThrowingDisposeStreamHandler : IStreamRequestHandler<ThrowingDisposeStream, int>
{
    public CancellationToken Token { get; private set; }
    public InvalidOperationException Failure { get; } = new("dispose failed");
    public IAsyncEnumerable<int> HandleAsync(ThrowingDisposeStream request, CancellationToken cancellationToken)
    {
        Token = cancellationToken;
        return new Enumerator(Failure);
    }
    private sealed class Enumerator(Exception failure) : IAsyncEnumerable<int>, IAsyncEnumerator<int>
    {
        public int Current => 1;
        public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken = default) => this;
        public ValueTask<bool> MoveNextAsync() => new(true);
        public ValueTask DisposeAsync() => ValueTask.FromException(failure);
    }
}
