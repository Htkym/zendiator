using global::Zendiator;

namespace Zendiator.Tests;

public sealed class TopUpHandler : IRequestHandler<TopUp>
{
    public static int Seen = -1;
    public ValueTask HandleAsync(TopUp request, CancellationToken cancellationToken)
    {
        Seen = request.UserId;
        return default;
    }
}
