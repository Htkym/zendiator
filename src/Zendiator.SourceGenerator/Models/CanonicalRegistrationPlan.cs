using System;
using System.Collections.Generic;
using System.Linq;

namespace Zendiator.SourceGenerator;

// Registration facts stay separate from executable members and their private names.
internal sealed record CanonicalBranchRegistration(string HandlerName, string? FactoryName);
internal sealed record CanonicalRouteRegistration(CanonicalSyncCompositionEmission Registration, BoundDispatchFamily Family,
    int HandlerOrdinal, int PipelineOrdinal, BoundStreamOpenRegistration? Open,
    string? FinalItemName, EquatableArray<RegistrationStageEmission> DeclaredStages)
{
    public EquatableArray<CanonicalBranchRegistration> BranchHandlers { get; init; }
    internal IEnumerable<string> ServiceTypes => (Open != null
        ? Open.Stages.Select(s => s.FactoryName ?? s.TypeName).Concat(Open.HandlerDefinitionNames.Count == 0
            ? new[] { Open.HandlerFactoryDefinitionName ?? Open.HandlerDefinitionName } : Open.HandlerDefinitionNames)
        : (Registration.RegistrationStages.Count == 0
            ? Registration.Hooks.Select(h => h.Factory?.FactoryName ?? h.HookName)
            : Registration.RegistrationStages.Select(s => s.FactoryName ?? s.TypeName))
            .Concat(BranchHandlers.Count == 0 ? new[] { Registration.Factory?.FactoryName ?? Registration.Handler.HandlerName } : BranchHandlers.Select(h => h.FactoryName ?? h.HandlerName)))
        .Concat(Registration.StreamBeforeRegistration is { } before ? before.ServiceTypes : default);
}

internal sealed record CanonicalRegistrationPlan(CanonicalSyncCompositionEmission Composition, EquatableArray<CanonicalRouteRegistration> Routes)
{
    internal static CanonicalRegistrationPlan SendAllSync(CanonicalSendAllSyncCompositionEmission c) =>
        new(c.Registration, new(new[] { new CanonicalRouteRegistration(c.Registration, BoundDispatchFamily.SendAllSync, 0, 0, c.Registration.OpenRegistration, null, default) { BranchHandlers = new(c.Branches.Select(b => new CanonicalBranchRegistration(b.Handler.HandlerName, b.Factory?.FactoryName))) } }));
    internal static CanonicalRegistrationPlan SendAllAsync(CanonicalSendAllAsyncCompositionEmission c) =>
        new(c.Registration, new(new[] { new CanonicalRouteRegistration(c.Registration, BoundDispatchFamily.SendAllAsync, 0, 0, c.Registration.OpenRegistration, null, default) { BranchHandlers = new(c.Branches.Select(b => new CanonicalBranchRegistration(b.Handler.HandlerName, b.Factory?.FactoryName))) } }));
    internal static CanonicalRegistrationPlan Single(CanonicalSyncCompositionEmission c, BoundDispatchFamily family,
        BoundStreamOpenRegistration? open, string? finalItemName, EquatableArray<RegistrationStageEmission> stages) =>
        new(c, new(new[] { new CanonicalRouteRegistration(c, family, 0, 0, open, finalItemName, stages) }));

    internal static CanonicalRegistrationPlan Multi(CanonicalMultiCompositionEmission c) => new(c.Routes[0].Registration,
        new(c.Routes.Select(r => new CanonicalRouteRegistration(r.Registration, r.Family!.Value, r.HandlerOrdinal, r.PipelineOrdinal,
            r.Stream != null ? r.Stream.OpenRegistration : r.Registration.OpenRegistration, r.Stream?.FinalItemName, r.Stream?.DeclaredStages ?? default)
        { BranchHandlers = r.SendAllSync != null ? new(r.SendAllSync.Branches.Select(b => new CanonicalBranchRegistration(b.Handler.HandlerName, b.Factory?.FactoryName))) : r.SendAllAsync != null ? new(r.SendAllAsync.Branches.Select(b => new CanonicalBranchRegistration(b.Handler.HandlerName, b.Factory?.FactoryName))) : default })));

    internal static bool Compatible(CanonicalMultiCompositionEmission c)
    {
        if (c.Routes.Count == 0) return false;
        var first = c.Routes[0].Registration;
        return c.Routes.All(r => r.Registration.Namespace == c.Namespace && r.Registration.MarkerName == first.MarkerName &&
            r.Registration.MicrosoftDiProviderName == first.MicrosoftDiProviderName && r.Registration.UseLocalHandlers == first.UseLocalHandlers &&
            r.Registration.ExternAliases.Equals(first.ExternAliases) && r.Registration.ExplicitHandlerTypes.Equals(first.ExplicitHandlerTypes) &&
            r.Registration.CatalogMarkerTypes.Equals(first.CatalogMarkerTypes) && r.Family != null);
    }
}
