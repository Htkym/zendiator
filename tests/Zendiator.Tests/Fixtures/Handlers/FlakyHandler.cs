using global::Zendiator;

namespace Zendiator.Tests;

public sealed class FlakyHandler : IRequestHandler<FlakyCommand>
{
    public static int Calls;
    public ValueTask HandleAsync(FlakyCommand request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        return default;
    }
}
