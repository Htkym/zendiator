using Zendiator.SourceGenerator;
using Xunit;
using static Zendiator.SourceGenerator.SendExecutionNames;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    // The current public frontend has no session-factory API. Start from its real bound route,
    // then supply explicit symbol-free factory facts; this is an IR test, not frontend binding proof.
    private static BoundSessionModel SessionBinding(bool sync, bool borrowed = false)
    {
        var completion = sync ? BoundCompletionKind.Synchronous : BoundCompletionKind.ValueTask;
        var acquire = new BoundCallbackModel(BoundCallbackPhase.SessionAcquire, completion,
            "Fixture.SessionFactory", "Fixture.SessionFactory.Create", default, sync ? "Fixture.State" : "global::System.Threading.Tasks.ValueTask<Fixture.State>", "", !sync);
        var cleanup = new BoundCallbackModel(BoundCallbackPhase.SessionCleanup, completion,
            sync ? "global::System.IDisposable" : "global::System.IAsyncDisposable",
            sync ? "Fixture.State.Dispose" : "Fixture.State.DisposeAsync", default,
            sync ? "void" : "global::System.Threading.Tasks.ValueTask", "", !sync);
        return new("Fixture.SessionFactory", "Fixture.State", acquire,
            borrowed ? FusionOwnership.BorrowedResource : FusionOwnership.OwnedSession, borrowed ? null : cleanup);
    }

    private static BoundPipelineModel SessionRoute(bool sync = false, bool all = false, bool isVoid = false)
    {
        var route = Graph(BindLists(GraphFixture(sync, all, isVoid), "ZEN0037")).Contract;
        var owned = SessionBinding(sync);
        return route with {
            Stages = new(route.Stages.Select((stage, index) => stage with {
                Sessions = index == 0 ? new(new[] { owned, SessionBinding(sync, borrowed: true), owned }) : new(new[] { owned })
            })),
            Handlers = new(route.Handlers.Select(handler => handler with { Sessions = new(new[] { owned }) }))
        };
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    public void Explicit_Send_sessions_connect_to_real_guard_entered_finally_and_each_next_branch_boundaries(bool sync, bool all, bool isVoid)
    {
        var route = SessionRoute(sync, all, isVoid);
        var graph = CanonicalExecutionGraphNormalizer.Create(route);
        Assert.True(graph.IsValid, string.Join(",", graph.Facts.Select(f => f.Reason)));
        var schedule = graph.Schedule!;
        var inspection = CanonicalSendSessionValidator.Inspect(route, schedule);
        Assert.Empty(inspection.Facts);
        Assert.NotEmpty(inspection.Paths);
        Assert.All(schedule.Owners.Where(o => o.Kind == SendOwnerKind.Service || o.Kind == SendOwnerKind.Provider),
            owner => { Assert.True(owner.Borrowed); Assert.Equal(FusionCleanupKind.None, owner.Cleanup); });
        Assert.All(schedule.Owners.Where(o => o.Kind == SendOwnerKind.Session && !o.Borrowed),
            owner => Assert.Equal(sync ? FusionCleanupKind.Sync : FusionCleanupKind.Async, owner.Cleanup));
        Assert.All(schedule.Owners.Where(o => o.Kind == SendOwnerKind.Session && o.Id.StartsWith(Scope(0, 2))),
            owner => Assert.Equal(SendOwnerMultiplicity.EachNext, owner.Multiplicity));
        Assert.All(inspection.Paths, path => {
            var declaration = Sessions(route, int.Parse(path.Owner.Split('/')[0].Split(':')[1]), int.Parse(path.Owner.Split(':').Last()));
            Assert.Equal(path.Acquired.Where(i => declaration[i].Ownership == FusionOwnership.OwnedSession).Reverse(), path.CleanupOrder);
        });
        Assert.All(schedule.SessionCalls, call => { Assert.True(call.PreserveIdentityAndToken); Assert.True(call.ReplacePreviousError); Assert.False(call.Aggregate); });
        Assert.All(schedule.Awaits.Where(a => a.Producer.Phase is SendExecutionPhase.AcquireSession or SendExecutionPhase.CleanupSession),
            a => { Assert.True(a.ConsumeOnce); Assert.True(a.ConfigureAwaitFalse); });
        // Normal public input still blocks runtime and has no implicit session acquisitions.
        var frontend = Graph(BindLists(GraphFixture(sync, all, isVoid), "ZEN0037"));
        Assert.All(frontend.Contract.Stages, stage => Assert.Empty(stage.Sessions));
        Assert.Empty(frontend.Schedule!.SessionCalls);
    }

    [Fact]
    public void Partial_factory_failure_cleans_only_successful_transfers_without_entering_own_Hook()
    {
        var route = SessionRoute();
        var owned = SessionBinding(false);
        route = route with { Stages = new(route.Stages.Select((s, i) => i == 0 ? s with { Sessions = new(new[] { owned, owned }) } : s)) };
        var schedule = CanonicalSendScheduleBuilder.Build(route);
        var inspection = CanonicalSendSessionValidator.Inspect(route, schedule);
        Assert.Empty(inspection.Facts);
        var paths = inspection.Paths.Where(p => p.Owner == Scope(0, 0)).ToArray();
        foreach (var phase in new[] { SendExecutionPhase.SessionGuard, SendExecutionPhase.ResolveSessionFactory, SendExecutionPhase.AcquireSession })
        {
            var failure = "fault:" + SessionSite(0, 0, 1, phase);
            Assert.Contains(paths, p => p.Acquired.SequenceEqual(new[] { 0 }) && p.CleanupOrder.SequenceEqual(new[] { 0 }) &&
                p.FailureOrder.Contains(failure) && !p.Visited.Contains(Site(0, 0, SendExecutionPhase.Entered)) &&
                !p.Visited.Contains(Site(0, 0, SendExecutionPhase.Finally)));
        }
        Assert.Contains(paths, p => p.Acquired.Count == 2 && p.CleanupOrder.SequenceEqual(new[] { 1, 0 }) &&
            p.FailureOrder.Contains("cancel:" + Site(0, 0, SendExecutionPhase.InvokeInner)) &&
            p.FailureOrder.Contains("fault:" + SessionSite(0, 0, 1, SendExecutionPhase.CleanupSession)) &&
            p.EscapingError == "cancel:" + SessionSite(0, 0, 0, SendExecutionPhase.CleanupSession) && p.Canceled);
        Assert.Contains(paths, p => p.Acquired.Count == 2 && p.CleanupOrder.SequenceEqual(new[] { 1, 0 }) &&
            p.FailureOrder.Contains("cancel:" + Site(0, 0, SendExecutionPhase.InvokeInner)) &&
            p.EscapingError == "fault:" + SessionSite(0, 0, 0, SendExecutionPhase.CleanupSession) && !p.Canceled);
    }

    [Fact]
    public void Session_operand_oracle_rejects_aliases_DI_cleanup_missing_detach_and_failure_aggregation_without_Required_comparison()
    {
        var route = SessionRoute(); var schedule = CanonicalSendScheduleBuilder.Build(route);
        var commit = SessionSite(0, 0, 0, SendExecutionPhase.CommitSession);
        Reject(schedule with { SessionTransfers = new(schedule.SessionTransfers.Select(t => t.At == commit
            ? t with { Source = Session(0, 0, 2) + "/factory-result" } : t)) }, "SessionTransferLostOrAliasedResource");
        Reject(schedule with { SessionTransfers = new(schedule.SessionTransfers.Where(t => !(t.At.Phase == SendExecutionPhase.DetachSession && t.Source == "null"))) },
            "SessionDetachedWithoutClearingExactLiveLease");
        var dispose = SessionSite(0, 0, 2, SendExecutionPhase.CleanupSession);
        Reject(schedule with { SessionCalls = new(schedule.SessionCalls.Select(c => c.At == dispose
            ? c with { Receiver = Session(0, 0, 2) + "/factory/resolved" } : c)) }, "CleanupReceiverIsNotTransferredSession");
        Reject(schedule with { SessionCalls = new(schedule.SessionCalls.Select(c => c.At == dispose ? c with { Aggregate = true } : c)) },
            "SessionExceptionReplacementIdentityMismatch");
        var awaitSite = Assert.Single(schedule.Awaits, a => a.Producer == dispose);
        Reject(schedule with { Awaits = new(schedule.Awaits.Select(a => a == awaitSite ? a with { ConsumeOnce = false } : a)) },
            "SessionAwaitConsumptionOrContinuationMismatch");
        void Reject(CanonicalSendExecutionSchedule candidate, string reason) =>
            Assert.Contains(CanonicalSendSessionValidator.Validate(route, candidate), fact => fact.Reason == reason);
    }

    [Fact]
    public void Actual_session_paths_reject_skip_reverse_order_double_release_and_cleanup_cancel_guard()
    {
        var route = SessionRoute(); var schedule = CanonicalSendScheduleBuilder.Build(route);
        var owner = Scope(0, 0);
        var dispose2 = Point(owner, SessionSite(0, 0, 2, SendExecutionPhase.CleanupSession));
        var present0 = Point(owner, SessionSite(0, 0, 0, SendExecutionPhase.SessionPresent));
        var result = Point(owner, SessionSite(0, 0, -1, SendExecutionPhase.SessionResult));
        Reject(ChangeControl(schedule, owner, e => e.From == dispose2 && e.Outcome == SendControlOutcome.Failure ? e with { To = result } : e),
            "AcquiredSessionsNotReleasedExactlyOnceInReverseOrder");
        Reject(ChangeControl(schedule, owner, e => e.From.Site?.Phase == SendExecutionPhase.DetachSession && e.From.Site.Session == 0
            ? e with { To = present0 } : e), "SessionCleanupOrderOrDuplicateMismatch");
        Reject(ChangeControl(schedule, owner, e => e.From == dispose2 && e.Outcome == SendControlOutcome.Success ? e with { To = dispose2 } : e),
            "SessionControlCycleOrRepeatedRelease");
        Reject(ChangeControl(schedule, owner, e => e.From == dispose2 && e.Outcome == SendControlOutcome.Failure
            ? e with { To = Point(owner, SessionSite(0, 0, 0, SendExecutionPhase.SessionGuard)) } : e), "CancellationGuardReachedDuringSessionCleanup");
        Reject(ChangeControl(schedule, owner, e => e.From.Site?.Phase == SendExecutionPhase.InvokeInner && e.Outcome == SendControlOutcome.Success
            ? e with { To = Point(owner, SessionSite(0, 0, 2, SendExecutionPhase.DetachSession)) } : e), "OwnedCleanupBeforeHookFinally");
        void Reject(CanonicalSendExecutionSchedule candidate, string reason) =>
            Assert.Contains(CanonicalSendSessionValidator.Validate(route, candidate), fact => fact.Reason == reason);
    }

    [Fact]
    public void Session_contract_refuses_implicit_DI_ownership_async_cleanup_on_sync_and_unconnected_Stream()
    {
        var route = SessionRoute(sync: true); var owned = SessionBinding(true);
        Reject(owned with { Cleanup = null }, "OwnedSessionCleanupRequired");
        Reject(owned with { Ownership = FusionOwnership.BorrowedResource }, "BorrowedResourceHasCleanup");
        Reject(owned with { Cleanup = SessionBinding(false).Cleanup }, "SessionCleanupModeMismatch");
        Reject(owned with { Ownership = FusionOwnership.BorrowedService }, "ExplicitResourceOwnershipRequired");
        var stream = Graph(BindLists(StreamGraphFixture(), "ZEN0037")).Contract;
        stream = stream with { Handlers = new(stream.Handlers.Select(h => h with { Sessions = new(new[] { SessionBinding(false) }) })) };
        Assert.Contains(CanonicalStreamGraphValidator.CheckContract(stream), f => f.Reason == "StreamSessionAcquisitionAndCleanupUnavailable");
        void Reject(BoundSessionModel binding, string reason) =>
            Assert.Contains(CanonicalSendGraphValidator.CheckContract(route with {
                Stages = new(route.Stages.Select((s, i) => i == 0 ? s with { Sessions = new(new[] { binding }) } : s))
            }), f => f.Reason == reason);
    }
}
