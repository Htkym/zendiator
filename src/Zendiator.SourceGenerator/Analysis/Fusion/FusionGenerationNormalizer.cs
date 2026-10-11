using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Zendiator.SourceGenerator;

/// <summary>
/// Bridges the existing bound catalog to the common IR. The current catalog has legacy
/// contracts only; canonical hook/guard/lifetime metadata must come from the new frontend.
/// </summary>
internal static class FusionGenerationNormalizer
{
    internal static FusionGenerationModel Create(GenerationModel emission)
    {
        var plans = new List<BehaviorFusionPlan>();
        AddRequests(emission, emission.Routes.Requests, false, plans);
        AddRequests(emission, emission.Routes.Synchronous, true, plans);
        for (var i = 0; i < emission.Routes.Streams.Count; i++)
        {
            var route = emission.Routes.Streams[i];
            plans.Add(Legacy(emission, FusionDispatchKind.Stream, i, -1, route.Request,
                route.Response, route.Handler, false, route.Behaviors, route.MethodConstraints));
        }
        // Notification has no fusion family in G02; its existing typed route stays in Emission.
        return new(emission, new(plans));
    }

    private static void AddRequests(GenerationModel emission, RequestRoutes routes, bool sync,
        List<BehaviorFusionPlan> plans)
    {
        var singleKind = sync ? FusionDispatchKind.SendSync : FusionDispatchKind.SendAsync;
        var branchKind = sync ? FusionDispatchKind.SendAllSyncBranch : FusionDispatchKind.SendAllAsyncBranch;
        for (var i = 0; i < routes.Single.Count; i++)
        {
            var route = routes.Single[i];
            plans.Add(Legacy(emission, singleKind, i, -1, route.Request, route.Response,
                route.Handler, route.IsVoid, route.Behaviors, route.MethodConstraints));
        }
        for (var i = 0; i < routes.Multiple.Count; i++)
        {
            var route = routes.Multiple[i];
            for (var branch = 0; branch < route.Branches.Count; branch++)
            {
                var node = route.Branches[branch];
                plans.Add(Legacy(emission, branchKind, i, branch, route.Request, route.Response,
                    node.Handler, route.IsVoid, node.Behaviors, route.MethodConstraints));
            }
        }
    }

    private static BehaviorFusionPlan Legacy(GenerationModel emission, FusionDispatchKind kind,
        int routeIndex, int branchIndex, EmissionType request, EmissionType response, EmissionType handler,
        bool isVoid, EquatableArray<EmissionType> behaviors, EquatableArray<string> constraints)
    {
        var key = emission.Target.MediatorName + "/" + kind + "/" + routeIndex.ToString(CultureInfo.InvariantCulture)
            + "/" + branchIndex.ToString(CultureInfo.InvariantCulture);
        var shape = kind == FusionDispatchKind.Stream ? FusionResultKind.Item
            : isVoid ? FusionResultKind.Void : FusionResultKind.Response;
        var input = new FusionPlanInput(key, kind, request, handler, shape, isVoid ? null : response,
            constraints, new(behaviors.Select(type =>
                new FusionDeclaredStage(type, FusionStageKind.Wrapper, FusionCapabilities.None))),
            default, default, "legacy-emission-v0.4")
        {
            ContractOrigin = FusionContractOrigin.LegacyEmissionBoundary
        };
        return BehaviorFusionPlanner.Create(input);
    }
}
