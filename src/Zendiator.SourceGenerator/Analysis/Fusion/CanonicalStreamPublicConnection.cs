using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace Zendiator.SourceGenerator;

internal sealed record CanonicalStreamPublicComposition(CanonicalSyncCompositionEmission Registration,
    string CandidateClassName)
{
    public BoundStreamGenericEmission? Generic { get; init; }
    public BoundStreamOpenRegistration? OpenRegistration { get; init; }
    public string? FinalItemName { get; init; }
    public EquatableArray<RegistrationStageEmission> DeclaredStages { get; init; }
}
internal sealed record CanonicalStreamPublicModel(EquatableArray<CanonicalStreamPublicComposition> Compositions);

// Public acceptance is composition-wide. Keep the already selected F1/Map/F2 backend;
// an individually valid route never authorizes skipping another route in registration.
internal static class CanonicalStreamPublicConnection
{
    internal static CanonicalStreamPublicModel Create(CompositionSelectionCatalog catalog,
        CanonicalStreamConnectionCatalog connections)
    {
        var output = new List<CanonicalStreamPublicComposition>();
        for (var i = 0; i < catalog.Compositions.Count; i++)
        {
            var c = catalog.Compositions[i];
            if (c.GeneratedNamespace == null || c.MarkerName == null || c.Errors.Count != 0 ||
                c.CompilerOwnsHandlerError || c.Pipelines.Count != 1 ||
                c.ExternAliases.Count != 0 || c.Facts.Count != 1 || c.Facts[0].Key != "IncompleteExecutionContract" ||
                catalog.Compositions.Count(other => other.Identity == c.Identity || other.Key == c.Key ||
                    other.GeneratedNamespace == c.GeneratedNamespace) != 1) continue;
            var route = c.Pipelines[0];
            var connection = connections.Routes.SingleOrDefault(r => r.CompositionIdentity == c.Identity && r.Anchor == route.Anchor);
            var prototype = connection?.Kind == CanonicalStreamConnectionKind.Candidate ? connection.Candidate : null;
            var canonical = connection?.Kind == CanonicalStreamConnectionKind.CanonicalCandidate ? connection.Canonical : null;
            var itemChange = canonical != null && (ClosedItemChangeShape(canonical) || ClosedTypedWrapperSessionShape(canonical) || OpenOwnedMapShape(canonical));
            var zeroStage = canonical != null && route.HasEmptyPipeline && route.PipelineFamily == BoundDispatchFamily.Stream && PlainZeroStageShape(canonical);
            if (prototype == null && (canonical == null || !zeroStage && !PlainSynchronousItemShape(canonical) && !PlainWrapperShape(canonical) && !AsyncItemShape(canonical) && !PlainOwnedSessionShape(canonical) && !WrapperOwnedSessionShape(canonical) && !itemChange)) continue;
            var h = prototype?.Handler ?? canonical!.Handler;
            var generic = prototype?.Generic ?? canonical?.Generic;
            var open = generic == null ? null : route.StreamOpenRegistration;
            if (route.StreamItemFlow != null && !itemChange || !route.ExplicitHandlerOrder || route.Handlers.Count != 1) continue;
            if (h.RequestIsValueType && (route.RequestShape != BoundRequestShape.ClosedNonGenericValue || prototype == null && !zeroStage || generic != null ||
                h.HandlerIsValueType || route.Handlers[0].Sessions.Count != 0 ||
                prototype != null && prototype.Hooks.Any(hook => hook.HookIsValueType || hook.Factory != null))) continue;
            if (generic == null)
            {
                if (c.Handlers.Count != 1 || c.Handlers[0].Identity != route.Handlers[0].Type) continue;
            }
            else if (prototype != null || open == null || open.Stages.Count != route.Stages.Count ||
                !c.Handlers.Any(member => member.Identity == route.Handlers[0].Type) ||
                c.Handlers.Any(member => !open.HandlerMembership.Contains(member.Identity))) continue;
            // Each selected projection proves its exact schedule; public dispatch never chooses another backend.
            var factory = canonical?.HandlerFactory;
            var registration = new CanonicalSyncCompositionEmission(i, c.GeneratedNamespace, c.MarkerName, c.UseLocalHandlers,
                new(h.RequestName, h.HandlerName, h.ContractName, h.MethodName, h.ItemName)
                { RequestIsValueType = h.RequestIsValueType, HandlerIsValueType = h.HandlerIsValueType },
                factory == null ? null : new(factory.FactoryName, factory.ContractName, factory.MethodName, factory.CleanupContractName)
                { FactoryIsValueType = factory.FactoryIsValueType, IsSourceDeclaration = factory.IsSourceDeclaration },
                route.HasEmptyPipeline, route.PipelineFamily, c.MicrosoftDiProviderName ?? "")
            {
                ExplicitHandlerTypes = c.ExplicitHandlerTypes,
                CatalogMarkerTypes = c.CatalogMarkerTypes,
                ExternAliases = c.ExternAliases,
                // Preserve declared order across segments; a wrapper is a registration stage too.
                RegistrationStages = new(route.Stages.Select(stage => new RegistrationStageEmission(
                    stage.StreamEmission?.HookName ?? stage.StreamWrapperEmission!.WrapperName, stage.Kind.ToString(),
                    stage.Sessions.Count == 1 ? CanonicalCapabilityEmissionNormalizer.Factory(stage.Sessions[0], false)!.FactoryName : null)))
            };
            output.Add(new(registration, prototype?.ClassName ?? canonical!.ClassName)
            { Generic = generic, OpenRegistration = open, FinalItemName = canonical?.ItemFlow?.Final.Name, DeclaredStages = route.StreamDeclaredStages });
        }
        return new(new(output));
    }

