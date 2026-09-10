using global::Zendiator;

namespace Zendiator.Tests;

public sealed record GetTransform(int Count) : IStreamRequest<int>;
