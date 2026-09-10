using global::Zendiator;

namespace Zendiator.Tests;

public sealed class GetByIdHandler<T>(IRepository<T> repository) : IRequestHandler<GetById<T>, T>
    where T : class
{
    public Guid Id { get; } = Guid.NewGuid();
    public ValueTask<T> HandleAsync(GetById<T> request, CancellationToken cancellationToken) =>
        repository.GetAsync(request.Id, cancellationToken);
}
