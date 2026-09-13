namespace Zendiator;

/// <summary>Includes the assembly containing a marker in compile-time discovery.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class IncludeAssemblyAttribute(Type markerType) : Attribute
{
    /// <summary>Gets the type identifying the included assembly.</summary>
    public Type MarkerType { get; } = markerType;
}
