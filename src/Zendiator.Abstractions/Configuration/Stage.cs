namespace Zendiator;

public enum DispatchFamily
{
    SendAsync,
    SendSync,
    /// <summary>Asynchronous handlers in the declared branch order.</summary>
    SendAllAsync,
    /// <summary>Synchronous handlers in the declared branch order.</summary>
    SendAllSync,
    Stream,
    /// <summary>Sequential notification subscribers.</summary>
    Notification
}

public enum StageKind
{
    /// <summary>A dedicated callback contract without a continuation.</summary>
    Hook,
    /// <summary>An opaque contract with an explicitly supplied continuation or upstream.</summary>
    Wrapper
}

/// <summary>An explicit stage declaration. Its position in the pipeline defines order.</summary>
public readonly record struct Stage
{
    private Stage(Type type, StageKind kind, Type? factoryType = null) { Type = type; Kind = kind; FactoryType = factoryType; }
    /// <summary>Gets the declared callback type, resolved from DI or returned by FactoryType.</summary>
    public Type? Type { get; }
    /// <summary>Gets the borrowed DI factory type, or null for a directly borrowed service.</summary>
    public Type? FactoryType { get; }
    public StageKind Kind { get; }
    public static Stage Hook<T>() => Hook(typeof(T));
    /// <summary>Declares a session hook from one explicit Owned or Borrowed factory contract.</summary>
    public static Stage Hook<TSession, TFactory>() where TSession : class => new(typeof(TSession), StageKind.Hook, typeof(TFactory));
    public static Stage Hook(Type type) => new(type ?? throw new ArgumentNullException(nameof(type)), StageKind.Hook);
    /// <summary>Declares literal receiver/factory definitions closed from the request and receiver contract.</summary>
    public static Stage Hook(Type session, Type factory) => new(session ?? throw new ArgumentNullException(nameof(session)), StageKind.Hook, factory ?? throw new ArgumentNullException(nameof(factory)));
    public static Stage Wrapper<T>() => Wrapper(typeof(T));
    /// <summary>Declares a session wrapper from one explicit Owned or Borrowed factory contract.</summary>
    public static Stage Wrapper<TSession, TFactory>() where TSession : class => new(typeof(TSession), StageKind.Wrapper, typeof(TFactory));
    public static Stage Wrapper(Type type) => new(type ?? throw new ArgumentNullException(nameof(type)), StageKind.Wrapper);
    /// <summary>Declares literal wrapper/factory definitions closed at the wrapper's typed boundary.</summary>
    public static Stage Wrapper(Type session, Type factory) => new(session ?? throw new ArgumentNullException(nameof(session)), StageKind.Wrapper, factory ?? throw new ArgumentNullException(nameof(factory)));
}
