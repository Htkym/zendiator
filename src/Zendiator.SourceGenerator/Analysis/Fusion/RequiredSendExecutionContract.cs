using System.Collections.Generic;
using System.Linq;
using static Zendiator.SourceGenerator.SendExecutionNames;

namespace Zendiator.SourceGenerator;

/// <summary>
/// S01/G01 obligations derived from bound contracts, without inspecting a candidate schedule.
/// The matrix walks declaration positions, independently of the builder's recursive scope traversal.
/// </summary>
internal sealed class RequiredSendExecutionContract
{
    internal readonly List<SendExecutionSite> Sites = new();
    internal readonly List<SendExecutionOrder> Orders = new();
    internal readonly List<SendExecutionOwner> Owners = new();
    internal readonly List<SendInvocationState> States = new();
    internal readonly List<SendExecutionScope> Scopes = new();
    internal readonly List<SendExecutionAwait> Awaits = new();
    internal readonly List<SendValueTransfer> Transfers = new();
    internal bool Async { get; private set; }

    internal static RequiredSendExecutionContract Derive(BoundPipelineModel route)
    {
        var required = new RequiredSendExecutionContract();
        var async = route.Family is BoundDispatchFamily.SendAsync or BoundDispatchFamily.SendAllAsync;
        var all = route.Family is BoundDispatchFamily.SendAllSync or BoundDispatchFamily.SendAllAsync;
        var span = route.Family == BoundDispatchFamily.SendAllSync && route.ResultKind == FusionResultKind.Response;
        required.Async = async;
        required.Owners.Add(new("provider", null, SendOwnerKind.Provider, SendOwnerLifetime.Caller, SendOwnerMultiplicity.Once, true, FusionCleanupKind.None, false));
        required.Owners.Add(new("operation", "provider", SendOwnerKind.Operation, SendOwnerLifetime.Operation, SendOwnerMultiplicity.Once, false, FusionCleanupKind.None, async));
        foreach (var phase in new[] { SendExecutionPhase.ArgumentGuard, SendExecutionPhase.OperationBoundary, SendExecutionPhase.OperationReturn })
            required.Sites.Add(new(Site(-1, -1, phase), route.Anchor, "operation",
                phase == SendExecutionPhase.OperationReturn ? SendExecutionCondition.InnerSuccess : SendExecutionCondition.Always,
                BoundCompletionKind.Synchronous, null, null, SendFailureDisposition.EscapeOperation));
        required.Edge(-1, -1, SendExecutionPhase.ArgumentGuard, -1, -1, SendExecutionPhase.OperationBoundary);
        if (span)
        {
            foreach (var phase in new[] { SendExecutionPhase.DestinationGuard, SendExecutionPhase.DestinationCommit })
                required.Sites.Add(new(Site(-1, -1, phase), route.Anchor, "operation", SendExecutionCondition.SpanDestination,
                    BoundCompletionKind.Synchronous, null, null, SendFailureDisposition.EscapeOperation));
            required.Edge(-1, -1, SendExecutionPhase.OperationBoundary, -1, -1, SendExecutionPhase.DestinationGuard, SendOrderCondition.SpanDestination);
            required.Edge(route.Handlers.Count - 1, 0, SendExecutionPhase.ScopeExit, -1, -1, SendExecutionPhase.DestinationCommit, SendOrderCondition.AllBranchesSuccess);
            required.Edge(-1, -1, SendExecutionPhase.DestinationCommit, -1, -1, SendExecutionPhase.OperationReturn, SendOrderCondition.SpanDestination);
        }
        required.Edge(route.Handlers.Count - 1, 0, SendExecutionPhase.ScopeExit, -1, -1, SendExecutionPhase.OperationReturn, SendOrderCondition.AllBranchesSuccess);
        for (var b = 0; b < route.Handlers.Count; b++)
        {
            required.Owners.Add(new(Branch(b), "operation", SendOwnerKind.Branch, SendOwnerLifetime.Branch,
                all ? SendOwnerMultiplicity.EachBranch : SendOwnerMultiplicity.Once, false, FusionCleanupKind.None, async));
            required.State(Branch(b), route, false, true);
            required.Transfers.Add(new(new("operation", SendValueKind.OperationRequest), new(Branch(b), SendValueKind.InputRequest), Site(b, -1, SendExecutionPhase.BranchStart), SendTransferCondition.BranchStart));
            required.Transfers.Add(new(new("operation", SendValueKind.OperationToken), new(Branch(b), SendValueKind.StartToken), Site(b, -1, SendExecutionPhase.BranchStart), SendTransferCondition.BranchStart));
            required.Sites.Add(new(Site(b, -1, SendExecutionPhase.BranchStart), route.Handlers[b].Anchor, Branch(b), SendExecutionCondition.EachBranch,
                BoundCompletionKind.Synchronous, null, null, SendFailureDisposition.EscapeOperation));
            required.Edge(-1, -1, SendExecutionPhase.OperationBoundary, b, -1, SendExecutionPhase.BranchStart);
            required.Edge(b, -1, SendExecutionPhase.BranchStart, b, 0, SendExecutionPhase.ArgumentGuard);
            if (span) required.Edge(-1, -1, SendExecutionPhase.DestinationGuard, b, -1, SendExecutionPhase.BranchStart, SendOrderCondition.SpanDestination);
            if (b > 0) required.Edge(b - 1, 0, SendExecutionPhase.ScopeExit, b, -1, SendExecutionPhase.BranchStart, SendOrderCondition.AllBranchesSuccess);
            for (var s = 0; s <= route.Stages.Count; s++)
            {
                var handler = s == route.Stages.Count;
                var hook = !handler && route.Stages[s].Kind == FusionStageKind.Hook;
                var owner = Scope(b, s);
                var anchor = handler ? route.Handlers[b].Anchor : route.Stages[s].Anchor;
                var parent = s == 0 ? Branch(b) : route.Stages[s - 1].Kind == FusionStageKind.Wrapper ? Next(b, s - 1) : Scope(b, s - 1);
                var multiplicity = route.Stages.Take(s).Any(st => st.Kind == FusionStageKind.Wrapper) ? SendOwnerMultiplicity.EachNext : SendOwnerMultiplicity.Once;
                required.Owners.Add(new(owner, parent, handler ? SendOwnerKind.Handler : SendOwnerKind.Stage, SendOwnerLifetime.Stage, multiplicity, false, FusionCleanupKind.None, false));
                required.Owners.Add(new(Service(b, s), "provider", SendOwnerKind.Service, SendOwnerLifetime.Caller, multiplicity, true, FusionCleanupKind.None, false));
                required.State(owner, route, hook, false);
                var requestSource = s == 0 || route.Stages[s - 1].Kind == FusionStageKind.Wrapper ? SendValueKind.InputRequest : SendValueKind.EffectiveRequest;
                required.Transfers.Add(new(new(parent, requestSource), new(owner, SendValueKind.InputRequest), Site(b, s, SendExecutionPhase.ArgumentGuard), SendTransferCondition.InvocationStart));
                required.Transfers.Add(new(new(parent, SendValueKind.StartToken), new(owner, SendValueKind.StartToken), Site(b, s, SendExecutionPhase.ArgumentGuard), SendTransferCondition.InvocationStart));
                foreach (var phase in new[] { SendExecutionPhase.ArgumentGuard, SendExecutionPhase.CancellationGuard, SendExecutionPhase.Resolve, SendExecutionPhase.Entered, SendExecutionPhase.ScopeExit })
                    required.Sites.Add(new(Site(b, s, phase), anchor, owner,
                        phase == SendExecutionPhase.ScopeExit ? SendExecutionCondition.Entered : SendExecutionCondition.Always,
                        BoundCompletionKind.Synchronous, null, null, phase == SendExecutionPhase.ScopeExit ? SendFailureDisposition.EscapeOperation : SendFailureDisposition.UnwindEnteredOuter));
                required.Edge(b, s, SendExecutionPhase.ArgumentGuard, b, s, SendExecutionPhase.CancellationGuard);
                required.Edge(b, s, SendExecutionPhase.CancellationGuard, b, s, SendExecutionPhase.Resolve);
                required.Edge(b, s, SendExecutionPhase.Resolve, b, s, SendExecutionPhase.Entered);
                if (handler)
                {
                    required.Callback(route, b, s, SendExecutionPhase.InvokeHandler, route.Handlers[b].Callback!, SendExecutionCondition.Always,
                        SendFailureDisposition.UnwindEnteredOuter, SendAwaitContinuation.NormalUnwind, SendFailureTarget.OuterScope);
                    required.Edge(b, s, SendExecutionPhase.Entered, b, s, SendExecutionPhase.InvokeHandler);
                    required.Edge(b, s, SendExecutionPhase.InvokeHandler, b, s, SendExecutionPhase.ScopeExit, SendOrderCondition.InnerSuccess);
                    required.Scopes.Add(new(owner, null, Site(b, s, SendExecutionPhase.InvokeHandler), null, null, null, SendExitPolicy.HandlerEscape));
                }
                else if (!hook)
                {
                    required.Callback(route, b, s, SendExecutionPhase.InvokeWrapper, route.Stages[s].Callbacks[0], SendExecutionCondition.Always,
                        SendFailureDisposition.UnwindEnteredOuter, SendAwaitContinuation.NormalUnwind, SendFailureTarget.OuterScope);
                    required.Owners.Add(new(Next(b, s), owner, SendOwnerKind.WrapperNext, SendOwnerLifetime.NextInvocation, SendOwnerMultiplicity.EachNext, false, FusionCleanupKind.None, false));
                    required.State(Next(b, s), route, false, false);
                    required.Transfers.Add(new(new(Next(b, s), SendValueKind.NextRequestArgument), new(Next(b, s), SendValueKind.InputRequest), Site(b, s, SendExecutionPhase.NextCall), SendTransferCondition.EachNextStart));
                    required.Transfers.Add(new(new(Next(b, s), SendValueKind.NextTokenArgument), new(Next(b, s), SendValueKind.StartToken), Site(b, s, SendExecutionPhase.NextCall), SendTransferCondition.EachNextStart));
                    required.Sites.Add(new(Site(b, s, SendExecutionPhase.NextCall), anchor, Next(b, s), SendExecutionCondition.EachNext,
                        BoundCompletionKind.Synchronous, null, null, SendFailureDisposition.OpaqueWrapperDecides));
                    required.Edge(b, s, SendExecutionPhase.Entered, b, s, SendExecutionPhase.InvokeWrapper);
                    required.Edge(b, s, SendExecutionPhase.InvokeWrapper, b, s, SendExecutionPhase.NextCall, SendOrderCondition.EachNext);
                    required.Edge(b, s, SendExecutionPhase.NextCall, b, s + 1, SendExecutionPhase.ArgumentGuard, SendOrderCondition.EachNext);
                    required.Edge(b, s, SendExecutionPhase.InvokeWrapper, b, s, SendExecutionPhase.ScopeExit, SendOrderCondition.InnerSuccess);
                    required.Scopes.Add(new(owner, Scope(b, s + 1), Site(b, s, SendExecutionPhase.InvokeWrapper), null, null, null, SendExitPolicy.OpaqueWrapperEscape));
                }
                else required.Hook(route, b, s);
            }
        }
        return required;
    }

