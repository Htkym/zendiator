using Zendiator;
using Zendiator.DependencyInjection;

namespace Zendiator.DiConfiguration.Tests;

public sealed record Pair(int Value) : IMultiRequest<int>;
