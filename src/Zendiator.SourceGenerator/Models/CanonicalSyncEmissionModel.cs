namespace Zendiator.SourceGenerator;

// Callable names come from bound symbols, never from parsing assembly identities or method displays.
internal sealed record BoundSyncFactoryEmission(string FactoryName, string ContractName, string MethodName, string? CleanupContractName);
internal sealed record BoundSyncHandlerEmission(string RequestName, string HandlerName, string ContractName, string MethodName, string? ResultName);
internal sealed record CanonicalSyncCompositionEmission(int Index, string Namespace, string? MarkerName, bool UseLocalHandlers,
    BoundSyncHandlerEmission Handler, BoundSyncFactoryEmission? Factory, bool HasEmptyPipeline, BoundDispatchFamily? PipelineFamily);
internal sealed record CanonicalSyncEmissionModel(EquatableArray<CanonicalSyncCompositionEmission> Compositions);
internal sealed record CanonicalSyncCallSite(string Namespace, string? MarkerName, int Version, string Data, bool ExtensionForm);
internal sealed record CanonicalSyncInterceptorModel(EquatableArray<CanonicalSyncCallSite> CallSites);
