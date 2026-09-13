using global::Zendiator;

namespace Zendiator.Tests;

public sealed class BroadcastHandlerB : IRequestHandler<Broadcast>
{
    public static List<string> Got { get; } = [];
    public ValueTask HandleAsync(Broadcast request, CancellationToken cancellationToken)
    {
        Got.Add($"b:{request.Message}");
        return default;
    }
}
