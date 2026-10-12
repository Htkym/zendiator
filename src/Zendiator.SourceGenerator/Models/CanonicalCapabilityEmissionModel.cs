namespace Zendiator.SourceGenerator;
internal sealed record CapabilityCallbackEmission(string ContractName,string MethodName,BoundCompletionKind Completion);
internal sealed record HookCapabilityEmission(string HookName,CapabilityCallbackEmission? Before,CapabilityCallbackEmission? Success,
    CapabilityCallbackEmission? Exception,CapabilityCallbackEmission? Finally);
internal sealed record CanonicalCapabilityCompositionEmission(CanonicalSyncCompositionEmission Registration,BoundDispatchFamily Family,
    EquatableArray<HookCapabilityEmission> Hooks);
internal sealed record CanonicalCapabilityEmissionModel(EquatableArray<CanonicalCapabilityCompositionEmission> Compositions);
