using global::Zendiator;

namespace Zendiator.Tests;

public sealed class QuoteTaxBehavior : IPipelineBehavior<GetQuotes, Quote>
{
    public async ValueTask<Quote> HandleAsync<TNext>(GetQuotes request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<GetQuotes, Quote>
    {
        var quote = await next.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
        return quote with { Price = quote.Price * 1.1m };
    }
}
