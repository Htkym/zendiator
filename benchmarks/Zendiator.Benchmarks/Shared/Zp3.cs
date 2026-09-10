using Zendiator;

namespace Zendiator.Benchmarks;

public readonly record struct Zp3(int Value) : IRequest<int>, IZrMarker1, IZrMarker2, IZrMarker3;
