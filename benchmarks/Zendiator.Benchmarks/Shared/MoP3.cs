namespace Zendiator.Benchmarks;

public sealed record MoP3(int Value) : global::Mediator.IRequest<int>, IMoMarker1, IMoMarker2, IMoMarker3;
