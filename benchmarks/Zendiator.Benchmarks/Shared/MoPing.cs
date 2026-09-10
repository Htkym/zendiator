namespace Zendiator.Benchmarks;

public sealed record MoPing(int Value) : global::Mediator.IRequest<int>;
