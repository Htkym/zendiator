namespace Zendiator;

/// <summary>Accepts or skips an item without invoking the upstream.</summary>
public interface IStreamItemFilter<TRequest, TItem>
{
    bool Accept(TRequest request, TItem item, CancellationToken token);
}
/// <summary>Maps an item to the same item type.</summary>
public interface IStreamItemMap<TRequest, TItem>
{
    TItem Map(TRequest request, TItem item, CancellationToken token);
}
/// <summary>Maps an item to a separately declared output type, preserving stage order.</summary>
public interface IStreamItemMap<TRequest, TIn, TOut>
{
    TOut Map(TRequest request, TIn item, CancellationToken token);
}
/// <summary>Asynchronously accepts or skips an item without invoking the upstream.</summary>
public interface IAsyncStreamItemFilter<TRequest, TItem>
{
    /// <summary>Returns one operation to consume once, before the next declared stage.</summary>
    ValueTask<bool> AcceptAsync(TRequest request, TItem item, CancellationToken token);
}
/// <summary>Asynchronously maps an item to the same item type.</summary>
public interface IAsyncStreamItemMap<TRequest, TItem>
{
    /// <summary>Returns one operation to consume once, before the next declared stage.</summary>
    ValueTask<TItem> MapAsync(TRequest request, TItem item, CancellationToken token);
}
/// <summary>Asynchronously maps an item to a separately declared output type.</summary>
public interface IAsyncStreamItemMap<TRequest, TIn, TOut>
{
    /// <summary>Returns one typed operation consumed once before the next stage.</summary>
    ValueTask<TOut> MapAsync(TRequest request, TIn item, CancellationToken token);
}
/// <summary>Opens an independent upstream execution with the supplied request and token.</summary>
public interface IStreamUpstream<TRequest, TItem>
{
    IAsyncEnumerable<TItem> Open(TRequest request, CancellationToken token);
}
/// <summary>An opaque wrapper. It owns enumerators it acquires; directly returned enumerables delegate cursor ownership to the consumer.</summary>
public interface IStreamWrapper<TRequest, TItem>
{
    /// <summary>Wraps the upstream and may open it zero or multiple times.</summary>
    IAsyncEnumerable<TItem> Wrap<TUpstream>(TRequest request, TUpstream upstream, CancellationToken token)
        where TUpstream : IStreamUpstream<TRequest, TItem>;
}
