using Di.Generated;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.DiConfiguration.Tests;

public sealed class MixedHandler : IRequestHandler<MixedFirst, int>, IRequestHandler<MixedSecond, int>,
    ISyncRequestHandler<MixedSyncFirst, int>, ISyncRequestHandler<MixedSyncSecond, int>,
    INotificationHandler<MixedNoteFirst>, INotificationHandler<MixedNoteSecond>,
    IStreamRequestHandler<MixedStreamFirst, int>, IStreamRequestHandler<MixedStreamSecond, int>
{
    public int NotificationResult { get; private set; }
    public ValueTask<int> HandleAsync(MixedFirst request, CancellationToken cancellationToken) => new(1);
    ValueTask<int> IRequestHandler<MixedSecond, int>.HandleAsync(MixedSecond request, CancellationToken cancellationToken) => new(7);
    public ValueTask<int> HandleAsync(MixedSecond request, CancellationToken cancellationToken) => new(99);
    public int Handle(MixedSyncFirst request, CancellationToken cancellationToken) => 1;
    int ISyncRequestHandler<MixedSyncSecond, int>.Handle(MixedSyncSecond request, CancellationToken cancellationToken) => 7;
    public int Handle(MixedSyncSecond request, CancellationToken cancellationToken) => 99;
    public ValueTask HandleAsync(MixedNoteFirst notification, CancellationToken cancellationToken) => default;
    ValueTask INotificationHandler<MixedNoteSecond>.HandleAsync(MixedNoteSecond notification, CancellationToken cancellationToken)
    {
        NotificationResult = 7;
        return default;
    }
    public ValueTask HandleAsync(MixedNoteSecond notification, CancellationToken cancellationToken)
    {
        NotificationResult = 99;
        return default;
    }
    public IAsyncEnumerable<int> HandleAsync(MixedStreamFirst request, CancellationToken cancellationToken) => Item(1);
    IAsyncEnumerable<int> IStreamRequestHandler<MixedStreamSecond, int>.HandleAsync(MixedStreamSecond request, CancellationToken cancellationToken) => Item(7);
    public IAsyncEnumerable<int> HandleAsync(MixedStreamSecond request, CancellationToken cancellationToken) => Item(99);
    private static async IAsyncEnumerable<int> Item(int value)
    {
        await Task.CompletedTask;
        yield return value;
    }
}
