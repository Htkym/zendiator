using Di.Generated;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.DiConfiguration.Tests;

public sealed class ConstructHandler<T> : IRequestHandler<Construct<T>, T> where T : new()
{
    public ValueTask<T> HandleAsync(Construct<T> request, CancellationToken cancellationToken) => new(new T());
}
