using Microsoft.Extensions.DependencyInjection;
using Zendiator;
using Zendiator.DependencyInjection;

namespace Zendiator.DiBench;

public sealed class SpanSumHandler : ISyncRequestHandler<SpanSum, int>
{
    public int Handle(scoped SpanSum request, CancellationToken cancellationToken) => request.Data.Length;
}
