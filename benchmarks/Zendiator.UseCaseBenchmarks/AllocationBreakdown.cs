using System.Diagnostics;
using System.Text.Json;
using Competitive;
using Microsoft.Extensions.DependencyInjection;

internal static class AllocationBreakdown
{
    private sealed class PlainEntry;
    private sealed class DisposableEntry : IDisposable { public void Dispose() { } }
    private sealed class ScaleComposition;
    private sealed class ScaleValue<T> { public ScaleValue() { } }

    public static Task Run(string output)
    {
        using var zendiator = Registration.Create("Zendiator", "Scoped");
        using var immediate = Registration.Create("Immediate", "Scoped");
        using var zendiatorTransient = Registration.Create("Zendiator", "Transient");
        using var immediateTransient = Registration.Create("Immediate", "Transient");
        using var plainProvider = new ServiceCollection().AddScoped<PlainEntry>().BuildServiceProvider();
        using var disposableProvider = new ServiceCollection().AddScoped<DisposableEntry>().BuildServiceProvider();
        using (var scope = zendiator.CreateScope())
        {
            var provider = scope.ServiceProvider;
            if (!ReferenceEquals(provider.GetRequiredService<Competitive.Generated.IZendiator>(), provider.GetRequiredService<Competitive.Generated.IZendiator>()))
                throw new InvalidOperationException("Scoped Zendiator was not shared in one scope.");
        }
        using (var scope = immediate.CreateScope())
        {
            var provider = scope.ServiceProvider;
            if (!ReferenceEquals(provider.GetRequiredService<IPing0.Handler>(), provider.GetRequiredService<IPing0.Handler>()))
                throw new InvalidOperationException("Scoped Immediate handler was not shared in one scope.");
        }
        using (var scope = immediateTransient.CreateScope())
        {
            var provider = scope.ServiceProvider;
            if (ReferenceEquals(provider.GetRequiredService<IPing0.Handler>(), provider.GetRequiredService<IPing0.Handler>()))
                throw new InvalidOperationException("Transient Immediate handler was shared in one scope.");
        }
        var request = new Ping0();
        var request3 = new Ping3();
        var request5 = new Ping5();
        var cases = new (string Name, Func<int> Invoke)[]
        {
            ("Z Scope", () => { using var scope = zendiator.CreateScope(); return 1; }),
            ("Z Scope+direct mediator", () => { using var scope = zendiator.CreateScope(); var entry = new Competitive.Generated.Zendiator(scope.ServiceProvider); GC.KeepAlive(entry); return 1; }),
            ("Z Scope+entry", () => { using var scope = zendiator.CreateScope(); var entry = scope.ServiceProvider.GetRequiredService<Competitive.Generated.IZendiator>(); GC.KeepAlive(entry); return 1; }),
            ("Z Scope+entry+Send0", () => { using var scope = zendiator.CreateScope(); var entry = scope.ServiceProvider.GetRequiredService<Competitive.Generated.IZendiator>(); return entry.SendAsync(request).Result; }),
            ("Z Scope+entry+first Behavior5", () => { using var scope = zendiator.CreateScope(); var entry = (Competitive.Generated.Zendiator)scope.ServiceProvider.GetRequiredService<Competitive.Generated.IZendiator>(); _ = entry.GetRequiredService<ZBehavior1<Ping5, int>>(); return 1; }),
            ("Z Scope+entry+two Behaviors5", () => { using var scope = zendiator.CreateScope(); var entry = (Competitive.Generated.Zendiator)scope.ServiceProvider.GetRequiredService<Competitive.Generated.IZendiator>(); _ = entry.GetRequiredService<ZBehavior1<Ping5, int>>(); _ = entry.GetRequiredService<ZBehavior2<Ping5, int>>(); return 1; }),
            ("Z Scope+entry+Send3", () => { using var scope = zendiator.CreateScope(); var entry = scope.ServiceProvider.GetRequiredService<Competitive.Generated.IZendiator>(); return entry.SendAsync(request3).Result; }),
            ("Z Scope+entry+Send5", () => { using var scope = zendiator.CreateScope(); var entry = scope.ServiceProvider.GetRequiredService<Competitive.Generated.IZendiator>(); return entry.SendAsync(request5).Result; }),
            ("I Scope", () => { using var scope = immediate.CreateScope(); return 1; }),
            ("I Scope+direct handler", () => { using var scope = immediate.CreateScope(); var entry = new IPing0.Handler(new IPing0.HandleBehavior()); GC.KeepAlive(entry); return 1; }),
            ("I Scope+entry", () => { using var scope = immediate.CreateScope(); var entry = scope.ServiceProvider.GetRequiredService<IPing0.Handler>(); GC.KeepAlive(entry); return 1; }),
            ("I Scope+entry+Send0", () => { using var scope = immediate.CreateScope(); var entry = scope.ServiceProvider.GetRequiredService<IPing0.Handler>(); return entry.HandleAsync(request).Result; }),
            ("Z transient Scope+entry", () => { using var scope = zendiatorTransient.CreateScope(); var entry = scope.ServiceProvider.GetRequiredService<Competitive.Generated.IZendiator>(); GC.KeepAlive(entry); return 1; }),
            ("Z transient Scope+entry+Send0", () => { using var scope = zendiatorTransient.CreateScope(); var entry = scope.ServiceProvider.GetRequiredService<Competitive.Generated.IZendiator>(); return entry.SendAsync(request).Result; }),
            ("I transient Scope+entry", () => { using var scope = immediateTransient.CreateScope(); var entry = scope.ServiceProvider.GetRequiredService<IPing0.Handler>(); GC.KeepAlive(entry); return 1; }),
            ("I transient Scope+entry+Send0", () => { using var scope = immediateTransient.CreateScope(); var entry = scope.ServiceProvider.GetRequiredService<IPing0.Handler>(); return entry.HandleAsync(request).Result; }),
            ("DI plain Scope+entry", () => { using var scope = plainProvider.CreateScope(); var entry = scope.ServiceProvider.GetRequiredService<PlainEntry>(); GC.KeepAlive(entry); return 1; }),
            ("DI disposable Scope+entry", () => { using var scope = disposableProvider.CreateScope(); var entry = scope.ServiceProvider.GetRequiredService<DisposableEntry>(); GC.KeepAlive(entry); return 1; }),
        };
        const int operations = 100_000;
        var results = new List<object>();
        for (var round = 0; round < 5; round++)
        {
            foreach (var candidate in round % 2 == 0 ? cases : cases.Reverse())
            {
                for (var n = 0; n < 10_000; n++) candidate.Invoke();
                var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
                var beforeTicks = Stopwatch.GetTimestamp();
                var checksum = 0;
                for (var n = 0; n < operations; n++) checksum += candidate.Invoke();
                var ticks = Stopwatch.GetTimestamp() - beforeTicks;
                var bytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
                if (checksum is not (100_000 or 4_200_000)) throw new InvalidOperationException($"Wrong result: {candidate.Name}, {checksum}");
                results.Add(new { round, candidate.Name, operations, checksum, bytesPerOperation = (double)bytes / operations, nanosecondsPerOperation = ticks * (1_000_000_000d / Stopwatch.Frequency) / operations });
            }
        }
        var path = Path.Combine(output, "allocation-breakdown.json");
        File.WriteAllText(path, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(path);
        MeasureResolverScale(output);
        return Task.CompletedTask;
    }

    private static void MeasureResolverScale(string output)
    {
        using var provider = new ServiceCollection().AddTransient(typeof(ScaleValue<>)).BuildServiceProvider();
        var method = typeof(Zendiator.DependencyInjection.ZendiatorServiceResolver<ScaleComposition>).GetMethod("GetRequiredService")!;
        var calls = (from first in Enumerable.Range(1, 16)
                     from second in Enumerable.Range(1, 8)
                     let argument = typeof(Tuple<,>).MakeGenericType(typeof(int).MakeArrayType(first), typeof(string).MakeArrayType(second))
                     select method.MakeGenericMethod(typeof(ScaleValue<>).MakeGenericType(argument))).ToArray();
        var primer = new Zendiator.DependencyInjection.ZendiatorServiceResolver<ScaleComposition>(provider);
        foreach (var call in calls) call.Invoke(primer, null);
        var results = new List<object>();
        foreach (var reverse in new[] { false, true })
        {
            var ordered = reverse ? calls.Reverse().ToArray() : calls;
            foreach (var count in new[] { 1, 2, 6, 32, 64, 128 })
            {
                void Resolve()
                {
                    var resolver = new Zendiator.DependencyInjection.ZendiatorServiceResolver<ScaleComposition>(provider);
                    for (var i = 0; i < count; i++)
                    {
                        var captured = ordered[i].Invoke(resolver, null);
                        if (!ReferenceEquals(captured, ordered[i].Invoke(resolver, null)))
                            throw new InvalidOperationException("A captured dependency was replaced.");
                    }
                }
                for (var i = 0; i < 100; i++) Resolve();
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 1000; i++) Resolve();
                var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                results.Add(new { reverse, count, bytesPerOperation = allocated / 1000d });
            }
        }
        File.WriteAllText(Path.Combine(output, "resolver-scale.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
    }
}
