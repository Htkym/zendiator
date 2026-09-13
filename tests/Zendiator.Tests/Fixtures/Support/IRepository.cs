using global::Zendiator;

namespace Zendiator.Tests;

public interface IRepository<T>
    where T : class
{
    ValueTask<T> GetAsync(int id, CancellationToken cancellationToken);
}
