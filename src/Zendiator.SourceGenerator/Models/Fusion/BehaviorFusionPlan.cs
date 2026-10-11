namespace Zendiator.SourceGenerator;

internal enum FusionDispatchKind { SendSync, SendAsync, SendAllSyncBranch, SendAllAsyncBranch, Stream }
internal enum FusionResultKind { Response, Void, Item }
internal enum FusionStageKind { Hook, Wrapper }
internal enum FusionSitePhase { OperationEntry, StageEntry, HandlerEntry, FirstMove, Item }
internal enum FusionGuardKind { Argument, Cancellation, TokenComposition, DestinationCapacity }
internal enum FusionOwnership { BorrowedService, BorrowedResource, OwnedSession }
internal enum FusionCleanupKind { None, Sync, Async }

[System.Flags]
internal enum FusionCapabilities
{
    None = 0,
    Before = 1,
    Success = 2,
    Exception = 4,
    Finally = 8,
    Filter = 16,
    Map = 32,
    Async = 64
}

/// <summary>Explicit contract facts, never facts inferred from a callback's body.</summary>
internal sealed record FusionDeclaredStage(
    EmissionType Type,
    FusionStageKind Kind,
    FusionCapabilities Capabilities,
    EmissionType? InputItem = null,
    EmissionType? OutputItem = null);

internal sealed record FusionGuardSite(int StageIndex, FusionSitePhase Phase, FusionGuardKind Kind);
internal sealed record FusionResolutionSite(
    int StageIndex, FusionSitePhase Phase, EmissionType Type,
    FusionOwnership Ownership, FusionCleanupKind Cleanup = FusionCleanupKind.None);
internal sealed record FusionDiagnosticFact(string Key, int StageIndex);
internal sealed record FusionSegment(int Start, int Length);

/// <summary>
/// The normalized semantic input: Send stages are outer-to-inner entry order;
/// Stream stages are source-to-consumer item order (C01).
/// </summary>
internal sealed record FusionPlanInput(
    string RouteKey,
    FusionDispatchKind DispatchKind,
    EmissionType Request,
    EmissionType Handler,
    FusionResultKind ResultKind,
    EmissionType? Result,
    EquatableArray<string> GenericConstraints,
    EquatableArray<FusionDeclaredStage> DeclaredStages,
    EquatableArray<FusionGuardSite> GuardSites,
    EquatableArray<FusionResolutionSite> ResolutionSites,
    string ContextModelId);

/// <summary>Symbol-free generation-time IR. It does not add a runtime dispatch engine.</summary>
internal sealed record BehaviorFusionPlanCore(
    FusionPlanInput Input,
    EquatableArray<FusionCapabilities> StageCapabilities,
    EquatableArray<int> AdvancedWrapperBoundaries,
    EquatableArray<FusionSegment> Segments,
    EquatableArray<FusionDiagnosticFact> DiagnosticFacts);

/// <summary>
/// A stage wraps its NextStageIndex scope; success, exception and finally belong to this scope.
/// Slots are per invocation. An opaque wrapper may invoke the inner scope zero or many times.
/// </summary>
internal sealed record SendNestedScope(
    int StageIndex, int NextStageIndex, FusionStageKind Kind,
    int StageRequestSlot, int StageTokenSlot,
    bool EnterAfterGuardAndResolution, bool InnerStatePerContinuationInvocation);

internal sealed record SendFusionPlan(
    BehaviorFusionPlanCore Core,
    EquatableArray<SendNestedScope> NestedScopes,
    int HandlerStageIndex);

/// <summary>Only this contiguous interval may use a fused item loop.</summary>
internal sealed record StreamItemSegment(FusionSegment Segment, EquatableArray<int> ItemFlowOrder);
internal sealed record StreamItemTransition(int StageIndex, EmissionType Input, EmissionType Output);
internal sealed record StreamEnumerationPlan(
    bool ResolveOnFirstMove, int SourceCurrentReadsPerSuccessfulMove,
    bool CacheMappedCurrent, bool OwnedSessionLivesUntilDispose);
internal sealed record StreamFusionPlan(
    BehaviorFusionPlanCore Core,
    EquatableArray<StreamItemSegment> ItemFlowSegments,
    EquatableArray<StreamItemTransition> ItemTypeTransitions,
    StreamEnumerationPlan Enumeration);

/// <summary>Unsupported input retains facts but has no executable fusion projection.</summary>
internal sealed record BehaviorFusionPlan(
    BehaviorFusionPlanCore Core, SendFusionPlan? Send, StreamFusionPlan? Stream);
