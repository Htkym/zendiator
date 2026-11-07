namespace Zendiator;

/// <summary>Opts a Stream middleware into an experimental typed upstream boundary.</summary>
/// <remarks>
/// The existing IStreamMiddleware contract remains the fallback. The generator directly calls
/// this generic method only for an accessible sealed class with a public, nonvirtual implementation
/// declared on that class. Explicit, inherited, virtual and other unsupported implementations use
/// the existing boundary. Both methods must implement the same middleware behavior.
/// Keep next in its generic type when retaining it in a cursor; converting it to an interface can box.
/// Copies of next share one upstream lease. A default next is uninitialized. The same opening,
/// cleanup, reopening, cancellation and borrowed ownership rules as IStreamMiddleware apply.
/// This opt-in does not change the public enumerable or terminal API, or pool/share cursor owners.
/// </remarks>
public interface IStructStreamMiddleware<TRequest, TItem> : IStreamMiddleware<TRequest, TItem>
{
    /// <summary>Opens one cursor with a typed, borrowed upstream port.</summary>
    ValueTask<IAsyncEnumerator<TItem>> OpenAsync<TNext>(TRequest request, TNext next,
        CancellationToken cancellationToken)
        where TNext : struct, IStreamUpstream<TRequest, TItem>;
}
