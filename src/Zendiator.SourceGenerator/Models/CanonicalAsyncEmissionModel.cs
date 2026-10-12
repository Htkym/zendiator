namespace Zendiator.SourceGenerator;

internal sealed record BoundAsyncHandlerEmission(string RequestName, string HandlerName, string ContractName, string MethodName, string? ResultName);
internal sealed record BoundAsyncFactoryEmission(string FactoryName, string ContractName, string MethodName, string? CleanupContractName, bool AwaitAcquire)
{
    public bool IsSourceDeclaration { get; init; }
}
internal sealed record CanonicalAsyncCompositionEmission(int Index, string Namespace, string? MarkerName, bool UseLocalHandlers,
    BoundAsyncHandlerEmission Handler, BoundAsyncFactoryEmission? Factory, bool HasEmptyPipeline, BoundDispatchFamily? PipelineFamily, string MicrosoftDiProviderName);
internal sealed record CanonicalAsyncEmissionModel(EquatableArray<CanonicalAsyncCompositionEmission> Compositions);
