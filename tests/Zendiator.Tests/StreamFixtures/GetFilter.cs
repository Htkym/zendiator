using global::Zendiator;

namespace Zendiator.Tests;

public sealed record GetFilter(int Count) : IStreamRequest<int>;
