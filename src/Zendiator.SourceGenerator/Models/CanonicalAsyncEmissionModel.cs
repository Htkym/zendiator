namespace Zendiator.SourceGenerator;

internal sealed record BoundAsyncHandlerEmission(string RequestName, string HandlerName, string ContractName, string MethodName, string? ResultName)
{public bool RequestIsValueType{get;init;}}
internal sealed record BoundAsyncFactoryEmission(string FactoryName, string ContractName, string MethodName, string? CleanupContractName, bool AwaitAcquire)
{
    public bool IsSourceDeclaration { get; init; }
}
internal sealed record CanonicalAsyncCompositionEmission(int Index, string Namespace, string? MarkerName, bool UseLocalHandlers,
    BoundAsyncHandlerEmission Handler, BoundAsyncFactoryEmission? Factory, bool HasEmptyPipeline, BoundDispatchFamily? PipelineFamily, string MicrosoftDiProviderName)
{
    public EquatableArray<string> ExplicitHandlerTypes { get; init; }
    public EquatableArray<BoundAsyncHookEmission> Hooks { get; init; } = new(System.Array.Empty<BoundAsyncHookEmission>());
}
internal sealed record BoundAsyncHookCallbackEmission(string ContractName, string MethodName);
internal sealed record BoundAsyncHookEmission(string HookName, BoundAsyncHookCallbackEmission Before,
    BoundAsyncHookCallbackEmission Success, BoundAsyncHookCallbackEmission Exception, BoundAsyncHookCallbackEmission Finally)
{
    public BoundAsyncFactoryEmission? Factory { get; init; }
}
internal sealed record CanonicalAsyncEmissionModel(EquatableArray<CanonicalAsyncCompositionEmission> Compositions);
