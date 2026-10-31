namespace Zendiator;

/// <summary>Controls storage of captured closed dependency instances.</summary>
public enum ClosedServiceCacheMode
{
    /// <summary>Stores each dependency in the Mediator itself.</summary>
    Inline = 0,

    /// <summary>Allocates groups of slots after their first successful native resolution.</summary>
    LazyBlocks = 1
}
