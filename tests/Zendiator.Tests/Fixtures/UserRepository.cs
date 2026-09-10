using global::Zendiator;

namespace Zendiator.Tests;

public sealed class UserRepository : IRepository<User>
{
    public static int Calls;
    public ValueTask<User> GetAsync(int id, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        return new(new User(id, $"user-{id}"));
    }
}
