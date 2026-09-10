using Microsoft.Extensions.Logging;
using Zendiator.Sample.Contracts;

namespace Zendiator.Sample.Application;

/// <summary>Quotes from vendor B.</summary>
[HandlerOrder(Order = 1)]
public sealed class VendorBQuotesHandler : IRequestHandler<GetHouseholdQuotes, HouseholdQuote>
{
    public ValueTask<HouseholdQuote> HandleAsync(GetHouseholdQuotes request, CancellationToken cancellationToken) =>
        new(new HouseholdQuote("B", 200m));
}
