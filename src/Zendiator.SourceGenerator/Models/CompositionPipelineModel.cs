namespace Zendiator.SourceGenerator;

internal enum BoundDispatchFamily { SendAsync, SendSync, SendAllAsync, SendAllSync, Stream, Notification }
internal enum BoundRequestShape { Unsupported, ClosedReference, ClosedNonGenericValue, ClosedRefLike, OpenSyncReference, OpenAsyncReference, OpenAllSyncReference, OpenAllAsyncReference }
internal enum BoundCallbackPhase { Before, Success, Exception, Finally, Filter, Map, Wrapper, Handler, SessionAcquire, SessionCleanup, ObserveBefore, ObserveSuccess, ObserveException, ObserveFinally }
internal enum BoundCompletionKind { Synchronous, ValueTask, Enumerable }
internal sealed record BoundParameterModel(string Type, string RefKind, string ScopedKind);
internal sealed record BoundCallbackModel(BoundCallbackPhase Phase, BoundCompletionKind Completion,
    string Interface, string Implementation, EquatableArray<BoundParameterModel> Parameters,
    string ReturnType, string GenericConstraints, bool RequiresSingleConsumption);
internal sealed record BoundStageModel(string Anchor, string Type, string Name, FusionStageKind Kind,
    EquatableArray<string> Hierarchy, EquatableArray<BoundCallbackModel> Callbacks,
    FusionOwnership ServiceOwnership, bool CompilerOwnsError)
{
    public bool ReceiverIsGeneric { get; init; }
    public bool ReceiverIsPublic { get; init; }
    public EquatableArray<BoundSessionModel> Sessions { get; init; }
    public BoundSyncHookEmission? SyncEmission { get; init; }
    public BoundScopedObserverEmission? ScopedObserver { get; init; }
    public BoundAsyncHookEmission? AsyncEmission { get; init; }
    public BoundWrapperEmission? WrapperEmission { get; init; }
    public HookCapabilityEmission? Capabilities { get; init; }
    public BoundStreamHookEmission? StreamEmission { get; init; }
    public BoundWrapperEmission? StreamWrapperEmission { get; init; }
    public BoundStreamStageItems? StreamItems { get; init; }
}
internal sealed record BoundHandlerModel(string Anchor, string Type, string Name, string Contract, string? Result,
    string Implementation, FusionOwnership ServiceOwnership, bool CompilerOwnsError, BoundCallbackModel? Callback)
{
    public bool ReceiverIsGeneric { get; init; }
    public bool ReceiverIsPublic { get; init; }
    public bool RequestReferenceResponse { get; init; }
    public bool ResponseContract { get; init; }
    public EquatableArray<BoundSessionModel> Sessions { get; init; }
    public BoundSyncHandlerEmission? SyncEmission { get; init; }
    public BoundAsyncHandlerEmission? AsyncEmission { get; init; }
    public BoundStreamHandlerEmission? StreamEmission { get; init; }
}
// Explicit factory facts for operation state. DI services remain borrowed; no callback body
// or IDisposable interface on a service authorizes an ownership transfer.
internal sealed record BoundSessionModel(string FactoryServiceType, string ResourceType,
    BoundCallbackModel Acquire, FusionOwnership Ownership, BoundCallbackModel? Cleanup)
{
    // A factory-produced callback receiver cannot be lowered as a separately resolved DI service.
    public bool SuppliesCallbackReceiver { get; init; }
    public bool FactoryIsGeneric { get; init; }
    public bool FactoryIsPublic { get; init; }
    public EquatableArray<string> FactoryHierarchy { get; init; }
    public BoundSyncFactoryEmission? SyncEmission { get; init; }
    public BoundAsyncFactoryEmission? AsyncEmission { get; init; }
}
internal sealed record BoundPipelineModel(string Anchor, string Request, string RequestName,
    BoundDispatchFamily? Family, FusionResultKind? ResultKind, string? Result,
    EquatableArray<BoundHandlerModel> Handlers, EquatableArray<BoundStageModel> Stages,
    bool ExplicitHandlerOrder, FusionContractOrigin ContractOrigin)
{
    public int HandlerListOrdinal { get; init; }
    public int PipelineListOrdinal { get; init; }
    public bool HasEmptyPipeline { get; init; }
    public BoundDispatchFamily? PipelineFamily { get; init; }
    public BoundRequestShape RequestShape { get; init; }
    public bool RequestIsGeneric { get; init; }
    public string RequestRuntimeIdentity { get; init; } = "";
    public BoundStreamGenericEmission? StreamGeneric { get; init; }
    public BoundStreamGenericEmission? SendGeneric { get; init; }
    public BoundStreamOpenRegistration? SendOpenRegistration { get; init; }
    public BoundStreamItemFlow? StreamItemFlow { get; init; }
    public BoundStreamOpenRegistration? StreamOpenRegistration { get; init; }
    public EquatableArray<RegistrationStageEmission> StreamDeclaredStages { get; init; }
    public EquatableArray<BoundStreamBeforeEmission> StreamBefore { get; init; }
    public BoundStreamBeforeRegistration? StreamBeforeRegistration { get; init; }
    public EquatableArray<string> StreamBeforeAnchors { get; init; }
}
// Registration definitions are captured from frontend symbols, independently of execution patterns.
internal sealed record BoundStreamOpenRegistration(string RequestDefinitionName, string HandlerDefinitionName,
    EquatableArray<string> HandlerMembership, EquatableArray<RegistrationStageEmission> Stages)
{
    public string? FinalDefinitionName { get; init; }
    public string? HandlerFactoryDefinitionName { get; init; }
    public EquatableArray<string> HandlerDefinitionNames { get; init; }
}
// Nullable-aware identities and callable C# names are retained separately from receiver shape.
internal sealed record BoundStreamItemType(string Identity, string Name, bool IsValueType);
internal sealed record BoundStreamStageItems(BoundStreamItemType Input, BoundStreamItemType Output);
internal sealed record BoundStreamItemFlow(BoundStreamItemType Source, BoundStreamItemType Final,
    EquatableArray<BoundStreamStageItems> Stages);
// Symbols stay in the frontend. Generic declarations participate in value equality and rendering.
internal sealed record BoundStreamGenericEmission(EquatableArray<string> Parameters, EquatableArray<string> Constraints)
{
    public string TypeParameters => "<" + string.Join(", ", Parameters) + ">";
    public string TypeConstraints => string.Concat(Constraints);
}
internal sealed record CompositionDiagnosticFact(string Key, string Anchor, string Message, string? AdditionalAnchor = null)
{
    public string? FixHandler { get; init; }
    public string? FixSourceChecksum { get; init; }
    public string? FixTargetChecksum { get; init; }
}
