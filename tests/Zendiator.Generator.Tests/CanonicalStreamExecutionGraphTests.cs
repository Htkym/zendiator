using Microsoft.CodeAnalysis;
using Zendiator.SourceGenerator;
using Xunit;
using static Zendiator.SourceGenerator.StreamExecutionNames;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private static string StreamGraphFixture(string pipeline = "Stage.Hook<Positive>(),Stage.Hook<DoubleItem>(),Stage.Hook<Small>()") =>
        ListFixture("c.Handlers<Items>(DispatchFamily.Stream,typeof(ItemsHandler));" +
            (pipeline.Length == 0 ? "" : "c.Pipeline<Items>(" + pipeline + ");")) + """
        public sealed record Items : IStreamRequest<int>;
        [ZendiatorHandler] public sealed class ItemsHandler : IStreamRequestHandler<Items,int> {
            public System.Collections.Generic.IAsyncEnumerable<int> HandleAsync(Items r,CancellationToken c)=>throw new NotImplementedException();
        }
        public sealed class Positive : IStreamItemFilter<Items,int> {
            public bool Accept(Items r,int item,CancellationToken c)=>item>0;
        }
        public sealed class Small : IStreamItemFilter<Items,int> {
            public bool Accept(Items r,int item,CancellationToken c)=>item<4;
        }
        public sealed class DoubleItem : IStreamItemMap<Items,int> {
            public int Map(Items r,int item,CancellationToken c)=>item*2;
        }
        public sealed class StreamOpaque : IStreamWrapper<Items,int> {
            public System.Collections.Generic.IAsyncEnumerable<int> Wrap<N>(Items r,N n,CancellationToken c)
                where N : IStreamUpstream<Items,int> => n.Open(r,c);
        }
        """;
    private static CanonicalStreamExecutionSchedule ChangeStreamControl(CanonicalStreamExecutionSchedule schedule, StreamProgram program,
        Func<StreamControlEdge, StreamControlEdge?> edit) => schedule with {
            Flows = new(schedule.Flows.Select(f => f.Program == program ? f with { Edges = new(f.Edges.Select(edit).Where(e => e != null).Select(e => e!)) } : f))
        };
    private static CanonicalStreamExecutionSchedule Retarget(CanonicalStreamExecutionSchedule schedule, StreamProgram program,
        StreamPhase from, StreamOutcome outcome, StreamPhase to, int fromStage = -1, int toStage = -1) =>
        ChangeStreamControl(schedule, program, e => e.From == Id(program, from, fromStage) && e.Outcome == outcome ? e with { To = Id(program, to, toStage) } : e);

    [Theory]
    [InlineData("")]
    [InlineData("Stage.Hook<Positive>()")]
    [InlineData("Stage.Hook<DoubleItem>()")]
    [InlineData("Stage.Hook<Positive>(),Stage.Hook<DoubleItem>(),Stage.Hook<Small>()")]
    public void Stream_real_frontend_builds_independently_valid_lifecycle_graph_and_keeps_runtime_blocked(string pipeline)
    {
        var driver = BindLists(StreamGraphFixture(pipeline), "ZEN0037");
        var graph = Graph(driver);
        Assert.True(graph.IsValid, string.Join(",", graph.Facts.Select(f => f.Reason)));
        Assert.Null(graph.Schedule);
        var schedule = Assert.IsType<CanonicalStreamExecutionSchedule>(graph.StreamSchedule);
        Assert.Equal(5, schedule.Flows.Count);
        Assert.Empty(CanonicalStreamControlValidator.Validate(graph.Contract, schedule));
        Assert.Contains("Stream execution graph satisfies", Assert.Single(driver.GetRunResult().Diagnostics).GetMessage());
        Assert.Contains("runtime generation and equivalence are pending", Assert.Single(driver.GetRunResult().Diagnostics).GetMessage());
        Assert.Empty(driver.GetRunResult().Results.Single().GeneratedSources.Where(s => s.HintName.Contains("Items")));
        Assert.True(schedule.FreshCursorPerEnumeration);
        Assert.False(schedule.RequestSnapshot);
        Assert.False(schedule.ReLendPublicCursor);
        Assert.Equal(2, schedule.Awaits.Count);
        Assert.All(schedule.Awaits, a => { Assert.True(a.ConsumeOnce); Assert.True(a.ConfigureAwaitFalse); });
        Assert.Equal(StreamOwnerLifetime.EachEnumeration, Assert.Single(schedule.Owners, o => o.Id == "source").Lifetime);
        Assert.Equal(StreamCleanup.AsyncDispose, Assert.Single(schedule.Owners, o => o.Id == "source").Cleanup);
        Assert.All(schedule.Owners.Where(o => o.Borrowed), o => Assert.Equal(StreamCleanup.None, o.Cleanup));
        Assert.Equal("stream-call-caller-v1", schedule.ContextModelId);
    }

    [Fact]
    public void Stream_required_derivation_rejects_site_owner_token_await_and_value_mutations_without_copying_candidate()
    {
        var graph = Graph(BindLists(StreamGraphFixture(), "ZEN0037")); var s = graph.StreamSchedule!;
        var source = Assert.Single(s.Owners, o => o.Id == "source");
        Reject(s with { Owners = new(s.Owners.Select(o => o == source ? o with { Borrowed = true, Cleanup = StreamCleanup.None } : o)) }, "MissingOwner");
        var service = Assert.Single(s.Owners, o => o.Id == Service(1));
        Reject(s with { Owners = new(s.Owners.Select(o => o == service ? o with { Cleanup = StreamCleanup.Dispose } : o)) }, "BorrowedOwnerHasCleanup");
        var guard = Assert.Single(s.Sites, site => site.Id == Id(StreamProgram.Move, StreamPhase.SourceGuard));
        Reject(s with { Sites = new(s.Sites.Where(site => site != guard)) }, "MissingSite");
        Reject(s with { Sites = new(s.Sites.Append(guard)) }, "UnexpectedSite");
        Reject(s with { Tokens = new(s.Tokens.Select(t => t.Case == StreamTokenCase.DistinctCancelable ? t with { Source = StreamTokenSource.Api, OwnsLinkedToken = false } : t)) }, "MissingTokenRule");
        Reject(s with { Tokens = new(s.Tokens.Where(t => t.Case != StreamTokenCase.SameToken)) }, "MissingTokenRule");
        Reject(s with { Awaits = new(s.Awaits.Select(a => a with { ConsumeOnce = false })) }, "AwaitConsumptionMismatch");
        Reject(s with { Awaits = new(s.Awaits.Append(s.Awaits[0])) }, "DuplicateAwaitConsumer");
        Reject(s with { Awaits = new(s.Awaits.Select(a => a with { ConfigureAwaitFalse = false, Normal = "early-return" })) }, "MissingAwait");
        var mapped = Assert.Single(s.Transfers, t => t.At == Id(StreamProgram.Move, StreamPhase.Map, 1) && t.Destination == "cursor/item");
        Reject(s with { Transfers = new(s.Transfers.Select(t => t == mapped ? t with { Source = "source/Current" } : t)) }, "MissingValueTransfer");
        Reject(s with { Transfers = new(s.Transfers.Select(t => t == mapped ? t with { Outcome = StreamOutcome.Failure } : t)) }, "MissingValueTransfer");
        var token = Assert.Single(s.Transfers, t => t.At == Id(StreamProgram.Move, StreamPhase.AcquireSource) && t.Destination == "source/token");
        Reject(s with { Transfers = new(s.Transfers.Select(t => t == token ? t with { Source = "cursor/api-token" } : t)) }, "MissingValueTransfer");
        Reject(s with { FreshCursorPerEnumeration = false }, "ReplayCursorOwnershipMismatch");
        Reject(s with { ReLendPublicCursor = true }, "ReplayCursorOwnershipMismatch");
        Reject(s with { ContextModelId = "saved-previous-move-context" }, "FreshMoveDisposeCallerContextRequired");
        Reject(s with { Failures = new(s.Failures.Select(f => f with { Aggregate = true })) }, "ExceptionReplacementMismatch");
        void Reject(CanonicalStreamExecutionSchedule candidate, string reason) => Assert.Contains(CanonicalStreamGraphValidator.Validate(graph.Contract, candidate), f => f.Reason == reason);
    }

    [Theory]
    [InlineData((int)StreamPhase.PreCancel, (int)StreamOutcome.Success, (int)StreamPhase.SourceFactory, "SourceFactoryBeforeResolveOrRepeated")]
    [InlineData((int)StreamPhase.MergeTokens, (int)StreamOutcome.Success, (int)StreamPhase.ResolveHandler, "HandlerResolveWithoutPreCancel")]
    [InlineData((int)StreamPhase.ResolveHandler, (int)StreamOutcome.Failure, (int)StreamPhase.SourceFactory, "SourceFactoryBeforeResolveOrRepeated")]
    [InlineData((int)StreamPhase.SourceGuard, (int)StreamOutcome.Success, (int)StreamPhase.SourceCurrent, "SourceCurrentWithoutSuccessfulMoveOrRepeated")]
    [InlineData((int)StreamPhase.SourceMove, (int)StreamOutcome.True, (int)StreamPhase.CacheCurrent, "CacheBeforeCompleteItemPipelineOrRepeated")]
    [InlineData((int)StreamPhase.SourceMove, (int)StreamOutcome.Failure, (int)StreamPhase.ReleaseFalse, "FalseReleaseBeforeEnd")]
    [InlineData((int)StreamPhase.SourceCurrent, (int)StreamOutcome.Success, (int)StreamPhase.SourceCurrent, "SourceCurrentWithoutSuccessfulMoveOrRepeated")]
    [InlineData((int)StreamPhase.SourceMove, (int)StreamOutcome.False, (int)StreamPhase.Started, "RestartedStoppedCursor")]
    public void Stream_actual_paths_reject_startup_current_and_failure_ordering_with_metadata_unchanged(int fromValue, int outcomeValue, int toValue, string reason)
    {
        var graph = Graph(BindLists(StreamGraphFixture(), "ZEN0037")); var s = graph.StreamSchedule!;
        var candidate = Retarget(s, StreamProgram.Move, (StreamPhase)fromValue, (StreamOutcome)outcomeValue, (StreamPhase)toValue);
        Assert.Equal(s.Sites, candidate.Sites); Assert.Equal(s.Transfers, candidate.Transfers); Assert.Equal(s.Awaits, candidate.Awaits);
        Assert.Contains(CanonicalStreamControlValidator.Validate(graph.Contract, candidate), f => f.Reason == reason);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Filter_false_retry_must_pass_a_fresh_source_cancel_guard_even_after_service_is_cached(int stage)
    {
        var graph = Graph(BindLists(StreamGraphFixture(), "ZEN0037")); var s = graph.StreamSchedule!;
        var bad = Retarget(s, StreamProgram.Move, StreamPhase.Filter, StreamOutcome.False, StreamPhase.SourceMove, stage);
        Assert.Equal(s.Sites, bad.Sites);
        Assert.Contains(CanonicalStreamControlValidator.Validate(graph.Contract, bad), f => f.Reason == "SourceMoveWithoutFreshCancellationGuard");
        // Last true is allowed to publish the accepted item; cancellation resumes at the next source guard.
        Assert.Empty(CanonicalStreamControlValidator.Validate(graph.Contract, s));
    }

    [Theory]
    [InlineData((int)StreamPhase.Filter, 0)]
    [InlineData((int)StreamPhase.Map, 1)]
    [InlineData((int)StreamPhase.Filter, 2)]
    public void Reached_item_callbacks_require_guard_and_lazy_service_resolution(int callbackValue, int stage)
    {
        var graph = Graph(BindLists(StreamGraphFixture(), "ZEN0037")); var s = graph.StreamSchedule!;
        var bad = Retarget(s, StreamProgram.Move, StreamPhase.HookGuard, StreamOutcome.Success, (StreamPhase)callbackValue, stage, stage);
        Assert.Contains(CanonicalStreamControlValidator.Validate(graph.Contract, bad), f => f.Reason == "ItemCallbackWithoutCurrentGuardResolveOrDeclarationOrder");
        bad = Retarget(s, StreamProgram.Move, StreamPhase.Filter, StreamOutcome.False, StreamPhase.HookGuard, 0, 1);
        Assert.Contains(CanonicalStreamControlValidator.Validate(graph.Contract, bad), f => f.Reason == "HookGuardBeforeCurrentOrOutOfOrder");
    }

    [Theory]
    [InlineData((int)StreamPhase.DetachSource, (int)StreamOutcome.Always, (int)StreamPhase.DisposeSource, "SourceCleanupWithoutDetachedOwnerOrRepeated")]
    [InlineData((int)StreamPhase.DisposeSource, (int)StreamOutcome.Failure, (int)StreamPhase.ReleaseDispose, "DisposeReleasedBeforeAllCleanupCompleted")]
    [InlineData((int)StreamPhase.DisposeSource, (int)StreamOutcome.Success, (int)StreamPhase.DisposeSource, "SourceCleanupWithoutDetachedOwnerOrRepeated")]
    [InlineData((int)StreamPhase.LinkPresent, (int)StreamOutcome.True, (int)StreamPhase.ReleaseDispose, "DisposeReleasedBeforeAllCleanupCompleted")]
    [InlineData((int)StreamPhase.DisposeSource, (int)StreamOutcome.Failure, (int)StreamPhase.DisposeSuccess, "InvalidStreamExit:DisposeSuccess")]
    public void Dispose_actual_paths_detach_once_cleanup_all_owners_and_preserve_failure(int fromValue, int outcomeValue, int toValue, string reason)
    {
        var graph = Graph(BindLists(StreamGraphFixture(), "ZEN0037")); var s = graph.StreamSchedule!;
        Assert.Contains(CanonicalStreamControlValidator.Validate(graph.Contract, Retarget(s, StreamProgram.Dispose, (StreamPhase)fromValue, (StreamOutcome)outcomeValue, (StreamPhase)toValue)), f => f.Reason == reason);
    }

    [Fact]
    public void Stream_busy_and_stopped_paths_cannot_start_execution_or_recover_a_failed_cursor()
    {
        var graph = Graph(BindLists(StreamGraphFixture(), "ZEN0037")); var s = graph.StreamSchedule!;
        var busy = Retarget(s, StreamProgram.Move, StreamPhase.MoveGuard, StreamOutcome.Failure, StreamPhase.ClearCurrent);
        Assert.Contains(CanonicalStreamControlValidator.Validate(graph.Contract, busy), f => f.Reason == "ExecutionAfterBusyRejection");
        var stopped = Retarget(s, StreamProgram.Move, StreamPhase.Stopped, StreamOutcome.True, StreamPhase.Started);
        Assert.Contains(CanonicalStreamControlValidator.Validate(graph.Contract, stopped), f => f.Reason == "RestartedStoppedCursor");
        var badCurrent = Retarget(s, StreamProgram.Current, StreamPhase.CurrentGuard, StreamOutcome.False, StreamPhase.ReadCache);
        Assert.Contains(CanonicalStreamControlValidator.Validate(graph.Contract, badCurrent), f => f.Reason == "InvalidStreamExit:ReadCache");
    }

    [Fact]
    public void Stream_wrapper_uses_a_separate_segment_graph_and_owned_or_unknown_completion_never_alias_send()
    {
        var graph = Graph(BindLists(StreamGraphFixture("Stage.Hook<Positive>(),Stage.Wrapper<StreamOpaque>(),Stage.Hook<Small>()"), "ZEN0037"));
        Assert.True(graph.IsValid, string.Join(",", graph.Facts.Select(f => f.Reason)));
        Assert.Null(graph.StreamSchedule); Assert.Null(graph.Schedule); Assert.NotNull(graph.StreamWrapperSchedule);
        var bare = Graph(BindLists(StreamGraphFixture(""), "ZEN0037"));
        var handler = bare.Contract.Handlers[0];
        foreach (var completion in new[] { BoundCompletionKind.Synchronous, BoundCompletionKind.ValueTask, (BoundCompletionKind)(-1), (BoundCompletionKind)42 })
        {
            var rejected = CanonicalExecutionGraphNormalizer.Create(bare.Contract with { Handlers = new(new[] { handler with { Callback = handler.Callback! with { Completion = completion } } }) });
            Assert.False(rejected.IsValid); Assert.Null(rejected.StreamSchedule);
        }
        var owned = CanonicalExecutionGraphNormalizer.Create(bare.Contract with { Handlers = new(new[] { handler with { ServiceOwnership = FusionOwnership.OwnedSession } }) });
        Assert.Contains(owned.Facts, f => f.Reason == "OwnedHandlerAcquisitionAndCleanupUnavailable");
    }

    [Fact]
    public void Stream_frontend_order_edits_change_graph_while_body_and_trivia_keep_cached_value_and_fresh_location()
    {
        var source = StreamGraphFixture(); var driver = BindLists(source, "ZEN0037"); var original = Graph(driver);
        var reordered = source.Replace("Stage.Hook<Positive>(),Stage.Hook<DoubleItem>()", "Stage.Hook<DoubleItem>(),Stage.Hook<Positive>()");
        driver = driver.RunGenerators(Compilation(reordered));
        GraphReason(driver, IncrementalStepRunReason.Modified);
        Assert.True(Graph(driver).IsValid); Assert.NotEqual(original.StreamSchedule, Graph(driver).StreamSchedule);
        var previous = Graph(driver);
        var changedCompilation = Compilation("\n\n" + reordered.Replace("=>item*2;", "=>item+item;"));
        driver = driver.RunGenerators(changedCompilation);
        GraphReason(driver, IncrementalStepRunReason.Cached); Assert.Equal(previous, Graph(driver));
        Assert.Same(changedCompilation.SyntaxTrees.Single(), Assert.Single(driver.GetRunResult().Diagnostics).Location.SourceTree);
    }
}
