namespace Zendiator.Benchmarks;

public sealed record MrP1(int Value) : global::MediatR.IRequest<int>, IMrMarker1;
