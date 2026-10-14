using Zendiator.Tests.Contracts;

namespace Zendiator.Generator.Tests;

public sealed class CanonicalStreamCleanupProbe
{
    internal object Request = null!;
    internal object BorrowedResult = null!;
    internal readonly List<string> Events = [];
    internal readonly ContractOnce<bool> InnerGate = new(), OuterGate = new();
    internal readonly TaskCompletionSource InnerEntered = new(TaskCreationOptions.RunContinuationsAsynchronously), OuterEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal readonly FormatException InnerError = new("inner cleanup failed");
    internal readonly ApplicationException OuterError = new("outer cleanup failed");
    internal int InnerCalls, OuterCalls, BorrowedCalls, FactoryCalls, FactoryDisposals, SourceDisposals, Starts, Moves, Reads;
    public void Resolve(string role) => Events.Add("DI.Resolve:" + role);
    public void DisposeFactory(string role) { FactoryDisposals++; Events.Add("Factory.Dispose:" + role); }
    private void Check(object request, CancellationToken token)
    { if (!ReferenceEquals(request, Request) || token != default) throw new InvalidOperationException("Cleanup request/token changed."); }
    public void Factory(string role, object request, CancellationToken token)
    { Check(request, token); FactoryCalls++; Events.Add("Factory.Create:" + role); }
    public void RegisterBorrowed(object result)
    { if (BorrowedResult != null) throw new InvalidOperationException(); BorrowedResult = result; Events.Add("DI.CreateResult:H"); }
    public ValueTask DisposeBorrowed(object result)
    {
        if (!ReferenceEquals(result, BorrowedResult)) throw new InvalidOperationException();
        BorrowedCalls++; Events.Add("DI.DisposeResult:H"); return default;
    }
    public ValueTask DisposeOwned(string role)
    {
        if (role == "I") { InnerCalls++; Events.Add("Cleanup.Pending:I"); InnerEntered.TrySetResult(); return InnerGate.Void; }
        OuterCalls++; Events.Add("Cleanup.Pending:O"); OuterEntered.TrySetResult(); return OuterGate.Void;
    }
    public int Map(string role, object request, int item, CancellationToken token)
    { Check(request, token); Events.Add("Map:" + role + ":" + item + "->" + (item + 1)); return item + 1; }
    public IAsyncEnumerable<int> Start(object receiver, object request, CancellationToken token)
    {
        Check(request, token); if (!ReferenceEquals(receiver, BorrowedResult)) throw new InvalidOperationException("Handler is not the DI-owned borrowed result.");
        Starts++; Events.Add("H.Start"); return new Source(this);
    }
    private sealed class Source(CanonicalStreamCleanupProbe p) : IAsyncEnumerable<int>, IAsyncEnumerator<int>
    {
        private int _index;
        public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken token = default)
        { if (token != default) throw new InvalidOperationException(); p.Events.Add("H.Get"); return this; }
        public int Current { get { p.Reads++; p.Events.Add("H.Current:1"); return 1; } }
        public ValueTask<bool> MoveNextAsync() { p.Moves++; var value = _index++ == 0; p.Events.Add("H.Move:" + value); return new(value); }
        public ValueTask DisposeAsync() { p.SourceDisposals++; p.Events.Add("H.Dispose"); return default; }
    }
}
