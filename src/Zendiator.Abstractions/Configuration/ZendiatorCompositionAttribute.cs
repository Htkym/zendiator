namespace Zendiator;

/// <summary>Names an explicit, independent 0.5 composition.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ZendiatorCompositionAttribute(string key) : Attribute
{
    /// <summary>Gets the composition key, which must be nonempty and have no surrounding whitespace or control characters.</summary>
    public string Key { get; } = key;

    /// <summary>Gets or sets closed dependency storage, selected when generating this composition.</summary>
    public ClosedServiceCacheMode ClosedServiceCache { get; set; } = ClosedServiceCacheMode.Inline;

    /// <summary>Gets or sets closed Send-route dependencies injected by native DI when the mediator is constructed. Unlisted services remain lazy.</summary>
    public Type[] EagerDependencies { get; set; } = [];
}
