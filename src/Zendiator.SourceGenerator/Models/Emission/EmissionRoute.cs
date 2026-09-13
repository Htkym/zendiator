namespace Zendiator.SourceGenerator;

internal sealed record EmissionRoute
{
    public EmissionType Request { get; init; } = null!;
    public EmissionType Response { get; init; } = null!;
    public EmissionType Handler { get; init; } = null!;
    public bool IsVoid { get; init; }
    public bool IsSync { get; init; }
    public bool IsOpen { get; init; }
    public EquatableArray<EmissionType> Behaviors { get; init; }
    public EquatableArray<string> OpenTypeParams { get; init; }
    public EquatableArray<string> MethodConstraints { get; init; }
    public string RequestDisplay { get; init; } = "";
    public string ResponseDisplay { get; init; } = "";
    public string HandlerDisplay { get; init; } = "";
    public string HandlerContractDisplay { get; init; } = "";
    public EquatableArray<string> BehaviorDisplays { get; init; }
    public EquatableArray<string> BehaviorContractDisplays { get; init; }
}
