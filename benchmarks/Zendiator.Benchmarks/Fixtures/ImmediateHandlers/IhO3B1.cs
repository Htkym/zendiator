namespace Zendiator.Benchmarks;

public sealed class IhO3B1 : global::Immediate.Handlers.Shared.Behavior<IhO3.Query, int>
{
    public override async ValueTask<int> HandleAsync(IhO3.Query request, CancellationToken cancellationToken)
    {
        var downstream = await Next(request, cancellationToken).ConfigureAwait(false);
        return downstream + 100;
    }
}