    internal static CanonicalStreamPublicComposition? MixedRoute(CompositionSelectionCatalog catalog,
        CompositionSelectionModel composition, BoundPipelineModel route, CanonicalStreamConnectionCatalog connections, int index)
    {
        if (catalog.Compositions.Count(c => c.Identity == composition.Identity || c.Key == composition.Key ||
            c.GeneratedNamespace == composition.GeneratedNamespace) != 1 ||
            route.Family != BoundDispatchFamily.Stream || route.Handlers.Count != 1) return null;
        var connection = connections.Routes.SingleOrDefault(c => c.CompositionIdentity == composition.Identity && c.Anchor == route.Anchor);
        if (connection is not { Kind: CanonicalStreamConnectionKind.Candidate or CanonicalStreamConnectionKind.CanonicalCandidate } ||
            route.RequestShape is not (BoundRequestShape.ClosedReference or BoundRequestShape.ClosedNonGenericValue) ||
            route.StreamGeneric != null || route.StreamOpenRegistration != null) return null;
        var projected = composition with
        {
            Pipelines = new(new[] { route }),
            Facts = new(composition.Facts.Where(f => f.Anchor == route.Anchor)),
            Handlers = new(composition.Handlers.Where(h => h.Identity == route.Handlers[0].Type))
        };
        var accepted = Create(new(new(new[] { projected })), connections).Compositions.SingleOrDefault();
        return accepted == null ? null : accepted with { Registration = accepted.Registration with { Index = index } };
    }

    private static bool PlainZeroStageShape(CanonicalStreamFallbackModel model)
    {
        if (model.ItemFlow != null || model.HandlerFactory != null || model.Handler.HandlerIsValueType ||
            model.Segments.Count != 1 || model.ConsumerSegment != 0) return false;
        var segment = model.Segments[0];
        return segment.Id == 0 && segment.Wrapper == null && segment.Upstream == null &&
            segment.WrapperFactory == null && segment.Hooks.Count == 0;
    }

    private static bool PlainSynchronousItemShape(CanonicalStreamFallbackModel model)
    {
        if (model.Generic != null || model.ItemFlow != null || model.HandlerFactory != null || model.Handler.HandlerIsValueType ||
            model.Segments.Count != 1 || model.ConsumerSegment != 0) return false;
        var segment = model.Segments[0];
        return segment.Id == 0 && segment.Wrapper == null && segment.Upstream == null && segment.WrapperFactory == null &&
            segment.Hooks.Count is 1 or 2 && segment.Hooks.All(h => h.Factory == null && !h.HookIsValueType &&
                h.Completion == BoundCompletionKind.Synchronous && h.Phase is BoundCallbackPhase.Filter or BoundCallbackPhase.Map);
    }

