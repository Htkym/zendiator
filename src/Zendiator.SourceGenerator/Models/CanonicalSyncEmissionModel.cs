namespace Zendiator.SourceGenerator;

// Callable names come from bound symbols, never from parsing assembly identities or method displays.
internal sealed record BoundSyncFactoryEmission(string FactoryName, string ContractName, string MethodName, string? CleanupContractName)
{
    public bool FactoryIsValueType
    {
        get; init;
    }
    public bool IsSourceDeclaration
    {
        get; init;
    }
}
internal sealed record BoundSyncHandlerEmission(string RequestName, string HandlerName, string ContractName, string MethodName, string? ResultName)
{
    public bool RequestIsValueType
    {
        get; init;
    }
    public bool RequestIsRefLike
    {
        get; init;
    }
    public bool HandlerIsValueType
    {
        get; init;
    }
}
internal sealed record BoundSyncHookCallbackEmission(string ContractName, string MethodName);
internal sealed record BoundSyncHookEmission(string HookName, BoundSyncHookCallbackEmission Before,
    BoundSyncHookCallbackEmission Success, BoundSyncHookCallbackEmission Exception, BoundSyncHookCallbackEmission Finally)
{
    public bool HookIsValueType
    {
        get; init;
    }
    public BoundSyncFactoryEmission? Factory
    {
        get; init;
    }
}
internal sealed record BoundScopedObserverEmission(string ObserverName, BoundSyncHookCallbackEmission Before,
    BoundSyncHookCallbackEmission Success, BoundSyncHookCallbackEmission Exception, BoundSyncHookCallbackEmission Finally)
{
    public bool ObserverIsValueType
    {
        get; init;
    }
}
internal sealed record CanonicalSyncCompositionEmission(int Index, string Namespace, string? MarkerName, bool UseLocalHandlers,
    BoundSyncHandlerEmission Handler, BoundSyncFactoryEmission? Factory, bool HasEmptyPipeline, BoundDispatchFamily? PipelineFamily, string MicrosoftDiProviderName)
{
    public BoundScopedObserverEmission? ScopedObserver
    {
        get; init;
    }
    public BoundStreamGenericEmission? Generic
    {
        get; init;
    }
    public BoundStreamOpenRegistration? OpenRegistration
    {
        get; init;
    }
    public EquatableArray<string> ExternAliases
    {
        get; init;
    }
    public EquatableArray<string> ExplicitHandlerTypes
    {
        get; init;
    }
    public EquatableArray<string> CatalogMarkerTypes
    {
        get; init;
    }
    public EquatableArray<BoundSyncHookEmission> Hooks { get; init; } = new(System.Array.Empty<BoundSyncHookEmission>());
    public EquatableArray<RegistrationStageEmission> RegistrationStages { get; init; } = new(System.Array.Empty<RegistrationStageEmission>());
    public BoundStreamBeforeRegistration? StreamBeforeRegistration
    {
        get; init;
    }
    public EquatableArray<string> RouteDependencyTypes
    {
        get; init;
    }
}
internal sealed record CanonicalSyncEmissionModel(EquatableArray<CanonicalSyncCompositionEmission> Compositions);
internal sealed record CanonicalSyncCallSite(string Namespace, string? MarkerName, int Version, string Data, bool ExtensionForm)
{
    public EquatableArray<string> ExternAliases
    {
        get; init;
    }
}
internal sealed record CanonicalSyncInterceptorModel(EquatableArray<CanonicalSyncCallSite> CallSites);
