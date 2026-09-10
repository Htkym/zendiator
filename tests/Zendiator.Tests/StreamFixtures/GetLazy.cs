using global::Zendiator;

namespace Zendiator.Tests;

public sealed record GetLazy(int Count) : IStreamRequest<int>;
