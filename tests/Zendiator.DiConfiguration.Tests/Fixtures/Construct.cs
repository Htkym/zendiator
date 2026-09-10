using Di.Generated;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.DiConfiguration.Tests;

public sealed record Construct<T> : IRequest<T> where T : new();
