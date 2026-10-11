using System.Collections.Generic;
using System.Linq;
using static Zendiator.SourceGenerator.SendExecutionNames;

namespace Zendiator.SourceGenerator;

/// <summary>Builds nested candidate schedules. Required contracts are derived separately by the validator.</summary>
internal sealed class CanonicalSendScheduleBuilder
{
    private readonly BoundPipelineModel _route;
    private readonly List<SendExecutionSite> _sites = new();
    private readonly List<SendExecutionOrder> _orders = new();
    private readonly List<SendExecutionOwner> _owners = new();
    private readonly List<SendInvocationState> _states = new();
    private readonly List<SendExecutionScope> _scopes = new();
    private readonly List<SendExecutionAwait> _awaits = new();
    private readonly List<SendValueTransfer> _transfers = new();
    private readonly bool _multiple;
    private readonly bool _async;

    private CanonicalSendScheduleBuilder(BoundPipelineModel route)
    {
        _route = route;
        _multiple = route.Family is BoundDispatchFamily.SendAllSync or BoundDispatchFamily.SendAllAsync;
        _async = route.Family is BoundDispatchFamily.SendAsync or BoundDispatchFamily.SendAllAsync;
    }

    internal static CanonicalSendExecutionSchedule Build(BoundPipelineModel route) => new CanonicalSendScheduleBuilder(route).Build();

    private CanonicalSendExecutionSchedule Build()
    {
        Owner("provider", null, SendOwnerKind.Provider, SendOwnerLifetime.Caller, SendOwnerMultiplicity.Once, borrowed: true);
        Owner("operation", "provider", SendOwnerKind.Operation, SendOwnerLifetime.Operation, SendOwnerMultiplicity.Once, asyncBoundary: _async);
        var argument = Plain(-1, -1, SendExecutionPhase.ArgumentGuard, _route.Anchor, "operation");
        var boundary = Plain(-1, -1, SendExecutionPhase.OperationBoundary, _route.Anchor, "operation");
        Link(argument, boundary);
        var span = _route.Family == BoundDispatchFamily.SendAllSync && _route.ResultKind == FusionResultKind.Response;
        if (span)
        {
            var guard = Plain(-1, -1, SendExecutionPhase.DestinationGuard, _route.Anchor, "operation", SendExecutionCondition.SpanDestination);
            Link(boundary, guard, SendOrderCondition.SpanDestination);
        }
        for (var branch = 0; branch < _route.Handlers.Count; branch++)
        {
            var branchOwner = Branch(branch);
            Owner(branchOwner, "operation", SendOwnerKind.Branch, SendOwnerLifetime.Branch,
                _multiple ? SendOwnerMultiplicity.EachBranch : SendOwnerMultiplicity.Once, asyncBoundary: _async);
            State(branchOwner, hook: false, original: true);
            var start = Plain(branch, -1, SendExecutionPhase.BranchStart, _route.Handlers[branch].Anchor, branchOwner, SendExecutionCondition.EachBranch);
            Transfer("operation", SendValueKind.OperationRequest, branchOwner, SendValueKind.InputRequest, start, SendTransferCondition.BranchStart);
            Transfer("operation", SendValueKind.OperationToken, branchOwner, SendValueKind.StartToken, start, SendTransferCondition.BranchStart);
            Link(boundary, start);
            if (span) Link(Site(-1, -1, SendExecutionPhase.DestinationGuard), start, SendOrderCondition.SpanDestination);
            if (branch > 0) Link(Site(branch - 1, 0, SendExecutionPhase.ScopeExit), start, SendOrderCondition.AllBranchesSuccess);
            BuildScope(branch, 0, branchOwner, SendOwnerMultiplicity.Once, SendValueKind.InputRequest);
            Link(start, Site(branch, 0, SendExecutionPhase.ArgumentGuard));
        }
        var returned = Plain(-1, -1, SendExecutionPhase.OperationReturn, _route.Anchor, "operation", SendExecutionCondition.InnerSuccess);
        if (span)
        {
            var commit = Plain(-1, -1, SendExecutionPhase.DestinationCommit, _route.Anchor, "operation", SendExecutionCondition.SpanDestination);
            Link(Site(_route.Handlers.Count - 1, 0, SendExecutionPhase.ScopeExit), commit, SendOrderCondition.AllBranchesSuccess);
            Link(commit, returned, SendOrderCondition.SpanDestination);
        }
        Link(Site(_route.Handlers.Count - 1, 0, SendExecutionPhase.ScopeExit), returned, SendOrderCondition.AllBranchesSuccess);
        return CanonicalSendSessionBuilder.Attach(_route,
            new(new(_sites), new(_orders), new(_owners), new(_states), new(_scopes), new(_awaits), "operation-callback-v1", _async)
            { Transfers = new(_transfers), ControlFlows = CanonicalSendControlBuilder.Build(_route) });
    }

