using global::Zendiator;

namespace Zendiator.Tests;

public readonly record struct FlushRequest(int Id) : ISyncRequest;
