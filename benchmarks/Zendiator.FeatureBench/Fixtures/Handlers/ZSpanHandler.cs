using Zendiator;

namespace Zendiator.FeatureBench;

public sealed class ZSpanHandler : ISyncRequestHandler<ZSpan, int>
{
    public int Handle(scoped ZSpan request, CancellationToken cancellationToken) => request.Data.Length;
}
