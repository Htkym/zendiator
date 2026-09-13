using Zendiator;
using Zendiator.DependencyInjection;

namespace Zendiator.DiConfiguration.Tests;

public sealed class ParseHandler : ISyncRequestHandler<Parse, int>
{
    public int Handle(scoped Parse request, CancellationToken cancellationToken) => request.Data.Length;
}
