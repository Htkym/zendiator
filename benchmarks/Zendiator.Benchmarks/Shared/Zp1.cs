using Zendiator;

namespace Zendiator.Benchmarks;

public readonly record struct Zp1(int Value) : IRequest<int>, IZrMarker1;