    private static bool PlainWrapperShape(CanonicalStreamFallbackModel model)
    {
        if (model.Generic != null || model.ItemFlow != null || model.HandlerFactory != null || model.Handler.HandlerIsValueType ||
            model.Segments.Count != 2 || model.ConsumerSegment != 1) return false;
        var inner = model.Segments[0]; var outer = model.Segments[1];
        return inner.Id == 0 && inner.Upstream == null && inner.Wrapper == null && inner.WrapperFactory == null && inner.Hooks.Count == 0 &&
            outer.Id == 1 && outer.Upstream == 0 && outer.Wrapper is { WrapperIsValueType: false, Completion: BoundCompletionKind.Enumerable } &&
            outer.WrapperFactory == null && outer.Hooks.Count == 0;
    }

    private static bool AsyncItemShape(CanonicalStreamFallbackModel model)
    {
        if (model.ItemFlow != null || model.HandlerFactory != null || model.Handler.HandlerIsValueType ||
            model.Generic == null && model.Handler.ItemName is not ("int" or "global::System.Int32") ||
            model.Segments.Count != 1 || model.ConsumerSegment != 0) return false;
        var segment = model.Segments[0];
        return segment.Wrapper == null && segment.Upstream == null && segment.WrapperFactory == null &&
            segment.Hooks.Count == 3 && segment.Hooks.All(h => h.Factory == null && !h.HookIsValueType) &&
            segment.Hooks.Select(h => h.Phase).SequenceEqual(new[] { BoundCallbackPhase.Filter, BoundCallbackPhase.Map, BoundCallbackPhase.Filter }) &&
            segment.Hooks.Select(h => h.Completion).SequenceEqual(new[] { BoundCompletionKind.ValueTask, BoundCompletionKind.ValueTask, BoundCompletionKind.Synchronous });
    }

    // Factory acquisition/cleanup is still entirely emitted by the existing canonical cursor.
    private static bool PlainOwnedSessionShape(CanonicalStreamFallbackModel model)
    {
        if (model.Generic != null || model.ItemFlow != null || model.Handler.HandlerIsValueType ||
            model.Handler.ItemName is not ("int" or "global::System.Int32") || model.Segments.Count != 1 || model.ConsumerSegment != 0 ||
            model.HandlerFactory is not { AwaitAcquire: false, CleanupContractName: not null, FactoryIsValueType: false }) return false;
        var segment = model.Segments[0];
        return segment.Wrapper == null && segment.Upstream == null && segment.WrapperFactory == null && segment.Hooks.Count == 3 &&
            segment.Hooks.All(h => !h.HookIsValueType && h.Phase == BoundCallbackPhase.Map && h.Completion == BoundCompletionKind.Synchronous) &&
            segment.Hooks[0].Factory is { AwaitAcquire: true, CleanupContractName: not null, FactoryIsValueType: false } &&
            segment.Hooks[1].Factory == null &&
            segment.Hooks[2].Factory is { AwaitAcquire: false, CleanupContractName: not null, FactoryIsValueType: false };
    }

