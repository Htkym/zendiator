using Zendiator;

namespace Zendiator.FeatureBench;

public sealed class ZMultiHandlerA : IRequestHandler<ZMulti, int>
{
    public ValueTask<int> HandleAsync(ZMulti request, CancellationToken cancellationToken) => new(request.Value + 1);
}
