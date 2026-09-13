using Zendiator;
using Zendiator.DependencyInjection;

namespace Zendiator.DiConfiguration.Tests;

public sealed class WipeHandler : ISyncRequestHandler<Wipe>
{
    public static List<int> Got { get; } = [];
    public void Handle(Wipe request, CancellationToken cancellationToken) => Got.Add(request.Id);
}
