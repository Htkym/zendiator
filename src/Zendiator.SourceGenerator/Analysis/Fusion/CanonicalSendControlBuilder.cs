using System.Collections.Generic;
using System.Linq;
using static Zendiator.SourceGenerator.SendExecutionNames;

namespace Zendiator.SourceGenerator;

/// <summary>Small Send CFGs. Inner/wrapper/handler calls are validated invocation summaries, not user-body rewrites.</summary>
internal sealed class CanonicalSendControlBuilder
{
    private readonly string _owner;
    private readonly List<SendControlEdge> _edges = new();
    private CanonicalSendControlBuilder(string owner) => _owner = owner;
    private SendControlPoint Node(int branch, int stage, SendExecutionPhase phase) => Point(_owner, Site(branch, stage, phase));
    private void Edge(SendControlPoint from, SendControlPoint to, SendControlOutcome outcome = SendControlOutcome.Always,
        SendControlPredicate predicate = SendControlPredicate.Any) => _edges.Add(new(from, to, outcome, predicate));
    private SendControlFlow Finish() => new(_owner, new(_edges));

    internal static EquatableArray<SendControlFlow> Build(BoundPipelineModel route)
    {
        var flows = new List<SendControlFlow> { Operation(route) };
        for (var branch = 0; branch < route.Handlers.Count; branch++)
            for (var stage = 0; stage <= route.Stages.Count; stage++)
            {
                flows.Add(ScopeFlow(route, branch, stage));
                if (stage < route.Stages.Count && route.Stages[stage].Kind == FusionStageKind.Wrapper)
                {
                    var next = new CanonicalSendControlBuilder(Next(branch, stage));
                    var call = next.Node(branch, stage, SendExecutionPhase.NextCall);
                    next.Edge(Entry(next._owner), call);
                    next.Edge(call, Exit(next._owner, true), SendControlOutcome.Success);
                    next.Edge(call, Exit(next._owner, false), SendControlOutcome.Failure);
                    flows.Add(next.Finish());
                }
            }
        return new(flows);
    }

    private static SendControlFlow Operation(BoundPipelineModel route)
    {
        var flow = new CanonicalSendControlBuilder("operation");
        var argument = flow.Node(-1, -1, SendExecutionPhase.ArgumentGuard);
        var boundary = flow.Node(-1, -1, SendExecutionPhase.OperationBoundary);
        var guard = flow.Node(-1, -1, SendExecutionPhase.DestinationGuard);
        var commit = flow.Node(-1, -1, SendExecutionPhase.DestinationCommit);
        var returned = flow.Node(-1, -1, SendExecutionPhase.OperationReturn);
        var span = route.Family == BoundDispatchFamily.SendAllSync && route.ResultKind == FusionResultKind.Response;
        flow.Edge(Entry("operation"), argument);
        flow.Edge(argument, boundary, SendControlOutcome.Success);
        flow.Edge(argument, Exit("operation", false), SendControlOutcome.Failure);
        var first = flow.Node(0, -1, SendExecutionPhase.BranchStart);
        if (span)
        {
            flow.Edge(boundary, guard, predicate: SendControlPredicate.SpanDestination);
            flow.Edge(boundary, first, predicate: SendControlPredicate.NoSpanDestination);
            flow.Edge(guard, first, SendControlOutcome.Success);
            flow.Edge(guard, Exit("operation", false), SendControlOutcome.Failure);
        }
        else flow.Edge(boundary, first);
        for (var b = 0; b < route.Handlers.Count; b++)
        {
            var branch = flow.Node(b, -1, SendExecutionPhase.BranchStart);
            flow.Edge(branch, Exit("operation", false), SendControlOutcome.Failure);
            if (b + 1 < route.Handlers.Count)
                flow.Edge(branch, flow.Node(b + 1, -1, SendExecutionPhase.BranchStart), SendControlOutcome.Success);
            else if (span)
            {
                flow.Edge(branch, commit, SendControlOutcome.Success, SendControlPredicate.SpanDestination);
                flow.Edge(branch, returned, SendControlOutcome.Success, SendControlPredicate.NoSpanDestination);
            }
            else flow.Edge(branch, returned, SendControlOutcome.Success);
        }
        if (span) flow.Edge(commit, returned);
        flow.Edge(returned, Exit("operation", true));
        return flow.Finish();
    }

