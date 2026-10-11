namespace Zendiator.SourceGenerator;

internal enum StreamWrapperProgram { Invoke, Open, AcquireByWrapper, AcquireByConsumer, ReleaseByWrapper, ReleaseByConsumer }
internal enum StreamWrapperPhase
{
    Begin, OpenChoice, CallOpen, OutputChoice, DirectReturn, AlternativeReturn, WrapperFailure,
    NullGuard, ArgumentFailure, SaveRequest, SaveToken, EnumerableReturn,
    AcquireCursor, AssignOwner, CursorReturn, AcquireFailure,
    OwnedCursorEntry, UseChoice, MoveUpstream, DetachCursor, DisposeCursor, ReleaseResult, ReleaseSuccess, ReleaseFailure
}
internal enum StreamWrapperOutcome { Always, Success, Failure, Zero, More, Direct, Alternative, Continue, Finish, Recover }
internal enum StreamCursorConsumer { Wrapper, Consumer }
internal enum StreamMultiplicity { Once, ZeroOrMore, EachOpen, EachAcquiredCursor }
internal sealed record StreamSegmentDescriptor(string Id, int SourcePosition, EquatableArray<int> HookPositions,
    string? UpstreamSegment, StreamSegmentContract Contract);
internal sealed record StreamExecutionSegment(StreamSegmentDescriptor Descriptor, CanonicalStreamExecutionSchedule Cursor);
internal sealed record StreamWrapperSite(StreamWrapperProgram Program, StreamWrapperPhase Phase);
internal sealed record StreamWrapperEdge(StreamWrapperSite From, StreamWrapperSite To, StreamWrapperOutcome Outcome);
internal sealed record StreamWrapperFlow(StreamWrapperProgram Program, StreamWrapperSite Entry, EquatableArray<StreamWrapperEdge> Edges);
internal sealed record StreamWrapperTransfer(string Source, string Destination, StreamWrapperSite At, StreamWrapperOutcome Outcome);
internal sealed record StreamWrapperCursorOwner(StreamCursorConsumer Consumer, StreamMultiplicity Acquisitions,
    string Lifetime, bool DetachBeforeDispose, bool DisposeOnce, bool AsyncDispose, bool BorrowedService);
internal sealed record StreamWrapperAwait(StreamWrapperSite Producer, string ResultType, bool ConsumeOnce,
    bool ConfigureAwaitFalse, bool CompleteBeforeOwnerExit, bool EscapingOceIsCanceled, bool PreserveIdentityAndToken, bool LastErrorReplaces);
internal sealed record StreamWrapperCall(StreamWrapperSite At, StreamWrapperProgram? ProtocolTarget,
    string? SegmentTarget, StreamProgram? CursorTarget, StreamMultiplicity Multiplicity);
internal sealed record StreamWrapperProtocol(int Position, string InputSegment, string OutputSegment,
    StreamMultiplicity OpenCalls, StreamMultiplicity EnumerationsPerOpen, bool LazyOpen, bool FreshEnvelopeEachOpen,
    bool FreshCursorEachAcquisition, bool BodyOpaque, bool BodyCleanupProven, bool InsertAsyncAdapter,
    EquatableArray<StreamWrapperFlow> Flows, EquatableArray<StreamWrapperTransfer> Transfers,
    EquatableArray<StreamWrapperCursorOwner> Owners, EquatableArray<StreamWrapperAwait> Awaits)
{
    public EquatableArray<StreamWrapperCall> Calls { get; init; }
    public string OpenAvailability { get; init; } = "wrapper-call-or-enumeration";
}
internal sealed record CanonicalStreamWrapperSchedule(EquatableArray<StreamExecutionSegment> Segments,
    EquatableArray<StreamWrapperProtocol> Wrappers, string ConsumerSegment, string ContextModelId);
internal static class StreamWrapperNames
{
    internal static string Segment(int sourcePosition) => "segment:" + sourcePosition;
    internal static StreamWrapperSite Site(StreamWrapperProgram program, StreamWrapperPhase phase) => new(program, phase);
}
