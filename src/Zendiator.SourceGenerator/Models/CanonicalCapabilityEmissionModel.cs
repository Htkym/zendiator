namespace Zendiator.SourceGenerator;
internal sealed record CapabilityCallbackEmission(string ContractName,string MethodName,BoundCompletionKind Completion);
internal sealed record HookCapabilityEmission(string HookName,CapabilityCallbackEmission? Before,CapabilityCallbackEmission? Success,
    CapabilityCallbackEmission? Exception,CapabilityCallbackEmission? Finally)
{public bool HookIsValueType{get;init;} public BoundAsyncFactoryEmission? Factory{get;init;}}
internal sealed record CanonicalCapabilityCompositionEmission(CanonicalSyncCompositionEmission Registration,BoundDispatchFamily Family,
    EquatableArray<HookCapabilityEmission> Hooks,BoundAsyncFactoryEmission? HandlerFactory);
internal sealed record CanonicalCapabilityEmissionModel(EquatableArray<CanonicalCapabilityCompositionEmission> Compositions);
