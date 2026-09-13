namespace Zendiator.Benchmarks;

// Closed per-request behaviors (open behaviors are not constrained per request here).
public sealed class DrP1B1 : global::DispatchR.Abstractions.Send.IPipelineBehavior<DrP1, ValueTask<int>>
{
    public required global::DispatchR.Abstractions.Send.IRequestHandler<DrP1, ValueTask<int>> NextPipeline { get; set; }

    public ValueTask<int> Handle(DrP1 request, CancellationToken cancellationToken)
    {
        DrCounters.B1++;
        return NextPipeline.Handle(request, cancellationToken);
    }
}
