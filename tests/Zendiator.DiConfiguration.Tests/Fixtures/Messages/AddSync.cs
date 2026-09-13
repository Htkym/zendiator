using Zendiator;
using Zendiator.DependencyInjection;

namespace Zendiator.DiConfiguration.Tests;

public readonly record struct AddSync(int Value) : ISyncMultiRequest<int>;
