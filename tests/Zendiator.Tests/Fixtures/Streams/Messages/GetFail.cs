using global::Zendiator;

namespace Zendiator.Tests;

public sealed record GetFail(int Count, int FailAt) : IStreamRequest<int>;
