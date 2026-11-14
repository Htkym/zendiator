using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Zendiator;
using Zendiator.DependencyInjection;

namespace Competitive;

[ZendiatorComposition("perf03NamedStreams")]
public sealed class Perf03NamedStreams;

[ZendiatorComposition("perf03IteratorStreams")]
public sealed class Perf03IteratorStreams;

[Handler]
public sealed class Perf03IteratorStream0 : IStreamHandler<Stream0, int>
{
    public IAsyncEnumerable<int> Handle(Stream0 request, CancellationToken token) =>
        Work.Stream(request.Count, request.Asynchronous, request.Probe, token);
}

[Handler]
public sealed class Perf03IteratorRelay5 : IStreamHandler<Relay5, int>
{
    public IAsyncEnumerable<int> Handle(Relay5 request, CancellationToken token) =>
        Work.Stream(request.Count, request.Asynchronous, request.Probe, token);
}

internal static class Perf03CauseRegistration
{
    internal static ServiceProvider Create(bool iterator)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (iterator)
            services.AddZendiator<Perf03IteratorStreams>(configuration =>
            {
                configuration.Handlers<Stream0>(DispatchFamily.Stream, typeof(Perf03IteratorStream0));
                configuration.Pipeline<Stream0>(DispatchFamily.Stream);
                configuration.Handlers<Relay5>(DispatchFamily.Stream, typeof(Perf03IteratorRelay5));
                configuration.Pipeline<Relay5>(DispatchFamily.Stream,
                    Stage.Wrapper<ZRBehavior5<Relay5, int>>(), Stage.Wrapper<ZRBehavior4<Relay5, int>>(),
                    Stage.Wrapper<ZRBehavior3<Relay5, int>>(), Stage.Wrapper<ZRBehavior2<Relay5, int>>(),
                    Stage.Wrapper<ZRBehavior1<Relay5, int>>());
            });
        else
            services.AddZendiator<Perf03NamedStreams>(configuration =>
            {
                configuration.Handlers<Stream0>(DispatchFamily.Stream, typeof(ZStream0));
                configuration.Pipeline<Stream0>(DispatchFamily.Stream);
                configuration.Handlers<Relay5>(DispatchFamily.Stream, typeof(ZRelay5));
                configuration.Pipeline<Relay5>(DispatchFamily.Stream,
                    Stage.Wrapper<ZRBehavior5<Relay5, int>>(), Stage.Wrapper<ZRBehavior4<Relay5, int>>(),
                    Stage.Wrapper<ZRBehavior3<Relay5, int>>(), Stage.Wrapper<ZRBehavior2<Relay5, int>>(),
                    Stage.Wrapper<ZRBehavior1<Relay5, int>>());
            });
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}

internal sealed class Perf03CauseSession : IDisposable
{
    private readonly ServiceProvider provider;
    private readonly IServiceScope scope;
    private readonly CancellationTokenSource? cancellation;
    private readonly Competitive.Generated.Perf03NamedStreams.IZendiator? named;
    private readonly Competitive.Generated.Perf03NamedStreams.Zendiator? namedConcrete;
    private readonly Competitive.Generated.Perf03IteratorStreams.IZendiator? iteratorEntry;
    private readonly Stream0 streamRequest;
    private readonly Relay5 relayRequest;

    internal Perf03CauseSession(bool iterator, bool live, bool relay, int count, bool asynchronous)
    {
        Iterator = iterator;
        Live = live;
        Relay = relay;
        Count = count;
        Asynchronous = asynchronous;
        cancellation = live ? new CancellationTokenSource() : null;
        provider = Perf03CauseRegistration.Create(iterator);
        scope = provider.CreateScope();
        if (iterator)
            iteratorEntry = scope.ServiceProvider.GetRequiredService<Competitive.Generated.Perf03IteratorStreams.IZendiator>();
        else
        {
            named = scope.ServiceProvider.GetRequiredService<Competitive.Generated.Perf03NamedStreams.IZendiator>();
            namedConcrete = (Competitive.Generated.Perf03NamedStreams.Zendiator)named;
        }
        streamRequest = new(count, asynchronous);
        relayRequest = new(count, asynchronous);
    }

    internal bool Iterator
    {
        get;
    }
    internal bool Live
    {
        get;
    }
    internal bool Relay
    {
        get;
    }
    internal int Count
    {
        get;
    }
    internal bool Asynchronous
    {
        get;
    }
    internal CancellationToken ApiToken => cancellation?.Token ?? default;
    internal object Entry => (object?)named ?? iteratorEntry!;
    internal object ScopeIdentity => scope.ServiceProvider;
    internal Type HandlerType => Iterator
        ? (Relay ? typeof(Perf03IteratorRelay5) : typeof(Perf03IteratorStream0))
        : (Relay ? typeof(ZRelay5) : typeof(ZStream0));

    internal IAsyncEnumerable<int> Open(Probe? probe = null) => Open(Request(probe), ApiToken);

