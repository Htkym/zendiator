namespace Zendiator.SourceGenerator;

internal enum StreamProgram { Create, GetEnumerator, Move, Current, Dispose }
internal enum StreamSourceKind { Handler, Wrapper }
internal enum StreamPhase
{
    NullGuard, NullFailure, CreateEnvelope, EnvelopeReturn, CreateCursor, CursorReturn,
    MoveGuard, ClearCurrent, Stopped, Started, MergeTokens, PreCancel, ResolveHandler,
    SourceFactory, ResolveWrapper, WrapperFactory, AcquireSource, MarkStarted, SourceGuard, SourceMove, SourceCurrent,
    HookGuard, HookCached, ResolveHook, Filter, Map, CacheCurrent,
    StartupDetachLink, StartupLinkPresent, StartupDisposeLink, EndFailure, EndFalse,
    ReleaseTrue, ReleaseFalse, ReleaseFailure, MoveTrue, MoveFalse, MoveFailure, BusyFailure,
    CurrentGuard, ReadCache, CurrentFailure,
    DisposeGuard, AlreadyDisposed, MarkDisposed, DetachSource, DetachLink, SourcePresent,
    DisposeSource, LinkPresent, DisposeLink, ReleaseDispose, DisposeResult, DisposeSuccess, DisposeFailure
}
internal enum StreamOutcome { Always, Success, Failure, True, False }
internal enum StreamTokenCase { ApiNotCancelable, EnumeratorNotCancelable, SameToken, DistinctCancelable }
internal enum StreamTokenSource { Api, Enumerator, Linked }
internal enum StreamOwnerKind { Provider, Envelope, Cursor, SourceCursor, LinkedToken, Service }
internal enum StreamOwnerLifetime { Caller, Envelope, EachEnumeration }
internal enum StreamCleanup { None, AsyncDispose, Dispose }
internal sealed record StreamSiteId(StreamProgram Program, StreamPhase Phase, int Stage = -1);
internal sealed record StreamExecutionSite(StreamSiteId Id, string Anchor, string Owner,
    BoundCompletionKind Completion, string? Producer);
internal sealed record StreamControlEdge(StreamSiteId From, StreamSiteId To, StreamOutcome Outcome);
internal sealed record StreamControlFlow(StreamProgram Program, StreamSiteId Entry, EquatableArray<StreamControlEdge> Edges);
internal sealed record StreamTokenRule(StreamTokenCase Case, StreamTokenSource Source, bool OwnsLinkedToken);
internal sealed record StreamExecutionOwner(string Id, string? Parent, StreamOwnerKind Kind,
    StreamOwnerLifetime Lifetime, bool Borrowed, StreamCleanup Cleanup);
internal sealed record StreamExecutionAwait(StreamSiteId Producer, string ResultType, bool ConsumeOnce,
    bool ConfigureAwaitFalse, string Normal, string Failure, EquatableArray<string> RetainedSlots);
internal sealed record StreamValueTransfer(string Source, string Destination, StreamSiteId At, StreamOutcome Outcome);
internal sealed record StreamFailurePolicy(StreamSiteId At, bool PreserveIdentityAndToken,
    bool EscapingOceIsCanceled, bool ReplacesPreviousError, bool Aggregate);
internal sealed record CanonicalStreamExecutionSchedule(EquatableArray<StreamExecutionSite> Sites,
    EquatableArray<StreamControlFlow> Flows, EquatableArray<StreamExecutionOwner> Owners,
    EquatableArray<StreamTokenRule> Tokens, EquatableArray<StreamExecutionAwait> Awaits,
    EquatableArray<StreamValueTransfer> Transfers, EquatableArray<StreamFailurePolicy> Failures,
    string ContextModelId, bool FreshCursorPerEnumeration, bool RequestSnapshot, bool ReLendPublicCursor);
internal sealed record StreamSegmentContract(string Anchor, StreamSourceKind SourceKind, BoundCallbackModel Callback,
    EquatableArray<BoundStageModel> Hooks);

// Names only: contract derivation and construction do not share lifecycle policy.
internal static class StreamExecutionNames
{
    internal static StreamSiteId Id(StreamProgram program, StreamPhase phase, int stage = -1) => new(program, phase, stage);
    internal static string Service(int stage) => "service:" + stage;
    internal static string HookItem(int stage) => "hook:" + stage + "/item";
}
