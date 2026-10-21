using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace Zendiator.SourceGenerator;

internal sealed record CanonicalStreamPublicComposition(CanonicalSyncCompositionEmission Registration,
    string CandidateClassName)
{
    public BoundStreamGenericEmission? Generic { get; init; }
    public BoundStreamOpenRegistration? OpenRegistration { get; init; }
    public string? FinalItemName { get; init; }
    public EquatableArray<RegistrationStageEmission> DeclaredStages { get; init; }
    public EquatableArray<string> BoxedDependencies { get; init; }
}
internal sealed record CanonicalStreamPublicModel(EquatableArray<CanonicalStreamPublicComposition> Compositions);

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
                c.CompilerOwnsHandlerError || c.Pipelines.Count != 1 || c.ExternAliases.Count != 0 ||
                c.Facts.Any(f => f.Key != "IncompleteExecutionContract") ||
                catalog.Compositions.Count(other => other.Identity == c.Identity || other.Key == c.Key ||
                    other.GeneratedNamespace == c.GeneratedNamespace) != 1) continue;
            var route = c.Pipelines[0];
            var connection = connections.Routes.SingleOrDefault(r => r.CompositionIdentity == c.Identity && r.Anchor == route.Anchor);
            var canonical = connection?.Kind == CanonicalStreamConnectionKind.CanonicalCandidate ? connection.Canonical : null;
            if (canonical == null || canonical.Handler.CursorName.Length == 0 || !route.ExplicitHandlerOrder || route.Handlers.Count != 1) continue;
            var h = canonical.Handler;
            var generic = canonical.Generic;
            var open = generic == null ? null : route.StreamOpenRegistration;
            if (generic == null)
            {
                if (c.Handlers.Count != 1 || c.Handlers[0].Identity != route.Handlers[0].Type) continue;
            }
            else if (open == null || open.Stages.Count != route.Stages.Count ||
                !c.Handlers.Any(member => member.Identity == route.Handlers[0].Type) ||
                c.Handlers.Any(member => !open.HandlerMembership.Contains(member.Identity))) continue;
            var factory = canonical.HandlerFactory;
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
                RegistrationStages = new(route.Stages.Select(stage => new RegistrationStageEmission(
                    stage.StreamEmission?.HookName ?? stage.StreamWrapperEmission!.WrapperName, stage.Kind.ToString(),
                    stage.Sessions.Count == 1 ? CanonicalCapabilityEmissionNormalizer.Factory(stage.Sessions[0], false)!.FactoryName : null)))
            };
            output.Add(new(registration, canonical.ClassName)
            {
                Generic = generic,
                OpenRegistration = open,
                FinalItemName = canonical.ItemFlow?.Final.Name,
                DeclaredStages = route.StreamDeclaredStages,
                BoxedDependencies = new(Boxed(canonical).Distinct())
            });
        }
        return new(new(output));
    }

    private static IEnumerable<string> Boxed(CanonicalStreamFallbackModel model)
    {
        if (model.HandlerFactory is { } handlerFactory)
        { if (handlerFactory.FactoryIsValueType) yield return handlerFactory.FactoryName; }
        else if (model.Handler.HandlerIsValueType) yield return model.Handler.HandlerName;
        foreach (var segment in model.Segments)
        {
            if (segment.WrapperFactory is { } wrapperFactory)
            { if (wrapperFactory.FactoryIsValueType) yield return wrapperFactory.FactoryName; }
            else if (segment.Wrapper is { WrapperIsValueType: true } wrapper) yield return wrapper.WrapperName;
            foreach (var hook in segment.Hooks)
            {
                if (hook.Factory is { } factory)
                { if (factory.FactoryIsValueType) yield return factory.FactoryName; }
                else if (hook.HookIsValueType) yield return hook.HookName;
            }
        }
    }

    internal static CanonicalStreamPublicComposition? MixedRoute(CompositionSelectionCatalog catalog,
        CompositionSelectionModel composition, BoundPipelineModel route, CanonicalStreamConnectionCatalog connections, int index)
    {
        if (catalog.Compositions.Count(c => c.Identity == composition.Identity || c.Key == composition.Key ||
            c.GeneratedNamespace == composition.GeneratedNamespace) != 1 ||
            route.Family != BoundDispatchFamily.Stream || route.Handlers.Count != 1) return null;
        var connection = connections.Routes.SingleOrDefault(c => c.CompositionIdentity == composition.Identity && c.Anchor == route.Anchor);
        if (connection?.Kind != CanonicalStreamConnectionKind.CanonicalCandidate) return null;
        var projected = composition with
        {
            Pipelines = new(new[] { route }),
            Facts = new(composition.Facts.Where(f => f.Anchor == route.Anchor)),
            Handlers = new(composition.Handlers.Where(h => CanonicalMultiEmissionNormalizer.HasHandler(route, h)))
        };
        var accepted = Create(new(new(new[] { projected })), connections).Compositions.SingleOrDefault();
        return accepted == null ? null : accepted with { Registration = accepted.Registration with { Index = index } };
    }

    internal static bool PlainZeroStageShape(CanonicalStreamFallbackModel model) => model.ItemFlow == null &&
        model.HandlerFactory == null && model.Segments.Count == 1 && model.ConsumerSegment == 0 &&
        model.Segments[0] is { Id: 0, Wrapper: null, Upstream: null, WrapperFactory: null } segment && segment.Hooks.Count == 0;

    internal static CanonicalSyncInterceptorModel BindCallSites(CanonicalStreamPublicModel model,
        EquatableArray<CanonicalSyncCallSite?> current, CancellationToken token) =>
        CanonicalSyncEmissionNormalizer.BindCallSites(model.Compositions.Select(c => c.Registration.Index), current, token);
    internal static bool IsConnected(CanonicalStreamPublicComposition c, CanonicalSyncInterceptorModel sites) =>
        sites.CallSites.Any(s => s.Namespace == c.Registration.Namespace && s.MarkerName == c.Registration.MarkerName);
    internal static IEnumerable<CanonicalSourceFile> Files(CanonicalStreamPublicModel model, CanonicalSyncInterceptorModel sites)
    {
        foreach (var c in model.Compositions.Where(c => IsConnected(c, sites)))
            yield return new("Zendiator.Canonical.Stream.Public." + CanonicalSplitSourceEmitter.Id(c.Registration.Namespace) + ".g.cs", Source(c))
            { BoxedDependencies = c.BoxedDependencies };
    }
    internal static string Source(CanonicalStreamPublicComposition c)
    {
        var registration = c.Registration;
        var result = "global::" + registration.Namespace + "." + CanonicalStreamFallbackEmitter.SourceName(c.CandidateClassName) + c.Generic?.TypeParameters;
        var b = new StringBuilder();
        b.AppendLine($$"""
            // <auto-generated/>
            #nullable enable
            namespace {{registration.Namespace}}
            {
                public interface IZendiator
                {
                    {{result}} StreamAsync{{c.Generic?.TypeParameters}}({{registration.Handler.RequestName}} request,
                        global::System.Threading.CancellationToken cancellationToken = default){{c.Generic?.TypeConstraints}};
                }
                public sealed class Zendiator : IZendiator
                {
                    private readonly global::System.IServiceProvider _provider;
                    public Zendiator(global::System.IServiceProvider provider)
                    { global::System.ArgumentNullException.ThrowIfNull(provider); _provider = provider; }
                    public {{result}} StreamAsync{{c.Generic?.TypeParameters}}({{registration.Handler.RequestName}} request,
                        global::System.Threading.CancellationToken cancellationToken = default){{c.Generic?.TypeConstraints}}
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
