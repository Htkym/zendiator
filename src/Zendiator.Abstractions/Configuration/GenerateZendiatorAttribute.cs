namespace Zendiator;

/// <summary>Generates a typed mediator. On a class, targets an empty public sealed partial class named Zendiator. On an assembly, generates the mediator without a hand-written class.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Assembly, Inherited = false)]
public sealed class GenerateZendiatorAttribute : Attribute
{
    /// <summary>Gets or sets the namespace for assembly-level generation. Ignored on a class declaration.</summary>
    public string? Namespace { get; set; }
}
