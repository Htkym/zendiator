using Microsoft.Extensions.DependencyInjection;
using Zendiator;

namespace Zendiator.CachePolicy.Tests;

public readonly record struct ReReq(int Value) : IRequest<int>;
