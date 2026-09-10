using Microsoft.Extensions.DependencyInjection;
using Zendiator;

namespace Zendiator.CachePolicy.Tests;

public sealed record RefReq(string? Text) : IRequest<string?>;
