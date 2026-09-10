namespace Zendiator.Benchmarks;

public sealed record MrP5(int Value) : global::MediatR.IRequest<int>, IMrMarker1, IMrMarker2, IMrMarker3, IMrMarker4, IMrMarker5;
