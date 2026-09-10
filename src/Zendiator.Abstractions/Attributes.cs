namespace Zendiator;

/// <summary>Generates a typed mediator. On a class, targets an empty public sealed partial class named Zendiator. On an assembly, generates the mediator without a hand-written class.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Assembly, Inherited = false)]
public sealed class GenerateZendiatorAttribute : Attribute
{
    /// <summary>Gets or sets the namespace for assembly-level generation. Ignored on a class declaration.</summary>
    public string? Namespace { get; set; }
}

/// <summary>Includes the assembly containing a marker in compile-time discovery.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class IncludeAssemblyAttribute(Type markerType) : Attribute
{
    /// <summary>Gets the type identifying the included assembly.</summary>
    public Type MarkerType { get; } = markerType;
}

/// <summary>Adds a closed behavior or a two-parameter generic behavior to the generated pipeline.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class PipelineBehaviorAttribute(Type behaviorType) : Attribute
{
    /// <summary>Gets the behavior implementation type.</summary>
    public Type BehaviorType { get; } = behaviorType;

    /// <summary>Gets or sets the unique order. Lower values run outside higher values.</summary>
    public int Order { get; set; }
}

/// <summary>Sets the dispatch order of a notification subscriber or a multi-request handler. Lower values run first. The default is 0.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class HandlerOrderAttribute : Attribute
{
    /// <summary>Creates an order marker with order 0.</summary>
    public HandlerOrderAttribute()
    {
    }

    /// <summary>Creates an order marker with the supplied order.</summary>
    public HandlerOrderAttribute(int order) => Order = order;

    /// <summary>Gets or sets the order. Lower values run first.</summary>
    public int Order { get; set; }
}

/// <summary>Declares a known notification type that has no subscriber in the scanned assemblies.</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class NotificationAttribute(Type notificationType) : Attribute
{
    /// <summary>Gets the declared notification type.</summary>
    public Type NotificationType { get; } = notificationType;
}
