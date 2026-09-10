namespace Zendiator.Benchmarks;

[global::Immediate.Handlers.Shared.Handler]
public static partial class IhP0
{
    public record Query(int Value);

    private static ValueTask<int> HandleAsync(Query request, CancellationToken cancellationToken) => new(request.Value + 1);
}
