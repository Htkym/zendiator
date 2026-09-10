using Zendiator;

namespace Zendiator.FeatureBench;

public sealed record ZMulti(int Value) : IMultiRequest<int>;
