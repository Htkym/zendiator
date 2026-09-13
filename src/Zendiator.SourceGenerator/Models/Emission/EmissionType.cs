namespace Zendiator.SourceGenerator;

/// <summary>Only the type facts needed by templates; never retains a Roslyn symbol.</summary>
internal sealed record EmissionType(string Name, string OpenName, bool IsReferenceType, bool IsRefLikeType, bool DirectCall = false);
