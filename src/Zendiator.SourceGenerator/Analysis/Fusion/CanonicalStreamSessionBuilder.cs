using System.Collections.Generic;
using System.Linq;
using static Zendiator.SourceGenerator.StreamExecutionNames;

namespace Zendiator.SourceGenerator;

/// <summary>Session startup is outer-to-inner within a segment; source disposal precedes reverse session cleanup.</summary>
internal static class CanonicalStreamSessionBuilder
{
    internal static CanonicalStreamExecutionSchedule Attach(StreamSegmentContract contract, CanonicalStreamExecutionSchedule schedule)
    {
        var positions = Enumerable.Range(-1, contract.Hooks.Count + 1).Where(i => Sessions(contract, i).Count != 0).ToArray();
        if (positions.Length == 0) return schedule;
        var sites = schedule.Sites.ToList(); var flows = schedule.Flows.ToList();
        var owners = schedule.Owners.ToList(); var awaits = schedule.Awaits.ToList();
        var transfers = schedule.Transfers.ToList(); var failures = schedule.Failures.ToList();
        var resolutions = new List<StreamServiceResolution>(); var calls = new List<StreamSessionCall>();
        var receivers = new List<StreamCallbackReceiver>();
        var resolve = contract.SourceKind == StreamSourceKind.Handler ? StreamPhase.ResolveHandler : StreamPhase.ResolveWrapper;
        var factory = contract.SourceKind == StreamSourceKind.Handler ? StreamPhase.SourceFactory : StreamPhase.WrapperFactory;
        var move = flows.Single(f => f.Program == StreamProgram.Move).Edges.ToList();
        var dispose = flows.Single(f => f.Program == StreamProgram.Dispose).Edges.ToList();
        var retained = positions.SelectMany(i => new[] { Session(i) + "/live", Session(i) + "/detached", Service(i) + "/resolved" }).ToArray();
        for (var i = 0; i < awaits.Count; i++) awaits[i] = awaits[i] with { RetainedSlots = new(awaits[i].RetainedSlots.Concat(retained)) };
        foreach (var position in positions)
        {
            var binding = Sessions(contract, position)[0]; var resource = Session(position);
            owners.Add(new(resource, "cursor", StreamOwnerKind.Session, StreamOwnerLifetime.EachEnumeration,
                binding.Ownership == FusionOwnership.BorrowedResource,
                binding.Ownership == FusionOwnership.OwnedSession ? StreamCleanup.AsyncDispose : StreamCleanup.None));
            var resolution = Id(StreamProgram.Move, position < 0 ? resolve : StreamPhase.ResolveSessionFactory, position);
            resolutions.Add(new(resolution, binding.FactoryServiceType, Service(position) + "/resolved"));
            var body = Id(StreamProgram.Move, position < 0 ? factory :
                contract.Hooks[position].Callbacks[0].Phase == BoundCallbackPhase.Filter ? StreamPhase.Filter : StreamPhase.Map, position);
            receivers.Add(new(body, resource + "/live", "cursor/request", "cursor/start-token"));
            if (position >= 0)
            {
                // Explicit operation sessions start before the source. Stateless DI item hooks retain their lazy cache path.
                var removed = new[] { Id(StreamProgram.Move, StreamPhase.HookCached, position), Id(StreamProgram.Move, StreamPhase.ResolveHook, position) };
                sites.RemoveAll(s => removed.Contains(s.Id)); failures.RemoveAll(f => removed.Contains(f.At)); transfers.RemoveAll(t => removed.Contains(t.At));
                move.RemoveAll(e => removed.Contains(e.From) || removed.Contains(e.To));
                move.Add(new(Id(StreamProgram.Move, StreamPhase.HookGuard, position), body, StreamOutcome.Success));
            }
            var sequence = position < 0
                ? new[] { StreamPhase.SessionGuard, StreamPhase.AcquireSession, StreamPhase.ValidateSessionResult, StreamPhase.CommitSession }
                : new[] { StreamPhase.SessionGuard, StreamPhase.ResolveSessionFactory, StreamPhase.AcquireSession, StreamPhase.ValidateSessionResult, StreamPhase.CommitSession };
            foreach (var phase in sequence) Add(StreamProgram.Move, phase, position,
                phase == StreamPhase.AcquireSession ? binding.Acquire : null);
            calls.Add(new(Id(StreamProgram.Move, StreamPhase.AcquireSession, position), Service(position) + "/resolved", resource + "/factory-result", "cursor/request", "cursor/start-token"));
            transfers.Add(new(resource + "/factory-result", resource + "/live", Id(StreamProgram.Move, StreamPhase.CommitSession, position), StreamOutcome.Always));
            transfers.Add(new("null", resource + "/factory-result", Id(StreamProgram.Move, StreamPhase.CommitSession, position), StreamOutcome.Always));
            for (var i = 0; i + 1 < sequence.Length; i++)
                move.Add(new(Id(StreamProgram.Move, sequence[i], position), Id(StreamProgram.Move, sequence[i + 1], position), StreamOutcome.Success));
            // Commit is a pure ownership boundary, not an additional callback completion.
            var commit = Id(StreamProgram.Move, StreamPhase.CommitSession, position);
            if (position < 0)
            {
                var edge = move.Single(e => e.From == Id(StreamProgram.Move, resolve) && e.Outcome == StreamOutcome.Success);
                move.Remove(edge); move.Add(edge with { To = Id(StreamProgram.Move, sequence[0], position) });
                move.Add(new(commit, edge.To, StreamOutcome.Always));
            }
            foreach (var phase in sequence.Where(p => p != StreamPhase.CommitSession))
                Fault(StreamProgram.Move, phase, position, Id(StreamProgram.Move, StreamPhase.StartupDetachLink));
            foreach (var program in new[] { StreamProgram.Move, StreamProgram.Dispose })
            {
                var detach = Id(program, StreamPhase.DetachSession, position);
                Add(program, StreamPhase.DetachSession, position);
                transfers.Add(new(resource + "/live", resource + "/detached", detach, StreamOutcome.Always));
                transfers.Add(new("null", resource + "/live", detach, StreamOutcome.Always));
                if (binding.Ownership != FusionOwnership.OwnedSession) continue;
                Add(program, StreamPhase.SessionPresent, position); Add(program, StreamPhase.CleanupSession, position, binding.Cleanup);
                calls.Add(new(Id(program, StreamPhase.CleanupSession, position), resource + "/detached", null, null, null));
            }
        }
        // PreCancel -> outermost Hook session -> ... -> innermost Hook session -> source resolution/session.
        var nextStartup = Id(StreamProgram.Move, resolve);
        foreach (var position in positions.Where(i => i >= 0))
        {
            move.Add(new(Id(StreamProgram.Move, StreamPhase.CommitSession, position), nextStartup, StreamOutcome.Always));
            nextStartup = Id(StreamProgram.Move, StreamPhase.SessionGuard, position);
        }
        for (var i = 0; i < move.Count; i++)
            if (move[i].From == Id(StreamProgram.Move, StreamPhase.PreCancel) && move[i].Outcome == StreamOutcome.Success)
                move[i] = move[i] with { To = nextStartup };
        var owned = positions.Where(i => Sessions(contract, i)[0].Ownership == FusionOwnership.OwnedSession).ToArray();
        foreach (var program in new[] { StreamProgram.Move, StreamProgram.Dispose })
        {
            var edges = program == StreamProgram.Move ? move : dispose;
            var afterCleanup = Id(program, program == StreamProgram.Move ? StreamPhase.StartupLinkPresent : StreamPhase.LinkPresent);
            var firstCleanup = owned.Length == 0 ? afterCleanup : Id(program, StreamPhase.SessionPresent, owned[0]);
            for (var i = 0; i < positions.Length; i++)
                edges.Add(new(Id(program, StreamPhase.DetachSession, positions[i]), i + 1 < positions.Length
                    ? Id(program, StreamPhase.DetachSession, positions[i + 1])
                    : program == StreamProgram.Move ? firstCleanup : Id(program, StreamPhase.SourcePresent), StreamOutcome.Always));
            for (var i = 0; i < owned.Length; i++)
            {
                var next = i + 1 < owned.Length ? Id(program, StreamPhase.SessionPresent, owned[i + 1]) : afterCleanup;
                edges.Add(new(Id(program, StreamPhase.SessionPresent, owned[i]), Id(program, StreamPhase.CleanupSession, owned[i]), StreamOutcome.True));
                edges.Add(new(Id(program, StreamPhase.SessionPresent, owned[i]), next, StreamOutcome.False));
                edges.Add(new(Id(program, StreamPhase.CleanupSession, owned[i]), next, StreamOutcome.Success));
                Fault(program, StreamPhase.CleanupSession, owned[i], next);
            }
            for (var i = 0; i < edges.Count; i++)
            {
                var edge = edges[i];
                if (edge.From == Id(program, program == StreamProgram.Move ? StreamPhase.StartupDetachLink : StreamPhase.DetachLink))
                    edges[i] = edge with { To = Id(program, StreamPhase.DetachSession, positions[0]) };
                else if (program == StreamProgram.Dispose && edge.To == afterCleanup &&
                    edge.From.Phase is StreamPhase.SourcePresent or StreamPhase.DisposeSource)
                    edges[i] = edge with { To = firstCleanup };
            }
        }
        transfers.Add(new("null", "cursor/linked-token", Id(StreamProgram.Move, StreamPhase.StartupDetachLink), StreamOutcome.Always));
        transfers.Add(new("null", "cursor/source", Id(StreamProgram.Dispose, StreamPhase.DetachSource), StreamOutcome.Always));
        transfers.Add(new("null", "cursor/linked-token", Id(StreamProgram.Dispose, StreamPhase.DetachLink), StreamOutcome.Always));
        flows = flows.Select(f => f.Program == StreamProgram.Move ? f with { Edges = new(move) } : f.Program == StreamProgram.Dispose ? f with { Edges = new(dispose) } : f).ToList();
        return schedule with { Sites = new(sites), Flows = new(flows), Owners = new(owners), Awaits = new(awaits), Transfers = new(transfers), Failures = new(failures),
            ServiceResolutions = new(resolutions), SessionCalls = new(calls), CallbackReceivers = new(receivers) };

        void Add(StreamProgram program, StreamPhase phase, int position, BoundCallbackModel? callback = null)
        {
            var id = Id(program, phase, position); var binding = Sessions(contract, position)[0];
            sites.Add(new(id, position < 0 ? contract.Anchor : contract.Hooks[position].Anchor, "cursor", callback?.Completion ?? BoundCompletionKind.Synchronous, callback?.Implementation));
            if (callback?.Completion == BoundCompletionKind.ValueTask)
                awaits.Add(new(id, phase == StreamPhase.AcquireSession ? binding.ResourceType : "void", true, true,
                    phase == StreamPhase.AcquireSession ? "transfer-session" : "continue-session-cleanup",
                    phase == StreamPhase.AcquireSession ? "startup-session-cleanup" : "replace-and-continue-session-cleanup",
                    new(new[] { "cursor/request", "cursor/start-token", "cursor/source", "cursor/linked-token", "dispose/source", "dispose/linked-token", "startup/linked-token" }.Concat(retained))));
        }
        void Fault(StreamProgram program, StreamPhase phase, int position, StreamSiteId next)
        {
            var id = Id(program, phase, position); (program == StreamProgram.Move ? move : dispose).Add(new(id, next, StreamOutcome.Failure));
            failures.Add(new(id, true, true, true, false)); transfers.Add(new("producer/error", "cursor/error", id, StreamOutcome.Failure));
        }
    }
}
