namespace Zendiator.SourceGenerator;

internal sealed record CanonicalSendAllAsyncCompositionEmission(EquatableArray<CanonicalAsyncCompositionEmission> Branches)
{
    internal CanonicalAsyncCompositionEmission Branch => Branches[0];
    internal CanonicalSyncCompositionEmission Registration => CanonicalAsyncEmissionNormalizer.Registration(Branch);
}
internal sealed record CanonicalSendAllAsyncEmissionModel(EquatableArray<CanonicalSendAllAsyncCompositionEmission> Compositions);
