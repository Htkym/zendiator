namespace Zendiator.SourceGenerator;

internal sealed record CanonicalSendAllAsyncCompositionEmission(EquatableArray<CanonicalAsyncCompositionEmission> Branches)
{
    public EquatableArray<CanonicalWrapperCompositionEmission> Wrappers
    {
        get; init;
    }
    internal CanonicalAsyncCompositionEmission Branch => Branches[0];
    internal CanonicalSyncCompositionEmission Registration => Wrappers.Count == 0 ? CanonicalAsyncEmissionNormalizer.Registration(Branch) : Wrappers[0].Registration;
}
internal sealed record CanonicalSendAllAsyncEmissionModel(EquatableArray<CanonicalSendAllAsyncCompositionEmission> Compositions);
