using global::Zendiator;

namespace Zendiator.Tests;

[HandlerOrder(Order = 1)]
public sealed class VendorBQuotesHandler : IRequestHandler<GetQuotes, Quote>
{
    public static int Calls;
    public ValueTask<Quote> HandleAsync(GetQuotes request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        return new(new Quote("B", 200m));
    }
}
