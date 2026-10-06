using System.Diagnostics;
using System.Text.Json;
using Competitive;
using Microsoft.Extensions.DependencyInjection;

internal static class AllocationBreakdown
{
    private sealed class PlainEntry;
    private sealed class DisposableEntry : IDisposable { public void Dispose() { } }

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
        var cases = new (string Name, Func<int> Invoke)[]
        {
            ("Z Scope", () => { using var scope = zendiator.CreateScope(); return 1; }),
            ("Z Scope+direct mediator", () => { using var scope = zendiator.CreateScope(); var entry = new Competitive.Generated.Zendiator(scope.ServiceProvider); GC.KeepAlive(entry); return 1; }),
            ("Z Scope+entry", () => { using var scope = zendiator.CreateScope(); var entry = scope.ServiceProvider.GetRequiredService<Competitive.Generated.IZendiator>(); GC.KeepAlive(entry); return 1; }),
            ("Z Scope+entry+Send0", () => { using var scope = zendiator.CreateScope(); var entry = scope.ServiceProvider.GetRequiredService<Competitive.Generated.IZendiator>(); return entry.SendAsync(request).Result; }),
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
        return Task.CompletedTask;
    }
}
