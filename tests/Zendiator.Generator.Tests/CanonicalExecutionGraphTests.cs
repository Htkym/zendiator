using Microsoft.CodeAnalysis;
using Zendiator.SourceGenerator;
using Xunit;
using static Zendiator.SourceGenerator.SendExecutionNames;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private static string GraphFixture(bool sync = false, bool all = false, bool isVoid = false)
    {
        var family = all ? sync ? "SendAllSync" : "SendAllAsync" : sync ? "SendSync" : "SendAsync";
        var source = FamilyFixture("c.Handlers<Ping>(DispatchFamily." + family + ",typeof(A)" + (all ? ",typeof(B)" : "") + "); " +
            "c.Pipeline<Ping>(Stage.Hook<Full<string>>(),Stage.Wrapper<" + (sync ? "SyncOpaque" : "Opaque") + ">(),Stage.Hook<Tail<string>>());", sync, isVoid);
        const string full = """
            public sealed class Full<T> : ISendRequestTransform<Ping>, ISendResponseTransform<Ping,int>,
                ISendExceptionObserver<Ping>, IAsyncSendFinallyObserver<Ping> where T : class {
                public Ping Before(Ping r,CancellationToken c)=>r;
                public int After(Ping r,int result,CancellationToken c)=>result;
                public void OnException(Ping r,Exception e,CancellationToken c){}
                public ValueTask FinallyAsync(Ping r,SendExitInfo e,CancellationToken c)=>default;
            }
            """;
        var stages = full;
        if (sync) stages = stages.Replace("IAsyncSendFinallyObserver<Ping>", "ISendFinallyObserver<Ping>")
            .Replace("public ValueTask FinallyAsync(Ping r,SendExitInfo e,CancellationToken c)=>default;", "public void Finally(Ping r,SendExitInfo e,CancellationToken c){}");
        if (isVoid) stages = stages.Replace("ISendResponseTransform<Ping,int>", "ISendSuccessObserver<Ping>")
            .Replace("public int After(Ping r,int result,CancellationToken c)=>result;", "public void After(Ping r,CancellationToken c){}");
        return source + stages + stages.Replace("class Full<T>", "class Tail<T>");
    }
    private static CanonicalExecutionGraphModel Graph(GeneratorDriver driver) => Assert.Single(Assert.Single(
        Assert.IsType<CanonicalExecutionGraphCatalog>(Assert.Single(driver.GetRunResult().Results.Single()
            .TrackedSteps["CanonicalExecutionGraphModel"].SelectMany(s => s.Outputs)).Value).Compositions));
    private static void GraphReason(GeneratorDriver driver, IncrementalStepRunReason expected) =>
        Assert.All(driver.GetRunResult().Results.Single().TrackedSteps["CanonicalExecutionGraphModel"].SelectMany(s => s.Outputs), o => Assert.Equal(expected, o.Reason));
    private static CanonicalSendExecutionSchedule ChangeControl(CanonicalSendExecutionSchedule schedule, string owner, Func<SendControlEdge, SendControlEdge?> change) =>
        schedule with
        {
            ControlFlows = new(schedule.ControlFlows.Select(f => f.Owner == owner
            ? f with { Edges = new(f.Edges.Select(change).Where(e => e != null).Select(e => e!)) } : f))
        };

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void Canonical_send_graph_binds_real_frontend_and_accepted_emission_for_each_family_and_shape(bool sync, bool all, bool isVoid)
    {
        var driver = BindAcceptedSendLists(GraphFixture(sync, all, isVoid));
        var graph = Graph(driver);
        Assert.True(graph.IsValid, string.Join(",", graph.Facts.Select(f => f.Reason)));
        var schedule = Assert.IsType<CanonicalSendExecutionSchedule>(graph.Schedule);
        Assert.Equal(all ? 2 : 1, graph.Contract.Handlers.Count);
        Assert.All(graph.Contract.Handlers, h => Assert.Equal(sync ? BoundCompletionKind.Synchronous : BoundCompletionKind.ValueTask, h.Callback!.Completion));
        Assert.All(schedule.Owners, o => Assert.Equal(FusionCleanupKind.None, o.Cleanup));
        Assert.All(schedule.Owners.Where(o => o.Kind is SendOwnerKind.Operation or SendOwnerKind.Branch), o => Assert.Equal(!sync, o.AsyncBoundary));
        Assert.All(schedule.Owners.Where(o => o.Kind == SendOwnerKind.WrapperNext), o => Assert.Equal(SendOwnerMultiplicity.EachNext, o.Multiplicity));
        Assert.All(schedule.Owners.Where(o => o.Kind is SendOwnerKind.Provider or SendOwnerKind.Service), o => Assert.True(o.Borrowed));
        Assert.All(schedule.States.Where(s => s.StartsWithOriginalRequest), s => Assert.StartsWith("branch:", s.Owner));
        Assert.Equal(schedule.States.Count, schedule.States.Select(s => s.StartTokenSlot).Distinct().Count());
        Assert.All(schedule.States, s => Assert.Equal(isVoid, s.ResponseSlot == null));
        Assert.All(schedule.Awaits, a => { Assert.True(a.ConsumeOnce); Assert.True(a.ConfigureAwaitFalse); });
        Assert.Equal(sync ? 0 : (all ? 2 : 1) * 4, schedule.Awaits.Count); // Two Finally, opaque wrapper, handler per branch.
        Assert.All(schedule.Awaits.Where(a => a.Producer.Phase == SendExecutionPhase.Finally), a => Assert.Equal("void", a.ResultType));
        Assert.All(schedule.Awaits.Where(a => a.Producer.Phase is SendExecutionPhase.InvokeHandler or SendExecutionPhase.InvokeWrapper),
            a => Assert.Equal(isVoid ? "void" : graph.Contract.Result, a.ResultType));
        Assert.Equal(sync && all && !isVoid ? 2 : 0, schedule.Sites.Count(s => s.Condition == SendExecutionCondition.SpanDestination));
        Assert.All(schedule.Sites.Where(s => s.Id.Phase == SendExecutionPhase.CancellationGuard), s => Assert.Equal(SendExecutionCondition.Always, s.Condition));
        Assert.Contains(new SendExecutionOrder(Site(0, 0, SendExecutionPhase.Resolve), Site(0, 0, SendExecutionPhase.Entered), SendOrderCondition.Start), schedule.Orders);
        // next is a dynamic template, never an unconditional edge starting the child.
        Assert.Contains(new SendExecutionOrder(Site(0, 1, SendExecutionPhase.NextCall), Site(0, 2, SendExecutionPhase.ArgumentGuard), SendOrderCondition.EachNext), schedule.Orders);
        Assert.DoesNotContain(schedule.Orders, e => e.Before == Site(0, 1, SendExecutionPhase.InvokeWrapper) && e.After == Site(0, 2, SendExecutionPhase.ArgumentGuard));
        if (all) Assert.Contains(new SendExecutionOrder(Site(0, 0, SendExecutionPhase.ScopeExit), Site(1, -1, SendExecutionPhase.BranchStart), SendOrderCondition.AllBranchesSuccess), schedule.Orders);
    }

    [Fact]
    public void Send_graph_contract_rejects_missing_duplicate_extra_and_misplaced_sites_independently_of_candidate()
    {
        var graph = Graph(BindAcceptedSendLists(GraphFixture()));
        var schedule = graph.Schedule!;
        var guarded = Assert.Single(schedule.Sites, s => s.Id == Site(0, 2, SendExecutionPhase.CancellationGuard));
        var entered = Assert.Single(schedule.Sites, s => s.Id == Site(0, 2, SendExecutionPhase.Entered));
        AssertRejected(schedule with { Sites = new(schedule.Sites.Where(s => s != guarded)) }, "MissingSite:CancellationGuard");
        AssertRejected(schedule with { Sites = new(schedule.Sites.Append(guarded)) }, "DuplicateSite");
        AssertRejected(schedule with { Sites = new(schedule.Sites.Append(guarded with { Id = Site(0, 100, SendExecutionPhase.CancellationGuard) })) }, "UnexpectedSite:CancellationGuard");
        AssertRejected(schedule with { Sites = new(schedule.Sites.Select(s => s == entered ? s with { Owner = "operation" } : s)) }, "SiteContractMismatch:Entered");
        var order = Assert.Single(schedule.Orders, e => e.Before == Site(0, 2, SendExecutionPhase.CancellationGuard) && e.After == Site(0, 2, SendExecutionPhase.Resolve));
        AssertRejected(schedule with { Orders = new(schedule.Orders.Select(e => e == order ? e with { Before = order.After, After = order.Before } : e)) }, "MissingOrder");
        AssertRejected(schedule with { Orders = new(schedule.Orders.Append(order)) }, "UnexpectedOrder");
        void AssertRejected(CanonicalSendExecutionSchedule candidate, string reason) => Assert.Contains(CanonicalSendGraphValidator.Validate(graph.Contract, candidate), f => f.Reason == reason);
    }

    [Fact]
    public void Send_graph_preserves_entered_exception_replacement_and_finally_boundaries()
    {
        var graph = Graph(BindAcceptedSendLists(GraphFixture()));
        var schedule = graph.Schedule!;
        var before = Assert.Single(schedule.Sites, s => s.Id == Site(0, 0, SendExecutionPhase.Before));
        var observer = Assert.Single(schedule.Sites, s => s.Id == Site(0, 0, SendExecutionPhase.Exception));
        var cleanup = Assert.Single(schedule.Sites, s => s.Id == Site(0, 0, SendExecutionPhase.Finally));
        var guard = Assert.Single(schedule.Sites, s => s.Id == Site(0, 0, SendExecutionPhase.CancellationGuard));
        Assert.Equal(SendFailureDisposition.UnwindEnteredOuter, guard.Failure);
        Assert.Equal(SendFailureDisposition.ObserveCurrentOnceThenFinally, before.Failure);
        Assert.Equal(SendFailureDisposition.ReplaceAndFinallyWithoutObserver, observer.Failure);
        Assert.Equal(SendFailureDisposition.ReplaceAndUnwindOuter, cleanup.Failure);
        Assert.Contains(new SendExecutionOrder(observer.Id, cleanup.Id, SendOrderCondition.Unwind), schedule.Orders);
        Assert.DoesNotContain(schedule.Orders, o => o.Before == cleanup.Id && o.After == observer.Id);
        Assert.Contains(new SendExecutionOrder(Site(0, 1, SendExecutionPhase.ScopeExit), Site(0, 0, SendExecutionPhase.Success), SendOrderCondition.InnerSuccess), schedule.Orders);
        var state = Assert.Single(schedule.States, s => s.Owner == Scope(0, 0));
        Assert.True(state.BeforeFailureKeepsInput);
        Assert.True(state.BeforeSuccessSavesReturnedRequest);
        Assert.Contains(CanonicalSendGraphValidator.Validate(graph.Contract, schedule with
        {
            Sites = new(schedule.Sites.Select(s => s == cleanup ? s with { Failure = SendFailureDisposition.ObserveCurrentOnceThenFinally } : s))
        }),
            f => f.Reason == "SiteContractMismatch:Finally");
        Assert.Contains(CanonicalSendGraphValidator.Validate(graph.Contract, schedule with
        {
            States = new(schedule.States.Select(s => s == state ? s with { BeforeFailureKeepsInput = false } : s))
        }), f => f.Reason == "MissingState");
    }

    [Fact]
    public void Send_graph_rejects_double_await_wrong_resume_result_and_ownership_or_state_aliases()
    {
        var graph = Graph(BindAcceptedSendLists(GraphFixture(all: true)));
        var schedule = graph.Schedule!;
        var awaitSite = schedule.Awaits[0];
        AssertRejected(schedule with { Awaits = new(schedule.Awaits.Append(awaitSite)) }, "DuplicateAwaitConsumer");
        AssertRejected(schedule with { Awaits = new(schedule.Awaits.Select(a => a == awaitSite ? a with { ConsumeOnce = false } : a)) }, "AwaitConsumptionMismatch");
        AssertRejected(schedule with { Awaits = new(schedule.Awaits.Select(a => a == awaitSite ? a with { ConfigureAwaitFalse = false } : a)) }, "AwaitConsumptionMismatch");
        AssertRejected(schedule with { Awaits = new(schedule.Awaits.Where(a => a != awaitSite)) }, "MissingAwait");
        AssertRejected(schedule with { Awaits = new(schedule.Awaits.Select(a => a == awaitSite ? a with { Normal = SendAwaitContinuation.BeginInner, RetainedSlots = new(new[] { "other/start-token" }) } : a)) }, "MissingAwait");
        AssertRejected(schedule with { Awaits = new(schedule.Awaits.Select(a => a == awaitSite ? a with { ResultType = "wrong", Cancellation = SendFailureTarget.CurrentScope } : a)) }, "MissingAwait");
        var service = Assert.Single(schedule.Owners, o => o.Id == Service(0, 0));
        AssertRejected(schedule with { Owners = new(schedule.Owners.Select(o => o == service ? o with { Cleanup = FusionCleanupKind.Sync } : o)) }, "BorrowedOwnerHasCleanup");
        var next = Assert.Single(schedule.Owners, o => o.Id == Next(0, 1));
        AssertRejected(schedule with { Owners = new(schedule.Owners.Select(o => o == next ? o with { Parent = o.Id } : o)) }, "OwnerCycle");
        AssertRejected(schedule with { Owners = new(schedule.Owners.Select(o => o == next ? o with { Multiplicity = SendOwnerMultiplicity.Once } : o)) }, "MissingOwner");
        var secondBranch = Assert.Single(schedule.States, s => s.Owner == Branch(1));
        AssertRejected(schedule with { States = new(schedule.States.Select(s => s == secondBranch ? s with { InputRequestSlot = Scope(0, 0) + "/effective-request" } : s)) }, "MissingState");
        AssertRejected(schedule with { EscapingOceIsCanceled = false }, "EscapingOceBoundaryMismatch");
        void AssertRejected(CanonicalSendExecutionSchedule candidate, string reason) => Assert.Contains(CanonicalSendGraphValidator.Validate(graph.Contract, candidate), f => f.Reason == reason);
    }

    [Fact]
    public void Async_before_success_and_exception_contract_edits_change_real_graph_await_producers_and_resume_policies()
    {
        var source = GraphFixture();
        var driver = BindAcceptedSendLists(source);
        var changed = source.Replace("ISendRequestTransform<Ping>, ISendResponseTransform<Ping,int>", "IAsyncSendRequestTransform<Ping>, IAsyncSendResponseTransform<Ping,int>")
            .Replace("ISendExceptionObserver<Ping>, IAsyncSendFinallyObserver<Ping>", "IAsyncSendExceptionObserver<Ping>, IAsyncSendFinallyObserver<Ping>")
            .Replace("public Ping Before(Ping r,CancellationToken c)=>r;", "public ValueTask<Ping> BeforeAsync(Ping r,CancellationToken c)=>new(r);")
            .Replace("public int After(Ping r,int result,CancellationToken c)=>result;", "public ValueTask<int> AfterAsync(Ping r,int result,CancellationToken c)=>new(result);")
            .Replace("public void OnException(Ping r,Exception e,CancellationToken c){}", "public ValueTask OnExceptionAsync(Ping r,Exception e,CancellationToken c)=>default;");
        driver = driver.RunGenerators(Compilation(changed));
        GraphReason(driver, IncrementalStepRunReason.Modified);
        var graph = Graph(driver);
        Assert.True(graph.IsValid);
        Assert.Equal(10, graph.Schedule!.Awaits.Count);
        var before = Assert.Single(graph.Schedule.Awaits, a => a.Producer == Site(0, 0, SendExecutionPhase.Before));
        Assert.Equal(graph.Contract.Request, before.ResultType);
        Assert.Equal(SendAwaitContinuation.BeginInner, before.Normal);
        Assert.Equal(SendFailureTarget.CurrentScope, before.Cancellation);
        Assert.Contains(Scope(0, 0) + "/effective-request", before.RetainedSlots);
        var observer = Assert.Single(graph.Schedule.Awaits, a => a.Producer == Site(0, 0, SendExecutionPhase.Exception));
        Assert.Equal("void", observer.ResultType);
        Assert.Equal(SendAwaitContinuation.EndObserver, observer.Normal);
        var success = Assert.Single(graph.Schedule.Awaits, a => a.Producer == Site(0, 0, SendExecutionPhase.Success));
        Assert.Equal(graph.Contract.Result, success.ResultType);
        Assert.Equal(SendAwaitContinuation.NormalUnwind, success.Normal);
        driver = driver.RunGenerators(Compilation(changed.Replace("=>new(r);", "=>new(new Ping());")));
        Assert.Equal(graph, Graph(driver));
        GraphReason(driver, IncrementalStepRunReason.Cached);
    }

    [Fact]
    public void Handler_only_graph_has_its_own_guard_resolution_and_await_without_inventing_hook_callbacks()
    {
        var driver = BindAcceptedSendLists(ListFixture("c.Handlers<Ping>(DispatchFamily.SendAsync,typeof(A));"));
        var graph = Graph(driver);
        Assert.True(graph.IsValid);
        Assert.Single(graph.Schedule!.Awaits);
        Assert.DoesNotContain(graph.Schedule.Sites, s => s.Id.Phase is SendExecutionPhase.Before or SendExecutionPhase.Success or SendExecutionPhase.Exception or SendExecutionPhase.Finally);
        Assert.Contains(new SendExecutionOrder(Site(0, 0, SendExecutionPhase.ArgumentGuard), Site(0, 0, SendExecutionPhase.CancellationGuard), SendOrderCondition.Start), graph.Schedule.Orders);
    }

    [Fact]
    public void Actual_frontend_edits_invalidate_graph_capability_order_constraints_and_handler_family_while_trivia_refreshes_locations()
    {
        // Two distinct wrappers keep runtime emission unsupported while the graph stays valid.
        var source = GraphFixture(all: true).Replace("Stage.Wrapper<Opaque>()", "Stage.Wrapper<Opaque>(),Stage.Wrapper<OtherOpaque>()") + OtherOpaqueListStage;
        var driver = BindLists(source, "ZEN0037");
        var original = Graph(driver);
        var diagnostic = Assert.Single(driver.GetRunResult().Diagnostics);
        var moved = Compilation("\n\n" + source);
        driver = driver.RunGenerators(moved);
        Assert.Equal(original, Graph(driver));
        GraphReason(driver, IncrementalStepRunReason.Cached);
        var current = Assert.Single(driver.GetRunResult().Diagnostics);
        Assert.Same(moved.SyntaxTrees[0], current.Location.SourceTree);
        Assert.Equal(diagnostic.Location.SourceSpan.Start + 2, current.Location.SourceSpan.Start);
        driver = driver.RunGenerators(Compilation(source.Replace("=>r;", "=>new Ping();") + "public sealed class Unrelated {}"));
        Assert.Equal(original, Graph(driver));
        GraphReason(driver, IncrementalStepRunReason.Cached);
        foreach (var changed in new[] {
            source.Replace("Stage.Hook<Full<string>>(),Stage.Wrapper<Opaque>(),Stage.Wrapper<OtherOpaque>(),Stage.Hook<Tail<string>>()", "Stage.Hook<Tail<string>>(),Stage.Wrapper<Opaque>(),Stage.Wrapper<OtherOpaque>(),Stage.Hook<Full<string>>()"),
            source.Replace("where T : class", "where T : notnull"),
            source.Replace("ISendExceptionObserver<Ping>, IAsyncSendFinallyObserver<Ping>", "ISendExceptionObserver<Ping>, ISendFinallyObserver<Ping>")
                .Replace("public ValueTask FinallyAsync(Ping r,SendExitInfo e,CancellationToken c)=>default;", "public void Finally(Ping r,SendExitInfo e,CancellationToken c){}"),
            GraphFixture(sync: true) })
        {
            Assert.NotEqual(source, changed);
            driver = driver.RunGenerators(Compilation(changed));
            Assert.NotEqual(original, Graph(driver));
            Assert.True(Graph(driver).IsValid);
            GraphReason(driver, IncrementalStepRunReason.Modified);
        }
    }

    [Fact]
    public void Stream_graph_is_separate_from_send_and_owned_sessions_keep_execution_blocked()
    {
        var source = ListFixture("c.Handlers<Items>(DispatchFamily.Stream,typeof(ItemsHandler));") + """
            public sealed record Items : IStreamRequest<int>;
            [ZendiatorHandler] public sealed class ItemsHandler : IStreamRequestHandler<Items,int> {
                public System.Collections.Generic.IAsyncEnumerable<int> HandleAsync(Items r,CancellationToken c)=>throw new NotImplementedException();
            }
            """;
        var stream = Graph(BindLists(source, "ZEN0037"));
        Assert.True(stream.IsValid, string.Join(",", stream.Facts.Select(f => f.Reason)));
        Assert.Null(stream.Schedule);
        Assert.NotNull(stream.StreamSchedule);
        var valid = Graph(BindAcceptedSendLists(GraphFixture()));
        var owned = valid.Contract with { Stages = new(valid.Contract.Stages.Select((s, i) => i == 0 ? s with { ServiceOwnership = FusionOwnership.OwnedSession } : s)) };
        var rejected = CanonicalExecutionGraphNormalizer.Create(owned);
        Assert.False(rejected.IsValid);
        Assert.Null(rejected.Schedule);
        Assert.Contains(rejected.Facts, f => f.Reason == "OwnedStageAcquisitionAndCleanupUnavailable");
    }

    [Fact]
    public void Request_token_transfers_reject_wrong_sources_destinations_and_before_failure_propagation()
    {
        var graph = Graph(BindAcceptedSendLists(GraphFixture(all: true)));
        var schedule = graph.Schedule!;
        var before = Assert.Single(schedule.Transfers, t => t.At == Site(0, 0, SendExecutionPhase.Before));
        Assert.Equal(new SendValueReference(Scope(0, 0), SendValueKind.BeforeResult), before.Source);
        Assert.Equal(new SendValueReference(Scope(0, 0), SendValueKind.EffectiveRequest), before.Destination);
        Assert.Equal(SendTransferCondition.ProducerSuccess, before.Condition);
        AssertRejected(before, before with { Source = before.Source with { Kind = SendValueKind.InputRequest } }, "MissingValueTransfer");
        AssertRejected(before, before with { Destination = before.Destination with { Owner = Scope(0, 2) } }, "MissingValueTransfer");
        AssertRejected(before, before with { Condition = SendTransferCondition.Entered }, "BeforeResultTransferredWithoutSuccess");
        var nextToken = Assert.Single(schedule.Transfers, t => t.At == Site(0, 1, SendExecutionPhase.NextCall) && t.Source.Kind == SendValueKind.NextTokenArgument);
        AssertRejected(nextToken, nextToken with { Source = new(Scope(0, 1), SendValueKind.StartToken) }, "MissingValueTransfer");
        var childRequest = Assert.Single(schedule.Transfers, t => t.At == Site(0, 2, SendExecutionPhase.ArgumentGuard) && t.Destination.Kind == SendValueKind.InputRequest);
        Assert.Equal(new SendValueReference(Next(0, 1), SendValueKind.InputRequest), childRequest.Source);
        AssertRejected(childRequest, childRequest with { Source = new(Scope(0, 0), SendValueKind.EffectiveRequest) }, "MissingValueTransfer");
        var branch = Assert.Single(schedule.Transfers, t => t.At == Site(1, -1, SendExecutionPhase.BranchStart) && t.Destination.Kind == SendValueKind.InputRequest);
        Assert.Equal(new SendValueReference("operation", SendValueKind.OperationRequest), branch.Source);
        AssertRejected(branch, branch with { Source = new(Branch(0), SendValueKind.InputRequest) }, "MissingValueTransfer");
        var token = Assert.Single(schedule.Transfers, t => t.At == Site(1, -1, SendExecutionPhase.BranchStart) && t.Destination.Kind == SendValueKind.StartToken);
        Assert.Equal(new SendValueReference("operation", SendValueKind.OperationToken), token.Source);
        AssertRejected(token, token with { Destination = new(Branch(0), SendValueKind.StartToken) }, "MissingValueTransfer");
        void AssertRejected(SendValueTransfer original, SendValueTransfer changed, string reason) =>
            Assert.Contains(CanonicalSendGraphValidator.Validate(graph.Contract, schedule with { Transfers = new(schedule.Transfers.Select(t => t == original ? changed : t)) }), f => f.Reason == reason);
    }

    [Theory]
    [InlineData("ArgumentGuard")]
    [InlineData("CancellationGuard")]
    [InlineData("Resolve")]
    public void Conditional_control_proof_rejects_guard_or_di_failure_reaching_entered_or_callbacks_even_when_orders_are_unchanged(string phaseName)
    {
        var graph = Graph(BindAcceptedSendLists(GraphFixture()));
        var schedule = graph.Schedule!;
        var phase = Enum.Parse<SendExecutionPhase>(phaseName);
        var owner = Scope(0, 0);
        var point = Point(owner, Site(0, 0, phase));
        var bad = ChangeControl(schedule, owner, e => e.From == point && e.Outcome == SendControlOutcome.Failure
            ? e with { To = Point(owner, Site(0, 0, SendExecutionPhase.Entered)) } : e);
        Assert.Equal(schedule.Orders, bad.Orders);
        Assert.Contains(CanonicalSendControlValidator.Validate(graph.Contract, bad), f => f.Reason == "EnteredNotDominatedByGuardsAndResolution");
        bad = ChangeControl(schedule, owner, e => e.From == point && e.Outcome == SendControlOutcome.Failure
            ? e with { To = Point(owner, Site(0, 0, SendExecutionPhase.Before)) } : e);
        Assert.Contains(CanonicalSendControlValidator.Validate(graph.Contract, bad), f => f.Reason == "CallbackReachedBeforeEntered");
    }

    [Fact]
    public void Conditional_control_proof_requires_finally_once_without_own_observer_reentry_or_missing_failure_paths()
    {
        var graph = Graph(BindAcceptedSendLists(GraphFixture()));
        var schedule = graph.Schedule!;
        var owner = Scope(0, 0);
        var after = Point(owner, Site(0, 0, SendExecutionPhase.Success));
        var cleanup = Point(owner, Site(0, 0, SendExecutionPhase.Finally));
        var observer = Point(owner, Site(0, 0, SendExecutionPhase.Exception));
        var skip = ChangeControl(schedule, owner, e => e.From == after && e.Outcome == SendControlOutcome.Success ? e with { To = Exit(owner, true) } : e);
        Assert.Contains(CanonicalSendControlValidator.Validate(graph.Contract, skip), f => f.Reason == "EnteredScopeSkippedFinally");
        var repeat = ChangeControl(schedule, owner, e => e.From == cleanup && e.Outcome == SendControlOutcome.Success && e.Predicate == SendControlPredicate.NoFailure
            ? e with { To = cleanup } : e);
        Assert.Contains(CanonicalSendControlValidator.Validate(graph.Contract, repeat), f => f.Reason == "FinallyRepeated");
        var reenter = ChangeControl(schedule, owner, e => e.From == cleanup && e.Outcome == SendControlOutcome.Failure ? e with { To = observer } : e);
        Assert.Contains(CanonicalSendControlValidator.Validate(graph.Contract, reenter), f => f.Reason == "ObserverReenteredAfterFinally");
        var before = Point(owner, Site(0, 0, SendExecutionPhase.Before));
        var propagate = ChangeControl(schedule, owner, e => e.From == before && e.Outcome == SendControlOutcome.Failure
            ? e with { To = Point(owner, Site(0, 0, SendExecutionPhase.InvokeInner)) } : e);
        Assert.Contains(CanonicalSendControlValidator.Validate(graph.Contract, propagate), f => f.Reason == "InvocationStartedAfterFailure");
        var handler = Scope(0, 3);
        var incomplete = ChangeControl(schedule, handler, e => e.From.Site?.Phase == SendExecutionPhase.InvokeHandler && e.Outcome == SendControlOutcome.Failure ? null : e);
        Assert.Contains(CanonicalSendControlValidator.Validate(graph.Contract, incomplete), f => f.Reason == "MissingControlOutcome");
        Assert.Equal(schedule.Orders, repeat.Orders);
    }

    [Fact]
    public void Conditional_branch_paths_stop_on_failure_and_publish_only_after_all_branches_succeed()
    {
        var graph = Graph(BindAcceptedSendLists(GraphFixture(sync: true, all: true)));
        var schedule = graph.Schedule!;
        var first = Point("operation", Site(0, -1, SendExecutionPhase.BranchStart));
        var second = Point("operation", Site(1, -1, SendExecutionPhase.BranchStart));
        var bad = ChangeControl(schedule, "operation", e => e.From == first && e.Outcome == SendControlOutcome.Failure ? e with { To = second } : e);
        Assert.Equal(schedule.Orders, bad.Orders);
        Assert.Contains(CanonicalSendControlValidator.Validate(graph.Contract, bad), f => f.Reason == "BranchStartedAfterFailure");
        var publish = ChangeControl(schedule, "operation", e => e.From == first && e.Outcome == SendControlOutcome.Success
            ? e with { To = Point("operation", Site(-1, -1, SendExecutionPhase.OperationReturn)) } : e);
        Assert.Contains(CanonicalSendControlValidator.Validate(graph.Contract, publish), f => f.Reason == "ResultPublishedBeforeAllBranchesSucceeded");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(42)]
    public void Unknown_async_hook_completion_is_rejected_at_ir_boundary_even_without_single_consumption_flag(int completion)
    {
        var graph = Graph(BindAcceptedSendLists(GraphFixture()));
        var malformed = graph.Contract with
        {
            Stages = new(graph.Contract.Stages.Select((s, i) => i == 0 ? s with
            {
                Callbacks = new(s.Callbacks.Select(c => c.Phase == BoundCallbackPhase.Finally ? c with { Completion = (BoundCompletionKind)completion, RequiresSingleConsumption = false } : c))
            } : s))
        };
        var result = CanonicalExecutionGraphNormalizer.Create(malformed);
        Assert.Null(result.Schedule);
        Assert.Contains(result.Facts, f => f.Reason == "CallbackCompletionUnavailable");
        var wrongFlag = graph.Contract with
        {
            Stages = new(graph.Contract.Stages.Select((s, i) => i == 0 ? s with
            {
                Callbacks = new(s.Callbacks.Select(c => c.Completion == BoundCompletionKind.ValueTask ? c with { RequiresSingleConsumption = false } : c))
            } : s))
        };
        Assert.Contains(CanonicalExecutionGraphNormalizer.Create(wrongFlag).Facts, f => f.Reason == "SingleConsumptionContractMismatch");
    }

    [Fact]
    public void Actual_callback_order_requires_before_and_inner_success_even_when_declared_orders_and_counts_are_correct()
    {
        var graph = Graph(BindAcceptedSendLists(GraphFixture()));
        var schedule = graph.Schedule!;
        Assert.Empty(CanonicalSendControlValidator.Validate(graph.Contract, schedule));
        var owner = Scope(0, 0);
        var entered = Point(owner, Site(0, 0, SendExecutionPhase.Entered));
        var before = Point(owner, Site(0, 0, SendExecutionPhase.Before));
        var inner = Point(owner, Site(0, 0, SendExecutionPhase.InvokeInner));
        var after = Point(owner, Site(0, 0, SendExecutionPhase.Success));
        var cleanup = Point(owner, Site(0, 0, SendExecutionPhase.Finally));
        // Entered -> After -> Before -> inner -> Finally. Every original failure path is retained.
        var bad = ChangeControl(schedule, owner, e => e.From == entered ? e with { To = after } :
            e.Outcome == SendControlOutcome.Success && e.From == after ? e with { To = before } :
            e.Outcome == SendControlOutcome.Success && e.From == inner ? e with { To = cleanup } : e);
        Assert.Equal(schedule.Orders, bad.Orders);
        var facts = CanonicalSendControlValidator.Validate(graph.Contract, bad);
        Assert.Contains(facts, f => f.Reason == "SuccessNotDominatedByInnerSuccess");
        Assert.Contains(facts, f => f.Reason == "BeforeReachedAfterInnerOrUnwind");
        var skippedBefore = ChangeControl(schedule, owner, e => e.From == entered ? e with { To = inner } : e);
        Assert.Contains(CanonicalSendControlValidator.Validate(graph.Contract, skippedBefore), f => f.Reason == "InnerNotDominatedByBeforeSuccess");
        Assert.Contains(CanonicalSendControlValidator.Validate(graph.Contract, skippedBefore), f => f.Reason == "ControlViolatesDeclaredOrder");
    }

    [Fact]
    public void Finally_only_hook_cannot_run_cleanup_before_inner_completion_despite_one_finally_and_unchanged_orders()
    {
        var source = ListFixture("c.Handlers<Ping>(DispatchFamily.SendAsync,typeof(A)); c.Pipeline<Ping>(Stage.Hook<EndOnly>());") + """
            public sealed class EndOnly : IAsyncSendFinallyObserver<Ping> {
                public ValueTask FinallyAsync(Ping r,SendExitInfo e,CancellationToken c)=>default;
            }
            """;
        var graph = Graph(BindAcceptedSendLists(source));
        var schedule = graph.Schedule!;
        Assert.True(graph.IsValid);
        Assert.Empty(CanonicalSendControlValidator.Validate(graph.Contract, schedule));
        var owner = Scope(0, 0);
        var entered = Point(owner, Site(0, 0, SendExecutionPhase.Entered));
        var inner = Point(owner, Site(0, 0, SendExecutionPhase.InvokeInner));
        var cleanup = Point(owner, Site(0, 0, SendExecutionPhase.Finally));
        var bad = ChangeControl(schedule, owner, e => e.From == entered ? e with { To = cleanup } :
            e.From == cleanup && e.Outcome == SendControlOutcome.Success && e.Predicate == SendControlPredicate.NoFailure ? e with { To = inner } :
            e.From == inner ? e with { To = Exit(owner, e.Outcome == SendControlOutcome.Success) } : e);
        Assert.Equal(schedule.Orders, bad.Orders);
        var facts = CanonicalSendControlValidator.Validate(graph.Contract, bad);
        Assert.Contains(facts, f => f.Reason == "FinallyNotDominatedByInnerCompletion");
        Assert.Contains(facts, f => f.Reason == "InvocationStartedAfterFinally");
    }

    [Fact]
    public void Span_commit_must_precede_return_at_return_point_even_if_success_exit_later_observes_commit()
    {
        var graph = Graph(BindAcceptedSendLists(GraphFixture(sync: true, all: true)));
        var schedule = graph.Schedule!;
        Assert.Empty(CanonicalSendControlValidator.Validate(graph.Contract, schedule));
        var last = Point("operation", Site(1, -1, SendExecutionPhase.BranchStart));
        var returned = Point("operation", Site(-1, -1, SendExecutionPhase.OperationReturn));
        var commit = Point("operation", Site(-1, -1, SendExecutionPhase.DestinationCommit));
        var bad = schedule with
        {
            ControlFlows = new(schedule.ControlFlows.Select(f => f.Owner != "operation" ? f : f with
            {
                Edges = new(f.Edges.Where(e => e.From != returned).Select(e =>
                        e.From == last && e.Outcome == SendControlOutcome.Success && e.Predicate == SendControlPredicate.SpanDestination ? e with { To = returned } :
                        e.From == commit ? e with { To = Exit("operation", true) } : e)
                .Append(new(returned, commit, SendControlOutcome.Always, SendControlPredicate.SpanDestination))
                .Append(new(returned, Exit("operation", true), SendControlOutcome.Always, SendControlPredicate.NoSpanDestination)))
            }))
        };
        Assert.Equal(schedule.Orders, bad.Orders);
        var facts = CanonicalSendControlValidator.Validate(graph.Contract, bad);
        Assert.Contains(facts, f => f.Reason == "ReturnNotDominatedByDestinationCommit");
        Assert.Contains(facts, f => f.Reason == "DestinationCommittedAfterReturn");
        Assert.Contains(facts, f => f.Reason == "ControlViolatesDeclaredOrder");
    }
}
