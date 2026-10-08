namespace Zendiator;

/// <summary>Adds a closed synchronous validator to matching closed StreamAsync routes.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class StreamRequestValidatorAttribute(Type validatorType) : Attribute
{
    /// <summary>Gets the concrete validator implementation type.</summary>
    public Type ValidatorType { get; } = validatorType;

    /// <summary>Gets or sets the unique validator order. Lower values validate first.</summary>
    public int Order { get; set; }
}
