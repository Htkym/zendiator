using global::Zendiator;

namespace Zendiator.Tests;

public sealed class FirstTraceHandler : IRequestHandler<GetTrace, string>
{
    public ValueTask<string> HandleAsync(GetTrace request, CancellationToken cancellationToken)
    {
        request.Log.Entries.Add("first");
        return new("r1");
    }
}
