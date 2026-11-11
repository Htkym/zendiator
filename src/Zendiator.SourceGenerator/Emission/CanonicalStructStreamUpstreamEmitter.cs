using System.Text;

namespace Zendiator.SourceGenerator;

internal static class CanonicalStructStreamUpstreamEmitter
{
    internal static bool Enabled(CanonicalStreamFallbackSegment segment) => segment.Wrapper?.StructStreamCall != null;

    internal static void EmitOwner(StringBuilder b, BoundStreamHandlerEmission handler,
        CanonicalStreamFallbackSegment segment, bool identity)
    {
        var upstream = segment.Upstream!.Value;
        var item = segment.SourceItemName ?? handler.ItemName;
        b.AppendLine($$"""
            private Cursor{{upstream}}? _upstreamActive;
            private int _upstreamOpening;
            internal global::System.Threading.Tasks.ValueTask<global::System.Collections.Generic.IAsyncEnumerator<{{item}}>> OpenUpstreamAsync(
                {{handler.RequestName}} request, global::System.Threading.CancellationToken token)
            {
                if (global::System.Threading.Interlocked.CompareExchange(ref _upstreamOpening, 1, 0) != 0)
                    throw new global::System.InvalidOperationException("Await the preceding Stream open.");
                try
                {
                    if (IsClosed || _root.IsClosed) throw new global::System.ObjectDisposedException("Stream");
                    if (_upstreamActive is { CanReopen: false }) throw new global::System.InvalidOperationException("The preceding Stream cursor must reach normal EOF and finish cleanup before reopening.");
                    {{(handler.RequestIsValueType ? "" : "global::System.ArgumentNullException.ThrowIfNull(request);")}}
                    var cursor = new Cursor{{upstream}}(_services, request, token, default, _root);
                    _upstreamActive = cursor;
                    return cursor.OpenForMiddlewareAsync(new Upstream{{segment.Id}}(this));
                }
                catch { global::System.Threading.Volatile.Write(ref _upstreamOpening, 0); throw; }
            }
            internal void ReleaseUpstreamOpening() => global::System.Threading.Volatile.Write(ref _upstreamOpening, 0);
            private global::System.Threading.Tasks.ValueTask DisposeActiveUpstreamAsync()
            {
                var active = _upstreamActive; _upstreamActive = null;
                return active is null ? default : DisposeActiveUpstreamCoreAsync(active);
            }
            private static async global::System.Threading.Tasks.ValueTask DisposeActiveUpstreamCoreAsync(Cursor{{upstream}} active)
            {
                await active.DisposeAsync().ConfigureAwait(false);
            }
            """);
        if (identity)
            b.AppendLine($$"""
                private Cursor{{upstream}}? MatchUpstreamIdentity(global::System.Collections.Generic.IAsyncEnumerator<{{item}}> source)
                { var active = _upstreamActive; return global::System.Object.ReferenceEquals(source, active) ? active : null; }
                private void DetachUpstreamIdentity(Cursor{{upstream}} child)
                { if (global::System.Object.ReferenceEquals(_upstreamActive, child)) _upstreamActive = null; }
                """);
    }

    internal static void EmitPort(StringBuilder b, BoundStreamHandlerEmission handler, CanonicalStreamFallbackSegment segment)
    {
        var item = segment.SourceItemName ?? handler.ItemName;
        b.AppendLine($$"""
            private readonly struct Upstream{{segment.Id}} : global::Zendiator.IStreamUpstream<{{handler.RequestName}}, {{item}}>
            {
                private readonly Cursor{{segment.Id}}? _owner;
                internal Upstream{{segment.Id}}(Cursor{{segment.Id}} owner) => _owner = owner;
                public global::System.Threading.Tasks.ValueTask<global::System.Collections.Generic.IAsyncEnumerator<{{item}}>> OpenAsync(
                    {{handler.RequestName}} request, global::System.Threading.CancellationToken token) =>
                    (_owner ?? throw new global::System.InvalidOperationException("The Stream upstream is uninitialized.")).OpenUpstreamAsync(request, token);
                internal void ReleaseOpening() => _owner!.ReleaseUpstreamOpening();
            }
            """);
    }
}
