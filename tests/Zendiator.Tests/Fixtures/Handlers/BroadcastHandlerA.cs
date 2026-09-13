using global::Zendiator;

namespace Zendiator.Tests;

public sealed class BroadcastHandlerA : IRequestHandler<Broadcast>
{
    public static List<string> Got { get; } = [];
    public ValueTask HandleAsync(Broadcast request, CancellationToken cancellationToken)
    {
        Got.Add($"a:{request.Message}");
        return default;
    }
}
