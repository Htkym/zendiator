using global::Zendiator;

namespace Zendiator.Tests;

public sealed record GetNumbers(int Count) : IStreamRequest<int>;
