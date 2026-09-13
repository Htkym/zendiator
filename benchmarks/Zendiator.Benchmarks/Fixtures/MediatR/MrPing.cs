namespace Zendiator.Benchmarks;

public sealed record MrPing(int Value) : global::MediatR.IRequest<int>;
