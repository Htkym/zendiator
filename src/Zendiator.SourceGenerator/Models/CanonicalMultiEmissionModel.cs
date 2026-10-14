namespace Zendiator.SourceGenerator;

// Compile-time composition only. Every route retains its accepted backend model.
internal sealed record CanonicalMultiRouteEmission(string Key, int HandlerOrdinal, int PipelineOrdinal,
    CanonicalSyncCompositionEmission Registration, CanonicalSyncCompositionEmission? Sync,
    CanonicalAsyncCompositionEmission? Async, CanonicalWrapperCompositionEmission? Wrapper, CanonicalCapabilityCompositionEmission? Capability)
{
    public CanonicalStreamPublicComposition? Stream { get; init; }
    public string? StreamStableKey { get; init; }
    public BoundDispatchFamily? Family => Sync != null ? BoundDispatchFamily.SendSync :
        Async != null ? BoundDispatchFamily.SendAsync : Wrapper != null ? Wrapper.Family :
        Stream != null ? BoundDispatchFamily.Stream : Capability!.Family;
}
internal sealed record CanonicalMultiCompositionEmission(int Index, string Namespace, EquatableArray<CanonicalMultiRouteEmission> Routes);
internal sealed record CanonicalMultiEmissionModel(EquatableArray<CanonicalMultiCompositionEmission> Compositions);
