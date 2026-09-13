using global::Zendiator;

namespace Zendiator.Tests;

public sealed record Broadcast(string Message) : IMultiRequest;
