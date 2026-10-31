namespace Zendiator;

/// <summary>One handler in an authoritative route list, including an optional borrowed DI factory.</summary>
public readonly record struct HandlerEntry
{
    private HandlerEntry(Type type, Type? factoryType = null)
    {
        Type = type;
        FactoryType = factoryType;
    }
    public Type? Type
    {
        get;
    }
    /// <summary>Gets the borrowed DI factory type, or null for a directly borrowed handler service.</summary>
    public Type? FactoryType
    {
        get;
    }
    /// <summary>Declares a marked handler resolved as a borrowed DI service.</summary>
    public static HandlerEntry Service<THandler>() => Service(typeof(THandler));
    public static HandlerEntry Service(Type type) => new(type ?? throw new ArgumentNullException(nameof(type)));
    /// <summary>Declares a marked handler session returned by one explicit Owned or Borrowed factory contract.</summary>
    public static HandlerEntry Session<TSession, TFactory>() where TSession : class => new(typeof(TSession), typeof(TFactory));
    /// <summary>Declares literal receiver/factory definitions whose arguments are determined by the route contract.</summary>
    public static HandlerEntry Session(Type session, Type factory) => new(session ?? throw new ArgumentNullException(nameof(session)), factory ?? throw new ArgumentNullException(nameof(factory)));
    /// <summary>Allows literal typeof service entries beside explicit session entries in the same ordered list.</summary>
    public static implicit operator HandlerEntry(Type type) => Service(type);
}
