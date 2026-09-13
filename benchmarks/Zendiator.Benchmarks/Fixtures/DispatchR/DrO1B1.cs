namespace Zendiator.Benchmarks;

public sealed class DrO1B1 : global::DispatchR.Abstractions.Send.IPipelineBehavior<DrO1, ValueTask<int>>
{
    public required global::DispatchR.Abstractions.Send.IRequestHandler<DrO1, ValueTask<int>> NextPipeline { get; set; }

    public async ValueTask<int> Handle(DrO1 request, CancellationToken cancellationToken)
    {
        var downstream = await NextPipeline.Handle(request, cancellationToken).ConfigureAwait(false);
        return downstream + 100;
    }
}
