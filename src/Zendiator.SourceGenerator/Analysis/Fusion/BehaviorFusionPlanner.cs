using System;
using System.Collections.Generic;
using System.Linq;

namespace Zendiator.SourceGenerator;

/// <summary>
/// Plans explicit hooks between opaque wrapper boundaries. Semantic analysis owns type binding,
/// source locations and exact guard/resolution sites; this planner must not move those sites.
/// </summary>
internal static class BehaviorFusionPlanner
{
    internal static BehaviorFusionPlan Create(FusionPlanInput input)
    {
        var facts = Validate(input);
        var wrappers = new EquatableArray<int>(Enumerable.Range(0, input.DeclaredStages.Count)
            .Where(i => input.DeclaredStages[i].Kind == FusionStageKind.Wrapper));
        var segments = new List<FusionSegment>();
        if (facts.Count == 0)
        {
            var start = -1;
            for (var i = 0; i <= input.DeclaredStages.Count; i++)
            {
                if (i < input.DeclaredStages.Count && input.DeclaredStages[i].Kind == FusionStageKind.Hook)
                {
                    if (start < 0) start = i;
                }
                else if (start >= 0)
                {
                    segments.Add(new FusionSegment(start, i - start));
                    start = -1;
                }
            }
        }
        var core = new BehaviorFusionPlanCore(input,
            new(input.DeclaredStages.Select(s => s.Capabilities)), wrappers, new(segments), new(facts));
        if (facts.Count != 0) return new(core, null, null);
        if (input.DispatchKind != FusionDispatchKind.Stream)
        {
            var scopes = Enumerable.Range(0, input.DeclaredStages.Count).Select(i =>
                new SendNestedScope(i, i + 1, input.DeclaredStages[i].Kind, i, i,
                    EnterAfterGuardAndResolution: true, InnerStatePerContinuationInvocation: true));
            return new(core, new(core, new(scopes), input.DeclaredStages.Count), null);
        }
        // C01 defines the Stream list in source-to-consumer item order.
        var flow = segments.Select(s => new StreamItemSegment(s,
            new(Enumerable.Range(s.Start, s.Length))));
        var transitions = Enumerable.Range(0, input.DeclaredStages.Count)
            .Where(i => input.DeclaredStages[i].Kind == FusionStageKind.Hook)
            .Select(i => new StreamItemTransition(i, input.DeclaredStages[i].InputItem!, input.DeclaredStages[i].OutputItem!));
        return new(core, null, new(core, new(flow), new(transitions),
            new(ResolveOnFirstMove: true, SourceCurrentReadsPerSuccessfulMove: 1,
                CacheMappedCurrent: true, OwnedSessionLivesUntilDispose: true)));
    }

