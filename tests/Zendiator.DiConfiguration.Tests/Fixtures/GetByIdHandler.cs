using Zendiator;
using Zendiator.DependencyInjection;

namespace Zendiator.DiConfiguration.Tests;

public sealed class GetByIdHandler<T> : IRequestHandler<GetById<T>, T>
    where T : class
{
    public ValueTask<T> HandleAsync(GetById<T> request, CancellationToken cancellationToken) =>
        new((T)(object)new Item(request.Id));
}
