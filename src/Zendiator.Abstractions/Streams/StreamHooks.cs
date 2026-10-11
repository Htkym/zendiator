namespace Zendiator;

/// <summary>Accepts or skips an item without invoking the upstream.</summary>
public interface IStreamItemFilter<TRequest, TItem>
{
    /// <summary>Returns whether the item continues to the next declared stage.</summary>
    bool Accept(TRequest request, TItem item, CancellationToken token);
}
/// <summary>Maps an item to the same item type.</summary>
public interface IStreamItemMap<TRequest, TItem>
{
    /// <summary>Returns the item passed to the next declared stage.</summary>
    TItem Map(TRequest request, TItem item, CancellationToken token);
}
/// <summary>Opens an independent upstream execution with the supplied request and token.</summary>
public interface IStreamUpstream<TRequest, TItem>
{
    /// <summary>Starts independent upstream state using the supplied request and token.</summary>
    IAsyncEnumerable<TItem> Open(TRequest request, CancellationToken token);
}
/// <summary>An opaque wrapper. It owns enumerators it acquires; directly returned enumerables delegate cursor ownership to the consumer.</summary>
public interface IStreamWrapper<TRequest, TItem>
{
    /// <summary>Wraps the upstream and may open it zero or multiple times.</summary>
    IAsyncEnumerable<TItem> Wrap<TUpstream>(TRequest request, TUpstream upstream, CancellationToken token)
        where TUpstream : IStreamUpstream<TRequest, TItem>;
}
