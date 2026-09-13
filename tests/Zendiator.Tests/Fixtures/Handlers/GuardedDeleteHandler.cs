using global::Zendiator;

namespace Zendiator.Tests;

public sealed class GuardedDeleteHandler : IRequestHandler<GuardedDelete>
{
    public static int Calls;
    public ValueTask HandleAsync(GuardedDelete request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        return default;
    }
}
