namespace Zendiator.Benchmarks;

public sealed record MrAsyncPing(int Value) : global::MediatR.IRequest<int>;
