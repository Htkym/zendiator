namespace Zendiator.SourceGenerator;

internal sealed record CanonicalSendAllSyncCompositionEmission(EquatableArray<CanonicalSyncCompositionEmission> Branches)
{
    internal CanonicalSyncCompositionEmission Registration => Branches[0];
}
internal sealed record CanonicalSendAllSyncEmissionModel(EquatableArray<CanonicalSendAllSyncCompositionEmission> Compositions);
