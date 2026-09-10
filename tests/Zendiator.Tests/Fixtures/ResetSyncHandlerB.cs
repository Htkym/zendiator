using global::Zendiator;

namespace Zendiator.Tests;

public sealed class ResetSyncHandlerB : ISyncRequestHandler<ResetSync>
{
    public static List<string> Got { get; } = [];
    public void Handle(ResetSync request, CancellationToken cancellationToken) => Got.Add($"b:{request.Name}");
}
