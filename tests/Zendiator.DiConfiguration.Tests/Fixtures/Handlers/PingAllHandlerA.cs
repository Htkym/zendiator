using Zendiator;
using Zendiator.DependencyInjection;

namespace Zendiator.DiConfiguration.Tests;

public sealed class PingAllHandlerA : IRequestHandler<PingAll>
{
    public static List<string> Got { get; } = [];
    public ValueTask HandleAsync(PingAll request, CancellationToken cancellationToken)
    {
        Got.Add("a:" + request.Message);
        return default;
    }
}
