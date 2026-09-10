using global::Zendiator;

namespace Zendiator.Tests;

public sealed record GetScopedItems(int Count) : IStreamRequest<int>;
