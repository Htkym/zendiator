using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Zendiator.DependencyInjection;

namespace Competitive;

public sealed record SameProducerStream0(int Count, bool Asynchronous, Probe? Probe = null)
    : Zendiator.IStreamRequest<int>, IWork;

[Zendiator.ZendiatorHandler]
public sealed class ZSameProducerStream0 : Zendiator.IStreamHandler<SameProducerStream0, int>
{
    public IAsyncEnumerable<int> Handle(SameProducerStream0 request, CancellationToken token) =>
        Work.Stream(request.Count, request.Asynchronous, request.Probe, token);
}

[Zendiator.ZendiatorComposition("sameProducerStreams")]
public sealed class BenchmarkSameProducerStreams;

internal static class SameProducerStreamRegistration
{
    internal static ServiceProvider Create()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddZendiator<BenchmarkSameProducerStreams>(configuration =>
        {
            configuration.Handlers<SameProducerStream0>(Zendiator.DispatchFamily.Stream, typeof(ZSameProducerStream0));
            configuration.Pipeline<SameProducerStream0>(Zendiator.DispatchFamily.Stream);
        });
        RegistrationAudit.Record(services, "ZendiatorSameProducer", "Default", "before-provider");
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}

[MemoryDiagnoser]
public class ZendiatorSameProducerStream0
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private Competitive.Generated.BenchmarkSameProducerStreams.IZendiator entry = null!;
    private SameProducerStream0 request = null!;

    [Params(16, 1024)] public int Count { get; set; }
    [Params(false, true)] public bool Asynchronous { get; set; }
    internal Type EntryType => entry.GetType();

    [GlobalSetup]
    public void Setup()
    {
        provider = SameProducerStreamRegistration.Create();
        scope = provider.CreateScope();
        entry = scope.ServiceProvider.GetRequiredService<Competitive.Generated.BenchmarkSameProducerStreams.IZendiator>();
        request = new(Count, Asynchronous);
        ChildEvidence.Record(nameof(ZendiatorSameProducerStream0), "Default");
    }

    [GlobalCleanup]
    public void Cleanup() { scope.Dispose(); provider.Dispose(); }

    public IAsyncEnumerable<int> Invoke(SameProducerStream0 value, CancellationToken token = default) => entry.StreamAsync(value, token);

    [Benchmark]
    public async ValueTask<int> Full()
    {
        int sum = 0;
        await foreach (var item in Invoke(request).ConfigureAwait(false)) sum += item;
        return sum;
    }

    [Benchmark]
    public async ValueTask<int> EarlyBreak()
    {
        await foreach (var item in Invoke(request).ConfigureAwait(false)) return item;
        return -1;
    }
}

[MemoryDiagnoser]
public class MediatRHistoricalSameProducerStream0
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private MediatR.IMediator entry = null!;
    private Stream0 request = null!;

    [Params(16, 1024)] public int Count { get; set; }
    [Params(false, true)] public bool Asynchronous { get; set; }
    internal Type EntryType => entry.GetType();

    [GlobalSetup]
    public void Setup()
    {
        provider = Registration.Create("MediatRHistorical");
        scope = provider.CreateScope();
        entry = scope.ServiceProvider.GetRequiredService<MediatR.IMediator>();
        request = new(Count, Asynchronous);
        ChildEvidence.Record(nameof(MediatRHistoricalSameProducerStream0), "Default");
    }

    [GlobalCleanup]
    public void Cleanup() { scope.Dispose(); provider.Dispose(); }

    public IAsyncEnumerable<int> Invoke(Stream0 value, CancellationToken token = default) => entry.CreateStream(value, token);

    [Benchmark]
    public async ValueTask<int> Full()
    {
        int sum = 0;
        await foreach (var item in Invoke(request).ConfigureAwait(false)) sum += item;
        return sum;
    }

    [Benchmark]
    public async ValueTask<int> EarlyBreak()
    {
        await foreach (var item in Invoke(request).ConfigureAwait(false)) return item;
        return -1;
    }
}

