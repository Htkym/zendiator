using global::Zendiator;

namespace Zendiator.Tests;

public sealed class SumHandler(Trace trace) : IQueryHandler<Sum, int>
{
    public ValueTask<int> HandleAsync(Sum request, CancellationToken cancellationToken)
    { trace.Add("handler"); return new(request.Left + request.Right); }
}