    private static List<FusionDiagnosticFact> Validate(FusionPlanInput input)
    {
        var facts = new List<FusionDiagnosticFact>();
        void Reject(string key, int stage = -1) => facts.Add(new(key, stage));
        var stream = input.DispatchKind == FusionDispatchKind.Stream;
        var sync = input.DispatchKind is FusionDispatchKind.SendSync or FusionDispatchKind.SendAllSyncBranch;
        if (input.ContractOrigin == FusionContractOrigin.Unspecified) Reject("MissingContractOrigin");
        if (!Enum.IsDefined(typeof(FusionContractOrigin), input.ContractOrigin)) Reject("UnknownContractOrigin");
        if (input.ContractOrigin == FusionContractOrigin.NewCanonical)
            Reject("CanonicalExecutionContractUnavailable");
        if (input.ContractOrigin == FusionContractOrigin.LegacyEmissionBoundary)
        {
            Reject("OpaqueLegacyContractBoundary");
            Reject("CanonicalGuardResolutionScheduleUnavailable");
            if (!sync) Reject("CanonicalAwaitScheduleUnavailable");
            if (stream)
            {
                Reject("CanonicalSourceMoveScheduleUnavailable");
                Reject("CanonicalDisposeOwnershipUnavailable");
            }
        }
        if (!Enum.IsDefined(typeof(FusionDispatchKind), input.DispatchKind)) Reject("UnsupportedFamily");
        if (string.IsNullOrEmpty(input.RouteKey) || string.IsNullOrEmpty(input.ContextModelId)) Reject("MissingRouteOrContext");
        if ((stream && (input.ResultKind != FusionResultKind.Item || input.Result is null)) ||
            (!stream && (input.ResultKind == FusionResultKind.Item ||
                (input.ResultKind == FusionResultKind.Response && input.Result is null) ||
                (input.ResultKind == FusionResultKind.Void && input.Result is not null))) ||
            !Enum.IsDefined(typeof(FusionResultKind), input.ResultKind)) Reject("ResultShapeMismatch");
        if (!sync && (input.Request.IsRefLikeType || input.Result?.IsRefLikeType == true)) Reject("RefLikeAcrossAsyncBoundary");
        var allowed = stream
            ? FusionCapabilities.Filter | FusionCapabilities.Map | FusionCapabilities.Async
            : FusionCapabilities.Before | FusionCapabilities.Success | FusionCapabilities.Exception | FusionCapabilities.Finally | FusionCapabilities.Async;
        for (var i = 0; i < input.DeclaredStages.Count; i++)
        {
            var stage = input.DeclaredStages[i];
            if (!Enum.IsDefined(typeof(FusionStageKind), stage.Kind)) Reject("UnknownStageKind", i);
            if (stage.Kind == FusionStageKind.Wrapper)
            {
                if (stage.Capabilities != FusionCapabilities.None || stage.InputItem is not null || stage.OutputItem is not null)
                    Reject("OpaqueWrapperHasHookFacts", i);
                continue;
            }
            if ((stage.Capabilities & ~allowed) != 0) Reject("UnsupportedCapability", i);
            if (sync && (stage.Capabilities & FusionCapabilities.Async) != 0) Reject("AsyncOnSyncRoute", i);
            if (stream)
            {
                if ((stage.Capabilities & (FusionCapabilities.Filter | FusionCapabilities.Map)) ==
                    (FusionCapabilities.Filter | FusionCapabilities.Map)) Reject("AmbiguousStageContract", i);
                if (stage.InputItem is null || stage.OutputItem is null) Reject("MissingItemTypes", i);
                // Type-changing/async Stream is represented in the input, but awaits C10/C11 support.
                else if (stage.InputItem != input.Result || stage.OutputItem != input.Result) Reject("UnsupportedItemTransition", i);
                if ((stage.Capabilities & FusionCapabilities.Async) != 0) Reject("AsyncStreamRequiresBackend", i);
            }
            else if (stage.InputItem is not null || stage.OutputItem is not null) Reject("ItemFactsOnSendRoute", i);
        }
        foreach (var site in input.GuardSites)
        {
            if (site.StageIndex < -1 || site.StageIndex > input.DeclaredStages.Count ||
                !Enum.IsDefined(typeof(FusionSitePhase), site.Phase) || !Enum.IsDefined(typeof(FusionGuardKind), site.Kind))
                Reject("InvalidGuardSite", site.StageIndex);
            else if (!SupportsGuardSite(input, site)) Reject("UnsupportedGuardSiteCombination", site.StageIndex);
        }
        foreach (var site in input.ResolutionSites)
        {
            if (site.StageIndex < 0 || site.StageIndex > input.DeclaredStages.Count ||
                !Enum.IsDefined(typeof(FusionSitePhase), site.Phase) ||
                !Enum.IsDefined(typeof(FusionOwnership), site.Ownership) || !Enum.IsDefined(typeof(FusionCleanupKind), site.Cleanup))
                Reject("InvalidResolutionSite", site.StageIndex);
            else if (!SupportsExecutionSite(input, site.StageIndex, site.Phase))
                Reject("UnsupportedResolutionSiteCombination", site.StageIndex);
            if ((site.Ownership == FusionOwnership.OwnedSession) != (site.Cleanup != FusionCleanupKind.None))
                Reject("OwnershipCleanupMismatch", site.StageIndex);
            if (sync && site.Cleanup == FusionCleanupKind.Async) Reject("AsyncCleanupOnSyncRoute", site.StageIndex);
        }
        return facts;
    }

    private static bool SupportsGuardSite(FusionPlanInput input, FusionGuardSite site)
    {
        if (site.Phase == FusionSitePhase.OperationEntry)
            return site.StageIndex == -1 && (site.Kind == FusionGuardKind.Argument ||
                (site.Kind == FusionGuardKind.DestinationCapacity && input.DispatchKind == FusionDispatchKind.SendAllSyncBranch &&
                    input.ResultKind == FusionResultKind.Response));
        if (site.Phase == FusionSitePhase.FirstMove)
            return input.DispatchKind == FusionDispatchKind.Stream && site.StageIndex == -1 &&
                site.Kind is FusionGuardKind.Cancellation or FusionGuardKind.TokenComposition;
        return (site.Kind is FusionGuardKind.Argument or FusionGuardKind.Cancellation) &&
            SupportsExecutionSite(input, site.StageIndex, site.Phase);
    }

    private static bool SupportsExecutionSite(FusionPlanInput input, int index, FusionSitePhase phase)
    {
        if (phase == FusionSitePhase.HandlerEntry) return index == input.DeclaredStages.Count;
        if (index < 0 || index >= input.DeclaredStages.Count) return false;
        if (input.DispatchKind == FusionDispatchKind.Stream)
            return (phase == FusionSitePhase.Item && input.DeclaredStages[index].Kind == FusionStageKind.Hook) ||
                (phase == FusionSitePhase.StageEntry && input.DeclaredStages[index].Kind == FusionStageKind.Wrapper);
        return phase == FusionSitePhase.StageEntry;
    }
}
