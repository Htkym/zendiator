namespace Zendiator.Benchmarks;

public sealed class DrP5B2 : global::DispatchR.Abstractions.Send.IPipelineBehavior<DrP5, ValueTask<int>>
{
    public required global::DispatchR.Abstractions.Send.IRequestHandler<DrP5, ValueTask<int>> NextPipeline { get; set; }

    public ValueTask<int> Handle(DrP5 request, CancellationToken cancellationToken)
    {
        DrCounters.B6++;
        return NextPipeline.Handle(request, cancellationToken);
    }
}
