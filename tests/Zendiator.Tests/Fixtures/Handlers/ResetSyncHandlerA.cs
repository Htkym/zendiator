using global::Zendiator;

namespace Zendiator.Tests;

public sealed class ResetSyncHandlerA : ISyncRequestHandler<ResetSync>
{
    public static List<string> Got { get; } = [];
    public void Handle(ResetSync request, CancellationToken cancellationToken) => Got.Add($"a:{request.Name}");
}
