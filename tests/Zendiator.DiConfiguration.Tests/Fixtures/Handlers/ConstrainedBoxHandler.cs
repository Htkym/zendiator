using Di.Generated;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.DiConfiguration.Tests;

public sealed class ConstrainedBoxHandler<T> : ISyncRequestHandler<ConstrainedBox<T>, int> where T : struct, allows ref struct
{
    public int Handle(scoped ConstrainedBox<T> request, CancellationToken cancellationToken) => 17;
}
