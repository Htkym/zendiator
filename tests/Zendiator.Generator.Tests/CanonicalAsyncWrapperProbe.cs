using Zendiator.Tests.Contracts;

namespace Zendiator.Generator.Tests;

public sealed class CanonicalAsyncWrapperProbe
{
    internal object Request = null!;
    internal readonly ContractOnce<bool> FilterGate = new();
    internal readonly ContractOnce<int> MapGate = new();
    internal readonly TaskCompletionSource FilterEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal readonly TaskCompletionSource MapEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal readonly List<StreamSeedEvent> Events = [];
    internal int Starts, Moves, Reads, SourceDisposals, Opens, UpstreamDisposals, WrapperDisposals, WrapperFinals, UpstreamReads, WrapperReads;
    internal void Event(string kind, string stage, int? input = null, int? output = null, bool? result = null)
    {
        var upstream = stage is "H" or "F1" or "Upstream";
        Events.Add(new(kind, stage, upstream ? 2 : 1, 1, 0, null, input, output, result, 0, null, upstream ? 1 : 0));
    }
    private void Check(object request, CancellationToken token)
    { if (!ReferenceEquals(request, Request) || token != default) throw new InvalidOperationException("Wrapper fixture request/token changed."); }
    public void Resolve(string role) => Event("Resolve", role);
    public IAsyncEnumerable<int> Start(object request, CancellationToken token)
    { Check(request, token); Starts++; Event("HandlerStart", "H"); return new Source(this); }
    public ValueTask<bool> First(object request, int item, CancellationToken token)
    {
        Check(request, token); if (item != 1) throw new InvalidOperationException("Filter must receive original 1.");
        Event("Filter", "F1", input: item); FilterEntered.TrySetResult(); return FilterGate.Task;
    }
    public ValueTask<int> Map(object request, int item, CancellationToken token)
    {
        Check(request, token); if (item != 10) throw new InvalidOperationException("Map must receive W replacement 10.");
        Event("Map", "Map", input: item); MapEntered.TrySetResult(); return MapGate.Task;
    }
    public void Wrap(object request, CancellationToken token) { Check(request, token); Event("Wrap", "W"); }
    public IAsyncEnumerable<int> ObserveOpen<R,N>(R request, N upstream, CancellationToken token) where N : IStreamUpstream<R,int>
    { Check(request!, token); Opens++; Event("UpstreamOpen", "W"); return new Tracked(this, upstream.Open(request, token), "Upstream"); }
    public IAsyncEnumerable<int> ObserveWrapper(IAsyncEnumerable<int> source) => new Tracked(this, source, "Wrapper");
    public int Replace(object request, int item, CancellationToken token)
    { Check(request, token); if (item != 1) throw new InvalidOperationException("W received wrong upstream item."); Event("WrapperReplace", "W", input: item, output: 10); return 10; }
    public void Finally(object request, CancellationToken token) { Check(request, token); WrapperFinals++; Event("WrapperFinally", "W"); }
    private sealed class Source(CanonicalAsyncWrapperProbe p) : IAsyncEnumerable<int>, IAsyncEnumerator<int>
    {
        private int _index;
        public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken token = default)
        { if (token != default) throw new InvalidOperationException(); p.Event("EnumeratorAcquired", "H"); return this; }
        public int Current { get { p.Reads++; p.Event("InnerCurrent", "H", output: 1); return 1; } }
        public ValueTask<bool> MoveNextAsync() { p.Moves++; var result = _index++ == 0; p.Event("InnerMove", "H", result: result); return new(result); }
        public ValueTask DisposeAsync() { p.SourceDisposals++; p.Event("InnerDisposeStart", "H"); p.Event("InnerDisposeEnd", "H"); return default; }
    }
    // Synchronous forwarding: retains actual cursor/ValueTask ownership without an async adapter.
    private sealed class Tracked(CanonicalAsyncWrapperProbe p, IAsyncEnumerable<int> source, string stage) : IAsyncEnumerable<int>
    {
        public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken token = default)
        { if (token != default) throw new InvalidOperationException(); p.Event("EnumeratorAcquired", stage); return new Cursor(p, source.GetAsyncEnumerator(token), stage); }
        private sealed class Cursor(CanonicalAsyncWrapperProbe p, IAsyncEnumerator<int> inner, string stage) : IAsyncEnumerator<int>
        {
            public int Current
            {
                get { var item = inner.Current; if (stage == "Upstream") p.UpstreamReads++; else p.WrapperReads++; p.Event("CursorCurrent", stage, output: item); return item; }
            }
            public ValueTask<bool> MoveNextAsync() { p.Event("CursorMove", stage); return inner.MoveNextAsync(); }
            public ValueTask DisposeAsync()
            {
                if (stage == "Upstream") p.UpstreamDisposals++; else p.WrapperDisposals++;
                p.Event("CursorDispose", stage); return inner.DisposeAsync();
            }
        }
    }
}
