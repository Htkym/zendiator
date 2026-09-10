using global::Zendiator;

namespace Zendiator.Tests;

public sealed class SecondTraceHandler : IRequestHandler<GetTrace, string>
{
    public ValueTask<string> HandleAsync(GetTrace request, CancellationToken cancellationToken)
    {
        request.Log.Entries.Add("second");
        return new("r2");
    }
}