    private static SendControlFlow ScopeFlow(BoundPipelineModel route, int b, int s)
    {
        var flow = new CanonicalSendControlBuilder(Scope(b, s));
        var handler = s == route.Stages.Count;
        var hook = !handler && route.Stages[s].Kind == FusionStageKind.Hook;
        SendControlPoint Node(SendExecutionPhase phase) => flow.Node(b, s, phase);
        var failed = Exit(flow._owner, false);
        var succeeded = Exit(flow._owner, true);
        var entered = Node(SendExecutionPhase.Entered);
        var sequence = new[] { SendExecutionPhase.ArgumentGuard, SendExecutionPhase.CancellationGuard, SendExecutionPhase.Resolve };
        flow.Edge(Entry(flow._owner), Node(sequence[0]));
        for (var index = 0; index < sequence.Length; index++)
        {
            flow.Edge(Node(sequence[index]), index + 1 < sequence.Length ? Node(sequence[index + 1]) : entered, SendControlOutcome.Success);
            flow.Edge(Node(sequence[index]), failed, SendControlOutcome.Failure);
        }
        if (!hook)
        {
            var invocation = Node(handler ? SendExecutionPhase.InvokeHandler : SendExecutionPhase.InvokeWrapper);
            flow.Edge(entered, invocation);
            flow.Edge(invocation, succeeded, SendControlOutcome.Success);
            flow.Edge(invocation, failed, SendControlOutcome.Failure);
            return flow.Finish();
        }
        var callbacks = route.Stages[s].Callbacks.Select(c => c.Phase).ToArray();
        var before = callbacks.Contains(BoundCallbackPhase.Before);
        var after = callbacks.Contains(BoundCallbackPhase.Success);
        var observer = callbacks.Contains(BoundCallbackPhase.Exception);
        var cleanup = callbacks.Contains(BoundCallbackPhase.Finally);
        var onFailure = observer ? Node(SendExecutionPhase.Exception) : cleanup ? Node(SendExecutionPhase.Finally) : failed;
        var onSuccess = cleanup ? Node(SendExecutionPhase.Finally) : succeeded;
        var inner = Node(SendExecutionPhase.InvokeInner);
        flow.Edge(entered, before ? Node(SendExecutionPhase.Before) : inner);
        if (before)
        {
            flow.Edge(Node(SendExecutionPhase.Before), inner, SendControlOutcome.Success);
            flow.Edge(Node(SendExecutionPhase.Before), onFailure, SendControlOutcome.Failure);
        }
        flow.Edge(inner, after ? Node(SendExecutionPhase.Success) : onSuccess, SendControlOutcome.Success);
        flow.Edge(inner, onFailure, SendControlOutcome.Failure);
        if (after)
        {
            flow.Edge(Node(SendExecutionPhase.Success), onSuccess, SendControlOutcome.Success);
            flow.Edge(Node(SendExecutionPhase.Success), onFailure, SendControlOutcome.Failure);
        }
        if (observer)
        {
            flow.Edge(Node(SendExecutionPhase.Exception), cleanup ? Node(SendExecutionPhase.Finally) : failed, SendControlOutcome.Success);
            flow.Edge(Node(SendExecutionPhase.Exception), cleanup ? Node(SendExecutionPhase.Finally) : failed, SendControlOutcome.Failure);
        }
        if (cleanup)
        {
            var finallyNode = Node(SendExecutionPhase.Finally);
            flow.Edge(finallyNode, succeeded, SendControlOutcome.Success, SendControlPredicate.NoFailure);
            flow.Edge(finallyNode, failed, SendControlOutcome.Success, SendControlPredicate.HasFailure);
            flow.Edge(finallyNode, failed, SendControlOutcome.Failure);
        }
        return flow.Finish();
    }
}
