using Zendiator;

namespace Zendiator.Benchmarks;

public readonly record struct ZpAsync(int Value) : IRequest<int>;
