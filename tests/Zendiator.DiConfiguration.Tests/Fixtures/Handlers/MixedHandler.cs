using Di.Generated;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.DiConfiguration.Tests;

[ZendiatorHandler]
public sealed class MixedHandler : IRequestHandler<MixedFirst, int>, IRequestHandler<MixedSecond, int>,
    ISyncRequestHandler<MixedSyncFirst, int>, ISyncRequestHandler<MixedSyncSecond, int>,
    IStreamSourceHandler<MixedStreamFirst, int, MixedCursor>, IStreamSourceHandler<MixedStreamSecond, int, MixedCursor>
{
    public ValueTask<int> HandleAsync(MixedFirst request, CancellationToken cancellationToken) => new(1);
    ValueTask<int> IRequestHandler<MixedSecond, int>.HandleAsync(MixedSecond request, CancellationToken cancellationToken) => new(7);
    public ValueTask<int> HandleAsync(MixedSecond request, CancellationToken cancellationToken) => new(99);
    public int Handle(MixedSyncFirst request, CancellationToken cancellationToken) => 1;
    int ISyncRequestHandler<MixedSyncSecond, int>.Handle(MixedSyncSecond request, CancellationToken cancellationToken) => 7;
    public int Handle(MixedSyncSecond request, CancellationToken cancellationToken) => 99;
    public ValueTask<MixedCursor> OpenAsync(MixedStreamFirst request, CancellationToken cancellationToken) => new(new MixedCursor(1));
    ValueTask<MixedCursor> IStreamSourceHandler<MixedStreamSecond, int, MixedCursor>.OpenAsync(MixedStreamSecond request, CancellationToken cancellationToken) => new(new MixedCursor(7));
    public ValueTask<MixedCursor> OpenAsync(MixedStreamSecond request, CancellationToken cancellationToken) => new(new MixedCursor(99));
}

public struct MixedCursor(int value) : IAsyncEnumerator<int>
{
    private bool _moved;
    public int Current => value;
    public ValueTask<bool> MoveNextAsync() { var hasValue = !_moved; _moved = true; return new(hasValue); }
    public ValueTask DisposeAsync() => default;
}
