using global::Zendiator;

namespace Zendiator.Tests;

public sealed class MaybeHandlerA : IRequestHandler<GetMaybe, int>
{
    public static int Calls;
    public ValueTask<int> HandleAsync(GetMaybe request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        return new(1);
    }
}
