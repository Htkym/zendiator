using Microsoft.Extensions.DependencyInjection;
using Zendiator;

namespace Zendiator.CachePolicy.Tests;

public readonly record struct ExpPipeReq(int Value) : IRequest<int>;
