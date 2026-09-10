namespace Zendiator.Benchmarks;

[global::Immediate.Handlers.Shared.Handler]
[global::Immediate.Handlers.Shared.Behaviors(typeof(IhB1<,>), typeof(IhB2<,>), typeof(IhB3<,>))]
public static partial class IhP3
{
    public record Query(int Value);

    private static ValueTask<int> HandleAsync(Query request, CancellationToken cancellationToken) => new(request.Value + 1);
}
