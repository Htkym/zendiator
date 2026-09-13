using Di.Generated;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.DiConfiguration.Tests;

public sealed class GenericResetAllHandler<T> : ISyncRequestHandler<GenericResetAll<T>>
{
    public int Calls;
    public void Handle(GenericResetAll<T> request, CancellationToken cancellationToken) => Calls++;
}
