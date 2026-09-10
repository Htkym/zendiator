using Zendiator;

namespace Zendiator.Benchmarks;

public sealed class Zp1Handler : IRequestHandler<Zp1, int>
{
    public ValueTask<int> HandleAsync(Zp1 request, CancellationToken cancellationToken) => new(request.Value + 1);
}
