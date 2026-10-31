using Zendiator.Tests.Contracts;

namespace Zendiator.Generator.Tests;

public sealed class CanonicalStreamTypeChangeProbe
{
    internal object Request = null!;
    internal readonly ContractOnce<long> Gate = new();
    internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal readonly List<string> Trace = [];
    internal int Starts, Moves, Reads, Disposals, SyncCalls, AsyncCalls;
    public void Resolve(string stage) => Trace.Add("Resolve:" + stage);
    private void Check(object request, CancellationToken token)
    {
        if (!ReferenceEquals(request, Request) || token != default)
            throw new InvalidOperationException("Type-change request/token changed.");
    }
    public IAsyncEnumerable<int> Start(object request, CancellationToken token)
    {
        Check(request, token);
        Starts++;
        Trace.Add("H.Start");
        return new Source(this);
    }
    public string? First(object request, int item, CancellationToken token)
    {
        Check(request, token);
        if (item != 7)
            throw new InvalidOperationException();
        SyncCalls++;
        Trace.Add("Sync:int7->string:null");
        return null;
    }
    public string? FirstText(object request, string? item, CancellationToken token)
    {
        Check(request, token);
        if (item != "7")
            throw new InvalidOperationException();
        SyncCalls++;
        Trace.Add("Sync:int7->string:null");
        return null;
    }
    public ValueTask<long> Last(object request, string? item, CancellationToken token)
    {
        Check(request, token);
        if (item != null)
            throw new InvalidOperationException("Nullable intermediate changed.");
        AsyncCalls++;
        Trace.Add("Async:string:null->long:pending");
        Entered.TrySetResult();
        return Gate.Task;
    }
    private sealed class Source(CanonicalStreamTypeChangeProbe p) : IAsyncEnumerable<int>, IAsyncEnumerator<int>
    {
        private int _index;
        public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken token = default)
        {
            if (token != default)
                throw new InvalidOperationException();
            p.Trace.Add("H.Get");
            return this;
        }
        public int Current
        {
            get
            {
                p.Reads++;
                p.Trace.Add("H.Current:7");
                return 7;
            }
        }
        public ValueTask<bool> MoveNextAsync()
        {
            p.Moves++;
            var result = _index++ == 0;
            p.Trace.Add("H.Move:" + result);
            return new(result);
        }
        public ValueTask DisposeAsync()
        {
            p.Disposals++;
            p.Trace.Add("H.Dispose");
            return default;
        }
    }
}
