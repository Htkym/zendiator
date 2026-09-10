using Microsoft.Extensions.DependencyInjection;
using Zendiator;

namespace Zendiator.CachePolicy.Tests;

public readonly record struct PipeReq(int Value) : IRequest<int>;