    private void Hook(BoundPipelineModel route, int b, int s)
    {
        var stage = route.Stages[s];
        bool Has(BoundCallbackPhase phase) => stage.Callbacks.Any(c => c.Phase == phase);
        SendExecutionSiteId? Optional(BoundCallbackPhase callback, SendExecutionPhase site) => Has(callback) ? Site(b, s, site) : null;
        var success = Optional(BoundCallbackPhase.Success, SendExecutionPhase.Success);
        var error = Optional(BoundCallbackPhase.Exception, SendExecutionPhase.Exception);
        var finallySite = Optional(BoundCallbackPhase.Finally, SendExecutionPhase.Finally);
        Transfers.Add(new(new(Scope(b, s), SendValueKind.InputRequest), new(Scope(b, s), SendValueKind.EffectiveRequest), Site(b, s, SendExecutionPhase.Entered), SendTransferCondition.Entered));
        Sites.Add(new(Site(b, s, SendExecutionPhase.InvokeInner), stage.Anchor, Scope(b, s), SendExecutionCondition.Always,
            BoundCompletionKind.Synchronous, null, null, SendFailureDisposition.ObserveCurrentOnceThenFinally));
        // Presence is per capability; missing callbacks create no callback or await obligation.
        if (Has(BoundCallbackPhase.Before))
        {
            Transfers.Add(new(new(Scope(b, s), SendValueKind.BeforeResult), new(Scope(b, s), SendValueKind.EffectiveRequest), Site(b, s, SendExecutionPhase.Before), SendTransferCondition.ProducerSuccess));
            Callback(route, b, s, SendExecutionPhase.Before, stage.Callbacks.Single(c => c.Phase == BoundCallbackPhase.Before), SendExecutionCondition.Always,
                SendFailureDisposition.ObserveCurrentOnceThenFinally, SendAwaitContinuation.BeginInner, SendFailureTarget.CurrentScope);
            Edge(b, s, SendExecutionPhase.Entered, b, s, SendExecutionPhase.Before);
            Edge(b, s, SendExecutionPhase.Before, b, s, SendExecutionPhase.InvokeInner);
            Orders.Add(new(Site(b, s, SendExecutionPhase.Before), error ?? finallySite ?? Site(b, s, SendExecutionPhase.ScopeExit), SendOrderCondition.Failure));
        }
        else Edge(b, s, SendExecutionPhase.Entered, b, s, SendExecutionPhase.InvokeInner);
        if (success != null)
        {
            Callback(route, b, s, SendExecutionPhase.Success, stage.Callbacks.Single(c => c.Phase == BoundCallbackPhase.Success), SendExecutionCondition.InnerSuccess,
                SendFailureDisposition.ObserveCurrentOnceThenFinally, SendAwaitContinuation.NormalUnwind, SendFailureTarget.CurrentScope);
            Orders.Add(new(success, finallySite ?? Site(b, s, SendExecutionPhase.ScopeExit), SendOrderCondition.InnerSuccess));
            Orders.Add(new(success, error ?? finallySite ?? Site(b, s, SendExecutionPhase.ScopeExit), SendOrderCondition.Failure));
        }
        if (error != null)
        {
            Callback(route, b, s, SendExecutionPhase.Exception, stage.Callbacks.Single(c => c.Phase == BoundCallbackPhase.Exception), SendExecutionCondition.FailureBeforeFinally,
                SendFailureDisposition.ReplaceAndFinallyWithoutObserver, SendAwaitContinuation.EndObserver, SendFailureTarget.CurrentScope);
            Orders.Add(new(error, finallySite ?? Site(b, s, SendExecutionPhase.ScopeExit), SendOrderCondition.Unwind));
        }
        if (finallySite != null)
        {
            Callback(route, b, s, SendExecutionPhase.Finally, stage.Callbacks.Single(c => c.Phase == BoundCallbackPhase.Finally), SendExecutionCondition.Entered,
                SendFailureDisposition.ReplaceAndUnwindOuter, SendAwaitContinuation.EndScope, SendFailureTarget.OuterScope);
            Edge(b, s, SendExecutionPhase.Finally, b, s, SendExecutionPhase.ScopeExit, SendOrderCondition.Unwind);
        }
        Edge(b, s, SendExecutionPhase.InvokeInner, b, s + 1, SendExecutionPhase.ArgumentGuard);
        Orders.Add(new(Site(b, s + 1, SendExecutionPhase.ScopeExit), success ?? finallySite ?? Site(b, s, SendExecutionPhase.ScopeExit), SendOrderCondition.InnerSuccess));
        Orders.Add(new(Site(b, s, SendExecutionPhase.InvokeInner), error ?? finallySite ?? Site(b, s, SendExecutionPhase.ScopeExit), SendOrderCondition.Failure));
        Scopes.Add(new(Scope(b, s), Scope(b, s + 1), Site(b, s, SendExecutionPhase.InvokeInner), success, error, finallySite, SendExitPolicy.S01NestedHook));
    }