    private void BuildScope(int branch, int stageIndex, string parent, SendOwnerMultiplicity multiplicity, SendValueKind requestSource)
    {
        var owner = Scope(branch, stageIndex);
        var handler = stageIndex == _route.Stages.Count;
        var stage = handler ? null : _route.Stages[stageIndex];
        var hook = stage?.Kind == FusionStageKind.Hook;
        var anchor = handler ? _route.Handlers[branch].Anchor : stage!.Anchor;
        Owner(owner, parent, handler ? SendOwnerKind.Handler : SendOwnerKind.Stage, SendOwnerLifetime.Stage, multiplicity);
        Owner(Service(branch, stageIndex), "provider", SendOwnerKind.Service, SendOwnerLifetime.Caller, multiplicity, borrowed: true);
        State(owner, hook, original: false);
        Transfer(parent, requestSource, owner, SendValueKind.InputRequest, Site(branch, stageIndex, SendExecutionPhase.ArgumentGuard), SendTransferCondition.InvocationStart);
        Transfer(parent, SendValueKind.StartToken, owner, SendValueKind.StartToken, Site(branch, stageIndex, SendExecutionPhase.ArgumentGuard), SendTransferCondition.InvocationStart);
        var entry = new[] { SendExecutionPhase.ArgumentGuard, SendExecutionPhase.CancellationGuard, SendExecutionPhase.Resolve, SendExecutionPhase.Entered };
        for (var index = 0; index < entry.Length; index++)
        {
            Plain(branch, stageIndex, entry[index], anchor, owner, failure: SendFailureDisposition.UnwindEnteredOuter);
            if (index > 0) Link(Site(branch, stageIndex, entry[index - 1]), Site(branch, stageIndex, entry[index]));
        }
        var entered = Site(branch, stageIndex, SendExecutionPhase.Entered);
        var exit = Plain(branch, stageIndex, SendExecutionPhase.ScopeExit, anchor, owner, SendExecutionCondition.Entered);
        if (handler)
        {
            var invoke = Callback(branch, stageIndex, SendExecutionPhase.InvokeHandler, anchor, owner, _route.Handlers[branch].Callback!,
                SendExecutionCondition.Always, SendFailureDisposition.UnwindEnteredOuter, SendAwaitContinuation.NormalUnwind);
            Link(entered, invoke);
            Link(invoke, exit, SendOrderCondition.InnerSuccess);
            _scopes.Add(new(owner, null, invoke, null, null, null, SendExitPolicy.HandlerEscape));
            return;
        }
        if (!hook)
        {
            var invoke = Callback(branch, stageIndex, SendExecutionPhase.InvokeWrapper, anchor, owner, stage!.Callbacks[0],
                SendExecutionCondition.Always, SendFailureDisposition.UnwindEnteredOuter, SendAwaitContinuation.NormalUnwind);
            var nextOwner = Next(branch, stageIndex);
            Owner(nextOwner, owner, SendOwnerKind.WrapperNext, SendOwnerLifetime.NextInvocation, SendOwnerMultiplicity.EachNext);
            State(nextOwner, hook: false, original: false);
            var next = Plain(branch, stageIndex, SendExecutionPhase.NextCall, anchor, nextOwner, SendExecutionCondition.EachNext,
                SendFailureDisposition.OpaqueWrapperDecides);
            Transfer(nextOwner, SendValueKind.NextRequestArgument, nextOwner, SendValueKind.InputRequest, next, SendTransferCondition.EachNextStart);
            Transfer(nextOwner, SendValueKind.NextTokenArgument, nextOwner, SendValueKind.StartToken, next, SendTransferCondition.EachNextStart);
            Link(entered, invoke);
            Link(invoke, next, SendOrderCondition.EachNext);
            Link(next, Site(branch, stageIndex + 1, SendExecutionPhase.ArgumentGuard), SendOrderCondition.EachNext);
            Link(invoke, exit, SendOrderCondition.InnerSuccess);
            _scopes.Add(new(owner, Scope(branch, stageIndex + 1), invoke, null, null, null, SendExitPolicy.OpaqueWrapperEscape));
            BuildScope(branch, stageIndex + 1, nextOwner, SendOwnerMultiplicity.EachNext, SendValueKind.InputRequest);
            return;
        }
        var invocation = Plain(branch, stageIndex, SendExecutionPhase.InvokeInner, anchor, owner,
            failure: SendFailureDisposition.ObserveCurrentOnceThenFinally);
        Transfer(owner, SendValueKind.InputRequest, owner, SendValueKind.EffectiveRequest, entered, SendTransferCondition.Entered);
        var callbacks = new Dictionary<BoundCallbackPhase, SendExecutionSiteId>();
        foreach (var callback in stage!.Callbacks)
        {
            var phase = callback.Phase switch
            {
                BoundCallbackPhase.Before => SendExecutionPhase.Before,
                BoundCallbackPhase.Success => SendExecutionPhase.Success,
                BoundCallbackPhase.Exception => SendExecutionPhase.Exception,
                _ => SendExecutionPhase.Finally
            };
            var condition = phase == SendExecutionPhase.Before ? SendExecutionCondition.Always :
                phase == SendExecutionPhase.Success ? SendExecutionCondition.InnerSuccess :
                phase == SendExecutionPhase.Exception ? SendExecutionCondition.FailureBeforeFinally : SendExecutionCondition.Entered;
            var failure = phase == SendExecutionPhase.Exception ? SendFailureDisposition.ReplaceAndFinallyWithoutObserver :
                phase == SendExecutionPhase.Finally ? SendFailureDisposition.ReplaceAndUnwindOuter : SendFailureDisposition.ObserveCurrentOnceThenFinally;
            var normal = phase == SendExecutionPhase.Before ? SendAwaitContinuation.BeginInner :
                phase == SendExecutionPhase.Exception ? SendAwaitContinuation.EndObserver :
                phase == SendExecutionPhase.Finally ? SendAwaitContinuation.EndScope : SendAwaitContinuation.NormalUnwind;
            callbacks.Add(callback.Phase, Callback(branch, stageIndex, phase, anchor, owner, callback, condition, failure, normal));
        }
        callbacks.TryGetValue(BoundCallbackPhase.Before, out var before);
        callbacks.TryGetValue(BoundCallbackPhase.Success, out var success);
        callbacks.TryGetValue(BoundCallbackPhase.Exception, out var error);
        callbacks.TryGetValue(BoundCallbackPhase.Finally, out var cleanup);
        Link(entered, before ?? invocation);
        if (before != null) Link(before, invocation);
        if (before != null) Transfer(owner, SendValueKind.BeforeResult, owner, SendValueKind.EffectiveRequest, before, SendTransferCondition.ProducerSuccess);
        Link(invocation, Site(branch, stageIndex + 1, SendExecutionPhase.ArgumentGuard));
        Link(Site(branch, stageIndex + 1, SendExecutionPhase.ScopeExit), success ?? cleanup ?? exit, SendOrderCondition.InnerSuccess);
        if (success != null) Link(success, cleanup ?? exit, SendOrderCondition.InnerSuccess);
        foreach (var producer in new[] { before, invocation, success }.Where(p => p != null))
            Link(producer!, error ?? cleanup ?? exit, SendOrderCondition.Failure);
        if (error != null) Link(error, cleanup ?? exit, SendOrderCondition.Unwind);
        if (cleanup != null) Link(cleanup, exit, SendOrderCondition.Unwind);
        _scopes.Add(new(owner, Scope(branch, stageIndex + 1), invocation, success, error, cleanup, SendExitPolicy.S01NestedHook));
        BuildScope(branch, stageIndex + 1, owner, multiplicity, SendValueKind.EffectiveRequest);
    }

