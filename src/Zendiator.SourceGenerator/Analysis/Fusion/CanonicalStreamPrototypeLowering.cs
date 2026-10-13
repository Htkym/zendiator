using System.Collections.Generic;
using System.Linq;

namespace Zendiator.SourceGenerator;

// Only the selected borrowed, same-item region is supported. Any unknown edge/operand rejects emission.
internal static class CanonicalStreamPrototypeLowering
{
    internal static (CanonicalStreamItemRegion Item, CanonicalStreamSourceCleanupRegion Cleanup)? Create(
        BoundPipelineModel route, CanonicalStreamExecutionSchedule schedule)
    {
        // Revalidate current values: a cached graph's IsValid must not authorize a mutated schedule.
        if (CanonicalStreamGraphValidator.Validate(route, schedule).Count != 0) return null;
        var sites = schedule.Sites.ToDictionary(s => s.Id);
        var move = schedule.Flows.Single(f => f.Program == StreamProgram.Move);
        var dispose = schedule.Flows.Single(f => f.Program == StreamProgram.Dispose);
        var steps = new List<StreamExecutionSite>();
        StreamSiteId Id(StreamProgram p, StreamPhase phase, int stage = -1) => new(p, phase, stage);
        StreamSiteId? Next(StreamControlFlow flow, StreamSiteId from, StreamOutcome outcome)
        {
            var edges = flow.Edges.Where(e => e.From == from && e.Outcome == outcome).ToArray();
            return edges.Length == 1 ? edges[0].To : null;
        }
        bool Goes(StreamControlFlow flow, StreamSiteId from, StreamOutcome outcome, StreamSiteId to) => Next(flow, from, outcome) == to;
        bool Transfer(StreamSiteId at, string from, string to, StreamOutcome outcome) =>
            schedule.Transfers.Count(t => t.At == at && t.Source == from && t.Destination == to && t.Outcome == outcome) == 1;
        bool Failed(StreamControlFlow flow, StreamSiteId at, StreamSiteId target) =>
            Goes(flow, at, StreamOutcome.Failure, target) && schedule.Failures.Any(f => f.At == at &&
                f.PreserveIdentityAndToken && f.EscapingOceIsCanceled && f.ReplacesPreviousError && !f.Aggregate);
        var guard = Id(StreamProgram.Move, StreamPhase.SourceGuard);
        var sourceMove = Id(StreamProgram.Move, StreamPhase.SourceMove);
        var current = Id(StreamProgram.Move, StreamPhase.SourceCurrent);
        var failure = Id(StreamProgram.Move, StreamPhase.EndFailure);
        if (!Goes(move, guard, StreamOutcome.Success, sourceMove) || !Failed(move, guard, failure) ||
            !Goes(move, sourceMove, StreamOutcome.True, current) ||
            !Goes(move, sourceMove, StreamOutcome.False, Id(StreamProgram.Move, StreamPhase.EndFalse)) ||
            !Failed(move, sourceMove, failure) || !Failed(move, current, failure)) return null;
        var read = schedule.Transfers.SingleOrDefault(t => t.At == current && t.Source == "source/Current" &&
            t.Destination == "cursor/item" && t.Outcome == StreamOutcome.Success);
        var moveAwait = schedule.Awaits.SingleOrDefault(a => a.Producer == sourceMove);
        if (read == null || moveAwait == null || moveAwait.ResultType != "bool" || !moveAwait.ConsumeOnce || !moveAwait.ConfigureAwaitFalse) return null;
        steps.Add(sites[guard]); steps.Add(sites[sourceMove]); steps.Add(sites[current]);
        var previous = current; var success = StreamOutcome.Success; var visited = new HashSet<int>();
        while (true)
        {
            var next = Next(move, previous, success);
            if (next == Id(StreamProgram.Move, StreamPhase.CacheCurrent)) break;
            if (next is not { Phase: StreamPhase.HookGuard, Program: StreamProgram.Move } hookGuard ||
                !visited.Add(hookGuard.Stage) || hookGuard.Stage < 0 || hookGuard.Stage >= route.Stages.Count) return null;
            var stage = route.Stages[hookGuard.Stage];
            if (stage.StreamEmission == null) return null;
            var cached = Id(StreamProgram.Move, StreamPhase.HookCached, hookGuard.Stage);
            var resolve = Id(StreamProgram.Move, StreamPhase.ResolveHook, hookGuard.Stage);
            var callback = Id(StreamProgram.Move, stage.StreamEmission.Phase == BoundCallbackPhase.Filter ? StreamPhase.Filter : StreamPhase.Map, hookGuard.Stage);
            if (!Goes(move, hookGuard, StreamOutcome.Success, cached) || !Failed(move, hookGuard, failure) ||
                !Goes(move, cached, StreamOutcome.False, resolve) || !Goes(move, cached, StreamOutcome.True, callback) ||
                !Goes(move, resolve, StreamOutcome.Success, callback) || !Failed(move, resolve, failure) || !Failed(move, callback, failure) ||
                !Transfer(callback, "cursor/item", StreamExecutionNames.HookItem(hookGuard.Stage), StreamOutcome.Always) ||
                !Transfer(callback, "cursor/request", "hook:" + hookGuard.Stage + "/request", StreamOutcome.Always) ||
                !Transfer(callback, "cursor/start-token", "hook:" + hookGuard.Stage + "/token", StreamOutcome.Always) ||
                !schedule.Owners.Any(o => o.Id == StreamExecutionNames.Service(hookGuard.Stage) && o.Borrowed && o.Cleanup == StreamCleanup.None)) return null;
            if (callback.Phase == StreamPhase.Filter)
            { if (!Goes(move, callback, StreamOutcome.False, guard)) return null; success = StreamOutcome.True; }
            else
            {
                if (!Transfer(callback, "hook:" + hookGuard.Stage + "/result", "cursor/item", StreamOutcome.Success)) return null;
                success = StreamOutcome.Success;
            }
            steps.Add(sites[hookGuard]); steps.Add(sites[resolve]); steps.Add(sites[callback]); previous = callback;
        }
        var publish = Id(StreamProgram.Move, StreamPhase.CacheCurrent);
        if (visited.Count != route.Stages.Count || !Transfer(publish, "cursor/item", "cursor/current-cache", StreamOutcome.Always) ||
            !Goes(move, publish, StreamOutcome.Always, Id(StreamProgram.Move, StreamPhase.ReleaseTrue)) ||
            !Goes(move, Id(StreamProgram.Move, StreamPhase.ReleaseTrue), StreamOutcome.Always, Id(StreamProgram.Move, StreamPhase.MoveTrue))) return null;
        steps.Add(sites[publish]);
        var detach = Id(StreamProgram.Dispose, StreamPhase.DetachSource);
        var detachLink = Id(StreamProgram.Dispose, StreamPhase.DetachLink);
        var present = Id(StreamProgram.Dispose, StreamPhase.SourcePresent);
        var clean = Id(StreamProgram.Dispose, StreamPhase.DisposeSource);
        var link = Id(StreamProgram.Dispose, StreamPhase.LinkPresent);
        if (!Goes(dispose, detach, StreamOutcome.Always, detachLink) || !Goes(dispose, detachLink, StreamOutcome.Always, present) ||
            !Goes(dispose, present, StreamOutcome.True, clean) || !Goes(dispose, present, StreamOutcome.False, link) ||
            !Goes(dispose, clean, StreamOutcome.Success, link) || !Failed(dispose, clean, link)) return null;
        var detachTransfer = schedule.Transfers.SingleOrDefault(t => t.At == detach && t.Source == "cursor/source" &&
            t.Destination == "dispose/source" && t.Outcome == StreamOutcome.Always);
        var cleanupAwait = schedule.Awaits.SingleOrDefault(a => a.Producer == clean);
        var sourceOwner = schedule.Owners.SingleOrDefault(o => o.Id == "source" && o.Kind == StreamOwnerKind.SourceCursor &&
            o.Lifetime == StreamOwnerLifetime.EachEnumeration && !o.Borrowed && o.Cleanup == StreamCleanup.AsyncDispose);
        if (detachTransfer == null || cleanupAwait == null || sourceOwner == null || cleanupAwait.ResultType != "void" ||
            !cleanupAwait.ConsumeOnce || !cleanupAwait.ConfigureAwaitFalse) return null;
        return (new(new(steps), moveAwait, read), new(new(new[] { sites[detach], sites[present], sites[clean] }), cleanupAwait, detachTransfer, sourceOwner));
    }
}
