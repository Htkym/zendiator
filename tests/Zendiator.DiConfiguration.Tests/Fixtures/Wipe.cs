using Zendiator;
using Zendiator.DependencyInjection;

namespace Zendiator.DiConfiguration.Tests;

public readonly record struct Wipe(int Id) : ISyncRequest;
