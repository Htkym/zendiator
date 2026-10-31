namespace Zendiator;

/// <summary>The single value returned by commands without a response.</summary>
public readonly record struct Unit
{
    public static Unit Value => default;
}
