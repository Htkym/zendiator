using Zendiator.Tests.Contracts;

namespace Zendiator.Generator.Tests;

// Public only so the single generated consumer assembly can call these callbacks.
public sealed class CanonicalStreamAsyncItemProbe
{
    internal readonly ContractOnce<bool> FirstGate = new();
    internal readonly ContractOnce<int> MapGate = new();
    internal readonly ContractOnce<bool> DisposeGate = new();
    internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal readonly TaskCompletionSource Closing = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal readonly FormatException HookError = new("C10 hook failure");
    internal readonly ApplicationException CleanupError = new("C10 cleanup replacement");
    internal string Profile { get; init; } = "";
    internal object Request { get; init; } = null!;
    internal CancellationToken Token { get; init; }
    internal StreamSeedEvents Events { get; init; } = null!;
    internal int Starts, Moves, Reads, Disposals, LastCalls;
    internal readonly List<string> BorrowedDisposals = new();
    internal bool FirstPending => Profile is "pending-filter" or "filter-drop";
    internal bool MapPending => Profile is "pending-map" or "failure-cleanup" or "cancel-map";

    internal void Event(string kind, string stage, int? input = null, int? output = null, bool? result = null, Exception? error = null) =>
        Events.Add(new(kind, stage, Request, Token, Input: input, Output: output, Result: result, Error: error));
    public void Resolve(string stage) => Event("Resolve", stage);
    public void DisposeBorrowed(string stage) { BorrowedDisposals.Add(stage); Event("BorrowedDispose", stage); }
    public IAsyncEnumerable<int> Start(object request, CancellationToken token)
    {
        Check(request, token); Starts++; Event("HandlerStart", "H"); return new Source(this);
    }
    public ValueTask<bool> First(object request, int item, CancellationToken token)
    {
        Check(request, token); Event("Filter", "F1", item);
        if (FirstPending) Entered.TrySetResult(); else FirstGate.Complete(true);
        return FirstGate.Task;
    }
    public ValueTask<int> Map(object request, int item, CancellationToken token)
    {
        Check(request, token); Event("Map", "Map", item);
        if (MapPending) Entered.TrySetResult(); else MapGate.Complete(checked(item * 2));
        return MapGate.Task;
    }
    public bool Last(object request, int item, CancellationToken token)
    {
        Check(request, token); LastCalls++; var result = item < 4; Event("Filter", "F2", item, result: result); return result;
    }
    private void Check(object request, CancellationToken token)
    { if (!ReferenceEquals(request, Request) || token != Token) throw new InvalidOperationException("C10 request/token identity changed."); }
    private sealed class Source(CanonicalStreamAsyncItemProbe p) : IAsyncEnumerable<int>, IAsyncEnumerator<int>
    {
        private int _index;
        public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken token = default)
        { p.Check(p.Request, token); p.Event("EnumeratorAcquired", "H"); return this; }
        public int Current { get { p.Reads++; p.Event("InnerCurrent", "H", output: 1); return 1; } }
        public ValueTask<bool> MoveNextAsync()
        { p.Moves++; var result = _index++ == 0; p.Event("InnerMove", "H", result: result); return new(result); }
        public ValueTask DisposeAsync()
        {
            p.Disposals++; p.Event("InnerDisposeStart", "H");
            if (p.Profile == "failure-cleanup") { p.Closing.TrySetResult(); return p.DisposeGate.Void; }
            p.Event("InnerDisposeEnd", "H"); return default;
        }
    }
}
