namespace Zendiator.Benchmarks;

[global::Immediate.Handlers.Shared.Handler]
[global::Immediate.Handlers.Shared.Behaviors(typeof(IhO5B1), typeof(IhO5B2), typeof(IhO5B3), typeof(IhO5B4), typeof(IhO5B5))]
public static partial class IhO5
{
    public record Query(int Value);

    private static ValueTask<int> HandleAsync(Query request, CancellationToken cancellationToken) => new(request.Value);
}
