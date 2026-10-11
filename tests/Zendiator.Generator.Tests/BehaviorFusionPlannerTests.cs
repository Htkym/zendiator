using Zendiator.SourceGenerator;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed class BehaviorFusionPlannerTests
{
    private static EmissionType Type(string name, bool refLike = false) => new(name, name, !refLike, refLike);
    private static readonly EmissionType Item = new("global::System.Int32", "global::System.Int32", false, false);
    private static FusionDeclaredStage Hook(string name, FusionCapabilities capabilities = FusionCapabilities.Before) =>
        new(Type(name), FusionStageKind.Hook, capabilities);
    private static FusionDeclaredStage Wrapper(string name) => new(Type(name), FusionStageKind.Wrapper, FusionCapabilities.None);
    private static FusionDeclaredStage ItemHook(string name, FusionCapabilities capabilities) =>
        new(Type(name), FusionStageKind.Hook, capabilities, Item, Item);
    internal static FusionPlanInput Input(FusionDispatchKind kind, params FusionDeclaredStage[] stages) => new(
        "global::App.Mediator/Request", kind, Type("global::App.Request"), Type("global::App.Handler"),
        kind == FusionDispatchKind.Stream ? FusionResultKind.Item : FusionResultKind.Response,
        Item, default, new(stages), default, default, "operation-callback-v1");

    [Fact]
    public void Maximal_segments_stop_at_each_opaque_wrapper_and_keep_repeated_stage_slots()
    {
        var plan = BehaviorFusionPlanner.Create(Input(FusionDispatchKind.SendAsync,
            Hook("A"), Hook("A"), Wrapper("W"), Hook("B"), Wrapper("W"), Hook("C")));
        Assert.Empty(plan.Core.DiagnosticFacts);
        Assert.Equal(new[] { 2, 4 }, plan.Core.AdvancedWrapperBoundaries);
        Assert.Equal(new[] { new FusionSegment(0, 2), new FusionSegment(3, 1), new FusionSegment(5, 1) }, plan.Core.Segments);
        Assert.NotNull(plan.Send);
        Assert.Null(plan.Stream);
        Assert.Equal(6, plan.Send!.HandlerStageIndex);
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, plan.Send.NestedScopes.Select(s => s.StageRequestSlot));
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, plan.Send.NestedScopes.Select(s => s.NextStageIndex));
        Assert.All(plan.Send.NestedScopes, scope => Assert.True(scope.InnerStatePerContinuationInvocation));
    }

    [Fact]
    public void Guard_resolution_and_owned_cleanup_facts_are_retained_at_their_declared_positions()
    {
        var input = Input(FusionDispatchKind.SendSync, Hook("A"), Hook("B")) with
        {
            GuardSites = new(new[] {
                new FusionGuardSite(-1, FusionSitePhase.OperationEntry, FusionGuardKind.Argument),
                new FusionGuardSite(1, FusionSitePhase.StageEntry, FusionGuardKind.Cancellation) }),
            ResolutionSites = new(new[] {
                new FusionResolutionSite(0, FusionSitePhase.StageEntry, Type("A"), FusionOwnership.BorrowedService),
                new FusionResolutionSite(1, FusionSitePhase.StageEntry, Type("State"), FusionOwnership.OwnedSession, FusionCleanupKind.Sync),
                new FusionResolutionSite(2, FusionSitePhase.HandlerEntry, Type("Handler"), FusionOwnership.BorrowedService) })
        };
        var plan = BehaviorFusionPlanner.Create(input);
        Assert.Empty(plan.Core.DiagnosticFacts);
        Assert.Equal(input.GuardSites, plan.Core.Input.GuardSites);
        Assert.Equal(input.ResolutionSites, plan.Core.Input.ResolutionSites);
        Assert.All(plan.Send!.NestedScopes, scope => Assert.True(scope.EnterAfterGuardAndResolution));
        Assert.Equal(FusionOwnership.OwnedSession, plan.Core.Input.ResolutionSites[1].Ownership);
        Assert.Equal(FusionCleanupKind.None, plan.Core.Input.ResolutionSites[2].Cleanup);
    }

    [Fact]
    public void Stream_preserves_declared_source_to_consumer_order_with_each_wrapper_still_a_boundary()
    {
        var plan = BehaviorFusionPlanner.Create(Input(FusionDispatchKind.Stream,
            ItemHook("A", FusionCapabilities.Filter), ItemHook("B", FusionCapabilities.Map),
            Wrapper("W"), ItemHook("C", FusionCapabilities.Filter)));
        Assert.Empty(plan.Core.DiagnosticFacts);
        Assert.Null(plan.Send);
        var stream = Assert.IsType<StreamFusionPlan>(plan.Stream);
        Assert.Equal(new[] { 0, 1 }, stream.ItemFlowSegments[0].ItemFlowOrder);
        Assert.Equal(new[] { 3 }, stream.ItemFlowSegments[1].ItemFlowOrder);
        Assert.Equal(new[] { 2 }, plan.Core.AdvancedWrapperBoundaries);
        Assert.True(stream.Enumeration.ResolveOnFirstMove);
        Assert.Equal(1, stream.Enumeration.SourceCurrentReadsPerSuccessfulMove);
        Assert.True(stream.Enumeration.CacheMappedCurrent);
        Assert.True(stream.Enumeration.OwnedSessionLivesUntilDispose);
    }

    [Fact]
    public void Dual_filter_map_stream_hook_retains_stage_and_wrapper_facts_without_a_projection()
    {
        var input = Input(FusionDispatchKind.Stream, Wrapper("W0"),
            ItemHook("Dual", FusionCapabilities.Filter | FusionCapabilities.Map),
            Wrapper("W2"), ItemHook("Filter", FusionCapabilities.Filter)) with
        {
            GuardSites = new(new[] { new FusionGuardSite(1, FusionSitePhase.Item, FusionGuardKind.Cancellation) }),
            ResolutionSites = new(new[] {
                new FusionResolutionSite(1, FusionSitePhase.Item, Type("Dual"), FusionOwnership.BorrowedService) })
        };
        var plan = BehaviorFusionPlanner.Create(input);
        Assert.Equal(new FusionDiagnosticFact("AmbiguousStageContract", 1), Assert.Single(plan.Core.DiagnosticFacts));
        Assert.Equal(input, plan.Core.Input);
        Assert.Equal(new[] { 0, 2 }, plan.Core.AdvancedWrapperBoundaries);
        Assert.Empty(plan.Core.Segments);
        Assert.Null(plan.Send);
        Assert.Null(plan.Stream);
    }

    [Fact]
    public void Unsupported_inputs_retain_facts_without_fused_projections()
    {
        var cases = new[] {
            (Input(FusionDispatchKind.SendSync, Hook("A", FusionCapabilities.Before | FusionCapabilities.Async)), "AsyncOnSyncRoute"),
            (Input(FusionDispatchKind.SendAsync) with { Request = Type("Span", true) }, "RefLikeAcrossAsyncBoundary"),
            (Input((FusionDispatchKind)999), "UnsupportedFamily"),
            (Input(FusionDispatchKind.SendAsync) with { RouteKey = "" }, "MissingRouteOrContext"),
            (Input(FusionDispatchKind.SendSync) with { ResultKind = FusionResultKind.Item }, "ResultShapeMismatch"),
            (Input(FusionDispatchKind.SendAsync, Hook("A") with { Kind = (FusionStageKind)999 }), "UnknownStageKind"),
            (Input(FusionDispatchKind.SendAsync, Hook("A", (FusionCapabilities)128)), "UnsupportedCapability"),
            (Input(FusionDispatchKind.SendAsync, ItemHook("A", FusionCapabilities.Before)), "ItemFactsOnSendRoute"),
            (Input(FusionDispatchKind.SendAsync, Wrapper("W") with { Capabilities = FusionCapabilities.Before }), "OpaqueWrapperHasHookFacts"),
            (Input(FusionDispatchKind.Stream, Hook("A", FusionCapabilities.Filter)), "MissingItemTypes"),
            (Input(FusionDispatchKind.Stream, ItemHook("A", FusionCapabilities.Filter | FusionCapabilities.Async)), "AsyncStreamRequiresBackend"),
            (Input(FusionDispatchKind.Stream, ItemHook("A", FusionCapabilities.Map) with { OutputItem = Type("Other") }), "UnsupportedItemTransition")
        };
        foreach (var (input, expected) in cases)
        {
            var plan = BehaviorFusionPlanner.Create(input);
            Assert.Contains(plan.Core.DiagnosticFacts, fact => fact.Key == expected);
            Assert.Equal(input, plan.Core.Input);
            Assert.Empty(plan.Core.Segments);
            Assert.Null(plan.Send);
            Assert.Null(plan.Stream);
        }
    }

    [Fact]
    public void Invalid_sites_or_borrowed_resource_cleanup_cannot_receive_a_projection()
    {
        var cases = new[] {
            Input(FusionDispatchKind.SendSync) with { GuardSites = new(new[] { new FusionGuardSite(1, FusionSitePhase.HandlerEntry, FusionGuardKind.Cancellation) }) },
            Input(FusionDispatchKind.SendSync) with { ResolutionSites = new(new[] { new FusionResolutionSite(0, FusionSitePhase.HandlerEntry, Type("H"), FusionOwnership.BorrowedService, FusionCleanupKind.Sync) }) },
            Input(FusionDispatchKind.SendSync) with { ResolutionSites = new(new[] { new FusionResolutionSite(0, FusionSitePhase.HandlerEntry, Type("State"), FusionOwnership.OwnedSession, FusionCleanupKind.Async) }) }
        };
        foreach (var input in cases)
        {
            var plan = BehaviorFusionPlanner.Create(input);
            Assert.NotEmpty(plan.Core.DiagnosticFacts);
            Assert.Null(plan.Send);
            Assert.Null(plan.Stream);
        }
    }

    [Fact]
    public void Structural_equality_distinguishes_capabilities_constraints_and_guard_placement()
    {
        var input = Input(FusionDispatchKind.SendAsync, Hook("A"));
        var plan = BehaviorFusionPlanner.Create(input);
        Assert.Equal(plan, BehaviorFusionPlanner.Create(Input(FusionDispatchKind.SendAsync, Hook("A"))));
        Assert.NotEqual(plan, BehaviorFusionPlanner.Create(input with { DeclaredStages = new(new[] { Hook("A", FusionCapabilities.Success) }) }));
        Assert.NotEqual(plan, BehaviorFusionPlanner.Create(input with { GenericConstraints = new(new[] { "where T : class" }) }));
        Assert.NotEqual(plan, BehaviorFusionPlanner.Create(input with { GuardSites = new(new[] { new FusionGuardSite(0, FusionSitePhase.StageEntry, FusionGuardKind.Cancellation) }) }));
    }

    [Fact]
    public void Invalid_site_family_phase_and_index_combinations_cannot_receive_a_projection()
    {
        var send = Input(FusionDispatchKind.SendSync, Hook("A"));
        var stream = Input(FusionDispatchKind.Stream, ItemHook("A", FusionCapabilities.Filter), Wrapper("W"));
        var invalidGuards = new[] {
            (send, new FusionGuardSite(0, FusionSitePhase.Item, FusionGuardKind.Cancellation)),
            (send, new FusionGuardSite(-1, FusionSitePhase.FirstMove, FusionGuardKind.Cancellation)),
            (send, new FusionGuardSite(0, FusionSitePhase.HandlerEntry, FusionGuardKind.Cancellation)),
            (send, new FusionGuardSite(1, FusionSitePhase.StageEntry, FusionGuardKind.Cancellation)),
            (send, new FusionGuardSite(0, FusionSitePhase.StageEntry, FusionGuardKind.TokenComposition)),
            (send, new FusionGuardSite(-1, FusionSitePhase.OperationEntry, FusionGuardKind.DestinationCapacity)),
            (stream, new FusionGuardSite(0, FusionSitePhase.FirstMove, FusionGuardKind.Cancellation)),
            (stream, new FusionGuardSite(1, FusionSitePhase.Item, FusionGuardKind.Cancellation)),
            (stream, new FusionGuardSite(0, FusionSitePhase.StageEntry, FusionGuardKind.Cancellation))
        };
        foreach (var (input, site) in invalidGuards)
        {
            var plan = BehaviorFusionPlanner.Create(input with { GuardSites = new(new[] { site }) });
            Assert.Contains(plan.Core.DiagnosticFacts, fact => fact.Key == "UnsupportedGuardSiteCombination");
            Assert.Null(plan.Send);
            Assert.Null(plan.Stream);
        }
        foreach (var (input, index, phase) in new[] {
            (send, 0, FusionSitePhase.Item), (send, 0, FusionSitePhase.FirstMove),
            (send, 0, FusionSitePhase.HandlerEntry), (stream, 0, FusionSitePhase.StageEntry),
            (stream, 1, FusionSitePhase.Item), (stream, 2, FusionSitePhase.FirstMove) })
        {
            var site = new FusionResolutionSite(index, phase, Type("Service"), FusionOwnership.BorrowedService);
            var plan = BehaviorFusionPlanner.Create(input with { ResolutionSites = new(new[] { site }) });
            Assert.Contains(plan.Core.DiagnosticFacts, fact => fact.Key == "UnsupportedResolutionSiteCombination");
            Assert.Null(plan.Send);
            Assert.Null(plan.Stream);
        }
    }

    [Fact]
    public void Supported_stream_hook_wrapper_and_handler_sites_keep_their_positions()
    {
        var input = Input(FusionDispatchKind.Stream, ItemHook("A", FusionCapabilities.Filter), Wrapper("W")) with
        {
            GuardSites = new(new[] {
                new FusionGuardSite(-1, FusionSitePhase.OperationEntry, FusionGuardKind.Argument),
                new FusionGuardSite(-1, FusionSitePhase.FirstMove, FusionGuardKind.TokenComposition),
                new FusionGuardSite(-1, FusionSitePhase.FirstMove, FusionGuardKind.Cancellation),
                new FusionGuardSite(0, FusionSitePhase.Item, FusionGuardKind.Cancellation),
                new FusionGuardSite(1, FusionSitePhase.StageEntry, FusionGuardKind.Cancellation),
                new FusionGuardSite(2, FusionSitePhase.HandlerEntry, FusionGuardKind.Cancellation) }),
            ResolutionSites = new(new[] {
                new FusionResolutionSite(0, FusionSitePhase.Item, Type("A"), FusionOwnership.BorrowedService),
                new FusionResolutionSite(1, FusionSitePhase.StageEntry, Type("W"), FusionOwnership.BorrowedService),
                new FusionResolutionSite(2, FusionSitePhase.HandlerEntry, Type("H"), FusionOwnership.BorrowedService) })
        };
        var plan = BehaviorFusionPlanner.Create(input);
        Assert.Empty(plan.Core.DiagnosticFacts);
        Assert.NotNull(plan.Stream);
        Assert.Equal(input.GuardSites, plan.Core.Input.GuardSites);
        Assert.Equal(input.ResolutionSites, plan.Core.Input.ResolutionSites);
    }

    [Fact]
    public void Void_and_zero_hook_ref_like_sync_keep_a_typed_empty_plan()
    {
        var input = Input(FusionDispatchKind.SendAllSyncBranch) with { Request = Type("Span", true), ResultKind = FusionResultKind.Void, Result = null };
        var plan = BehaviorFusionPlanner.Create(input);
        Assert.Empty(plan.Core.DiagnosticFacts);
        Assert.Empty(plan.Core.Segments);
        Assert.Empty(plan.Send!.NestedScopes);
        Assert.Equal(0, plan.Send.HandlerStageIndex);
    }

    [Fact]
    public void Each_send_family_preserves_response_and_void_shapes_without_a_public_unit()
    {
        foreach (var kind in new[] { FusionDispatchKind.SendSync, FusionDispatchKind.SendAsync,
            FusionDispatchKind.SendAllSyncBranch, FusionDispatchKind.SendAllAsyncBranch })
        {
            foreach (var result in new[] { FusionResultKind.Response, FusionResultKind.Void })
            {
                var input = Input(kind, Hook("A")) with { ResultKind = result, Result = result == FusionResultKind.Void ? null : Item };
                var plan = BehaviorFusionPlanner.Create(input);
                Assert.Empty(plan.Core.DiagnosticFacts);
                Assert.NotNull(plan.Send);
                Assert.Null(plan.Stream);
                Assert.Equal(result, plan.Core.Input.ResultKind);
                Assert.Equal(input.Result, plan.Core.Input.Result);
            }
        }
    }
}
