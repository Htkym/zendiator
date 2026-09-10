using global::Zendiator;

namespace Zendiator.Tests;

public sealed record GetPiped5(int Count) : IStreamRequest<int>;
