using global::Zendiator;

namespace Zendiator.Tests;

public sealed record GetRelay(int Count, bool FailBefore = false) : IStreamRequest<int>;
