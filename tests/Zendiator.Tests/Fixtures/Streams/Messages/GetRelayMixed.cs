using global::Zendiator;

namespace Zendiator.Tests;

public sealed record GetRelayMixed(int Count, int FailAt = -1) : IStreamRequest<int>;
