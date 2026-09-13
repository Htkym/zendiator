using Microsoft.Extensions.Logging;
using Zendiator.Sample.Contracts;

namespace Zendiator.Sample.Application;

/// <summary>Counts households for any marker type.</summary>
public sealed class GetHouseholdCountHandler<TMarker> : IRequestHandler<GetHouseholdCount<TMarker>, int>
{
    public ValueTask<int> HandleAsync(GetHouseholdCount<TMarker> request, CancellationToken cancellationToken) => new(42);
}
