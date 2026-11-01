namespace Zendiator.SourceGenerator;

internal sealed record BoundAsyncHandlerEmission(string RequestName, string HandlerName, string ContractName, string MethodName, string? ResultName)
{
    public bool RequestIsValueType
    {
        get; init;
    }
    public bool HandlerIsValueType
    {
        get; init;
    }
    public BoundAsyncCallTargetEmission? CallTarget
    {
        get; init;
    }
}
internal sealed record BoundAsyncFactoryEmission(string FactoryName, string ContractName, string MethodName, string? CleanupContractName, bool AwaitAcquire)
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
internal sealed record CanonicalAsyncCompositionEmission(int Index, string Namespace, string? MarkerName, bool UseLocalHandlers,
    BoundAsyncHandlerEmission Handler, BoundAsyncFactoryEmission? Factory, bool HasEmptyPipeline, BoundDispatchFamily? PipelineFamily, string MicrosoftDiProviderName)
{
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
    public EquatableArray<BoundAsyncHookEmission> Hooks { get; init; } = new(System.Array.Empty<BoundAsyncHookEmission>());
}
internal sealed record BoundAsyncCallTargetEmission(string ReceiverName, string ImplementationTypeName, string MethodName,
    EquatableArray<string> ParameterTypes, bool RequiresExactReceiver);
internal sealed record BoundAsyncHookCallbackEmission(string ContractName, string MethodName)
{
    public BoundAsyncCallTargetEmission? CallTarget
    {
        get; init;
    }
}
internal sealed record BoundAsyncHookEmission(string HookName, BoundAsyncHookCallbackEmission Before,
    BoundAsyncHookCallbackEmission Success, BoundAsyncHookCallbackEmission Exception, BoundAsyncHookCallbackEmission Finally)
{
    public bool HookIsValueType
    {
        get; init;
    }
    public BoundAsyncFactoryEmission? Factory
    {
        get; init;
    }
}
internal sealed record CanonicalAsyncEmissionModel(EquatableArray<CanonicalAsyncCompositionEmission> Compositions);