[MemoryDiagnoser]
public class MediatorSameProducerStream0
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private Mediator.Mediator entry = null!;
    private Stream0 request = null!;

    [Params(16, 1024)] public int Count { get; set; }
    [Params(false, true)] public bool Asynchronous { get; set; }
    internal Type EntryType => entry.GetType();

    [GlobalSetup]
    public void Setup()
    {
        provider = Registration.Create("Mediator");
        scope = provider.CreateScope();
        entry = scope.ServiceProvider.GetRequiredService<Mediator.Mediator>();
        request = new(Count, Asynchronous);
        ChildEvidence.Record(nameof(MediatorSameProducerStream0), "Default");
    }

    [GlobalCleanup]
    public void Cleanup() { scope.Dispose(); provider.Dispose(); }

    public IAsyncEnumerable<int> Invoke(Stream0 value, CancellationToken token = default) => entry.CreateStream(value, token);

    [Benchmark]
    public async ValueTask<int> Full()
    {
        int sum = 0;
        await foreach (var item in Invoke(request).ConfigureAwait(false)) sum += item;
        return sum;
    }

    [Benchmark]
    public async ValueTask<int> EarlyBreak()
    {
        await foreach (var item in Invoke(request).ConfigureAwait(false)) return item;
        return -1;
    }
}

[MemoryDiagnoser]
public class DispatchRSameProducerStream0
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private DispatchR.IMediator entry = null!;
    private Stream0 request = null!;

    [Params(16, 1024)] public int Count { get; set; }
    [Params(false, true)] public bool Asynchronous { get; set; }
    internal Type EntryType => entry.GetType();

    [GlobalSetup]
    public void Setup()
    {
        provider = Registration.Create("DispatchR");
        scope = provider.CreateScope();
        entry = scope.ServiceProvider.GetRequiredService<DispatchR.IMediator>();
        request = new(Count, Asynchronous);
        ChildEvidence.Record(nameof(DispatchRSameProducerStream0), "Default");
    }

    [GlobalCleanup]
    public void Cleanup() { scope.Dispose(); provider.Dispose(); }

    public IAsyncEnumerable<int> Invoke(Stream0 value, CancellationToken token = default) => entry.CreateStream(value, token);

    [Benchmark]
    public async ValueTask<int> Full()
    {
        int sum = 0;
        await foreach (var item in Invoke(request).ConfigureAwait(false)) sum += item;
        return sum;
    }

    [Benchmark]
    public async ValueTask<int> EarlyBreak()
    {
        await foreach (var item in Invoke(request).ConfigureAwait(false)) return item;
        return -1;
    }
}

[MemoryDiagnoser]
public class ImmediateSameProducerStream0
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private IStream0.Handler entry = null!;
    private Stream0 request = null!;

    [Params(16, 1024)] public int Count { get; set; }
    [Params(false, true)] public bool Asynchronous { get; set; }
    internal Type EntryType => entry.GetType();

    [GlobalSetup]
    public void Setup()
    {
        provider = Registration.Create("Immediate");
        scope = provider.CreateScope();
        entry = scope.ServiceProvider.GetRequiredService<IStream0.Handler>();
        request = new(Count, Asynchronous);
        ChildEvidence.Record(nameof(ImmediateSameProducerStream0), "Default");
    }

    [GlobalCleanup]
    public void Cleanup() { scope.Dispose(); provider.Dispose(); }

    public IAsyncEnumerable<int> Invoke(Stream0 value, CancellationToken token = default) => entry.HandleAsync(value, token);

    [Benchmark]
    public async ValueTask<int> Full()
    {
        int sum = 0;
        await foreach (var item in Invoke(request).ConfigureAwait(false)) sum += item;
        return sum;
    }

    [Benchmark]
    public async ValueTask<int> EarlyBreak()
    {
        await foreach (var item in Invoke(request).ConfigureAwait(false)) return item;
        return -1;
    }
}

