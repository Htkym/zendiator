namespace Zendiator;

/// <summary>Opens the remaining Stream pipeline at an explicit, opaque cursor boundary.</summary>
/// <remarks>
/// Await an opening operation once and operate on its cursor sequentially. Another cursor may
/// only be opened after the preceding cursor completes normally and its cleanup finishes.
/// Early disposal or a failed opening, Move, or cleanup aborts the lease and prevents reopening.
/// Closing the enclosing enumeration, including a terminal sink's false result, prevents reopening.
/// The item type describes this pipeline boundary, including the output of any preceding map.
/// </remarks>
public interface IStreamUpstream<TRequest, TItem>
{
    /// <summary>Returns one independently owned cursor operation.</summary>
    ValueTask<IAsyncEnumerator<TItem>> OpenAsync(TRequest request, CancellationToken cancellationToken);
}
