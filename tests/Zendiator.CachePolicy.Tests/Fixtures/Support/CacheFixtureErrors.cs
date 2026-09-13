using Microsoft.Extensions.DependencyInjection;
using Zendiator;

namespace Zendiator.CachePolicy.Tests;

public static class CacheFixtureErrors
{
    public static readonly InvalidOperationException Instance = new("fixture failure");
}
