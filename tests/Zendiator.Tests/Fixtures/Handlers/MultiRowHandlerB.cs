using global::Zendiator;

namespace Zendiator.Tests;

public sealed class MultiRowHandlerB<T> : ISyncRequestHandler<MultiRow<T>, string>
    where T : allows ref struct
{
    public string Handle(scoped MultiRow<T> request, CancellationToken cancellationToken) => $"b:{request.Id}";
}
