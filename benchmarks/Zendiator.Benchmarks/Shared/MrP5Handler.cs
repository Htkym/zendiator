namespace Zendiator.Benchmarks;

public sealed class MrP5Handler : global::MediatR.IRequestHandler<MrP5, int>
{
    public Task<int> Handle(MrP5 request, CancellationToken cancellationToken) => Task.FromResult(request.Value + 1);
}