    private static bool WrapperOwnedSessionShape(CanonicalStreamFallbackModel model)
    {
        if (model.Generic != null || model.ItemFlow != null || model.Handler.HandlerIsValueType ||
            model.Handler.ItemName is not ("int" or "global::System.Int32") || model.Segments.Count != 2 || model.ConsumerSegment != 1 ||
            model.HandlerFactory is not { AwaitAcquire: false, CleanupContractName: not null, FactoryIsValueType: false }) return false;
        var inner = model.Segments[0]; var outer = model.Segments[1];
        return inner.Id == 0 && inner.Upstream == null && inner.Wrapper == null && inner.WrapperFactory == null &&
            outer.Id == 1 && outer.Upstream == 0 && outer.Wrapper is { WrapperIsValueType: false, Completion: BoundCompletionKind.Enumerable } &&
            outer.WrapperFactory is { AwaitAcquire: false, CleanupContractName: not null, FactoryIsValueType: false } &&
            new[] { inner, outer }.All(segment => segment.Hooks.Count == 1 && segment.Hooks[0] is
            {
                Phase: BoundCallbackPhase.Map, Completion: BoundCompletionKind.Synchronous, HookIsValueType: false,
                Factory: { AwaitAcquire: false, CleanupContractName: not null, FactoryIsValueType: false }
            });
    }

    private static bool ClosedItemChangeShape(CanonicalStreamFallbackModel model)
    {
        if (model.Generic != null || model.HandlerFactory != null || model.Handler.HandlerIsValueType || model.ItemFlow is not { } flow ||
            flow.Source.Name != "int" || flow.Final.Name != "long" || flow.Stages.Count != 2 || model.Segments.Count != 1 || model.ConsumerSegment != 0) return false;
        var segment = model.Segments[0];
        return segment.Wrapper == null && segment.Upstream == null && segment.WrapperFactory == null && segment.Hooks.Count == 2 &&
            segment.Hooks.All(h => !h.HookIsValueType && h.Factory == null && h.Phase == BoundCallbackPhase.Map) &&
            segment.Hooks[0].Completion == BoundCompletionKind.Synchronous && segment.Hooks[1].Completion == BoundCompletionKind.ValueTask &&
            flow.Stages[0].Input.Identity == flow.Source.Identity && flow.Stages[0].Output.Name == "string?" &&
            flow.Stages[1].Input.Identity == flow.Stages[0].Output.Identity && flow.Stages[1].Output.Identity == flow.Final.Identity;
    }

    // The existing typed cursor owns sessions; the registrar owns only their borrowed DI factories.
    private static bool ClosedTypedWrapperSessionShape(CanonicalStreamFallbackModel model)
    {
        if (model.Generic != null || model.Handler.HandlerIsValueType || model.ItemFlow is not { } flow ||
            flow.Source.Name != "int" || !flow.Final.IsValueType || flow.Source.Identity == flow.Final.Identity ||
            flow.Stages.Count != 3 || model.Segments.Count != 2 || model.ConsumerSegment != 1 ||
            model.HandlerFactory is not { AwaitAcquire: false, CleanupContractName: not null, FactoryIsValueType: false }) return false;
        var inner = model.Segments[0]; var outer = model.Segments[1];
        return inner.Id == 0 && inner.Upstream == null && inner.Wrapper == null && inner.WrapperFactory == null &&
            inner.SourceItemName == flow.Source.Name && inner.OutputItemName == flow.Final.Name &&
            outer.Id == 1 && outer.Upstream == 0 && outer.SourceItemName == flow.Final.Name && outer.OutputItemName == flow.Final.Name &&
            outer.Wrapper is { WrapperIsValueType: false, Completion: BoundCompletionKind.Enumerable } &&
            outer.WrapperFactory is { AwaitAcquire: false, CleanupContractName: not null, FactoryIsValueType: false } &&
            new[] { inner, outer }.All(segment => segment.Hooks.Count == 1 && segment.Hooks[0] is
            {
                Phase: BoundCallbackPhase.Map, HookIsValueType: false,
                Factory: { AwaitAcquire: false, CleanupContractName: not null, FactoryIsValueType: false }
            }) &&
            inner.Hooks[0].Completion == BoundCompletionKind.Synchronous && outer.Hooks[0].Completion == BoundCompletionKind.ValueTask &&
            flow.Stages[0].Input.Identity == flow.Source.Identity &&
            flow.Stages.All(stage => stage.Output.Identity == flow.Final.Identity) &&
            flow.Stages.Skip(1).All(stage => stage.Input.Identity == flow.Final.Identity);
    }

