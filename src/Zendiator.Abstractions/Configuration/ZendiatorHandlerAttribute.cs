namespace Zendiator;

/// <summary>Explicitly identifies a source handler for the 0.5 generation contract.</summary>
/// <remarks>The new generation backend must support the selected contract. A marked handler
/// is never silently generated through the legacy discovery or execution contract.</remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class ZendiatorHandlerAttribute : Attribute;
