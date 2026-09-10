namespace Zendiator.Benchmarks;

public sealed class IhO1B1 : global::Immediate.Handlers.Shared.Behavior<IhO1.Query, int>
{
    public override async ValueTask<int> HandleAsync(IhO1.Query request, CancellationToken cancellationToken)
    {
        var downstream = await Next(request, cancellationToken).ConfigureAwait(false);
        return downstream + 100;
    }
}
