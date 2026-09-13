using Zendiator;
using Zendiator.DependencyInjection;

namespace Zendiator.DiConfiguration.Tests;

public readonly record struct Add(int Left, int Right) : IRequest<int>;
