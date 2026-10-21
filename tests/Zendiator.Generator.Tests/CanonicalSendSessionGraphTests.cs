using Zendiator.SourceGenerator;
using Xunit;
using static Zendiator.SourceGenerator.SendExecutionNames;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    // Multiple auxiliary resources still use synthetic symbol-free facts. Public factory callback
    // receivers are covered separately by real frontend fixtures in SendSessionReceiverTests.
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
        var route = Graph(BindAcceptedSendLists(GraphFixture(sync, all, isVoid))).Contract;
        var owned = SessionBinding(sync);
        return route with
        {
            Stages = new(route.Stages.Select((stage, index) => stage with
            {
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
        Assert.All(inspection.Paths, path =>
        {
            var declaration = Sessions(route, int.Parse(path.Owner.Split('/')[0].Split(':')[1]), int.Parse(path.Owner.Split(':').Last()));
            Assert.Equal(path.Acquired.Where(i => declaration[i].Ownership == FusionOwnership.OwnedSession).Reverse(), path.CleanupOrder);
        });
        Assert.All(schedule.SessionCalls, call => { Assert.True(call.PreserveIdentityAndToken); Assert.True(call.ReplacePreviousError); Assert.False(call.Aggregate); });
        Assert.All(schedule.Awaits.Where(a => a.Producer.Phase is SendExecutionPhase.AcquireSession or SendExecutionPhase.CleanupSession),
            a => { Assert.True(a.ConsumeOnce); Assert.True(a.ConfigureAwaitFalse); });
        // Accepted frontend input still has no implicit session acquisitions.
        var frontend = Graph(BindAcceptedSendLists(GraphFixture(sync, all, isVoid)));
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
        Reject(schedule with
        {
            SessionTransfers = new(schedule.SessionTransfers.Select(t => t.At == commit
            ? t with { Source = Session(0, 0, 2) + "/factory-result" } : t))
        }, "SessionTransferLostOrAliasedResource");
        Reject(schedule with { SessionTransfers = new(schedule.SessionTransfers.Where(t => !(t.At.Phase == SendExecutionPhase.DetachSession && t.Source == "null"))) },
            "SessionDetachedWithoutClearingExactLiveLease");
        var dispose = SessionSite(0, 0, 2, SendExecutionPhase.CleanupSession);
        Reject(schedule with
        {
            SessionCalls = new(schedule.SessionCalls.Select(c => c.At == dispose
            ? c with { Receiver = Session(0, 0, 2) + "/factory/resolved" } : c))
        }, "CleanupReceiverIsNotTransferredSession");
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

    private static BoundPipelineModel CleanupBoundaryRoute(int scope, bool hasFinally, bool sync = false)
    {
        var route = SessionRoute(sync);
        var sessions = new EquatableArray<BoundSessionModel>(new[] { SessionBinding(sync) });
        return route with
        {
            Stages = new(route.Stages.Select((s, i) => s with
            {
                Sessions = i == scope ? sessions : default,
                Callbacks = i == scope && !hasFinally ? new(s.Callbacks.Where(c => c.Phase != BoundCallbackPhase.Finally)) : s.Callbacks
            })),
            Handlers = new(route.Handlers.Select(h => h with { Sessions = scope == route.Stages.Count ? sessions : default }))
        };
    }

    [Theory]
    [InlineData(3, false, false)]
    [InlineData(1, false, false)]
    [InlineData(2, false, false)]
    [InlineData(0, true, false)]
    [InlineData(3, false, true)]
    [InlineData(1, false, true)]
    [InlineData(2, false, true)]
    [InlineData(0, true, true)]
    public void Cleanup_boundary_accepts_completed_or_failed_body_and_partial_startup_for_each_scope(int scope, bool hasFinally, bool sync)
    {
        var route = CleanupBoundaryRoute(scope, hasFinally, sync);
        var graph = CanonicalExecutionGraphNormalizer.Create(route);
        Assert.True(graph.IsValid, string.Join(",", graph.Facts.Select(f => f.Reason)));
        var inspection = CanonicalSendSessionValidator.Inspect(route, graph.Schedule!);
        Assert.Empty(inspection.Facts);
        Assert.Empty(CanonicalSendControlValidator.Validate(route, graph.Schedule!));
        var paths = inspection.Paths.Where(p => p.Owner == Scope(0, scope)).ToArray();
        var invocation = scope == route.Stages.Count ? SendExecutionPhase.InvokeHandler :
            route.Stages[scope].Kind == FusionStageKind.Wrapper ? SendExecutionPhase.InvokeWrapper : SendExecutionPhase.InvokeInner;
        var body = Site(0, scope, invocation);
        Assert.Contains(paths, p => p.EscapingError == null && p.Visited.Contains(body) && p.CleanupOrder.SequenceEqual(new[] { 0 }));
        Assert.Contains(paths, p => p.EscapingError == "fault:" + body && p.FailureOrder.Count == 1 && p.CleanupOrder.SequenceEqual(new[] { 0 }));
        Assert.Contains(paths, p => p.EscapingError == "fault:" + SessionSite(0, scope, 0, SendExecutionPhase.AcquireSession) &&
            p.Acquired.Count == 0 && p.CleanupOrder.Count == 0 && !p.Visited.Contains(Site(0, scope, SendExecutionPhase.Entered)));
        if (invocation == SendExecutionPhase.InvokeInner)
        {
            foreach (var callback in new[] { SendExecutionPhase.Before, SendExecutionPhase.Success, SendExecutionPhase.Exception }
                .Concat(hasFinally ? new[] { SendExecutionPhase.Finally } : Array.Empty<SendExecutionPhase>()))
                Assert.Contains(paths, p => p.EscapingError == "fault:" + Site(0, scope, callback) && p.CleanupOrder.SequenceEqual(new[] { 0 }));
            Assert.Contains(paths, p => p.EscapingError == "fault:" + Site(0, scope, SendExecutionPhase.Before) &&
                !p.Visited.Contains(body) && p.Visited.Contains(Site(0, scope, SendExecutionPhase.Exception)) &&
                (p.Visited.Contains(Site(0, scope, SendExecutionPhase.Finally)) == hasFinally));
        }
    }

    [Theory]
    [InlineData(3, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    public void Metadata_preserving_CFG_rejects_detach_cleanup_then_body_even_when_release_count_and_Orders_match(int scope, bool hasFinally)
    {
        var route = CleanupBoundaryRoute(scope, hasFinally);
        var schedule = CanonicalSendScheduleBuilder.Build(route);
        var owner = Scope(0, scope);
        var entered = Point(owner, Site(0, scope, SendExecutionPhase.Entered));
        var originalBody = Assert.Single(schedule.ControlFlows.Single(f => f.Owner == owner).Edges, e => e.From == entered).To;
        var detach = Point(owner, SessionSite(0, scope, 0, SendExecutionPhase.DetachSession));
        var cleanup = Point(owner, SessionSite(0, scope, 0, SendExecutionPhase.CleanupSession));
        var result = Point(owner, SessionSite(0, scope, -1, SendExecutionPhase.SessionResult));
        var candidate = schedule with
        {
            ControlFlows = new(schedule.ControlFlows.Select(f => f.Owner != owner ? f : f with
            {
                Edges = new(f.Edges.Select(e => e.From == entered ? e with { To = detach } :
                    e.From == cleanup && e.Outcome == SendControlOutcome.Success ? e with { To = originalBody, Predicate = SendControlPredicate.NoFailure } :
                    e.To == detach && e.From.Site?.Phase is SendExecutionPhase.Before or SendExecutionPhase.InvokeInner or SendExecutionPhase.InvokeHandler or
                        SendExecutionPhase.InvokeWrapper or SendExecutionPhase.Success or SendExecutionPhase.Exception or SendExecutionPhase.Finally ? e with { To = result } : e)
                .Append(new(cleanup, result, SendControlOutcome.Success, SendControlPredicate.HasFailure)))
            }))
        };
        // Sites, Orders, owners, scopes, calls, transfers and await metadata are completely unchanged.
        Assert.Equal(schedule, candidate with { ControlFlows = schedule.ControlFlows });
        var independent = CanonicalSendSessionValidator.Inspect(route, candidate);
        Assert.Contains(independent.Facts, f => f.Reason == "SessionCleanupStartedBeforeBodyOutcome");
        Assert.Contains(independent.Facts, f => f.Reason == "SessionBodyCallbackAfterDetach");
        Assert.Contains(CanonicalSendControlValidator.Validate(route, candidate), f => f.Reason == "SessionCleanupStartedBeforeBodyOutcome");
        Assert.Contains(CanonicalSendControlValidator.Validate(route, candidate), f => f.Reason == "SessionBodyCallbackAfterDetach");
        // The dangerous successful path does release once and execute the body afterwards.
        Assert.Contains(independent.Paths, p => p.Owner == owner && p.EscapingError == null &&
            p.CleanupOrder.SequenceEqual(new[] { 0 }) && p.Visited.ToList().IndexOf(cleanup.Site!) < p.Visited.ToList().IndexOf(originalBody.Site!));
    }

    [Fact]
    public void Hook_detach_requires_normal_After_and_failure_observer_completion_before_optional_Finally()
    {
        foreach (var hasFinally in new[] { false, true })
        {
            var scope = hasFinally ? 0 : 2;
            var route = CleanupBoundaryRoute(scope, hasFinally);
            var schedule = CanonicalSendScheduleBuilder.Build(route);
            var owner = Scope(0, scope);
            var detach = Point(owner, SessionSite(0, scope, 0, SendExecutionPhase.DetachSession));
            foreach (var cut in new[] {
                (Phase: SendExecutionPhase.Before, Outcome: SendControlOutcome.Success),
                (Phase: SendExecutionPhase.Before, Outcome: SendControlOutcome.Failure),
                (Phase: SendExecutionPhase.InvokeInner, Outcome: SendControlOutcome.Success),
                (Phase: SendExecutionPhase.InvokeInner, Outcome: SendControlOutcome.Failure)
            })
            {
                var candidate = ChangeControl(schedule, owner, e => e.From.Site?.Phase == cut.Phase && e.Outcome == cut.Outcome ? e with { To = detach } : e);
                Assert.Equal(schedule, candidate with { ControlFlows = schedule.ControlFlows });
                Assert.Contains(CanonicalSendSessionValidator.Validate(route, candidate), f => f.Reason == "SessionCleanupStartedBeforeBodyOutcome");
                Assert.Contains(CanonicalSendControlValidator.Validate(route, candidate), f => f.Reason == "SessionCleanupStartedBeforeBodyOutcome");
            }
        }
    }

    [Fact]
    public void Send_session_contract_refuses_implicit_DI_ownership_and_async_cleanup_on_sync()
    {
        var route = SessionRoute(sync: true); var owned = SessionBinding(true);
        Reject(owned with { Cleanup = null }, "OwnedSessionCleanupRequired");
        Reject(owned with { Ownership = FusionOwnership.BorrowedResource }, "BorrowedResourceHasCleanup");
        Reject(owned with { Cleanup = SessionBinding(false).Cleanup }, "SessionCleanupModeMismatch");
        Reject(owned with { Ownership = FusionOwnership.BorrowedService }, "ExplicitResourceOwnershipRequired");
        void Reject(BoundSessionModel binding, string reason) =>
            Assert.Contains(CanonicalSendGraphValidator.CheckContract(route with
            {
                Stages = new(route.Stages.Select((s, i) => i == 0 ? s with { Sessions = new(new[] { binding }) } : s))
            }), f => f.Reason == reason);
    }
}
