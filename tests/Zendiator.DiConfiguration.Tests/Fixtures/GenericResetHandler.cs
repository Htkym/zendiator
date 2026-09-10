using Di.Generated;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.DiConfiguration.Tests;

public sealed class GenericResetHandler<T> : ISyncRequestHandler<GenericReset<T>>
{
    public int Calls;
    public void Handle(GenericReset<T> request, CancellationToken cancellationToken) => Calls++;
}
