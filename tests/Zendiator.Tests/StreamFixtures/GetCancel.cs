using global::Zendiator;

namespace Zendiator.Tests;

public sealed record GetCancel(int Count) : IStreamRequest<int>;
