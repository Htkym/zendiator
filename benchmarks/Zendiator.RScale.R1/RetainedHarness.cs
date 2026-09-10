using BenchmarkDotNet.Running;
using Microsoft.Extensions.DependencyInjection;
using Zendiator.RScale.R1;

namespace Zendiator.RScale.R1;

public static class RetainedHarness
{
    public static long Measure(int scopes, Action<IZendiator> use)
    {
        var services = new ServiceCollection();
        services.AddZendiator();
        using var provider = services.BuildServiceProvider();
        var held = new List<AsyncServiceScope>(scopes);
        for (var i = 0; i < scopes; i++)
        {
            var scope = provider.CreateAsyncScope();
            held.Add(scope);
            use(scope.ServiceProvider.GetRequiredService<IZendiator>());
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var total = GC.GetTotalMemory(true);
        foreach (var scope in held)
            scope.Dispose();
        return total;
    }
}
