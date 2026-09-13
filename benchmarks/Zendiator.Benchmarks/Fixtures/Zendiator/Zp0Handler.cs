using Zendiator;

namespace Zendiator.Benchmarks;

public sealed class Zp0Handler : IRequestHandler<Zp0, int>
{
    public ValueTask<int> HandleAsync(Zp0 request, CancellationToken cancellationToken) => new(request.Value + 1);
}
