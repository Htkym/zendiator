using Zendiator;
using Zendiator.DependencyInjection;

namespace Zendiator.DiConfiguration.Tests;

public sealed class AddSyncHandlerA : ISyncRequestHandler<AddSync, int>
{
    public int Handle(AddSync request, CancellationToken cancellationToken) => request.Value + 1;
}
