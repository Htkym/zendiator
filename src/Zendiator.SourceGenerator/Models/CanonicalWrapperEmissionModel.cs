namespace Zendiator.SourceGenerator;

internal sealed record BoundWrapperEmission(string WrapperName, string ContractName, string MethodName, BoundCompletionKind Completion)
{ public bool WrapperIsValueType { get; init; } }
internal sealed record RegistrationStageEmission(string TypeName, string Kind, string? FactoryName = null);
internal sealed record CanonicalWrapperRegionEmission(HookCapabilityEmission? Hook, BoundWrapperEmission? Wrapper, BoundAsyncFactoryEmission? Factory)
{ public EquatableArray<HookCapabilityEmission> Hooks { get; init; } }
internal sealed record CanonicalWrapperCompositionEmission(CanonicalSyncCompositionEmission Registration,
    BoundDispatchFamily Family, BoundWrapperEmission Wrapper, BoundSyncHookEmission? SyncOuter, BoundSyncHookEmission? SyncInner,
    BoundAsyncHookEmission? AsyncOuter, BoundAsyncHookEmission? AsyncInner, BoundAsyncFactoryEmission? AsyncFactory)
{ public HookCapabilityEmission? OuterCapabilities { get; init; } public HookCapabilityEmission? InnerCapabilities { get; init; } public EquatableArray<CanonicalWrapperRegionEmission> Regions { get; init; } }
internal sealed record CanonicalWrapperEmissionModel(EquatableArray<CanonicalWrapperCompositionEmission> Compositions);
