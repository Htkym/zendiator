namespace Zendiator;

/// <summary>Preprocesses one root Stream enumeration before its handler or middleware opens.</summary>
/// <remarks>
/// Runs once on the first public Move, not on creation, cold disposal or middleware reopening.
/// This callback cannot replace the request, return an empty Stream or enclose a cleanup scope.
/// It does not revalidate a changed request supplied by middleware to a reopened upstream.
/// A pending callback keeps that Move pending; disposing the cursor then is rejected.
/// Each returned operation is consumed once. Failure is propagated without a later cancellation
/// check replacing it; normal completion is followed by a cancellation check.
/// Receivers are borrowed and cached per mediator, including Transient registrations, rather than
/// recreated per enumeration. Support reuse and concurrent enumerations when the mediator is shared.
/// DI owns receiver cleanup. Keep the scope alive through enumeration and disposal.
/// Synchronous ambient changes may flow through root initialization and its first pull. Later Moves
/// use their caller's context; async callback changes are not promised to flow back to their caller.
/// Use middleware for paired lifetime scopes or validation on each upstream reopening.
/// </remarks>
public interface IAsyncStreamBefore<TRequest>
{
    /// <summary>Completes preprocessing before the remaining Stream pipeline opens.</summary>
    ValueTask BeforeAsync(TRequest request, CancellationToken cancellationToken);
}
