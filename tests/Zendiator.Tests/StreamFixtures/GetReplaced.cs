using global::Zendiator;

namespace Zendiator.Tests;

public sealed record GetReplaced(int Count) : IStreamRequest<int>;
