using System.Collections.Generic;
using System.Linq;
using static Zendiator.SourceGenerator.SendExecutionNames;

namespace Zendiator.SourceGenerator;

/// <summary>Inserts explicit session acquisition and nested-finally cleanup into actual Send flows.</summary>
internal static class CanonicalSendSessionBuilder
{
    internal static CanonicalSendExecutionSchedule Attach(BoundPipelineModel route, CanonicalSendExecutionSchedule schedule)
    {
        var sites = schedule.Sites.ToList(); var owners = schedule.Owners.ToList();
        var orders = schedule.Orders.ToList(); var awaits = schedule.Awaits.ToList();
        var flows = schedule.ControlFlows.ToList(); var transfers = new List<SendSessionTransfer>();
        var calls = new List<SendSessionCall>();
        for (var b = 0; b < route.Handlers.Count; b++)
            for (var s = 0; s <= route.Stages.Count; s++)
            {
                var sessions = Sessions(route, b, s); if (sessions.Count == 0) continue;
                var owner = Scope(b, s); var anchor = s == route.Stages.Count ? route.Handlers[b].Anchor : route.Stages[s].Anchor;
                var scopeOwner = owners.Single(o => o.Id == owner);
                var retained = Enumerable.Range(0, sessions.Count).SelectMany(i => new[] { Session(b, s, i) + "/live", Session(b, s, i) + "/detached" }).ToArray();
                for (var a = 0; a < awaits.Count; a++)
                    if (awaits[a].Owner == owner) awaits[a] = awaits[a] with { RetainedSlots = new(awaits[a].RetainedSlots.Concat(retained)) };
                SendExecutionSiteId Id(int i, SendExecutionPhase phase) => SessionSite(b, s, i, phase);
                SendControlPoint Node(int i, SendExecutionPhase phase) => Point(owner, Id(i, phase));
                var entered = Site(b, s, SendExecutionPhase.Entered);
                var first = Id(0, SendExecutionPhase.SessionGuard);
                orders.RemoveAll(o => o.Before == Site(b, s, SendExecutionPhase.Resolve) && o.After == entered);
                orders.Add(new(Site(b, s, SendExecutionPhase.Resolve), first, SendOrderCondition.Start));
                var index = flows.FindIndex(f => f.Owner == owner);
                var edges = flows[index].Edges.ToList();
                var cleanupStart = Node(sessions.Count - 1, SendExecutionPhase.DetachSession);
                // Every callback exit (including a throwing Finally) must finish this scope's leases.
                for (var e = 0; e < edges.Count; e++)
                {
                    var edge = edges[e];
                    if (edge.To == Point(owner, entered)) edges[e] = edge with { To = Point(owner, first) };
                    else if (edge.To.Terminal is SendControlTerminal.Success or SendControlTerminal.Failure &&
                        edge.From.Site?.Phase is not (SendExecutionPhase.ArgumentGuard or SendExecutionPhase.CancellationGuard or SendExecutionPhase.Resolve))
                        edges[e] = edge with { To = cleanupStart };
                }
                for (var i = 0; i < sessions.Count; i++)
                {
                    var binding = sessions[i]; var resource = Session(b, s, i);
                    owners.Add(new(resource + "/factory", "provider", SendOwnerKind.Service, SendOwnerLifetime.Caller,
                        scopeOwner.Multiplicity, true, FusionCleanupKind.None, false));
                    owners.Add(new(resource, owner, SendOwnerKind.Session, SendOwnerLifetime.Stage, scopeOwner.Multiplicity,
                        binding.Ownership == FusionOwnership.BorrowedResource,
                        binding.Cleanup == null ? FusionCleanupKind.None : binding.Cleanup.Completion == BoundCompletionKind.ValueTask ? FusionCleanupKind.Async : FusionCleanupKind.Sync, false));
                    Add(i, SendExecutionPhase.SessionGuard); Add(i, SendExecutionPhase.ResolveSessionFactory, producer: binding.FactoryServiceType);
                    Add(i, SendExecutionPhase.AcquireSession, binding.Acquire, SendFailureDisposition.UnwindAcquiredSessionsThenOuter);
                    calls.Add(new(Id(i, SendExecutionPhase.AcquireSession), resource + "/factory/resolved", resource + "/factory-result", true, true, false));
                    Add(i, SendExecutionPhase.CommitSession); Add(i, SendExecutionPhase.DetachSession);
                    var phases = new[] { SendExecutionPhase.SessionGuard, SendExecutionPhase.ResolveSessionFactory, SendExecutionPhase.AcquireSession, SendExecutionPhase.CommitSession };
                    for (var p = 0; p < phases.Length; p++)
                    {
                        var next = p + 1 < phases.Length ? Id(i, phases[p + 1]) : i + 1 < sessions.Count ? Id(i + 1, phases[0]) : entered;
                        orders.Add(new(Id(i, phases[p]), next, SendOrderCondition.Start));
                        var pure = phases[p] == SendExecutionPhase.CommitSession;
                        edges.Add(new(Node(i, phases[p]), Point(owner, next), pure ? SendControlOutcome.Always : SendControlOutcome.Success, SendControlPredicate.Any));
                        if (!pure) edges.Add(new(Node(i, phases[p]), cleanupStart, SendControlOutcome.Failure, SendControlPredicate.Any));
                    }
                    transfers.Add(new(resource + "/factory-result", resource + "/live", Id(i, SendExecutionPhase.CommitSession)));
                    transfers.Add(new(resource + "/live", resource + "/detached", Id(i, SendExecutionPhase.DetachSession)));
                    transfers.Add(new("null", resource + "/live", Id(i, SendExecutionPhase.DetachSession)));
                    if (binding.Ownership == FusionOwnership.OwnedSession)
                    {
                        Add(i, SendExecutionPhase.SessionPresent);
                        Add(i, SendExecutionPhase.CleanupSession, binding.Cleanup!, SendFailureDisposition.ReplaceAndContinueSessionCleanup);
                        calls.Add(new(Id(i, SendExecutionPhase.CleanupSession), resource + "/detached", null, true, true, false));
                        var nextOwned = Enumerable.Range(0, i).Reverse().Where(n => sessions[n].Ownership == FusionOwnership.OwnedSession).DefaultIfEmpty(-1).First();
                        var next = nextOwned < 0 ? Node(-1, SendExecutionPhase.SessionResult) : Node(nextOwned, SendExecutionPhase.SessionPresent);
                        edges.Add(new(Node(i, SendExecutionPhase.SessionPresent), Node(i, SendExecutionPhase.CleanupSession), SendControlOutcome.Always, SendControlPredicate.SessionPresent));
                        edges.Add(new(Node(i, SendExecutionPhase.SessionPresent), next, SendControlOutcome.Always, SendControlPredicate.SessionAbsent));
                        edges.Add(new(Node(i, SendExecutionPhase.CleanupSession), next, SendControlOutcome.Success, SendControlPredicate.Any));
                        edges.Add(new(Node(i, SendExecutionPhase.CleanupSession), next, SendControlOutcome.Failure, SendControlPredicate.Any));
                    }
                }
                var lastOwned = Enumerable.Range(0, sessions.Count).Reverse().Where(i => sessions[i].Ownership == FusionOwnership.OwnedSession).DefaultIfEmpty(-1).First();
                for (var i = sessions.Count - 1; i >= 0; i--)
                {
                    var next = i > 0 ? Node(i - 1, SendExecutionPhase.DetachSession) :
                        lastOwned < 0 ? Node(-1, SendExecutionPhase.SessionResult) : Node(lastOwned, SendExecutionPhase.SessionPresent);
                    edges.Add(new(Node(i, SendExecutionPhase.DetachSession), next, SendControlOutcome.Always, SendControlPredicate.Any));
                }
                Add(-1, SendExecutionPhase.SessionResult);
                edges.Add(new(Node(-1, SendExecutionPhase.SessionResult), Exit(owner, true), SendControlOutcome.Always, SendControlPredicate.NoFailure));
                edges.Add(new(Node(-1, SendExecutionPhase.SessionResult), Exit(owner, false), SendControlOutcome.Always, SendControlPredicate.HasFailure));
                flows[index] = new(owner, new(edges));

                void Add(int i, SendExecutionPhase phase, BoundCallbackModel? callback = null,
                    SendFailureDisposition failure = SendFailureDisposition.UnwindAcquiredSessionsThenOuter, string? producer = null)
                {
                    sites.Add(new(Id(i, phase), anchor, owner, SendExecutionCondition.Always,
                        callback?.Completion ?? BoundCompletionKind.Synchronous, callback?.Implementation ?? producer, callback?.ReturnType, failure));
                    if (callback?.Completion == BoundCompletionKind.ValueTask)
                    {
                        var state = schedule.States.Single(st => st.Owner == owner);
                        awaits.Add(new(Id(i, phase), owner, phase == SendExecutionPhase.AcquireSession ? sessions[i].ResourceType : "void", true, true,
                            phase == SendExecutionPhase.AcquireSession ? SendAwaitContinuation.TransferSession : SendAwaitContinuation.ContinueSessionCleanup,
                            SendFailureTarget.CurrentScope, SendFailureTarget.CurrentScope,
                            new(new[] { state.InputRequestSlot, state.EffectiveRequestSlot, state.StartTokenSlot, state.ErrorSlot, state.ResponseSlot }
                                .Where(slot => slot != null).Select(slot => slot!).Concat(retained))));
                    }
                }
            }
        return schedule with { Sites = new(sites), Orders = new(orders), Owners = new(owners), Awaits = new(awaits),
            ControlFlows = new(flows), SessionTransfers = new(transfers), SessionCalls = new(calls) };
    }
}
