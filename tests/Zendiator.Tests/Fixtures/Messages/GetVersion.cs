using global::Zendiator;

namespace Zendiator.Tests;

public sealed record GetVersion(int V) : IMultiRequest<int>;
