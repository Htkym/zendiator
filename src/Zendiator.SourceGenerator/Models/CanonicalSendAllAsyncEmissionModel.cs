namespace Zendiator.SourceGenerator;

internal sealed record CanonicalSendAllAsyncCompositionEmission(CanonicalAsyncCompositionEmission Branch,
    EquatableArray<BoundAsyncHandlerEmission> Handlers)
{
    internal CanonicalSyncCompositionEmission Registration => CanonicalAsyncEmissionNormalizer.Registration(Branch);
}
internal sealed record CanonicalSendAllAsyncEmissionModel(EquatableArray<CanonicalSendAllAsyncCompositionEmission> Compositions);
