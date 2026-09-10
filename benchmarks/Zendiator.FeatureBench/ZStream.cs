using Zendiator;

namespace Zendiator.FeatureBench;

public sealed record ZStream(int Count) : IStreamRequest<int>;