    private void Callback(BoundPipelineModel route, int b, int s, SendExecutionPhase phase, BoundCallbackModel callback,
        SendExecutionCondition condition, SendFailureDisposition failure, SendAwaitContinuation normal, SendFailureTarget failureTarget)
    {
        var handler = s == route.Stages.Count;
        var anchor = handler ? route.Handlers[b].Anchor : route.Stages[s].Anchor;
        Sites.Add(new(Site(b, s, phase), anchor, Scope(b, s), condition, callback.Completion, callback.Implementation, callback.ReturnType, failure));
        if (callback.Completion != BoundCompletionKind.ValueTask) return;
        var hook = !handler && route.Stages[s].Kind == FusionStageKind.Hook;
        var slots = new List<string> { Scope(b, s) + "/input-request" };
        if (hook) slots.Add(Scope(b, s) + "/effective-request");
        slots.Add(Scope(b, s) + "/start-token");
        if (hook) slots.Add(Scope(b, s) + "/error");
        if (route.ResultKind == FusionResultKind.Response) slots.Add(Scope(b, s) + "/response");
        var result = phase == SendExecutionPhase.Before ? route.Request :
            phase is SendExecutionPhase.InvokeHandler or SendExecutionPhase.InvokeWrapper or SendExecutionPhase.Success ? route.Result ?? "void" : "void";
        Awaits.Add(new(Site(b, s, phase), Scope(b, s), result, true, true, normal, failureTarget, failureTarget, new(slots)));
    }
    private void Edge(int b1, int s1, SendExecutionPhase p1, int b2, int s2, SendExecutionPhase p2, SendOrderCondition condition = SendOrderCondition.Start) =>
        Orders.Add(new(Site(b1, s1, p1), Site(b2, s2, p2), condition));
    private void State(string owner, BoundPipelineModel route, bool hook, bool original) => States.Add(new(owner, route.Request, owner + "/input-request",
        hook ? owner + "/effective-request" : null, owner + "/start-token", hook ? owner + "/error" : null, original, hook, hook,
        route.Result, route.ResultKind == FusionResultKind.Response ? owner + "/response" : null));
}
