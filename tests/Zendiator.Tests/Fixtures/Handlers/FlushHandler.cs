using global::Zendiator;

namespace Zendiator.Tests;

public sealed class FlushHandler : ISyncRequestHandler<FlushRequest>
{
    public static List<int> Flushed { get; } = [];
    public void Handle(FlushRequest request, CancellationToken cancellationToken) => Flushed.Add(request.Id);
}
