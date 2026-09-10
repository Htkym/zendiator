using global::Zendiator;

namespace Zendiator.Tests;

public readonly record struct SumSync(int A, int B) : ISyncMultiRequest<int>;