    private SendExecutionSiteId Plain(int branch, int stage, SendExecutionPhase phase, string anchor, string owner,
        SendExecutionCondition condition = SendExecutionCondition.Always, SendFailureDisposition failure = SendFailureDisposition.EscapeOperation)
    {
        var id = Site(branch, stage, phase);
        _sites.Add(new(id, anchor, owner, condition, BoundCompletionKind.Synchronous, null, null, failure));
        return id;
    }
    private SendExecutionSiteId Callback(int branch, int stage, SendExecutionPhase phase, string anchor, string owner, BoundCallbackModel callback,
        SendExecutionCondition condition, SendFailureDisposition failure, SendAwaitContinuation normal)
    {
        var id = Site(branch, stage, phase);
        _sites.Add(new(id, anchor, owner, condition, callback.Completion, callback.Implementation, callback.ReturnType, failure));
        if (callback.Completion == BoundCompletionKind.ValueTask)
        {
            var state = _states.Single(s => s.Owner == owner);
            var slots = new[] { state.InputRequestSlot, state.EffectiveRequestSlot, state.StartTokenSlot, state.ErrorSlot, state.ResponseSlot }.Where(s => s != null).Select(s => s!);
            var target = phase == SendExecutionPhase.Finally || phase == SendExecutionPhase.InvokeHandler || phase == SendExecutionPhase.InvokeWrapper
                ? SendFailureTarget.OuterScope : SendFailureTarget.CurrentScope;
            var resultType = phase == SendExecutionPhase.Before ? _route.Request :
                phase is SendExecutionPhase.Success or SendExecutionPhase.InvokeHandler or SendExecutionPhase.InvokeWrapper
                    ? _route.Result ?? "void" : "void";
            _awaits.Add(new(id, owner, resultType, callback.RequiresSingleConsumption, true, normal, target, target, new(slots)));
        }
        return id;
    }
    private void Link(SendExecutionSiteId first, SendExecutionSiteId second, SendOrderCondition condition = SendOrderCondition.Start) => _orders.Add(new(first, second, condition));
    private void Owner(string id, string? parent, SendOwnerKind kind, SendOwnerLifetime lifetime, SendOwnerMultiplicity multiplicity, bool borrowed = false, bool asyncBoundary = false) =>
        _owners.Add(new(id, parent, kind, lifetime, multiplicity, borrowed, FusionCleanupKind.None, asyncBoundary));
    private void State(string owner, bool hook, bool original) => _states.Add(new(owner, _route.Request, owner + "/input-request",
        hook ? owner + "/effective-request" : null, owner + "/start-token", hook ? owner + "/error" : null, original, hook, hook,
        _route.Result, _route.ResultKind == FusionResultKind.Response ? owner + "/response" : null));
    private void Transfer(string source, SendValueKind sourceKind, string destination, SendValueKind destinationKind,
        SendExecutionSiteId at, SendTransferCondition condition) => _transfers.Add(new(new(source, sourceKind), new(destination, destinationKind), at, condition));
}
