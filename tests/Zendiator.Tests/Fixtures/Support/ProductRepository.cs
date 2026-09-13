using global::Zendiator;

namespace Zendiator.Tests;

public sealed class ProductRepository : IRepository<Product>
{
    public static int Calls;
    public ValueTask<Product> GetAsync(int id, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        return new(new Product(id, $"code-{id}"));
    }
}
