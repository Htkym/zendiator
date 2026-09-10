using BenchmarkDotNet.Running;
using Microsoft.Extensions.DependencyInjection;
using Zendiator.RScale.R128;

if (args.Contains("--retained"))
{
    var rounds = 3;
    var scopes = 2000;
    foreach (var a in args)
    {
        if (a.StartsWith("--rounds=", StringComparison.Ordinal) && int.TryParse(a["--rounds=".Length..], out var r))
            rounds = r;
        if (a.StartsWith("--scopes=", StringComparison.Ordinal) && int.TryParse(a["--scopes=".Length..], out var n))
            scopes = n;
    }

    Console.WriteLine("variant,R,N,totalBytes");
    for (var round = 0; round < rounds; round++)
    {
        Console.WriteLine($"U0,{RScaleExpected.RouteCount},{scopes},{RetainedHarness.Measure(scopes, static m => { m.GetHashCode(); })}");
        Console.WriteLine($"Warm0,{RScaleExpected.RouteCount},{scopes},{RetainedHarness.Measure(scopes, static m => { _ = m.SendAsync(new Q0(0)).GetAwaiter().GetResult(); _ = m.SendAsync(new Q0(0)).GetAwaiter().GetResult(); })}");
        Console.WriteLine($"Warm5,{RScaleExpected.RouteCount},{scopes},{RetainedHarness.Measure(scopes, static m => { _ = m.SendAsync(new Q1(1)).GetAwaiter().GetResult(); _ = m.SendAsync(new Q1(1)).GetAwaiter().GetResult(); _ = m.SendAsync(new Q1(1)).GetAwaiter().GetResult(); })}");
    }

    return;
}

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

namespace Zendiator.RScale.R128
{
    public static class Program
    {
    }

    // Retained-memory survival experiment across scope variants.
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
}
