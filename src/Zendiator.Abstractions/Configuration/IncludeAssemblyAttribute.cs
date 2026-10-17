namespace Zendiator;

/// <summary>Includes the assembly containing a marker in compile-time discovery.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class IncludeAssemblyAttribute(Type markerType) : Attribute
{
    public Type MarkerType { get; } = markerType;
}
