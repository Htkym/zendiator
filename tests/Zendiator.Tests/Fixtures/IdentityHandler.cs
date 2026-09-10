using global::Zendiator;

namespace Zendiator.Tests;

public sealed class IdentityHandler : IRequestHandler<Identity, Guid>, IAsyncDisposable
{
    public Guid Id { get; } = Guid.NewGuid();
    public bool Disposed { get; private set; }
    public ValueTask<Guid> HandleAsync(Identity request, CancellationToken cancellationToken) => new(Id);
    public ValueTask DisposeAsync() { Disposed = true; return default; }
}
