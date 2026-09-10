namespace Zendiator.Benchmarks;

public sealed class DrP3B2 : global::DispatchR.Abstractions.Send.IPipelineBehavior<DrP3, ValueTask<int>>
{
    public required global::DispatchR.Abstractions.Send.IRequestHandler<DrP3, ValueTask<int>> NextPipeline { get; set; }

    public ValueTask<int> Handle(DrP3 request, CancellationToken cancellationToken)
    {
        DrCounters.B3++;
        return NextPipeline.Handle(request, cancellationToken);
    }
}
