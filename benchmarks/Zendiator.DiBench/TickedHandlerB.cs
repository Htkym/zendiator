using Microsoft.Extensions.DependencyInjection;
using Zendiator;
using Zendiator.DependencyInjection;

namespace Zendiator.DiBench;

public sealed class TickedHandlerB : INotificationHandler<Ticked>
{
    public ValueTask HandleAsync(Ticked notification, CancellationToken cancellationToken) => default;
}
