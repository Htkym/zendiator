namespace Zendiator;

/// <summary>Adds a closed behavior or a two-parameter generic behavior to the generated pipeline.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class PipelineBehaviorAttribute(Type behaviorType) : Attribute
{
    /// <summary>Gets the behavior implementation type.</summary>
    public Type BehaviorType { get; } = behaviorType;

    /// <summary>Gets or sets the unique order. Lower values run outside higher values.</summary>
    public int Order { get; set; }
}
