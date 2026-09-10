namespace Zendiator.Benchmarks;

public sealed class DrO5B4 : global::DispatchR.Abstractions.Send.IPipelineBehavior<DrO5, ValueTask<int>>
{
    public required global::DispatchR.Abstractions.Send.IRequestHandler<DrO5, ValueTask<int>> NextPipeline { get; set; }

    public async ValueTask<int> Handle(DrO5 request, CancellationToken cancellationToken)
    {
        var downstream = await NextPipeline.Handle(request, cancellationToken).ConfigureAwait(false);
        return downstream + 100;
    }
}
