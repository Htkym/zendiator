using Zendiator;

namespace Zendiator.Benchmarks;

public sealed class Zp3Handler : IRequestHandler<Zp3, int>
{
    public ValueTask<int> HandleAsync(Zp3 request, CancellationToken cancellationToken) => new(request.Value + 1);
}