    internal IAsyncEnumerable<int> Open(object request, CancellationToken token)
    {
        if (request is Relay5 relay)
            return Iterator ? iteratorEntry!.StreamAsync(relay, token) : named!.StreamAsync(relay, token);
        var stream = (Stream0)request;
        return Iterator ? iteratorEntry!.StreamAsync(stream, token) : named!.StreamAsync(stream, token);
    }

    internal object Request(Probe? probe = null) => Relay
        ? (probe is null ? relayRequest : new Relay5(Count, Asynchronous, probe))
        : (probe is null ? streamRequest : new Stream0(Count, Asynchronous, probe));

    internal IAsyncEnumerator<int> OpenDirect(Probe? probe = null) => Iterator
        ? Work.Stream(Count, Asynchronous, probe, ApiToken).GetAsyncEnumerator(ApiToken)
        : new WorkStreamCursor(Count, Asynchronous, probe, ApiToken);

    internal IAsyncEnumerable<int> OpenDiagnostic() => OpenDiagnostic(Request(), ApiToken);

    internal IAsyncEnumerable<int> OpenDiagnostic(object request, CancellationToken token)
    {
        if (Iterator)
            throw new InvalidOperationException("The isolated control selects the named producer only.");
        return request is Relay5 relay
            ? Perf03.Diagnostics.Perf03DiagnosticControl.CreateRelay5(namedConcrete!, relay, token)
            : Perf03.Diagnostics.Perf03DiagnosticControl.CreateStream0(namedConcrete!, (Stream0)request, token);
    }

    public void Dispose()
    {
        try
        {
            scope.Dispose();
        }
        finally
        {
            try
            {
                provider.Dispose();
            }
            finally
            {
                cancellation?.Dispose();
            }
        }
    }
}

[MemoryDiagnoser]
public abstract class Perf03CauseBenchmark(bool iterator, bool live, bool relay)
{
    private protected Perf03CauseSession Session { get; private set; } = null!;

    [Params(1024)]
    public int Count
    {
        get; set;
    }

    [Params(false, true)]
    public bool Asynchronous
    {
        get; set;
    }

    [GlobalSetup]
    public void Setup()
    {
        Session = new(iterator, live, relay, Count, Asynchronous);
        ChildEvidence.Record(this);
    }

    [GlobalCleanup]
    public void Cleanup() => Session.Dispose();

    [Benchmark]
    public async ValueTask<int> Full()
    {
        int sum = 0;
        var cursor = Session.Open().GetAsyncEnumerator();
        try
        {
            while (await cursor.MoveNextAsync().ConfigureAwait(false))
                sum += cursor.Current;
            return sum;
        }
        finally
        {
            await cursor.DisposeAsync().ConfigureAwait(false);
        }
    }
}

public abstract class Perf03CauseDirectBenchmark(bool iterator, bool live)
    : Perf03CauseBenchmark(iterator, live, false)
{
    [Benchmark]
    public async ValueTask<int> FullDirect()
    {
        int sum = 0;
        var cursor = Session.OpenDirect();
        try
        {
            while (await cursor.MoveNextAsync().ConfigureAwait(false))
                sum += cursor.Current;
            return sum;
        }
        finally
        {
            await cursor.DisposeAsync().ConfigureAwait(false);
        }
    }
}

public class ZendiatorPerf03NamedDefaultStream0() : Perf03CauseDirectBenchmark(false, false);

public class ZendiatorPerf03NamedLiveStream0() : Perf03CauseDirectBenchmark(false, true)
{
    [Benchmark]
    public async ValueTask<int> FullDiagnostic()
    {
        int sum = 0;
        var cursor = Session.OpenDiagnostic().GetAsyncEnumerator();
        try
        {
            while (await cursor.MoveNextAsync().ConfigureAwait(false))
                sum += cursor.Current;
            return sum;
        }
        finally
        {
            await cursor.DisposeAsync().ConfigureAwait(false);
        }
    }
}

public class ZendiatorPerf03NamedDefaultRelay5() : Perf03CauseBenchmark(false, false, true)
{
    [Benchmark]
    public async ValueTask<int> FullDiagnostic()
    {
        int sum = 0;
        var cursor = Session.OpenDiagnostic().GetAsyncEnumerator();
        try
        {
            while (await cursor.MoveNextAsync().ConfigureAwait(false))
                sum += cursor.Current;
            return sum;
        }
        finally
        {
            await cursor.DisposeAsync().ConfigureAwait(false);
        }
    }
}

public class ZendiatorPerf03NamedLiveRelay5() : Perf03CauseBenchmark(false, true, true);
public class ZendiatorPerf03IteratorDefaultStream0() : Perf03CauseDirectBenchmark(true, false);
public class ZendiatorPerf03IteratorLiveStream0() : Perf03CauseDirectBenchmark(true, true);
public class ZendiatorPerf03IteratorDefaultRelay5() : Perf03CauseBenchmark(true, false, true);
public class ZendiatorPerf03IteratorLiveRelay5() : Perf03CauseBenchmark(true, true, true);
