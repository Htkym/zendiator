namespace Zendiator.SourceGenerator;

// These are execution sites, never aliases for the legacy fusion planner's coarse phases.
internal enum SendExecutionPhase
{
    ArgumentGuard, OperationBoundary, DestinationGuard, BranchStart, CancellationGuard,
    Resolve, SessionGuard, ResolveSessionFactory, AcquireSession, ValidateSessionResult, CommitSession,
    DetachSession, SessionPresent, CleanupSession, SessionResult,
    Entered, Before, InvokeInner, InvokeWrapper, NextCall, InvokeHandler,
    Success, Exception, Finally, ScopeExit, DestinationCommit, OperationReturn
}
internal enum SendExecutionCondition { Always, SpanDestination, EachBranch, EachNext, InnerSuccess, FailureBeforeFinally, Entered }
internal enum SendOrderCondition { Start, InnerSuccess, Failure, Unwind, EachNext, AllBranchesSuccess, SpanDestination }
internal enum SendOwnerKind { Provider, Operation, Branch, Stage, WrapperNext, Handler, Service, Session }
internal enum SendOwnerLifetime { Caller, Operation, Branch, Stage, NextInvocation }
internal enum SendOwnerMultiplicity { Once, EachBranch, EachNext }
internal enum SendAwaitContinuation { BeginInner, NormalUnwind, EndObserver, EndScope, TransferSession, ContinueSessionCleanup }
internal enum SendFailureTarget { CurrentScope, OuterScope }
internal enum SendFailureDisposition { EscapeOperation, UnwindEnteredOuter, ObserveCurrentOnceThenFinally, ReplaceAndFinallyWithoutObserver, ReplaceAndUnwindOuter, OpaqueWrapperDecides, UnwindAcquiredSessionsThenOuter, ReplaceAndContinueSessionCleanup }
internal enum SendExitPolicy { HandlerEscape, OpaqueWrapperEscape, S01NestedHook }
internal enum SendValueKind { OperationRequest, OperationToken, InputRequest, EffectiveRequest, StartToken, BeforeResult, NextRequestArgument, NextTokenArgument }
internal enum SendTransferCondition { InvocationStart, Entered, ProducerSuccess, EachNextStart, BranchStart }
internal enum SendControlTerminal { None, Entry, Success, Failure }
internal enum SendControlOutcome { Always, Success, Failure }
internal enum SendControlPredicate { Any, HasFailure, NoFailure, SpanDestination, NoSpanDestination, SessionPresent, SessionAbsent }

internal sealed record SendExecutionSiteId(int Branch, int Stage, SendExecutionPhase Phase, int Session = -1);
internal sealed record SendExecutionSite(SendExecutionSiteId Id, string Anchor, string Owner,
    SendExecutionCondition Condition, BoundCompletionKind Completion, string? Producer, string? ResultType,
    SendFailureDisposition Failure);
internal sealed record SendExecutionOrder(SendExecutionSiteId Before, SendExecutionSiteId After, SendOrderCondition Condition);
internal sealed record SendExecutionOwner(string Id, string? Parent, SendOwnerKind Kind, SendOwnerLifetime Lifetime,
    SendOwnerMultiplicity Multiplicity, bool Borrowed, FusionCleanupKind Cleanup, bool AsyncBoundary);
internal sealed record SendInvocationState(string Owner, string RequestType, string InputRequestSlot,
    string? EffectiveRequestSlot, string StartTokenSlot, string? ErrorSlot, bool StartsWithOriginalRequest,
    bool BeforeFailureKeepsInput, bool BeforeSuccessSavesReturnedRequest, string? ResponseType, string? ResponseSlot);
internal sealed record SendExecutionScope(string Owner, string? InnerOwner, SendExecutionSiteId Invocation,
    SendExecutionSiteId? Success, SendExecutionSiteId? Exception, SendExecutionSiteId? Finally,
    SendExitPolicy ExitPolicy);
internal sealed record SendExecutionAwait(SendExecutionSiteId Producer, string Owner, string ResultType,
    bool ConsumeOnce, bool ConfigureAwaitFalse, SendAwaitContinuation Normal,
    SendFailureTarget Fault, SendFailureTarget Cancellation, EquatableArray<string> RetainedSlots);
internal sealed record SendValueReference(string Owner, SendValueKind Kind);
internal sealed record SendValueTransfer(SendValueReference Source, SendValueReference Destination,
    SendExecutionSiteId At, SendTransferCondition Condition);
