namespace Zendiator.SourceGenerator;

// Callable names come from bound symbols, never from parsing assembly identities or method displays.
internal sealed record BoundSyncFactoryEmission(string FactoryName, string ContractName, string MethodName, string? CleanupContractName)
{
    public bool IsSourceDeclaration { get; init; }
}
internal sealed record BoundSyncHandlerEmission(string RequestName, string HandlerName, string ContractName, string MethodName, string? ResultName);
internal sealed record BoundSyncHookCallbackEmission(string ContractName, string MethodName);
internal sealed record BoundSyncHookEmission(string HookName, BoundSyncHookCallbackEmission Before,
    BoundSyncHookCallbackEmission Success, BoundSyncHookCallbackEmission Exception, BoundSyncHookCallbackEmission Finally)
{
    public BoundSyncFactoryEmission? Factory { get; init; }
}
internal sealed record CanonicalSyncCompositionEmission(int Index, string Namespace, string? MarkerName, bool UseLocalHandlers,
    BoundSyncHandlerEmission Handler, BoundSyncFactoryEmission? Factory, bool HasEmptyPipeline, BoundDispatchFamily? PipelineFamily, string MicrosoftDiProviderName)
{
    public EquatableArray<BoundSyncHookEmission> Hooks { get; init; } = new(System.Array.Empty<BoundSyncHookEmission>());
}
internal sealed record CanonicalSyncEmissionModel(EquatableArray<CanonicalSyncCompositionEmission> Compositions);
internal sealed record CanonicalSyncCallSite(string Namespace, string? MarkerName, int Version, string Data, bool ExtensionForm);
internal sealed record CanonicalSyncInterceptorModel(EquatableArray<CanonicalSyncCallSite> CallSites);
