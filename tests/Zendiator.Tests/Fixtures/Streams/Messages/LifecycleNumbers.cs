using global::Zendiator;

namespace Zendiator.Tests;

public sealed record LifecycleNumbers(int Count) : IStreamRequest<int>;
