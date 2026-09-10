namespace Zendiator;

/// <summary>The single value returned by commands without a response.</summary>
public readonly record struct Unit
{
    /// <summary>Gets the single unit value.</summary>
    public static Unit Value => default;
}
