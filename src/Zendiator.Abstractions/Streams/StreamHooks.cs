namespace Zendiator;

/// <summary>Accepts or skips one source item before the next declared Stream stage.</summary>
/// <remarks>A skipped item advances the same cursor; it does not end the enumeration.</remarks>
public interface IStreamItemFilter<TRequest, TItem>
{
    bool Accept(TRequest request, TItem item, CancellationToken token);
}
/// <summary>Maps an item before the next declared Stream stage.</summary>
public interface IStreamItemMap<TRequest, TItem>
{
    TItem Map(TRequest request, TItem item, CancellationToken token);
}
/// <summary>Maps an item to a separately declared output type, preserving stage order.</summary>
public interface IStreamItemMap<TRequest, TIn, TOut>
{
    TOut Map(TRequest request, TIn item, CancellationToken token);
}
/// <summary>Asynchronously accepts or skips one source item before the next declared Stream stage.</summary>
/// <remarks>A skipped item advances the same cursor; it does not end the enumeration.</remarks>
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
