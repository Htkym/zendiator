using Microsoft.Extensions.DependencyInjection;
using Zendiator;

namespace Zendiator.CachePolicy.Tests;

public readonly record struct Val0(int Value) : IRequest<int>;
