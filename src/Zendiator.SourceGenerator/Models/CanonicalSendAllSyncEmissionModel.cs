namespace Zendiator.SourceGenerator;

internal sealed record CanonicalSendAllSyncCompositionEmission(EquatableArray<CanonicalSyncCompositionEmission> Branches)
{
    public EquatableArray<CanonicalWrapperCompositionEmission> Wrappers { get; init; }
    internal CanonicalSyncCompositionEmission Registration => Branches[0];
}
internal sealed record CanonicalSendAllSyncEmissionModel(EquatableArray<CanonicalSendAllSyncCompositionEmission> Compositions);
