using System.Linq;
using static Zendiator.SourceGenerator.StreamExecutionNames;

namespace Zendiator.SourceGenerator;

/// <summary>Session requirements are derived solely from the bound segment, without reading builder output or CFG.</summary>
internal static class RequiredStreamSessionContract
{
    internal static void Append(StreamSegmentContract contract, RequiredStreamExecutionContract required)
    {
        var positions = Enumerable.Range(-1, contract.Hooks.Count + 1).Where(i => Sessions(contract, i).Count != 0).ToArray();
        if (positions.Length == 0) return;
        var retained = positions.SelectMany(i => new[] { Session(i) + "/live", Session(i) + "/detached", Service(i) + "/resolved" }).ToArray();
        for (var i = 0; i < required.Awaits.Count; i++)
            required.Awaits[i] = required.Awaits[i] with { RetainedSlots = new(required.Awaits[i].RetainedSlots.Concat(retained)) };
        foreach (var position in positions)
        {
            var session = Sessions(contract, position)[0]; var owned = session.Ownership == FusionOwnership.OwnedSession;
            var resource = Session(position);
            required.Owners.Add(new(resource, "cursor", StreamOwnerKind.Session, StreamOwnerLifetime.EachEnumeration, !owned,
                owned ? StreamCleanup.AsyncDispose : StreamCleanup.None));
            var resolve = position < 0 ? contract.SourceKind == StreamSourceKind.Handler ? StreamPhase.ResolveHandler : StreamPhase.ResolveWrapper : StreamPhase.ResolveSessionFactory;
            required.ServiceResolutions.Add(new(Id(StreamProgram.Move, resolve, position), session.FactoryServiceType, Service(position) + "/resolved"));
            var phase = position < 0 ? contract.SourceKind == StreamSourceKind.Handler ? StreamPhase.SourceFactory : StreamPhase.WrapperFactory :
                contract.Hooks[position].Callbacks[0].Phase == BoundCallbackPhase.Filter ? StreamPhase.Filter : StreamPhase.Map;
            required.CallbackReceivers.Add(new(Id(StreamProgram.Move, phase, position), resource + "/live", "cursor/request", "cursor/start-token"));
            if (position >= 0)
            {
                var removed = new[] { Id(StreamProgram.Move, StreamPhase.HookCached, position), Id(StreamProgram.Move, StreamPhase.ResolveHook, position) };
                required.Sites.RemoveAll(s => removed.Contains(s.Id)); required.Failures.RemoveAll(f => removed.Contains(f.At)); required.Transfers.RemoveAll(t => removed.Contains(t.At));
            }
            Add(StreamProgram.Move, StreamPhase.SessionGuard, position, fallible: true);
            if (position >= 0) Add(StreamProgram.Move, StreamPhase.ResolveSessionFactory, position, fallible: true);
            Add(StreamProgram.Move, StreamPhase.AcquireSession, position, session.Acquire, true);
            Add(StreamProgram.Move, StreamPhase.ValidateSessionResult, position, fallible: true);
            Add(StreamProgram.Move, StreamPhase.CommitSession, position);
            required.SessionCalls.Add(new(Id(StreamProgram.Move, StreamPhase.AcquireSession, position), Service(position) + "/resolved", resource + "/factory-result", "cursor/request", "cursor/start-token"));
            required.Transfers.Add(new(resource + "/factory-result", resource + "/live", Id(StreamProgram.Move, StreamPhase.CommitSession, position), StreamOutcome.Always));
            required.Transfers.Add(new("null", resource + "/factory-result", Id(StreamProgram.Move, StreamPhase.CommitSession, position), StreamOutcome.Always));
            foreach (var program in new[] { StreamProgram.Move, StreamProgram.Dispose })
            {
                Add(program, StreamPhase.DetachSession, position);
                required.Transfers.Add(new(resource + "/live", resource + "/detached", Id(program, StreamPhase.DetachSession, position), StreamOutcome.Always));
                required.Transfers.Add(new("null", resource + "/live", Id(program, StreamPhase.DetachSession, position), StreamOutcome.Always));
                if (!owned) continue;
                Add(program, StreamPhase.SessionPresent, position);
                Add(program, StreamPhase.CleanupSession, position, session.Cleanup!, true);
                required.SessionCalls.Add(new(Id(program, StreamPhase.CleanupSession, position), resource + "/detached", null, null, null));
            }
        }
        required.Transfers.Add(new("null", "cursor/linked-token", Id(StreamProgram.Move, StreamPhase.StartupDetachLink), StreamOutcome.Always));
        required.Transfers.Add(new("null", "cursor/source", Id(StreamProgram.Dispose, StreamPhase.DetachSource), StreamOutcome.Always));
        required.Transfers.Add(new("null", "cursor/linked-token", Id(StreamProgram.Dispose, StreamPhase.DetachLink), StreamOutcome.Always));

        void Add(StreamProgram program, StreamPhase phase, int position, BoundCallbackModel? callback = null, bool fallible = false)
        {
            var id = Id(program, phase, position);
            required.Sites.Add(new(id, position < 0 ? contract.Anchor : contract.Hooks[position].Anchor, "cursor", callback?.Completion ?? BoundCompletionKind.Synchronous, callback?.Implementation));
            if (fallible)
            {
                required.Failures.Add(new(id, true, true, true, false)); required.Transfers.Add(new("producer/error", "cursor/error", id, StreamOutcome.Failure));
            }
            if (callback?.Completion != BoundCompletionKind.ValueTask) return;
            required.Awaits.Add(new(id, phase == StreamPhase.AcquireSession ? Sessions(contract, position)[0].ResourceType : "void", true, true,
                phase == StreamPhase.AcquireSession ? "transfer-session" : "continue-session-cleanup",
                phase == StreamPhase.AcquireSession ? "startup-session-cleanup" : "replace-and-continue-session-cleanup",
                new(new[] { "cursor/request", "cursor/start-token", "cursor/source", "cursor/linked-token", "dispose/source", "dispose/linked-token", "startup/linked-token" }.Concat(retained))));
        }
    }
}
