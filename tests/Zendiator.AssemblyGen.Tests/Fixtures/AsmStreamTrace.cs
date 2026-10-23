using global::Zendiator;

namespace Zendiator.AssemblyGen.Tests;

public sealed class AsmStreamTrace(Trace trace) : IStreamMiddleware<AsmNumbers, int>
{
    public async ValueTask<IAsyncEnumerator<int>> OpenAsync(AsmNumbers request, IStreamUpstream<AsmNumbers, int> upstream, CancellationToken cancellationToken)
    {
        trace.Events.Add("s-before");
        try
        {
            return new TracedCursor(await upstream.OpenAsync(request, cancellationToken), trace);
        }
        catch { trace.Events.Add("s-after"); throw; }
    }

    private sealed class TracedCursor(IAsyncEnumerator<int> inner, Trace trace) : IAsyncEnumerator<int>
    {
        public int Current => inner.Current;
        public ValueTask<bool> MoveNextAsync() => inner.MoveNextAsync();
        public async ValueTask DisposeAsync()
        {
            try { await inner.DisposeAsync(); }
            finally { trace.Events.Add("s-after"); }
        }
    }
}
