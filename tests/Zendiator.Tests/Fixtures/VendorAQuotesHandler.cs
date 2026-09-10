using global::Zendiator;

namespace Zendiator.Tests;

public sealed class VendorAQuotesHandler : IRequestHandler<GetQuotes, Quote>
{
    public static int Calls;
    public ValueTask<Quote> HandleAsync(GetQuotes request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        return new(new Quote("A", 100m));
    }
}
