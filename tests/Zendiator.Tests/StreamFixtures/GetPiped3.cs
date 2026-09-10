using global::Zendiator;

namespace Zendiator.Tests;

public sealed record GetPiped3(int Count) : IStreamRequest<int>;