    private static bool OpenOwnedMapShape(CanonicalStreamFallbackModel model)
    {
        if (model.Generic == null || model.Handler.HandlerIsValueType || model.ItemFlow is not { } flow ||
            flow.Stages.Count != 1 || flow.Source.Identity == flow.Final.Identity || model.Segments.Count != 1 || model.ConsumerSegment != 0 ||
            model.HandlerFactory is not { AwaitAcquire: false, CleanupContractName: not null, FactoryIsValueType: false }) return false;
        var segment = model.Segments[0];
        return segment.Id == 0 && segment.Upstream == null && segment.Wrapper == null && segment.WrapperFactory == null &&
            segment.SourceItemName == flow.Source.Name && segment.OutputItemName == flow.Final.Name &&
            segment.Hooks.Count == 1 && segment.Hooks[0] is
            {
                Phase: BoundCallbackPhase.Map, Completion: BoundCompletionKind.Synchronous, HookIsValueType: false,
                Factory: { AwaitAcquire: false, CleanupContractName: not null, FactoryIsValueType: false }
            } &&
            flow.Stages[0].Input.Identity == flow.Source.Identity && flow.Stages[0].Output.Identity == flow.Final.Identity;
    }

    internal static CanonicalSyncInterceptorModel BindCallSites(CanonicalStreamPublicModel model,
        ImmutableArray<CompositionSelectionDiscovery.Binding> current, Compilation compilation, CancellationToken token) =>
        CanonicalSyncEmissionNormalizer.BindCallSites(new(new(model.Compositions.Select(c => c.Registration))),
            current, compilation, token);

    internal static bool IsConnected(CanonicalStreamPublicComposition c, CanonicalSyncInterceptorModel sites) =>
        sites.CallSites.Any(s => s.Namespace == c.Registration.Namespace && s.MarkerName == c.Registration.MarkerName);

    internal static IEnumerable<CanonicalSourceFile> Files(CanonicalStreamPublicModel model, CanonicalSyncInterceptorModel sites)
    {
        foreach (var c in model.Compositions.Where(c => IsConnected(c, sites)))
        {
            yield return new("Zendiator.Canonical.Stream.Public." + CanonicalSplitSourceEmitter.Id(c.Registration.Namespace) + ".g.cs", Source(c));
        }
    }

    internal static string Source(CanonicalStreamPublicComposition c)
    {
        var registration = c.Registration;
        var itemName = c.FinalItemName ?? registration.Handler.ResultName;
        var b = new StringBuilder();
        b.AppendLine($$"""
                // <auto-generated/>
                #nullable enable
                namespace {{registration.Namespace}}
                {
                    public interface IZendiator
                    {
                        global::System.Collections.Generic.IAsyncEnumerable<{{itemName}}> StreamAsync{{c.Generic?.TypeParameters}}(
                            {{registration.Handler.RequestName}} request, global::System.Threading.CancellationToken cancellationToken = default){{c.Generic?.TypeConstraints}};
                    }
                    public sealed class Zendiator : IZendiator
                    {
                        private readonly global::System.IServiceProvider _provider;
                        public Zendiator(global::System.IServiceProvider provider)
                        {
                            global::System.ArgumentNullException.ThrowIfNull(provider);
                            _provider = provider;
                        }
                        public global::System.Collections.Generic.IAsyncEnumerable<{{itemName}}> StreamAsync{{c.Generic?.TypeParameters}}(
                            {{registration.Handler.RequestName}} request, global::System.Threading.CancellationToken cancellationToken = default){{c.Generic?.TypeConstraints}}
                        {
                            {{CanonicalCapabilitySourceEmitter.RequestGuard(registration.Handler.RequestIsValueType, "global::System.ArgumentNullException.ThrowIfNull(request);")}}
                            return {{c.CandidateClassName}}{{c.Generic?.TypeParameters}}.Create(_provider, request, cancellationToken);
                        }
                    }
                """);
        CanonicalSyncSourceEmitter.EmitRegistrar(b, registration, BoundDispatchFamily.Stream, c.OpenRegistration, c.FinalItemName, c.DeclaredStages);
        b.AppendLine("}");
        return b.ToString();
    }
}
