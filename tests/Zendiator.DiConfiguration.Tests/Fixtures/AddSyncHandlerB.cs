using Zendiator;
using Zendiator.DependencyInjection;

namespace Zendiator.DiConfiguration.Tests;

public sealed class AddSyncHandlerB : ISyncRequestHandler<AddSync, int>
{
    public int Handle(AddSync request, CancellationToken cancellationToken) => request.Value + 2;
}
