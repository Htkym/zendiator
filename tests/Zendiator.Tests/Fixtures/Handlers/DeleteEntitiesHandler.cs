using global::Zendiator;

namespace Zendiator.Tests;

public sealed class DeleteEntitiesHandler<T> : IRequestHandler<DeleteEntities<T>>
    where T : class
{
    public static List<string> Deleted { get; } = [];
    public ValueTask HandleAsync(DeleteEntities<T> request, CancellationToken cancellationToken)
    {
        foreach (var id in request.Ids) Deleted.Add($"{typeof(T).Name}:{id}");
        return default;
    }
}
