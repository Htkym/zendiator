namespace Zendiator.SourceGenerator;

internal sealed record EmissionBranch
{
    public EmissionType Handler { get; init; } = null!;
    public bool HandlerIsOpen { get; init; }
    public string HandlerDisplay { get; init; } = "";
    public string HandlerContractDisplay { get; init; } = "";
    public EquatableArray<EmissionType> Behaviors { get; init; }
    public EquatableArray<string> BehaviorDisplays { get; init; }
    public EquatableArray<string> BehaviorContractDisplays { get; init; }
}
