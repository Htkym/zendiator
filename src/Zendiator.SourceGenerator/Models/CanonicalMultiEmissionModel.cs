namespace Zendiator.SourceGenerator;

internal sealed record CanonicalMultiRouteEmission(string Key, int HandlerOrdinal, int PipelineOrdinal,
    CanonicalSyncCompositionEmission Registration, CanonicalSyncCompositionEmission? Sync,
    CanonicalAsyncCompositionEmission? Async, CanonicalWrapperCompositionEmission? Wrapper, CanonicalCapabilityCompositionEmission? Capability)
{
    public CanonicalStreamPublicComposition? Stream { get; init; }
    public CanonicalSendAllSyncCompositionEmission? SendAllSync { get; init; }
    public CanonicalSendAllAsyncCompositionEmission? SendAllAsync { get; init; }
    public string? StreamStableKey { get; init; }
    public BoundDispatchFamily? Family => Sync != null ? BoundDispatchFamily.SendSync :
        Async != null ? BoundDispatchFamily.SendAsync : Wrapper != null ? Wrapper.Family :
        Stream != null ? BoundDispatchFamily.Stream : SendAllSync != null ? BoundDispatchFamily.SendAllSync :
        SendAllAsync != null ? BoundDispatchFamily.SendAllAsync : Capability!.Family;
}
internal sealed record CanonicalMultiCompositionEmission(int Index, string Namespace, EquatableArray<CanonicalMultiRouteEmission> Routes);
internal sealed record CanonicalMultiEmissionModel(EquatableArray<CanonicalMultiCompositionEmission> Compositions);
