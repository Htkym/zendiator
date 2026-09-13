namespace Zendiator.SourceGenerator;

internal sealed record EmissionMultiRoute
{
    public EmissionType Request { get; init; } = null!;
    public EmissionType Response { get; init; } = null!;
    public bool IsVoid { get; init; }
    public bool IsOpen { get; init; }
    public bool IsSync { get; init; }
    public EquatableArray<EmissionBranch> Branches { get; init; }
    public EquatableArray<string> OpenTypeParams { get; init; }
    public EquatableArray<string> MethodConstraints { get; init; }
    public string RequestDisplay { get; init; } = "";
    public string ResponseDisplay { get; init; } = "";
}
