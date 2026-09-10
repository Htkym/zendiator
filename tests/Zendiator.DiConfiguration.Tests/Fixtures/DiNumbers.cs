using Zendiator;
using Zendiator.DependencyInjection;

namespace Zendiator.DiConfiguration.Tests;

public sealed record DiNumbers(int Count) : IStreamRequest<int>;
