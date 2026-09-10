using global::Zendiator;

namespace Zendiator.Tests;

public sealed record Wipe<T>(int[] Ids) : IMultiRequest;
