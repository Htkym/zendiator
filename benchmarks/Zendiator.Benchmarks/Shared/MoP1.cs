namespace Zendiator.Benchmarks;

public sealed record MoP1(int Value) : global::Mediator.IRequest<int>, IMoMarker1;
