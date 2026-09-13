using global::Zendiator;

namespace Zendiator.Tests;

public sealed record GetGated(bool Open, int Count) : IStreamRequest<int>;
