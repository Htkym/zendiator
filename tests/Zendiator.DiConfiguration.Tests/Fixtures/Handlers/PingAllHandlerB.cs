using Zendiator;
using Zendiator.DependencyInjection;

namespace Zendiator.DiConfiguration.Tests;

public sealed class PingAllHandlerB : IRequestHandler<PingAll>
{
    public static List<string> Got { get; } = [];
    public ValueTask HandleAsync(PingAll request, CancellationToken cancellationToken)
    {
        Got.Add("b:" + request.Message);
        return default;
    }
}
