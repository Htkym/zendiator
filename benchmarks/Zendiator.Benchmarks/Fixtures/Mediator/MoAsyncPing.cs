namespace Zendiator.Benchmarks;

public sealed record MoAsyncPing(int Value) : global::Mediator.IRequest<int>;
