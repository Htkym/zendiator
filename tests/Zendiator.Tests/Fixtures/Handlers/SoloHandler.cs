using global::Zendiator;

namespace Zendiator.Tests;

public sealed class SoloHandler : IRequestHandler<GetSolo, int>
{
    public ValueTask<int> HandleAsync(GetSolo request, CancellationToken cancellationToken) => new(request.Id * 2);
}
