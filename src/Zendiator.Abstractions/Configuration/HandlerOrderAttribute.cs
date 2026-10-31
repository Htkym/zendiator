namespace Zendiator;

/// <summary>Sets the dispatch order of a notification subscriber or a multi-request handler. Lower values run first. The default is 0.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class HandlerOrderAttribute : Attribute
{
    public HandlerOrderAttribute()
    {
    }

    public HandlerOrderAttribute(int order) => Order = order;

    public int Order
    {
        get; set;
    }
}
