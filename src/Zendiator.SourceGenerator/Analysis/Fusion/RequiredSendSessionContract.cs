using System.Collections.Generic;
using System.Linq;
using static Zendiator.SourceGenerator.SendExecutionNames;

namespace Zendiator.SourceGenerator;

/// <summary>D04 requirements from explicit factory contracts; never reads a candidate or its CFG.</summary>
internal static class RequiredSendSessionContract
{
    internal static void Append(BoundPipelineModel route, RequiredSendExecutionContract required)
    {
        for (var branch = 0; branch < route.Handlers.Count; branch++)
            for (var stage = 0; stage <= route.Stages.Count; stage++)
            {
                var declared = Sessions(route, branch, stage); if (declared.Count == 0) continue;
                var owner = Scope(branch, stage);
                var anchor = stage == route.Stages.Count ? route.Handlers[branch].Anchor : route.Stages[stage].Anchor;
                var multiplicity = route.Stages.Take(stage).Any(st => st.Kind == FusionStageKind.Wrapper)
                    ? SendOwnerMultiplicity.EachNext : SendOwnerMultiplicity.Once;
                var callbackSession = declared.Count == 1 && declared[0].SuppliesCallbackReceiver;
                var state = required.States.Single(st => st.Owner == owner);
                if (callbackSession)
                {
                    required.ServiceResolutions.Add(new(Site(branch, stage, SendExecutionPhase.Resolve), declared[0].FactoryServiceType, Service(branch, stage) + "/resolved"));
                    var callbacks = stage == route.Stages.Count ? new[] { route.Handlers[branch].Callback! } : route.Stages[stage].Callbacks.ToArray();
                    foreach (var callback in callbacks)
                    {
                        var phase = callback.Phase switch
                        {
                            BoundCallbackPhase.Before => SendExecutionPhase.Before,
                            BoundCallbackPhase.Success => SendExecutionPhase.Success,
                            BoundCallbackPhase.Exception => SendExecutionPhase.Exception,
                            BoundCallbackPhase.Finally => SendExecutionPhase.Finally,
                            BoundCallbackPhase.Handler => SendExecutionPhase.InvokeHandler,
                            _ => SendExecutionPhase.InvokeWrapper
                        };
                        required.CallbackReceivers.Add(new(Site(branch, stage, phase), Session(branch, stage, 0) + "/live",
                            phase == SendExecutionPhase.Before || state.EffectiveRequestSlot == null ? state.InputRequestSlot : state.EffectiveRequestSlot, state.StartTokenSlot));
                    }
                }
                var slots = new List<string>();
                for (var index = 0; index < declared.Count; index++)
                {
                    slots.Add(Session(branch, stage, index) + "/live");
                    slots.Add(Session(branch, stage, index) + "/detached");
                }
                if (callbackSession) slots.Add(Service(branch, stage) + "/resolved");
                for (var index = 0; index < required.Awaits.Count; index++)
                    if (required.Awaits[index].Owner == owner)
                        required.Awaits[index] = required.Awaits[index] with { RetainedSlots = new(required.Awaits[index].RetainedSlots.Concat(slots)) };
                required.Orders.RemoveAll(order => order.Before == Site(branch, stage, SendExecutionPhase.Resolve) && order.After == Site(branch, stage, SendExecutionPhase.Entered));
                required.Orders.Add(new(Site(branch, stage, SendExecutionPhase.Resolve), SessionSite(branch, stage, 0, SendExecutionPhase.SessionGuard), SendOrderCondition.Start));
                for (var index = 0; index < declared.Count; index++)
                {
                    var binding = declared[index]; var resource = Session(branch, stage, index);
                    if (!callbackSession) required.Owners.Add(new(resource + "/factory", "provider", SendOwnerKind.Service, SendOwnerLifetime.Caller, multiplicity, true, FusionCleanupKind.None, false));
                    var owned = binding.Ownership == FusionOwnership.OwnedSession;
                    required.Owners.Add(new(resource, owner, SendOwnerKind.Session, SendOwnerLifetime.Stage, multiplicity, !owned,
                        !owned ? FusionCleanupKind.None : required.Async ? FusionCleanupKind.Async : FusionCleanupKind.Sync, false));
                    Add(index, SendExecutionPhase.SessionGuard);
                    if (!callbackSession) Add(index, SendExecutionPhase.ResolveSessionFactory, producer: binding.FactoryServiceType);
                    Add(index, SendExecutionPhase.AcquireSession, binding.Acquire);
                    required.SessionCalls.Add(new(SessionSite(branch, stage, index, SendExecutionPhase.AcquireSession), callbackSession ? Service(branch, stage) + "/resolved" : resource + "/factory/resolved", resource + "/factory-result", true, true, false)
                    { RequestSlot = state.InputRequestSlot, TokenSlot = state.StartTokenSlot });
                    if (callbackSession) Add(index, SendExecutionPhase.ValidateSessionResult);
                    Add(index, SendExecutionPhase.CommitSession);
                    Add(index, SendExecutionPhase.DetachSession);
                    if (owned)
                    {
                        Add(index, SendExecutionPhase.SessionPresent);
                        Add(index, SendExecutionPhase.CleanupSession, binding.Cleanup);
                        required.SessionCalls.Add(new(SessionSite(branch, stage, index, SendExecutionPhase.CleanupSession), resource + "/detached", null, true, true, false));
                    }
                    var sequence = callbackSession
                        ? new[] { SendExecutionPhase.SessionGuard, SendExecutionPhase.AcquireSession, SendExecutionPhase.ValidateSessionResult, SendExecutionPhase.CommitSession }
                        : new[] { SendExecutionPhase.SessionGuard, SendExecutionPhase.ResolveSessionFactory, SendExecutionPhase.AcquireSession, SendExecutionPhase.CommitSession };
                    for (var step = 0; step < sequence.Length; step++)
                        required.Orders.Add(new(SessionSite(branch, stage, index, sequence[step]), step + 1 < sequence.Length
                            ? SessionSite(branch, stage, index, sequence[step + 1]) : index + 1 < declared.Count
                                ? SessionSite(branch, stage, index + 1, sequence[0]) : Site(branch, stage, SendExecutionPhase.Entered), SendOrderCondition.Start));
                    required.SessionTransfers.Add(new(resource + "/factory-result", resource + "/live", SessionSite(branch, stage, index, SendExecutionPhase.CommitSession)));
                    if (callbackSession) required.SessionTransfers.Add(new("null", resource + "/factory-result", SessionSite(branch, stage, index, SendExecutionPhase.CommitSession)));
                    required.SessionTransfers.Add(new(resource + "/live", resource + "/detached", SessionSite(branch, stage, index, SendExecutionPhase.DetachSession)));
                    required.SessionTransfers.Add(new("null", resource + "/live", SessionSite(branch, stage, index, SendExecutionPhase.DetachSession)));
                }
                Add(-1, SendExecutionPhase.SessionResult);

                void Add(int index, SendExecutionPhase phase, BoundCallbackModel? callback = null, string? producer = null)
                {
                    var id = SessionSite(branch, stage, index, phase);
                    required.Sites.Add(new(id, anchor, owner, SendExecutionCondition.Always, callback?.Completion ?? BoundCompletionKind.Synchronous,
                        callback?.Implementation ?? producer, callback?.ReturnType,
                        phase == SendExecutionPhase.CleanupSession ? SendFailureDisposition.ReplaceAndContinueSessionCleanup : SendFailureDisposition.UnwindAcquiredSessionsThenOuter));
                    if (callback?.Completion != BoundCompletionKind.ValueTask) return;
                    var retained = new[] { state.InputRequestSlot, state.EffectiveRequestSlot, state.StartTokenSlot, state.ErrorSlot, state.ResponseSlot }
                        .Where(slot => slot != null).Select(slot => slot!).Concat(slots);
                    required.Awaits.Add(new(id, owner, phase == SendExecutionPhase.AcquireSession ? declared[index].ResourceType : "void", true, true,
                        phase == SendExecutionPhase.AcquireSession ? SendAwaitContinuation.TransferSession : SendAwaitContinuation.ContinueSessionCleanup,
                        SendFailureTarget.CurrentScope, SendFailureTarget.CurrentScope, new(retained)));
                }
            }
    }
}
