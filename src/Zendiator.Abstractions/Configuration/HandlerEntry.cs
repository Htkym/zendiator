namespace Zendiator;

/// <summary>One handler in an authoritative route list, including an optional borrowed DI factory.</summary>
public readonly record struct HandlerEntry
{
    private HandlerEntry(Type type, Type? factoryType = null) { Type = type; FactoryType = factoryType; }
    /// <summary>Gets the marked handler callback type.</summary>
    public Type? Type { get; }
    /// <summary>Gets the borrowed DI factory type, or null for a directly borrowed handler service.</summary>
    public Type? FactoryType { get; }
    /// <summary>Declares a marked handler resolved as a borrowed DI service.</summary>
    public static HandlerEntry Service<THandler>() => Service(typeof(THandler));
    /// <summary>Declares a marked handler by its known type.</summary>
    public static HandlerEntry Service(Type type) => new(type ?? throw new ArgumentNullException(nameof(type)));
    /// <summary>Declares a marked handler session returned by one explicit Owned or Borrowed factory contract.</summary>
    public static HandlerEntry Session<TSession, TFactory>() where TSession : class => new(typeof(TSession), typeof(TFactory));
    /// <summary>Allows literal typeof service entries beside explicit session entries in the same ordered list.</summary>
    public static implicit operator HandlerEntry(Type type) => Service(type);
}
