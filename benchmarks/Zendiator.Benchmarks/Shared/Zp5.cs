using Zendiator;

namespace Zendiator.Benchmarks;

public readonly record struct Zp5(int Value) : IRequest<int>, IZrMarker1, IZrMarker2, IZrMarker3, IZrMarker4, IZrMarker5;
