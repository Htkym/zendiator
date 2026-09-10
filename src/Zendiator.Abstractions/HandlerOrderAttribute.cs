namespace Zendiator;

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
