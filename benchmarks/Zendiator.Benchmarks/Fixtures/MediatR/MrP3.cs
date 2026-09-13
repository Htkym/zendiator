namespace Zendiator.Benchmarks;

public sealed record MrP3(int Value) : global::MediatR.IRequest<int>, IMrMarker1, IMrMarker2, IMrMarker3;
