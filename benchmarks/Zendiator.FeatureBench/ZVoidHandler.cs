using Zendiator;

namespace Zendiator.FeatureBench;

public sealed class ZVoidHandler : IRequestHandler<ZVoid>
{
    public ValueTask HandleAsync(ZVoid request, CancellationToken cancellationToken) => default;
}
