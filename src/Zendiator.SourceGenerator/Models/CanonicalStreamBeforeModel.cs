namespace Zendiator.SourceGenerator;

internal sealed record BoundStreamBeforeEmission(string ServiceName, string ContractName, string MethodName, bool IsValueType)
{
    public BoundAsyncCallTargetEmission? CallTarget { get; init; }
}

internal sealed record BoundStreamBeforeRegistration(string RequestName, EquatableArray<string> DeclaredTypes,
    EquatableArray<string> ServiceTypes, int Ordinal);
