using Zendiator;

namespace Zendiator.Benchmarks;

public sealed class Zp5Handler : IRequestHandler<Zp5, int>
{
    public ValueTask<int> HandleAsync(Zp5 request, CancellationToken cancellationToken) => new(request.Value + 1);
}