// Failure includes throwing/faulted/canceled callback completion; it never recovers a Send Hook.
internal sealed record SendControlPoint(string FlowOwner, SendExecutionSiteId? Site, SendControlTerminal Terminal);
internal sealed record SendControlEdge(SendControlPoint From, SendControlPoint To, SendControlOutcome Outcome, SendControlPredicate Predicate);
internal sealed record SendControlFlow(string Owner, EquatableArray<SendControlEdge> Edges);
internal sealed record SendSessionTransfer(string Source, string Destination, SendExecutionSiteId At);
internal sealed record SendSessionCall(SendExecutionSiteId At, string Receiver, string? Result,
    bool PreserveIdentityAndToken, bool ReplacePreviousError, bool Aggregate)
{
    public string? RequestSlot { get; init; }
    public string? TokenSlot { get; init; }
}
internal sealed record SendServiceResolution(SendExecutionSiteId At, string ServiceType, string ResultSlot);
internal sealed record SendCallbackReceiver(SendExecutionSiteId At, string Receiver, string RequestSlot, string TokenSlot);

/// <summary>Symbol-free schedule templates. EachNext instantiates fresh state on every opaque next call.</summary>
internal sealed record CanonicalSendExecutionSchedule(
    EquatableArray<SendExecutionSite> Sites, EquatableArray<SendExecutionOrder> Orders,
    EquatableArray<SendExecutionOwner> Owners, EquatableArray<SendInvocationState> States,
    EquatableArray<SendExecutionScope> Scopes, EquatableArray<SendExecutionAwait> Awaits,
    string ContextModelId, bool EscapingOceIsCanceled)
{
    public EquatableArray<SendValueTransfer> Transfers { get; init; }
    public EquatableArray<SendControlFlow> ControlFlows { get; init; }
    public EquatableArray<SendSessionTransfer> SessionTransfers { get; init; }
    public EquatableArray<SendSessionCall> SessionCalls { get; init; }
    public EquatableArray<SendServiceResolution> ServiceResolutions { get; init; }
    public EquatableArray<SendCallbackReceiver> CallbackReceivers { get; init; }
}
internal sealed record CanonicalExecutionGraphFact(string Reason, string Anchor);
internal sealed record CanonicalExecutionGraphModel(BoundPipelineModel Contract, CanonicalSendExecutionSchedule? Schedule,
    EquatableArray<CanonicalExecutionGraphFact> Facts)
{
    public CanonicalStreamExecutionSchedule? StreamSchedule { get; init; }
    public CanonicalStreamWrapperSchedule? StreamWrapperSchedule { get; init; }
    public bool IsValid => (Schedule != null || StreamSchedule != null || StreamWrapperSchedule != null) && Facts.Count == 0;
}
internal sealed record CanonicalExecutionGraphCatalog(EquatableArray<EquatableArray<CanonicalExecutionGraphModel>> Compositions);

// Shared names carry no scheduling, completeness or ownership policy.
internal static class SendExecutionNames
{
    internal static SendExecutionSiteId Site(int branch, int stage, SendExecutionPhase phase) => new(branch, stage, phase);
    internal static string Branch(int branch) => "branch:" + branch;
    internal static string Scope(int branch, int stage) => Branch(branch) + "/scope:" + stage;
    internal static string Next(int branch, int stage) => Scope(branch, stage) + "/next";
    internal static string Service(int branch, int stage) => Scope(branch, stage) + "/service";
    internal static string Session(int branch, int stage, int session) => Scope(branch, stage) + "/session:" + session;
    internal static SendExecutionSiteId SessionSite(int branch, int stage, int session, SendExecutionPhase phase) => new(branch, stage, phase, session);
    internal static EquatableArray<BoundSessionModel> Sessions(BoundPipelineModel route, int branch, int stage) =>
        stage == route.Stages.Count ? route.Handlers[branch].Sessions : route.Stages[stage].Sessions;
    internal static SendControlPoint Entry(string owner) => new(owner, null, SendControlTerminal.Entry);
    internal static SendControlPoint Exit(string owner, bool success) => new(owner, null, success ? SendControlTerminal.Success : SendControlTerminal.Failure);
    internal static SendControlPoint Point(string owner, SendExecutionSiteId site) => new(owner, site, SendControlTerminal.None);
}
