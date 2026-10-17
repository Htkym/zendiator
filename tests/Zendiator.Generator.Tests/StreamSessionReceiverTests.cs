using Zendiator.SourceGenerator;
using Xunit;
using static Zendiator.SourceGenerator.StreamExecutionNames;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private static (CanonicalExecutionGraphModel Graph, StreamSegmentContract Contract, CanonicalStreamExecutionSchedule Schedule) StreamSessionGraph(string source)
    {
        var graph = Graph(BindLists(source, "ZEN0037"));
        Assert.True(graph.IsValid, string.Join(",", graph.Facts.Select(f => f.Reason)));
        if (graph.StreamWrapperSchedule != null)
        {
            var segment = Assert.Single(graph.StreamWrapperSchedule.Segments, s => s.Descriptor.Id == graph.StreamWrapperSchedule.ConsumerSegment);
            return (graph, segment.Descriptor.Contract, segment.Cursor);
        }
        return (graph, new StreamSegmentContract(graph.Contract.Anchor, StreamSourceKind.Handler, graph.Contract.Handlers[0].Callback!, graph.Contract.Stages), graph.StreamSchedule!);
    }
    private static string TwoStreamSessionsFixture() => SessionFactoryFixture("Stream", asyncFactory: true)
        .Replace("Stage.Hook<OperationSession,Factory<string>>()", "Stage.Hook<OperationSession,Factory<string>>(),Stage.Hook<OuterSession,OuterFactory>()") + """
        public sealed class OuterSession : IStreamItemFilter<Items,int>,IAsyncDisposable {
            public bool Accept(Items r,int item,CancellationToken c)=>true;
            public ValueTask DisposeAsync()=>default;
        }
        public sealed class OuterFactory : IAsyncOwnedSessionFactory<Items,OuterSession> {
            public ValueTask<OuterSession> CreateAsync(Items r,CancellationToken c)=>new(new OuterSession());
        }
        """;

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    public void Public_Stream_factory_Hook_and_Wrapper_bind_lazy_session_receiver_and_keep_token_boundary(bool owned, bool asyncFactory, bool wrapper)
    {
        var (graph, contract, schedule) = StreamSessionGraph(SessionFactoryFixture("Stream", owned, asyncFactory, wrapper));
        var position = wrapper ? -1 : 0;
        var inspection = CanonicalStreamSessionValidator.Inspect(contract, schedule);
        Assert.Empty(inspection.Facts);
        var resolution = Assert.Single(schedule.ServiceResolutions);
        Assert.Equal(Assert.Single(graph.Contract.Stages[0].Sessions).FactoryServiceType, resolution.ServiceType);
        Assert.Equal(Service(position) + "/resolved", resolution.ResultSlot);
        var receiver = Assert.Single(schedule.CallbackReceivers);
        Assert.Equal(Session(position) + "/live", receiver.Receiver);
        Assert.Equal(wrapper ? StreamPhase.WrapperFactory : StreamPhase.Map, receiver.At.Phase);
        Assert.All(schedule.SessionCalls.Where(c => c.At.Phase == StreamPhase.AcquireSession), c =>
        {
            Assert.Equal(StreamProgram.Move, c.At.Program);
            Assert.Equal(resolution.ResultSlot, c.Receiver);
            Assert.Equal("cursor/request", c.RequestSlot); Assert.Equal("cursor/start-token", c.TokenSlot);
        });
        Assert.Equal(owned ? 2 : 0, schedule.SessionCalls.Count(c => c.At.Phase == StreamPhase.CleanupSession));
        Assert.All(schedule.Owners.Where(o => o.Kind == StreamOwnerKind.Service), o =>
        {
            Assert.True(o.Borrowed); Assert.Equal(StreamCleanup.None, o.Cleanup);
        });
        Assert.Contains(inspection.Paths, p => p.Program == StreamProgram.Dispose && !p.Started && p.Terminal == StreamPhase.DisposeSuccess &&
            p.Acquired.Count == 0 && p.Released.Count == 0 && !p.Visited.Any(s => s.Phase is StreamPhase.AcquireSession or StreamPhase.ResolveSessionFactory));
        Assert.Contains(inspection.Paths, p => p.Program == StreamProgram.Move && p.Terminal == StreamPhase.MoveFalse && p.Started &&
            p.Live.SequenceEqual(new[] { position }) && p.Released.Count == 0);
        Assert.Contains(inspection.Paths, p => p.Program == StreamProgram.Move && p.Terminal == StreamPhase.MoveFailure && p.Started &&
            p.Live.SequenceEqual(new[] { position }) && p.Released.Count == 0);
        Assert.Contains(inspection.Paths, p => p.Program == StreamProgram.Move && p.Terminal == StreamPhase.MoveTrue &&
            !p.Visited.Any(s => s.Phase == StreamPhase.AcquireSession));
        if (!owned) Assert.All(inspection.Paths, p => Assert.Empty(p.Released));
        if (wrapper)
        {
            Assert.Equal("CancellationToken.None", Assert.Single(schedule.Transfers, t => t.At.Phase == StreamPhase.AcquireSource && t.Destination == "source/token").Source);
            Assert.Empty(StreamWrapperTokenFlowValidator.Trace(graph.Contract, graph.StreamWrapperSchedule!).Facts);
            Assert.Empty(StreamWrapperTokenFlowValidator.Trace(graph.Contract, graph.StreamWrapperSchedule!, explicitInnerEnumeration: true).Facts);
        }
        Assert.DoesNotContain(schedule.Sites, s => s.Id.Program is StreamProgram.Create or StreamProgram.GetEnumerator && s.Id.Phase == StreamPhase.AcquireSession);
    }

    [Fact]
    public void Stream_sessions_start_outer_to_inner_recover_partial_startup_and_dispose_source_then_reverse_sessions_then_link()
    {
        var (_, contract, schedule) = StreamSessionGraph(TwoStreamSessionsFixture());
        var inspection = CanonicalStreamSessionValidator.Inspect(contract, schedule);
        Assert.Empty(inspection.Facts);
        Assert.Contains(inspection.Paths, p => p.Program == StreamProgram.Move && !p.Started && p.Acquired.SequenceEqual(new[] { 1 }) &&
            p.Released.SequenceEqual(new[] { 1 }) && p.Failures.Contains("fault:" + Id(StreamProgram.Move, StreamPhase.ValidateSessionResult, 0)) &&
            !p.Visited.Contains(Id(StreamProgram.Move, StreamPhase.SourceFactory)));
        Assert.Contains(inspection.Paths, p => p.Program == StreamProgram.Move && !p.Started && p.Acquired.SequenceEqual(new[] { 1, 0 }) &&
            p.Released.SequenceEqual(new[] { 0, 1 }) && p.Failures.Contains("fault:" + Id(StreamProgram.Move, StreamPhase.AcquireSource)));
        Assert.Contains(inspection.Paths, p => p.Program == StreamProgram.Dispose && p.Started && p.Acquired.SequenceEqual(new[] { 1, 0 }) &&
            p.Released.SequenceEqual(new[] { 0, 1 }) && p.Failures.Contains("fault:" + Id(StreamProgram.Dispose, StreamPhase.CleanupSession, 0)) &&
            p.EscapingError == "cancel:" + Id(StreamProgram.Dispose, StreamPhase.CleanupSession, 1) && p.Canceled);
        Assert.Contains(inspection.Paths, p => p.Program == StreamProgram.Dispose && p.Released.Count == 2 &&
            p.Visited.ToList().IndexOf(Id(StreamProgram.Dispose, StreamPhase.DisposeSource)) < p.Visited.ToList().IndexOf(Id(StreamProgram.Dispose, StreamPhase.CleanupSession, 0)) &&
            p.Visited.ToList().IndexOf(Id(StreamProgram.Dispose, StreamPhase.CleanupSession, 1)) < p.Visited.ToList().IndexOf(Id(StreamProgram.Dispose, StreamPhase.DisposeLink)));
        Assert.Contains(inspection.Paths, p => p.Program == StreamProgram.Dispose && p.Started && p.Acquired.Count == 2 && p.Released.Count == 0 &&
            p.Terminal == StreamPhase.DisposeSuccess && !p.Visited.Any(s => s.Phase == StreamPhase.DetachSession)); // sequential duplicate, including after failed Dispose
    }

    [Theory]
    [InlineData("factory-receiver", "StreamBodyReceiverIsNotLiveTransferredSession")]
    [InlineData("factory-result-receiver", "StreamBodyReceiverIsNotLiveTransferredSession")]
    [InlineData("body-token", "StreamBodyRequestOrTokenMismatch")]
    [InlineData("DI-session-type", "StreamSessionFactoryDITypeOrSlotMismatch")]
    [InlineData("acquire-receiver", "StreamFactoryCallReceiverOrResultMismatch")]
    [InlineData("acquire-token", "StreamFactoryRequestOrTokenMismatch")]
    [InlineData("retain-result-alias", "StreamSessionTransferLostOrAliasedResource")]
    [InlineData("retain-live-alias", "StreamSessionDetachLostOrRetainedLiveAlias")]
    [InlineData("cleanup-factory-receiver", "StreamCleanupReceiverIsNotTransferredSession")]
    [InlineData("cleanup-aggregate", "StreamSessionExceptionReplacementMismatch")]
    [InlineData("double-await", "StreamSessionAwaitConsumptionOrContinuationMismatch")]
    [InlineData("pending-source-lease-lost", "StreamSessionLeaseLostAcrossPendingAwait")]
    [InlineData("pending-factory-lost", "StreamSessionFactoryLostAcrossPendingAwait")]
    public void Independent_Stream_session_operand_inspection_rejects_wrong_values_without_Required(string mutation, string reason)
    {
        var (_, contract, original) = StreamSessionGraph(SessionFactoryFixture("Stream", asyncFactory: true));
        var resource = Session(0);
        var candidate = mutation switch
        {
            "factory-receiver" => original with { CallbackReceivers = new(original.CallbackReceivers.Select(c => c with { Receiver = Service(0) + "/resolved" })) },
            "factory-result-receiver" => original with { CallbackReceivers = new(original.CallbackReceivers.Select(c => c with { Receiver = resource + "/factory-result" })) },
            "body-token" => original with { CallbackReceivers = new(original.CallbackReceivers.Select(c => c with { TokenSlot = "source/token" })) },
            "DI-session-type" => original with { ServiceResolutions = new(original.ServiceResolutions.Select(c => c with { ServiceType = contract.Hooks[0].Type })) },
            "acquire-receiver" => original with { SessionCalls = new(original.SessionCalls.Select(c => c.At.Phase == StreamPhase.AcquireSession ? c with { Receiver = resource + "/live" } : c)) },
            "acquire-token" => original with { SessionCalls = new(original.SessionCalls.Select(c => c.At.Phase == StreamPhase.AcquireSession ? c with { TokenSlot = "source/token" } : c)) },
            "retain-result-alias" => original with { Transfers = new(original.Transfers.Where(t => !(t.At.Phase == StreamPhase.CommitSession && t.Source == "null"))) },
            "retain-live-alias" => original with { Transfers = new(original.Transfers.Where(t => !(t.At.Phase == StreamPhase.DetachSession && t.Source == "null"))) },
            "cleanup-factory-receiver" => original with { SessionCalls = new(original.SessionCalls.Select(c => c.At.Phase == StreamPhase.CleanupSession ? c with { Receiver = Service(0) + "/resolved" } : c)) },
            "cleanup-aggregate" => original with { Failures = new(original.Failures.Select(c => c.At.Phase == StreamPhase.CleanupSession ? c with { Aggregate = true } : c)) },
            "double-await" => original with { Awaits = new(original.Awaits.Select(a => a.Producer.Phase == StreamPhase.AcquireSession ? a with { ConsumeOnce = false } : a)) },
            "pending-source-lease-lost" => original with { Awaits = new(original.Awaits.Select(a => a.Producer.Phase == StreamPhase.SourceMove ? a with { RetainedSlots = new(a.RetainedSlots.Where(s => s != resource + "/live")) } : a)) },
            "pending-factory-lost" => original with { Awaits = new(original.Awaits.Select(a => a.Producer.Phase == StreamPhase.AcquireSession ? a with { RetainedSlots = new(a.RetainedSlots.Where(s => s != Service(0) + "/resolved")) } : a)) },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        Assert.Equal(original.Flows, candidate.Flows);
        Assert.Contains(CanonicalStreamSessionValidator.Inspect(contract, candidate).Facts, f => f.Reason == reason);
    }

    [Theory]
    [InlineData("skip-validation", "OwnershipTransferredBeforeNonNullSessionValidation")]
    [InlineData("skip-startup-cleanup", "StreamSessionStillLiveAfterCleanup")]
    [InlineData("release-on-false", "StreamSessionDetachOutsideCleanupBoundary")]
    [InlineData("skip-cleanup-after-source-fault", "StreamSessionsNotReleasedExactlyOnceInReverseOrder")]
    public void Metadata_preserving_Stream_CFG_rejects_premature_transfer_cleanup_and_skipped_partial_recovery(string mutation, string reason)
    {
        var (_, contract, original) = StreamSessionGraph(SessionFactoryFixture("Stream", asyncFactory: true));
        var candidate = mutation switch
        {
            "skip-validation" => Retarget(original, StreamProgram.Move, StreamPhase.AcquireSession, StreamOutcome.Success, StreamPhase.CommitSession, 0, 0),
            "skip-startup-cleanup" => Retarget(original, StreamProgram.Move, StreamPhase.SourceFactory, StreamOutcome.Failure, StreamPhase.EndFailure),
            "release-on-false" => Retarget(original, StreamProgram.Move, StreamPhase.EndFalse, StreamOutcome.Always, StreamPhase.StartupDetachLink),
            "skip-cleanup-after-source-fault" => Retarget(original, StreamProgram.Dispose, StreamPhase.DisposeSource, StreamOutcome.Failure, StreamPhase.LinkPresent),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        Assert.Equal(original, candidate with { Flows = original.Flows });
        Assert.Contains(CanonicalStreamSessionValidator.Inspect(contract, candidate).Facts, f => f.Reason == reason);
    }

    [Fact]
    public void Borrowed_Stream_session_cannot_be_cleaned_by_the_operation_even_when_disposable()
    {
        var (_, contract, original) = StreamSessionGraph(SessionFactoryFixture("Stream", owned: false));
        var cleanup = Id(StreamProgram.Dispose, StreamPhase.CleanupSession, 0);
        var candidate = Retarget(original, StreamProgram.Dispose, StreamPhase.DisposeSource, StreamOutcome.Success, StreamPhase.CleanupSession, toStage: 0);
        candidate = candidate with
        {
            SessionCalls = new(candidate.SessionCalls.Append(new(cleanup, Session(0) + "/detached", null, null, null))),
            Flows = new(candidate.Flows.Select(f => f.Program != StreamProgram.Dispose ? f : f with
            {
                Edges = new(f.Edges.Append(
                new StreamControlEdge(cleanup, Id(StreamProgram.Dispose, StreamPhase.LinkPresent), StreamOutcome.Always)))
            }))
        };
        Assert.Contains(CanonicalStreamSessionValidator.Inspect(contract, candidate).Facts, f => f.Reason == "BorrowedStreamSessionDisposedByOperation");
    }

    [Fact]
    public void Stream_Owned_factory_requires_async_cleanup_and_Borrowed_never_infers_disposal_ownership()
    {
        var missing = SessionFactoryFixture("Stream").Replace(",IDisposable,IAsyncDisposable", ",IDisposable");
        var driver = BindLists(missing, "ZEN0025");
        Assert.Empty(Assert.Single(Assert.Single(Selection(driver).Pipelines).Stages).Sessions);
        var borrowed = StreamSessionGraph(SessionFactoryFixture("Stream", owned: false));
        Assert.Null(Assert.Single(borrowed.Contract.Hooks[0].Sessions).Cleanup);
        Assert.DoesNotContain(borrowed.Schedule.SessionCalls, c => c.At.Phase == StreamPhase.CleanupSession);
    }
}
