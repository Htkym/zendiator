using Microsoft.Extensions.DependencyInjection;
using Zendiator;

namespace Zendiator.CachePolicy.Tests;

public readonly record struct AddReq(int Value) : IRequest<int>;
