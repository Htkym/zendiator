using Microsoft.Extensions.DependencyInjection;
using Zendiator;

namespace Zendiator.CachePolicy.Tests;

public readonly record struct TokReq(CancellationToken Replacement) : IRequest<CancellationToken>;
