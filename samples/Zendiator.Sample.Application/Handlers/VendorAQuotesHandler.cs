using Microsoft.Extensions.Logging;
using Zendiator.Sample.Contracts;

namespace Zendiator.Sample.Application;

public sealed class VendorAQuotesHandler : IRequestHandler<GetHouseholdQuotes, HouseholdQuote>
{
    public ValueTask<HouseholdQuote> HandleAsync(GetHouseholdQuotes request, CancellationToken cancellationToken) =>
        new(new HouseholdQuote("A", 100m));
}
