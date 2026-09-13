namespace Zendiator.Benchmarks;

public sealed class IhO5B5 : global::Immediate.Handlers.Shared.Behavior<IhO5.Query, int>
{
    public override async ValueTask<int> HandleAsync(IhO5.Query request, CancellationToken cancellationToken)
    {
        var downstream = await Next(request, cancellationToken).ConfigureAwait(false);
        return downstream + 100;
    }
}
