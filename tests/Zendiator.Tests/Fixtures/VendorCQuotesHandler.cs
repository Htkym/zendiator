using global::Zendiator;

namespace Zendiator.Tests;

public sealed class VendorCQuotesHandler : IRequestHandler<GetQuotes, Quote>
{
    public static int Calls;
    public ValueTask<Quote> HandleAsync(GetQuotes request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        return new(new Quote("C", 150m));
    }
}
