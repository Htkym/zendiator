using global::Zendiator;

namespace Zendiator.Tests;

public sealed class MultiRowHandlerA<T> : ISyncRequestHandler<MultiRow<T>, string>
    where T : allows ref struct
{
    public string Handle(scoped MultiRow<T> request, CancellationToken cancellationToken) => $"a:{request.Id}";
}
