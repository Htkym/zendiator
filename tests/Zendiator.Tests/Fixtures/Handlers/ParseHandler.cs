using global::Zendiator;

namespace Zendiator.Tests;

public sealed class ParseHandler : ISyncRequestHandler<ParseRequest, int>
{
    public static int Calls;
    public int Handle(scoped ParseRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        return request.Data.Length;
    }
}
