namespace Zendiator.Benchmarks;

public sealed class DrO3B3 : global::DispatchR.Abstractions.Send.IPipelineBehavior<DrO3, ValueTask<int>>
{
    public required global::DispatchR.Abstractions.Send.IRequestHandler<DrO3, ValueTask<int>> NextPipeline { get; set; }

    public async ValueTask<int> Handle(DrO3 request, CancellationToken cancellationToken)
    {
        var downstream = await NextPipeline.Handle(request, cancellationToken).ConfigureAwait(false);
        return downstream + 100;
    }
}
