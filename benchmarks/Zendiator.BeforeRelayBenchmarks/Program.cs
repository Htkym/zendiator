using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using Competitive;
using Perfolizer.Horology;

namespace BeforeRelayComparison;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--example")
        {
            using (var fixture = new Fixture(Route.Before))
            {
                var probe = new Probe();
                var checksum = await Consumer.Full(fixture.Invoke(new Items(16, probe), fixture.Token)).ConfigureAwait(false);
                if (checksum != 120 || !probe.Events.SequenceEqual(new[] { 1, 2, 3, 4, 5, 100 })
                    || probe.Handlers != 1 || probe.Items != 16 || probe.Disposals != 1)
                    throw new InvalidOperationException("The Before example did not preserve preprocessing order, input or cleanup.");
            }
            Console.WriteLine("Before preprocessing example succeeded.");
            return;
        }
        if (args.Length == 0)
        {
            Console.WriteLine("Use --example, or pass BenchmarkDotNet options such as --filter '*'.");
            return;
        }
        var config = ManualConfig.Create(DefaultConfig.Instance)
            .AddJob(Job.Default.WithId("BeforeRelay").WithAffinity(new IntPtr(1)).WithWarmupCount(20)
                .WithIterationCount(12).WithIterationTime(TimeInterval.FromMilliseconds(500)).WithLaunchCount(1))
            .AddExporter(JsonExporter.Full);
        _ = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config).ToArray();
    }
}
