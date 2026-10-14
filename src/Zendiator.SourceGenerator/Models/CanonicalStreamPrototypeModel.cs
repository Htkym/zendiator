namespace Zendiator.SourceGenerator;

// Typed callable names are captured from current symbols, independently of diagnostic identities.
internal sealed record BoundStreamHandlerEmission(string RequestName, string ItemName, string HandlerName,
    string ContractName, string MethodName, bool RequestIsValueType, bool HandlerIsValueType);
internal sealed record BoundStreamHookEmission(string HookName, string ContractName, string MethodName,
    BoundCallbackPhase Phase, bool HookIsValueType, BoundCompletionKind Completion = BoundCompletionKind.Synchronous)
{
    public string? InputItemName { get; init; }
    public string? OutputItemName { get; init; }
    public BoundAsyncFactoryEmission? Factory { get; init; }
}

internal sealed record CanonicalStreamItemRegion(EquatableArray<StreamExecutionSite> Steps,
    StreamExecutionAwait MoveAwait, StreamValueTransfer CurrentTransfer);
internal sealed record CanonicalStreamSourceCleanupRegion(EquatableArray<StreamExecutionSite> Steps,
    StreamExecutionAwait DisposeAwait, StreamValueTransfer DetachTransfer, StreamExecutionOwner SourceOwner);

// Selected region views reuse the existing sites/edges/operands, not a new generic IR.
// This is an unregistered C04 prototype; it does not accept a public canonical runtime route.
internal sealed record CanonicalStreamPrototypeModel(string Namespace, string ClassName, string HintName,
    CanonicalStreamExecutionSchedule Schedule, StreamSiteId TerminalCleanupEntry,
    BoundStreamHandlerEmission Handler, EquatableArray<BoundStreamHookEmission> Hooks,
    CanonicalStreamItemRegion ItemRegion, CanonicalStreamSourceCleanupRegion CleanupRegion)
{
    public BoundStreamGenericEmission? Generic { get; init; }
}
