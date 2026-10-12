namespace Zendiator.SourceGenerator;

internal sealed record BoundWrapperEmission(string WrapperName,string ContractName,string MethodName,BoundCompletionKind Completion);
internal sealed record RegistrationStageEmission(string TypeName,string Kind,string? FactoryName = null);
internal sealed record CanonicalWrapperCompositionEmission(CanonicalSyncCompositionEmission Registration,
    BoundDispatchFamily Family,BoundWrapperEmission Wrapper,BoundSyncHookEmission? SyncOuter,BoundSyncHookEmission? SyncInner,
    BoundAsyncHookEmission? AsyncOuter,BoundAsyncHookEmission? AsyncInner,BoundAsyncFactoryEmission? AsyncFactory);
internal sealed record CanonicalWrapperEmissionModel(EquatableArray<CanonicalWrapperCompositionEmission> Compositions);