internal static class SameProducerStreamGate
{
    internal const string Family = "same-producer-compiler-iterator-v1";

    internal static async Task Run(string output)
    {
        if (Correctness.Results.Count != 0) throw new InvalidOperationException("The same-producer gate must run separately.");
        Directory.CreateDirectory(output);
        Environment.SetEnvironmentVariable("COLD_CAPTURE_CHILD", "0");
        Environment.SetEnvironmentVariable("COLD_AUDIT_REG", "1");
        Environment.SetEnvironmentVariable("COLD_EVIDENCE_DIR", output);
        var bindings = new List<object>();

        var zendiator = new ZendiatorSameProducerStream0();
        await Check(nameof(ZendiatorSameProducerStream0), zendiator.Setup, zendiator.Cleanup,
            (count, asynchronous, probe, token) => zendiator.Invoke(new(count, asynchronous, probe), token), () => zendiator.EntryType);
        var mediatR = new MediatRHistoricalSameProducerStream0();
        await Check(nameof(MediatRHistoricalSameProducerStream0), mediatR.Setup, mediatR.Cleanup,
            (count, asynchronous, probe, token) => mediatR.Invoke(new(count, asynchronous, probe), token), () => mediatR.EntryType);
        var mediator = new MediatorSameProducerStream0();
        await Check(nameof(MediatorSameProducerStream0), mediator.Setup, mediator.Cleanup,
            (count, asynchronous, probe, token) => mediator.Invoke(new(count, asynchronous, probe), token), () => mediator.EntryType);
        var dispatchR = new DispatchRSameProducerStream0();
        await Check(nameof(DispatchRSameProducerStream0), dispatchR.Setup, dispatchR.Cleanup,
            (count, asynchronous, probe, token) => dispatchR.Invoke(new(count, asynchronous, probe), token), () => dispatchR.EntryType);
        var immediate = new ImmediateSameProducerStream0();
        await Check(nameof(ImmediateSameProducerStream0), immediate.Setup, immediate.Cleanup,
            (count, asynchronous, probe, token) => immediate.Invoke(new(count, asynchronous, probe), token), () => immediate.EntryType);

        Correctness.Save(output);
        ArtifactEvidence.Write(Path.Combine(output, "same-producer-bindings.json"), new
        {
            family = Family,
            behaviorDepth = 0,
            producer = "Competitive.Work.Stream",
            producerRepresentation = "compiler async iterator in all five libraries",
            source = "Sync and Task.Yield modes use the same existing Work.Stream implementation; Task.Yield does not guarantee every public Move is observed pending.",
            zendiatorInput = "IStreamHandler<SameProducerStream0,int>.Handle; no hand-written cursor adapter",
            peerInput = "Existing Stream0 handlers and registrations, each returning Work.Stream",
            bindings,
            assemblies = ArtifactEvidence.Assemblies(),
            timingExecuted = false,
            originalFamilyReplaced = false,
            scope = "Dedicated new-family correctness/binding; does not replace or certify the old 389-case gate or a future timed child artifact."
        });
        Console.WriteLine($"Same-producer correctness passed: {Correctness.Results.Count} checks.");

        async Task Check(string name, Action setup, Action cleanup,
            Func<int, bool, Probe, CancellationToken, IAsyncEnumerable<int>> stream, Func<Type> entryType)
        {
            setup();
            try
            {
                await Correctness.Stream(name, 0, stream);
                var type = entryType();
                bindings.Add(new
                {
                    scenario = name,
                    entry = type.FullName,
                    assembly = type.Assembly.FullName,
                    assemblyPath = type.Assembly.Location,
                    assemblySha256 = ArtifactEvidence.Hash(type.Assembly.Location)
                });
            }
            finally { cleanup(); }
        }
    }
}
