using System.Collections.Generic;


namespace Zendiator.SourceGenerator;

/// <summary>Names and registration strategy for the generated composition.</summary>
internal sealed record GenerationTarget
{
    internal GenerationTarget(
        string? targetNamespace,
        string mediatorName,
        string? configurationFingerprint = null,
        IEnumerable<(int Version, string Data, bool IsLambda, bool IsExtensionForm)>? callSites = null)
    {
        Namespace = targetNamespace;
        MediatorName = mediatorName;
        ConfigurationFingerprint = configurationFingerprint;
        CallSites = callSites == null ? default : new(callSites);
    }

    public string? Namespace { get; }
    public string Prefix => Namespace == null ? "global::" : "global::" + Namespace + ".";
    public string MediatorName { get; }
    public string? ConfigurationFingerprint { get; }
    public bool EmitExtensions => ConfigurationFingerprint == null;
    public EquatableArray<(int Version, string Data, bool IsLambda, bool IsExtensionForm)> CallSites { get; init; }
}
