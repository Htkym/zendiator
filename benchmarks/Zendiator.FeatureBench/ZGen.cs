using Zendiator;

namespace Zendiator.FeatureBench;

public sealed record ZGen<T>(int Value) : IRequest<T>
    where T : class;
