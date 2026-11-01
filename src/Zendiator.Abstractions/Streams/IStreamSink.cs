namespace Zendiator;

/// <summary>Consumes one item and reports whether the Stream may produce another.</summary>
/// <remarks>
/// Callbacks are sequential. Return false to stop the operation normally; cancellation does not
/// replace that already accepted stop with a cancellation exception. Return true to continue,
/// subject to cancellation before the next item. The Mediator never disposes a borrowed sink.
/// A value-type sink is passed by value: put externally observed mutable state in a shared owner.
/// </remarks>
public interface IStreamSink<in TItem>
{
    /// <summary>Returns a single-consumption operation; false is a monotonic operation-wide stop.</summary>
    ValueTask<bool> OnNextAsync(TItem item, CancellationToken cancellationToken);
}
