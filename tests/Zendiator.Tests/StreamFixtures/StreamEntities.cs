using global::Zendiator;

namespace Zendiator.Tests;

public sealed record StreamEntities<T>(int Count) : IStreamRequest<T>
    where T : new();
