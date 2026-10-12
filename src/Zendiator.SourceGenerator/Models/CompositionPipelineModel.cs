namespace Zendiator.SourceGenerator;

internal enum BoundDispatchFamily { SendAsync, SendSync, SendAllAsync, SendAllSync, Stream, Notification }
internal enum BoundCallbackPhase { Before, Success, Exception, Finally, Filter, Map, Wrapper, Handler, SessionAcquire, SessionCleanup }
internal enum BoundCompletionKind { Synchronous, ValueTask, Enumerable }
internal sealed record BoundParameterModel(string Type, string RefKind, string ScopedKind);
internal sealed record BoundCallbackModel(BoundCallbackPhase Phase, BoundCompletionKind Completion,
    string Interface, string Implementation, EquatableArray<BoundParameterModel> Parameters,
    string ReturnType, string GenericConstraints, bool RequiresSingleConsumption);
internal sealed record BoundStageModel(string Anchor, string Type, string Name, FusionStageKind Kind,
    EquatableArray<string> Hierarchy, EquatableArray<BoundCallbackModel> Callbacks,
    FusionOwnership ServiceOwnership, bool CompilerOwnsError)
{
    public EquatableArray<BoundSessionModel> Sessions { get; init; }
    public BoundSyncHookEmission? SyncEmission { get; init; }
    public BoundAsyncHookEmission? AsyncEmission { get; init; }
    public BoundWrapperEmission? WrapperEmission { get; init; }
    public HookCapabilityEmission? Capabilities { get; init; }
}
internal sealed record BoundHandlerModel(string Anchor, string Type, string Name, string Contract, string? Result,
    string Implementation, FusionOwnership ServiceOwnership, bool CompilerOwnsError, BoundCallbackModel? Callback)
{
    public EquatableArray<BoundSessionModel> Sessions { get; init; }
    public BoundSyncHandlerEmission? SyncEmission { get; init; }
    public BoundAsyncHandlerEmission? AsyncEmission { get; init; }
}
// Explicit factory facts for operation state. DI services remain borrowed; no callback body
// or IDisposable interface on a service authorizes an ownership transfer.
internal sealed record BoundSessionModel(string FactoryServiceType, string ResourceType,
    BoundCallbackModel Acquire, FusionOwnership Ownership, BoundCallbackModel? Cleanup)
{
    // A factory-produced callback receiver cannot be lowered as a separately resolved DI service.
    public bool SuppliesCallbackReceiver { get; init; }
    public EquatableArray<string> FactoryHierarchy { get; init; }
    public BoundSyncFactoryEmission? SyncEmission { get; init; }
    public BoundAsyncFactoryEmission? AsyncEmission { get; init; }
}
internal sealed record BoundPipelineModel(string Anchor, string Request, string RequestName,
    BoundDispatchFamily? Family, FusionResultKind? ResultKind, string? Result,
    EquatableArray<BoundHandlerModel> Handlers, EquatableArray<BoundStageModel> Stages,
    bool ExplicitHandlerOrder, FusionContractOrigin ContractOrigin)
{
    public bool HasEmptyPipeline { get; init; }
    public BoundDispatchFamily? PipelineFamily { get; init; }
}
internal sealed record CompositionDiagnosticFact(string Key, string Anchor, string Message, string? AdditionalAnchor = null);
