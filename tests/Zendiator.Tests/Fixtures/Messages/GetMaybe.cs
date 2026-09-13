using global::Zendiator;

namespace Zendiator.Tests;

public sealed record GetMaybe(bool Open) : IMultiRequest<int>;
