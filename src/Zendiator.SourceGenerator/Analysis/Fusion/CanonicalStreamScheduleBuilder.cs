using System.Collections.Generic;
using System.Linq;
using static Zendiator.SourceGenerator.StreamExecutionNames;

namespace Zendiator.SourceGenerator;

internal static class CanonicalStreamScheduleBuilder
{
    internal static CanonicalStreamExecutionSchedule Build(BoundPipelineModel route) => Build(new StreamSegmentContract(route.Anchor, StreamSourceKind.Handler, route.Handlers[0].Callback!, route.Stages));

    internal static CanonicalStreamExecutionSchedule Build(StreamSegmentContract contract)
    {
        var resolve = contract.SourceKind == StreamSourceKind.Handler ? StreamPhase.ResolveHandler : StreamPhase.ResolveWrapper;
        var factory = contract.SourceKind == StreamSourceKind.Handler ? StreamPhase.SourceFactory : StreamPhase.WrapperFactory;
        var sourceName = contract.SourceKind == StreamSourceKind.Handler ? "handler" : "wrapper";
        var sites = new Dictionary<StreamSiteId, StreamExecutionSite>();
        var edges = new Dictionary<StreamProgram, List<StreamControlEdge>>();
        var entries = new Dictionary<StreamProgram, StreamSiteId>();
        var awaits = new List<StreamExecutionAwait>();
        var transfers = new List<StreamValueTransfer>();
        var failures = new List<StreamFailurePolicy>();
        var owners = new List<StreamExecutionOwner> {
            new("provider", null, StreamOwnerKind.Provider, StreamOwnerLifetime.Caller, true, StreamCleanup.None),
            new("envelope", "provider", StreamOwnerKind.Envelope, StreamOwnerLifetime.Envelope, false, StreamCleanup.None),
            new("cursor", "envelope", StreamOwnerKind.Cursor, StreamOwnerLifetime.EachEnumeration, false, StreamCleanup.None),
            new("source", "cursor", StreamOwnerKind.SourceCursor, StreamOwnerLifetime.EachEnumeration, false, StreamCleanup.AsyncDispose),
            new("linked-token", "cursor", StreamOwnerKind.LinkedToken, StreamOwnerLifetime.EachEnumeration, false, StreamCleanup.Dispose),
            new(Service(-1), "provider", StreamOwnerKind.Service, StreamOwnerLifetime.Caller, true, StreamCleanup.None)
        };
        Begin(StreamProgram.Create, StreamPhase.NullGuard);
        Edge(StreamProgram.Create, StreamPhase.NullGuard, StreamPhase.CreateEnvelope, StreamOutcome.Success);
        Edge(StreamProgram.Create, StreamPhase.NullGuard, StreamPhase.NullFailure, StreamOutcome.Failure);
        Edge(StreamProgram.Create, StreamPhase.CreateEnvelope, StreamPhase.EnvelopeReturn);
        Transfer(StreamProgram.Create, StreamPhase.CreateEnvelope, "api/request", "envelope/request");
        Transfer(StreamProgram.Create, StreamPhase.CreateEnvelope, "api/token", "envelope/api-token");
        Begin(StreamProgram.GetEnumerator, StreamPhase.CreateCursor);
        Edge(StreamProgram.GetEnumerator, StreamPhase.CreateCursor, StreamPhase.CursorReturn);
        Transfer(StreamProgram.GetEnumerator, StreamPhase.CreateCursor, "envelope/request", "cursor/request");
        Transfer(StreamProgram.GetEnumerator, StreamPhase.CreateCursor, "envelope/api-token", "cursor/api-token");
        Transfer(StreamProgram.GetEnumerator, StreamPhase.CreateCursor, "enumerator/token", "cursor/enumerator-token");

        var move = StreamProgram.Move;
        Begin(move, StreamPhase.MoveGuard);
        Edge(move, StreamPhase.MoveGuard, StreamPhase.ClearCurrent, StreamOutcome.Success);
        Edge(move, StreamPhase.MoveGuard, StreamPhase.BusyFailure, StreamOutcome.Failure);
        Edge(move, StreamPhase.ClearCurrent, StreamPhase.Stopped);
        Edge(move, StreamPhase.Stopped, StreamPhase.ReleaseFalse, StreamOutcome.True);
        Edge(move, StreamPhase.Stopped, StreamPhase.Started, StreamOutcome.False);
        Edge(move, StreamPhase.Started, StreamPhase.SourceGuard, StreamOutcome.True);
        Edge(move, StreamPhase.Started, StreamPhase.MergeTokens, StreamOutcome.False);
        var startup = new[] { StreamPhase.MergeTokens, StreamPhase.PreCancel, resolve,
            factory, StreamPhase.AcquireSource };
        for (var i = 0; i < startup.Length; i++)
        {
            Edge(move, startup[i], i + 1 < startup.Length ? startup[i + 1] : StreamPhase.MarkStarted, StreamOutcome.Success);
            Fault(move, startup[i], StreamPhase.StartupDetachLink);
        }
        Transfer(move, StreamPhase.MergeTokens, "token-selection/result", "cursor/start-token", StreamOutcome.Success);
        Transfer(move, factory, "cursor/request", sourceName + "/request", StreamOutcome.Success);
        Transfer(move, factory, "cursor/start-token", sourceName + "/token", StreamOutcome.Success);
        Transfer(move, StreamPhase.AcquireSource, sourceName + "/enumerable", "cursor/source", StreamOutcome.Success);
        // Handler sources consume the execution token. An opaque Wrapper has already received it
        // in Wrap; forwarding it to a directly returned Open envelope would alter its selected token.
        Transfer(move, StreamPhase.AcquireSource, contract.SourceKind == StreamSourceKind.Handler ? "cursor/start-token" : "CancellationToken.None", "source/token", StreamOutcome.Success);
        Edge(move, StreamPhase.MarkStarted, StreamPhase.SourceGuard);
        Edge(move, StreamPhase.StartupDetachLink, StreamPhase.StartupLinkPresent);
        Edge(move, StreamPhase.StartupLinkPresent, StreamPhase.StartupDisposeLink, StreamOutcome.True);
        Edge(move, StreamPhase.StartupLinkPresent, StreamPhase.EndFailure, StreamOutcome.False);
        Edge(move, StreamPhase.StartupDisposeLink, StreamPhase.EndFailure, StreamOutcome.Success);
        Fault(move, StreamPhase.StartupDisposeLink, StreamPhase.EndFailure);
        Edge(move, StreamPhase.SourceGuard, StreamPhase.SourceMove, StreamOutcome.Success);
        Fault(move, StreamPhase.SourceGuard, StreamPhase.EndFailure);
        Edge(move, StreamPhase.SourceMove, StreamPhase.SourceCurrent, StreamOutcome.True);
        Edge(move, StreamPhase.SourceMove, StreamPhase.EndFalse, StreamOutcome.False);
        Fault(move, StreamPhase.SourceMove, StreamPhase.EndFailure);
        Edge(move, StreamPhase.SourceCurrent, StreamPhase.CacheCurrent, StreamOutcome.Success);
        Fault(move, StreamPhase.SourceCurrent, StreamPhase.EndFailure);
        Await(StreamProgram.Move, StreamPhase.SourceMove, "bool", "source-result", "ended-failure", new[] { "cursor/request", "cursor/start-token", "cursor/source", "cursor/hook-cache" });
        Transfer(move, StreamPhase.SourceCurrent, "source/Current", "cursor/item", StreamOutcome.Success);

        // Insert declaration-ordered, lazy item stages between Current and the cache publication.
        var previous = Id(move, StreamPhase.SourceCurrent);
        for (var i = 0; i < contract.Hooks.Count; i++)
        {
            var stage = contract.Hooks[i];
            owners.Add(new(Service(i), "provider", StreamOwnerKind.Service, StreamOwnerLifetime.Caller, true, StreamCleanup.None));
            var guard = Id(move, StreamPhase.HookGuard, i);
            var callback = Id(move, stage.Callbacks[0].Phase == BoundCallbackPhase.Filter ? StreamPhase.Filter : StreamPhase.Map, i);
            // Rewire only the successful previous item path; all its failures/skip paths stay intact.
            var old = edges[move].Single(e => e.From == previous && e.To.Phase == StreamPhase.CacheCurrent);
            edges[move].Remove(old);
            Link(previous, guard, old.Outcome);
            Link(guard, Id(move, StreamPhase.HookCached, i), StreamOutcome.Success);
            Fault(move, StreamPhase.HookGuard, StreamPhase.EndFailure, i);
            Link(Id(move, StreamPhase.HookCached, i), callback, StreamOutcome.True);
            Link(Id(move, StreamPhase.HookCached, i), Id(move, StreamPhase.ResolveHook, i), StreamOutcome.False);
            Link(Id(move, StreamPhase.ResolveHook, i), callback, StreamOutcome.Success);
            Fault(move, StreamPhase.ResolveHook, StreamPhase.EndFailure, i);
            Link(callback, Id(move, StreamPhase.CacheCurrent), callback.Phase == StreamPhase.Filter ? StreamOutcome.True : StreamOutcome.Success);
            if (callback.Phase == StreamPhase.Filter) Link(callback, Id(move, StreamPhase.SourceGuard), StreamOutcome.False);
            Fault(move, callback.Phase, StreamPhase.EndFailure, i);
            Transfer(move, callback.Phase, "cursor/item", HookItem(i), StreamOutcome.Always, i);
            Transfer(move, callback.Phase, "cursor/request", "hook:" + i + "/request", StreamOutcome.Always, i);
            Transfer(move, callback.Phase, "cursor/start-token", "hook:" + i + "/token", StreamOutcome.Always, i);
            if (callback.Phase == StreamPhase.Map) Transfer(move, callback.Phase, "hook:" + i + "/result", "cursor/item", StreamOutcome.Success, i);
            previous = callback;
        }
        Transfer(move, StreamPhase.CacheCurrent, "cursor/item", "cursor/current-cache");
        Edge(move, StreamPhase.CacheCurrent, StreamPhase.ReleaseTrue);
        Edge(move, StreamPhase.EndFalse, StreamPhase.ReleaseFalse);
        Edge(move, StreamPhase.EndFailure, StreamPhase.ReleaseFailure);
        Edge(move, StreamPhase.ReleaseTrue, StreamPhase.MoveTrue);
        Edge(move, StreamPhase.ReleaseFalse, StreamPhase.MoveFalse);
        Edge(move, StreamPhase.ReleaseFailure, StreamPhase.MoveFailure);

        Begin(StreamProgram.Current, StreamPhase.CurrentGuard);
        Edge(StreamProgram.Current, StreamPhase.CurrentGuard, StreamPhase.ReadCache, StreamOutcome.True);
        Edge(StreamProgram.Current, StreamPhase.CurrentGuard, StreamPhase.CurrentFailure, StreamOutcome.False);
        Transfer(StreamProgram.Current, StreamPhase.ReadCache, "cursor/current-cache", "consumer/Current");

        var dispose = StreamProgram.Dispose;
        Begin(dispose, StreamPhase.DisposeGuard);
        Edge(dispose, StreamPhase.DisposeGuard, StreamPhase.AlreadyDisposed, StreamOutcome.Success);
        Edge(dispose, StreamPhase.DisposeGuard, StreamPhase.BusyFailure, StreamOutcome.Failure);
        Edge(dispose, StreamPhase.AlreadyDisposed, StreamPhase.DisposeSuccess, StreamOutcome.True);
        Edge(dispose, StreamPhase.AlreadyDisposed, StreamPhase.MarkDisposed, StreamOutcome.False);
        Edge(dispose, StreamPhase.MarkDisposed, StreamPhase.DetachSource);
        Edge(dispose, StreamPhase.DetachSource, StreamPhase.DetachLink);
        Edge(dispose, StreamPhase.DetachLink, StreamPhase.SourcePresent);
        Edge(dispose, StreamPhase.SourcePresent, StreamPhase.DisposeSource, StreamOutcome.True);
        Edge(dispose, StreamPhase.SourcePresent, StreamPhase.LinkPresent, StreamOutcome.False);
        Edge(dispose, StreamPhase.DisposeSource, StreamPhase.LinkPresent, StreamOutcome.Success);
        Fault(dispose, StreamPhase.DisposeSource, StreamPhase.LinkPresent);
        Edge(dispose, StreamPhase.LinkPresent, StreamPhase.DisposeLink, StreamOutcome.True);
        Edge(dispose, StreamPhase.LinkPresent, StreamPhase.ReleaseDispose, StreamOutcome.False);
        Edge(dispose, StreamPhase.DisposeLink, StreamPhase.ReleaseDispose, StreamOutcome.Success);
        Fault(dispose, StreamPhase.DisposeLink, StreamPhase.ReleaseDispose);
        Edge(dispose, StreamPhase.ReleaseDispose, StreamPhase.DisposeResult);
        Edge(dispose, StreamPhase.DisposeResult, StreamPhase.DisposeFailure, StreamOutcome.True);
        Edge(dispose, StreamPhase.DisposeResult, StreamPhase.DisposeSuccess, StreamOutcome.False);
        Transfer(dispose, StreamPhase.DetachSource, "cursor/source", "dispose/source");
        Transfer(dispose, StreamPhase.DetachLink, "cursor/linked-token", "dispose/linked-token");
        Transfer(move, StreamPhase.StartupDetachLink, "cursor/linked-token", "startup/linked-token");
        Await(dispose, StreamPhase.DisposeSource, "void", "dispose-link", "replace-and-dispose-link", new[] { "dispose/source", "dispose/linked-token" });
        return new(new(sites.Values), new(entries.Select(e => new StreamControlFlow(e.Key, e.Value, new(edges[e.Key])))),
            new(owners), new(new[] {
                new StreamTokenRule(StreamTokenCase.ApiNotCancelable, StreamTokenSource.Enumerator, false),
                new StreamTokenRule(StreamTokenCase.EnumeratorNotCancelable, StreamTokenSource.Api, false),
                new StreamTokenRule(StreamTokenCase.SameToken, StreamTokenSource.Api, false),
                new StreamTokenRule(StreamTokenCase.DistinctCancelable, StreamTokenSource.Linked, true)
            }), new(awaits), new(transfers), new(failures), "stream-call-caller-v1", true, false, false);

        void Begin(StreamProgram program, StreamPhase phase) { entries.Add(program, Id(program, phase)); edges.Add(program, new()); }
        void Edge(StreamProgram program, StreamPhase from, StreamPhase to, StreamOutcome outcome = StreamOutcome.Always) => Link(Id(program, from), Id(program, to), outcome);
        void Link(StreamSiteId from, StreamSiteId to, StreamOutcome outcome) { Add(from); Add(to); edges[from.Program].Add(new(from, to, outcome)); }
        void Add(StreamSiteId id)
        {
            if (sites.ContainsKey(id)) return;
            var completion = id.Phase is StreamPhase.SourceMove or StreamPhase.DisposeSource ? BoundCompletionKind.ValueTask : BoundCompletionKind.Synchronous;
            var owner = id.Program == StreamProgram.Create ? "envelope" : id.Phase is StreamPhase.SourceMove or StreamPhase.SourceCurrent or StreamPhase.DisposeSource ? "source" :
                id.Phase is StreamPhase.StartupDisposeLink or StreamPhase.DisposeLink ? "linked-token" : "cursor";
            var producer = id.Phase is StreamPhase.SourceFactory or StreamPhase.WrapperFactory ? contract.Callback.Implementation :
                id.Phase is StreamPhase.Filter or StreamPhase.Map ? contract.Hooks[id.Stage].Callbacks[0].Implementation :
                id.Phase == StreamPhase.SourceMove ? "IAsyncEnumerator.MoveNextAsync" : id.Phase == StreamPhase.SourceCurrent ? "IAsyncEnumerator.Current" :
                id.Phase == StreamPhase.DisposeSource ? "IAsyncDisposable.DisposeAsync" : null;
            sites.Add(id, new(id, id.Stage >= 0 ? contract.Hooks[id.Stage].Anchor : contract.Anchor, owner, completion, producer));
        }
        void Fault(StreamProgram program, StreamPhase phase, StreamPhase next, int stage = -1)
        {
            var id = Id(program, phase, stage);
            Link(id, Id(program, next), StreamOutcome.Failure);
            failures.Add(new(id, true, true, true, false));
            transfers.Add(new("producer/error", "cursor/error", id, StreamOutcome.Failure));
        }
        void Transfer(StreamProgram program, StreamPhase phase, string from, string to, StreamOutcome outcome = StreamOutcome.Always, int stage = -1) => transfers.Add(new(from, to, Id(program, phase, stage), outcome));
        void Await(StreamProgram program, StreamPhase phase, string result, string normal, string failure, IEnumerable<string> slots) => awaits.Add(new(Id(program, phase), result, true, true, normal, failure, new(slots)));
    }
}
