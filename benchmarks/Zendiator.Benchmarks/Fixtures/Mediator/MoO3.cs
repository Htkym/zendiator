namespace Zendiator.Benchmarks;

public sealed record MoO3(int Value) : global::Mediator.IRequest<int>, IMoOb1, IMoOb2, IMoOb3;
