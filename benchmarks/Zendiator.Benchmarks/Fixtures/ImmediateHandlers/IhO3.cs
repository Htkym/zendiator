namespace Zendiator.Benchmarks;

[global::Immediate.Handlers.Shared.Handler]
[global::Immediate.Handlers.Shared.Behaviors(typeof(IhO3B1), typeof(IhO3B2), typeof(IhO3B3))]
public static partial class IhO3
{
    public record Query(int Value);

    private static ValueTask<int> HandleAsync(Query request, CancellationToken cancellationToken) => new(request.Value);
}
