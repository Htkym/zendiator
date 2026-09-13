using Zendiator;

namespace Zendiator.FeatureBench;

public sealed class ZMultiHandlerB : IRequestHandler<ZMulti, int>
{
    public ValueTask<int> HandleAsync(ZMulti request, CancellationToken cancellationToken) => new(request.Value + 2);
}
