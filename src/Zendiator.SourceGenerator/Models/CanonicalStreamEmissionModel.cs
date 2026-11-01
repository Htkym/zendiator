namespace Zendiator.SourceGenerator;

internal sealed record BoundStreamHandlerEmission(string RequestName, string ItemName, string HandlerName,
    string ContractName, string MethodName, bool RequestIsValueType, bool HandlerIsValueType)
{
    public string CursorName { get; init; } = "";
    public bool CursorIsValueType
    {
        get; init;
    }
    public bool ReturnsEnumerable
    {
        get; init;
    }
    public BoundAsyncCallTargetEmission? CallTarget
    {
        get; init;
    }
}
internal sealed record BoundStreamHookEmission(string HookName, string ContractName, string MethodName,
    BoundCallbackPhase Phase, bool HookIsValueType, BoundCompletionKind Completion = BoundCompletionKind.Synchronous)
{
    public string? InputItemName
    {
        get; init;
    }
    public string? OutputItemName
    {
        get; init;
    }
    public BoundAsyncFactoryEmission? Factory
    {
        get; init;
    }
}
