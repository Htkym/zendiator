using global::Zendiator;

namespace Zendiator.Tests;

public sealed record GetOne(int Value) : IStreamRequest<int>;
