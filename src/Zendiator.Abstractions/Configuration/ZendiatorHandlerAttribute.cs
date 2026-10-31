namespace Zendiator;

/// <summary>Marks a handler for explicit composition selection.</summary>
/// <remarks>Marked handlers are excluded from legacy automatic discovery.</remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class ZendiatorHandlerAttribute : Attribute;
