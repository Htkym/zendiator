using Microsoft.CodeAnalysis;
using Zendiator.SourceGenerator;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private const string ListStages = """
        public sealed class Mixed<T> : ISendRequestTransform<Ping>, IAsyncSendFinallyObserver<Ping> where T : class {
            public Ping Before(Ping r, CancellationToken c) => r;
            public ValueTask FinallyAsync(Ping r, SendExitInfo e, CancellationToken c) => default;
        }
        public sealed class Last : ISendExceptionObserver<Ping> {
            public void OnException(Ping r, Exception e, CancellationToken c) {}
        }
        public sealed class Opaque : IAsyncSendWrapper<Ping,int> {
            public ValueTask<int> InvokeAsync<N>(Ping r, N n, CancellationToken c) where N : IAsyncSendContinuation<Ping,int> => n.InvokeAsync(r,c);
        }
        """;
    private const string OtherOpaqueListStage = """
        public sealed class OtherOpaque : IAsyncSendWrapper<Ping,int> {
            public ValueTask<int> InvokeAsync<N>(Ping r,N n,CancellationToken c) where N : IAsyncSendContinuation<Ping,int> => n.InvokeAsync(r,c);
        }
        """;
    private static string ListFixture(string body, bool defaultComposition = false) => Head + Request + Handler +
        string.Concat(new[] { "A", "B", "C" }.Select(n => Handler.Replace("class Handler", "class " + n)
            .Replace("public sealed class " + n, "[ZendiatorHandler] public sealed class " + n))) + ListStages +
        SelectedComposition.Replace(defaultComposition ? "AddZendiator<Reads>" : "__unused__", "AddZendiator")
            .Replace("c.AddHandler<Handler>()", "{ " + body + " }");
    private static CompositionSelectionModel Selection(GeneratorDriver driver) => Assert.Single(Assert.IsType<CompositionSelectionCatalog>(
        Assert.Single(driver.GetRunResult().Results.Single().TrackedSteps["CompositionSelectionModel"].SelectMany(s => s.Outputs)).Value).Compositions);
    private static void SelectionReason(GeneratorDriver driver, IncrementalStepRunReason expected) =>
        Assert.All(driver.GetRunResult().Results.Single().TrackedSteps["CompositionSelectionModel"].SelectMany(s => s.Outputs), o => Assert.Equal(expected, o.Reason));
    private static GeneratorDriver BindLists(string source, params string[] expectedIds)
    {
        var driver = Driver().RunGeneratorsAndUpdateCompilation(Compilation(source), out var output, out var diagnostics);
        Assert.Equal(expectedIds.OrderBy(x => x), diagnostics.Select(d => d.Id).OrderBy(x => x));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error && !d.Id.StartsWith("ZEN", StringComparison.Ordinal)));
        if (expectedIds.Length != 0) AssertNoCanonicalOutput(driver.GetRunResult().Results.Single().GeneratedSources);
        return driver;
    }
    private static GeneratorDriver BindAcceptedSendLists(string source, int routes = 1, bool legacy = true)
    {
        var driver = BindLists(source);
        var selection = Selection(driver);
        Assert.Equal(routes, selection.Pipelines.Count);
        var generated = driver.GetRunResult().Results.Single().GeneratedSources;
        AssertCanonicalOutputRoles(generated, routes: routes, legacy: legacy);
        foreach (var route in selection.Pipelines)
        {
            Assert.True(route.Family is BoundDispatchFamily.SendSync or BoundDispatchFamily.SendAsync or BoundDispatchFamily.SendAllSync or BoundDispatchFamily.SendAllAsync);
            var body = string.Concat(generated.Where(IsCanonicalRoute).Select(s => s.SourceText.ToString()));
            var method = route.Family switch
            {
                BoundDispatchFamily.SendSync => "Send(",
                BoundDispatchFamily.SendAsync => "SendAsync(",
                BoundDispatchFamily.SendAllSync => "SendAll(",
                BoundDispatchFamily.SendAllAsync => "SendAllAsync(",
                _ => throw new InvalidOperationException()
            };
            Assert.Contains(method + route.RequestName + " request", body);
        }
        return driver;
    }
    private static void AssertAcceptedSendEmission(GeneratorDriver driver, IEnumerable<Diagnostic> diagnostics,
        BoundDispatchFamily family, bool isVoid)
    {
        Assert.Empty(diagnostics);
        var route = Assert.Single(Selection(driver).Pipelines);
        Assert.Equal(family, route.Family);
        Assert.Equal(isVoid ? FusionResultKind.Void : FusionResultKind.Response, route.ResultKind);
        Assert.Single(route.Handlers);
        Assert.All(route.Stages, s => Assert.NotEmpty(s.Callbacks));
        var files = driver.GetRunResult().Results.Single().GeneratedSources;
        AssertCanonicalOutputRoles(files);
        Assert.Contains((family == BoundDispatchFamily.SendSync ? "Send(" : "SendAsync(") + route.RequestName + " request", CanonicalRouteSource(files));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Ordered_lists_preserve_branches_and_phases_with_named_emission_and_default_rejection(bool defaultComposition)
    {
        const string body = "c.Pipeline<Ping>(Stage.Hook<Mixed<string>>(), Stage.Wrapper(typeof(Opaque)), Stage.Hook<Last>()); " +
            "c.Handlers<Ping>(DispatchFamily.SendAllAsync, typeof(B), typeof(A));";
        var source = ListFixture(body, defaultComposition);
        var driver = defaultComposition ? BindLists(source, "ZEN0037") : BindAcceptedSendLists(source);
        var selection = Selection(driver);
        Assert.Equal(defaultComposition ? 3 : 2, selection.Handlers.Count);
        Assert.DoesNotContain(selection.Handlers.Where(_ => !defaultComposition), h => h.Definition.Name == "global::App.C");
        var route = Assert.Single(selection.Pipelines);
        Assert.Equal(BoundDispatchFamily.SendAllAsync, route.Family);
        Assert.Equal(FusionContractOrigin.NewCanonical, route.ContractOrigin);
        Assert.Equal(new[] { "global::App.B", "global::App.A" }, route.Handlers.Select(h => h.Name));
        Assert.True(route.ExplicitHandlerOrder);
        Assert.Equal(new[] { "global::App.Mixed<string>", "global::App.Opaque", "global::App.Last" }, route.Stages.Select(s => s.Name));
        var mixed = route.Stages[0];
        Assert.Equal(new[] { BoundCallbackPhase.Before, BoundCallbackPhase.Finally }, mixed.Callbacks.Select(c => c.Phase));
        Assert.Equal(new[] { BoundCompletionKind.Synchronous, BoundCompletionKind.ValueTask }, mixed.Callbacks.Select(c => c.Completion));
        Assert.False(mixed.Callbacks[0].RequiresSingleConsumption);
        Assert.True(mixed.Callbacks[1].RequiresSingleConsumption);
        Assert.Equal(3, mixed.Callbacks[1].Parameters.Count);
        Assert.Equal(FusionStageKind.Wrapper, route.Stages[1].Kind);
        Assert.Equal(BoundCallbackPhase.Wrapper, Assert.Single(route.Stages[1].Callbacks).Phase);
        Assert.Contains("IAsyncSendContinuation", route.Stages[1].Callbacks[0].GenericConstraints);
        Assert.All(route.Stages, s => Assert.Equal(FusionOwnership.BorrowedService, s.ServiceOwnership));
        Assert.All(route.Handlers, h => Assert.Equal(FusionOwnership.BorrowedService, h.ServiceOwnership));
        Assert.Contains("global::App.Handler", LegacySource(driver.GetRunResult()));
        var explicitLocal = BindAcceptedSendLists(ListFixture("c.UseLocalHandlers(); " + body));
        Assert.Equal(3, Selection(explicitLocal).Handlers.Count);
        Assert.Equal(route.Handlers.Select(h => h.Name), Assert.Single(Selection(explicitLocal).Pipelines).Handlers.Select(h => h.Name));
    }

    [Fact]
    public void List_metadata_caches_bodies_and_trivia_but_tracks_order_capabilities_constraints_and_current_sources()
    {
        const string body = "c.Handlers<Ping>(DispatchFamily.SendAllAsync, typeof(A), typeof(B)); " +
            "c.Pipeline<Ping>(Stage.Hook<Mixed<string>>(), Stage.Wrapper<Opaque>(), Stage.Wrapper<OtherOpaque>(), Stage.Hook<Last>());";
        var source = ListFixture(body) + OtherOpaqueListStage;
        var driver = BindLists(source, "ZEN0037");
        var original = Selection(driver);
        var diagnostic = Assert.Single(driver.GetRunResult().Diagnostics);
        var moved = Compilation("\n\n" + source);
        driver = driver.RunGenerators(moved);
        Assert.Equal(original, Selection(driver));
        SelectionReason(driver, IncrementalStepRunReason.Cached);
        var current = Assert.Single(driver.GetRunResult().Diagnostics);
        Assert.Same(moved.SyntaxTrees[0], current.Location.SourceTree);
        Assert.Equal(diagnostic.Location.SourceSpan.Start + 2, current.Location.SourceSpan.Start);
        driver = driver.RunGenerators(Compilation(source.Replace("=> r;", "=> new Ping();")));
        Assert.Equal(original, Selection(driver));
        SelectionReason(driver, IncrementalStepRunReason.Cached);
        foreach (var changed in new[] {
            source.Replace("Stage.Hook<Mixed<string>>(), Stage.Wrapper<Opaque>(), Stage.Wrapper<OtherOpaque>(), Stage.Hook<Last>()", "Stage.Hook<Last>(), Stage.Wrapper<Opaque>(), Stage.Wrapper<OtherOpaque>(), Stage.Hook<Mixed<string>>()"),
            source.Replace("where T : class", "where T : notnull"),
            source.Replace("IAsyncSendFinallyObserver<Ping>", "ISendFinallyObserver<Ping>")
                .Replace("public ValueTask FinallyAsync(Ping r, SendExitInfo e, CancellationToken c) => default;", "public void Finally(Ping r, SendExitInfo e, CancellationToken c) {}") })
        {
            Assert.NotEqual(source, changed);
            driver = driver.RunGenerators(Compilation(changed));
            Assert.NotEqual(original, Selection(driver));
            SelectionReason(driver, IncrementalStepRunReason.Modified);
        }
        var duplicate = source.Replace("Stage.Hook<Last>()", "Stage.Hook<Last>(), Stage.Hook<Last>()");
        driver = BindLists(duplicate, "ZEN0035");
        var duplicateModel = Selection(driver);
        var first = Assert.Single(driver.GetRunResult().Diagnostics);
        Assert.Single(first.AdditionalLocations);
        var movedDuplicate = Compilation("\n\n" + duplicate);
        driver = driver.RunGenerators(movedDuplicate);
        Assert.Equal(duplicateModel, Selection(driver));
        SelectionReason(driver, IncrementalStepRunReason.Cached);
        var second = Assert.Single(driver.GetRunResult().Diagnostics);
        Assert.Same(movedDuplicate.SyntaxTrees[0], second.Location.SourceTree);
        Assert.Same(movedDuplicate.SyntaxTrees[0], Assert.Single(second.AdditionalLocations).SourceTree);
        Assert.Equal(first.Location.SourceSpan.Start + 2, second.Location.SourceSpan.Start);
        Assert.Equal(first.AdditionalLocations[0].SourceSpan.Start + 2, second.AdditionalLocations[0].SourceSpan.Start);
    }

    [Theory]
    [InlineData("Stage.Hook<Mixed<string>>(), Stage.Hook<Mixed<string>>()", "ZEN0035")]
    [InlineData("Stage.Hook<Mixed<object>>(), Stage.Hook<Mixed<dynamic>>()", "ZEN0035")]
    [InlineData("Stage.Hook<Mixed<string>>(), Stage.Hook<Mixed<string?>>()", "ZEN0035")]
    [InlineData("Stage.Hook<Last>(), Stage.Wrapper<Last>()", "ZEN0035", "ZEN0025")]
    [InlineData("Stage.Hook<Opaque>()", "ZEN0025")]
    [InlineData("Stage.Hook<Wrong>()", "ZEN0026")]
    [InlineData("Stage.Hook<Dual>()", "ZEN0027")]
    [InlineData("Stage.Hook<VoidOnly>()", "ZEN0031")]
    [InlineData("Stage.Hook(typeof(Mixed<>))", "ZEN0035")]
    [InlineData("Stage.Hook(typeof(RefStage))", "ZEN0034")]
    [InlineData("Stage.Hook(typeof(AbstractStage))", "ZEN0035")]
    [InlineData("Stage.Hook<Hidden>()", "ZEN0033")]
    [InlineData("Stages()", "ZEN0035")]
    public void Invalid_dedicated_stages_are_explicitly_rejected(string stages, params string[] ids)
    {
        var source = ListFixture("c.Handlers<Ping>(DispatchFamily.SendAsync, typeof(A)); c.Pipeline<Ping>(" + stages + ");") + """
            public sealed record Other : IRequest<int>;
            [ZendiatorHandler] public sealed class OtherHandler : IRequestHandler<Other,int> { public ValueTask<int> HandleAsync(Other r,CancellationToken c)=>new(1); }
            public sealed class Wrong : ISendRequestTransform<Other> { public Other Before(Other r,CancellationToken c)=>r; }
            public sealed class Dual : ISendRequestTransform<Ping>,IAsyncSendRequestTransform<Ping> {
                public Ping Before(Ping r,CancellationToken c)=>r; public ValueTask<Ping> BeforeAsync(Ping r,CancellationToken c)=>new(r);
            }
            public sealed class VoidOnly : ISendSuccessObserver<Ping> { public void After(Ping r,CancellationToken c){} }
            public ref struct RefStage;
            public abstract class AbstractStage : ISendRequestTransform<Ping> { public Ping Before(Ping r,CancellationToken c)=>r; }
            """;
        if (stages == "Stages()") source = source.Replace("public static class Startup {", "public static class Startup { public static Stage[] Stages()=>new[]{Stage.Hook<Last>()};");
        if (stages == "Stage.Hook<Hidden>()") source = source.Replace("public static class Startup {", "public static class Startup { private sealed class Hidden : ISendRequestTransform<Ping> { public Ping Before(Ping r,CancellationToken c)=>r; }");
        BindLists(source, ids);
    }

    [Fact]
    public void Explicit_family_separates_sync_async_and_void_routes_and_rejects_async_on_sync()
    {
        var source = ListFixture("c.Handlers<Ping>(DispatchFamily.SendSync, typeof(Sync)); c.Pipeline<Ping>(DispatchFamily.SendSync, Stage.Hook<Mixed<string>>());")
            .Replace("record struct Ping : IRequest<int>", "record struct Ping : IRequest<int>, ISyncRequest<int>")
            .Replace("[GenerateZendiator] public sealed partial class Zendiator;", "") + """
            [ZendiatorHandler] public sealed class Sync : ISyncRequestHandler<Ping,int> { public int Handle(Ping r,CancellationToken c)=>1; }
            """;
        var sync = BindLists(source, "ZEN0030");
        Assert.Equal(BoundDispatchFamily.SendSync, Assert.Single(Selection(sync).Pipelines).Family);
        var both = source.Replace("c.Pipeline<Ping>(DispatchFamily.SendSync, Stage.Hook<Mixed<string>>());",
            "c.Pipeline<Ping>(DispatchFamily.SendSync, Stage.Hook<Last>()); c.Handlers<Ping>(DispatchFamily.SendAsync,typeof(A)); c.Pipeline<Ping>(DispatchFamily.SendAsync,Stage.Hook<Mixed<string>>());");
        var separate = BindAcceptedSendLists(both, routes: 2, legacy: false);
        Assert.Equal(new[] { BoundDispatchFamily.SendSync, BoundDispatchFamily.SendAsync }, Selection(separate).Pipelines.Select(p => p.Family!.Value));
        BindLists(both.Replace("c.Pipeline<Ping>(DispatchFamily.SendSync, Stage.Hook<Last>())", "c.Pipeline<Ping>(Stage.Hook<Last>())"), "ZEN0029", "ZEN0037", "ZEN0037");
        var @void = ListFixture("c.Handlers<Ping>(DispatchFamily.SendAllAsync,typeof(B),typeof(A)); c.Pipeline<Ping>(Stage.Hook<Mixed<string>>(),Stage.Wrapper<Opaque>());")
            .Replace("IRequest<int>", "IRequest").Replace("IRequestHandler<Ping,int>", "IRequestHandler<Ping>")
            .Replace("ValueTask<int> HandleAsync", "ValueTask HandleAsync").Replace("=> new(1)", "=> default")
            .Replace("IAsyncSendWrapper<Ping,int>", "IAsyncSendWrapper<Ping>").Replace("IAsyncSendContinuation<Ping,int>", "IAsyncSendContinuation<Ping>")
            .Replace("ValueTask<int> InvokeAsync", "ValueTask InvokeAsync");
        var boundVoid = BindAcceptedSendLists(@void);
        Assert.Equal(FusionResultKind.Void, Assert.Single(Selection(boundVoid).Pipelines).ResultKind);
        Assert.Null(Selection(boundVoid).Pipelines[0].Result);
    }

    [Fact]
    public void Stream_declares_source_to_consumer_item_order_and_preserves_opaque_boundaries()
    {
        var source = ListFixture("c.Handlers<Items>(DispatchFamily.Stream,typeof(ItemsHandler)); c.Pipeline<Items>(Stage.Hook<Map>(),Stage.Wrapper<StreamOpaque>(),Stage.Hook<Filter>());") + """
            public sealed record Items : IStreamRequest<int>;
            [ZendiatorHandler] public sealed class ItemsHandler : IStreamRequestHandler<Items,int> { public System.Collections.Generic.IAsyncEnumerable<int> HandleAsync(Items r,CancellationToken c)=>throw new NotImplementedException(); }
            public sealed class Map : IStreamItemMap<Items,int> { public int Map(Items r,int item,CancellationToken c)=>item+1; }
            public sealed class Filter : IStreamItemFilter<Items,int> { public bool Accept(Items r,int item,CancellationToken c)=>true; }
            public sealed class StreamOpaque : IStreamWrapper<Items,int> {
                public System.Collections.Generic.IAsyncEnumerable<int> Wrap<N>(Items r,N n,CancellationToken c) where N:IStreamUpstream<Items,int> =>n.Open(r,c);
            }
            """;
        // A method cannot share its enclosing type name.
        source = source.Replace("class Map :", "class Mapper :").Replace("Stage.Hook<Map>()", "Stage.Hook<Mapper>()");
        var driver = BindLists(source, "ZEN0037");
        var route = Assert.Single(Selection(driver).Pipelines);
        Assert.Equal(FusionResultKind.Item, route.ResultKind);
        Assert.Equal(new[] { BoundCallbackPhase.Map, BoundCallbackPhase.Wrapper, BoundCallbackPhase.Filter }, route.Stages.Select(s => Assert.Single(s.Callbacks).Phase));
        Assert.Equal(BoundCompletionKind.Enumerable, route.Stages[1].Callbacks[0].Completion);
        var dual = source.Replace("class Mapper : IStreamItemMap<Items,int>", "class Mapper : IStreamItemMap<Items,int>, IStreamItemFilter<Items,int>")
            .Replace("public int Map(Items", "public bool Accept(Items r,int i,CancellationToken c)=>true; public int Map(Items");
        BindLists(dual, "ZEN0027");
    }

    [Theory]
    [InlineData("c.Handlers<Ping>(DispatchFamily.SendAsync,typeof(A),typeof(B));", "ZEN0035")]
    [InlineData("c.Handlers<Ping>(DispatchFamily.SendAllAsync);", "ZEN0035")]
    [InlineData("c.Handlers<Ping>(DispatchFamily.SendAllAsync,typeof(A),typeof(A));", "ZEN0035")]
    [InlineData("c.Handlers<Ping>(DispatchFamily.SendAsync,typeof(Handler));", "ZEN0035")]
    [InlineData("c.Handlers<Ping>(DispatchFamily.SendAsync,typeof(A)); c.Handlers<Ping>(DispatchFamily.SendAsync,typeof(A));", "ZEN0028")]
    [InlineData("c.Handlers<Ping>(DispatchFamily.SendAsync,typeof(A)); c.Pipeline<Ping>(); c.Pipeline<Ping>();", "ZEN0028")]
    [InlineData("c.Handlers<Event>(DispatchFamily.Notification);", "ZEN0029")]
    public void Handler_order_cardinality_and_authoritative_sites_are_diagnosed(string body, string id)
    {
        var source = ListFixture(body) + "public sealed record Event : INotification;";
        if (body.Contains("Event")) source = source.Replace("[GenerateZendiator] public sealed partial class Zendiator;", "");
        var driver = BindLists(source, id);
        if (id == "ZEN0028") Assert.Single(Assert.Single(driver.GetRunResult().Diagnostics).AdditionalLocations);
    }

    [Fact]
    public void Closed_binding_checks_handler_eligibility_and_public_route_shapes_before_execution_metadata()
    {
        var source = ListFixture("c.Handlers<Ping>(DispatchFamily.SendAsync,typeof(A)); c.Pipeline<Ping>();")
            .Replace("[GenerateZendiator] public sealed partial class Zendiator;", "");
        BindLists(source.Replace("[ZendiatorHandler] public sealed class A", "[ZendiatorHandler] public abstract class A"), "ZEN0035");
        var open = source.Replace("typeof(A)", "typeof(Open<>)") +
            "[ZendiatorHandler] public sealed class Open<T> : IRequestHandler<Ping,int> { public ValueTask<int> HandleAsync(Ping r,CancellationToken c)=>new(1); }";
        BindLists(open, "ZEN0035");
        var internalRequest = source.Replace("public readonly record struct Ping", "internal readonly record struct Ping")
            .Replace("public sealed class Handler", "internal sealed class Handler");
        foreach (var n in new[] { "A", "B", "C" }) internalRequest = internalRequest.Replace("public sealed class " + n, "internal sealed class " + n);
        internalRequest = internalRequest.Replace("public sealed class Mixed", "internal sealed class Mixed")
            .Replace("public sealed class Last", "internal sealed class Last").Replace("public sealed class Opaque", "internal sealed class Opaque");
        BindLists(internalRequest, "ZEN0035");
        var response = source.Replace("IRequest<int>", "IRequest<System.Collections.Generic.IEnumerable<int>>")
            .Replace("IRequestHandler<Ping,int>", "IRequestHandler<Ping,System.Collections.Generic.IEnumerable<int>>")
            .Replace("ValueTask<int> HandleAsync", "ValueTask<System.Collections.Generic.IEnumerable<int>> HandleAsync")
            .Replace("=> new(1)", "=> new(new int[]{1})");
        BindAcceptedSendLists(response, legacy: false); // An interface response is a valid closed public shape.
        var hiddenResponse = source.Replace("IRequest<int>", "IRequest<HiddenResult>").Replace("IRequestHandler<Ping,int>", "IRequestHandler<Ping,HiddenResult>")
            .Replace("ValueTask<int> HandleAsync", "ValueTask<HiddenResult> HandleAsync").Replace("=> new(1)", "=> new(new HiddenResult())")
            .Replace("public sealed class Handler", "internal sealed class Handler") + "internal sealed class HiddenResult;";
        foreach (var n in new[] { "A", "B", "C" }) hiddenResponse = hiddenResponse.Replace("public sealed class " + n, "internal sealed class " + n);
        BindLists(hiddenResponse, "ZEN0035");
    }

    [Fact]
    public void Sendall_pipeline_requires_explicit_handler_order_for_both_backends_even_with_one_candidate()
    {
        foreach (var sync in new[] { false, true })
        {
            var family = sync ? "SendAllSync" : "SendAllAsync";
            foreach (var useLocal in new[] { false, true })
                foreach (var defaultComposition in new[] { false, true })
                {
                    var selection = useLocal ? "c.UseLocalHandlers(); " : "c.AddHandler<A>(); ";
                    var body = selection + $"c.Pipeline<Ping>(DispatchFamily.{family});";
                    var driver = BindLists(FamilyFixture(body, sync, isVoid: false, defaultComposition), "ZEN0028");
                    var route = Assert.Single(Selection(driver).Pipelines);
                    Assert.False(route.ExplicitHandlerOrder);
                    Assert.Empty(route.Handlers); // A sorted candidate set cannot become a branch list.
                    Assert.Null(route.ResultKind);
                    var diagnostic = Assert.Single(driver.GetRunResult().Diagnostics);
                    Assert.Contains("explicit Handlers list", diagnostic.GetMessage());
                    Assert.Equal("c.Pipeline<Ping>(DispatchFamily." + family + ")", diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan));

                    // The list may occur later in the lambda; its declared B,A order is authoritative.
                    var explicitOrder = BindLists(FamilyFixture(body + $" c.Handlers<Ping>(DispatchFamily.{family},typeof(B),typeof(A));",
                        sync, isVoid: false, defaultComposition), "ZEN0037");
                    var ordered = Assert.Single(Selection(explicitOrder).Pipelines);
                    Assert.True(ordered.ExplicitHandlerOrder);
                    Assert.Equal(new[] { "global::App.B", "global::App.A" }, ordered.Handlers.Select(h => h.Name));
                }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Wrapper_continuation_mode_matches_each_family_and_shape_while_sync_hooks_remain_legal(bool isVoid)
    {
        foreach (var sync in new[] { false, true })
            foreach (var sendAll in new[] { false, true })
            {
                var family = sync ? (sendAll ? "SendAllSync" : "SendSync") : (sendAll ? "SendAllAsync" : "SendAsync");
                var compatible = sync ? "SyncOpaque" : "Opaque";
                var incompatible = sync ? "Opaque" : "SyncOpaque";
                var required = sync ? "ISendContinuation" : "IAsyncSendContinuation";
                string Body(string wrapper) => $"c.Handlers<Ping>(DispatchFamily.{family},typeof(A)); " +
                    $"c.Pipeline<Ping>(DispatchFamily.{family},Stage.Hook<Last>(),Stage.Wrapper<{wrapper}>());";
                var driver = BindAcceptedSendLists(FamilyFixture(Body(compatible), sync, isVoid));
                var route = Assert.Single(Selection(driver).Pipelines);
                Assert.Equal(isVoid ? FusionResultKind.Void : FusionResultKind.Response, route.ResultKind);
                Assert.Equal(BoundCompletionKind.Synchronous, Assert.Single(route.Stages[0].Callbacks).Completion);
                var callback = Assert.Single(route.Stages[1].Callbacks);
                Assert.Equal(BoundCallbackPhase.Wrapper, callback.Phase);
                Assert.Contains(required, callback.GenericConstraints);
                Assert.Equal(sync ? BoundCompletionKind.Synchronous : BoundCompletionKind.ValueTask, callback.Completion);

                var wrongSource = FamilyFixture(Body(incompatible), sync, isVoid);
                var wrong = BindLists(wrongSource, "ZEN0025");
                Assert.Contains(required, Assert.Single(wrong.GetRunResult().Diagnostics).GetMessage());
                Assert.Empty(Assert.Single(Selection(wrong).Pipelines).Stages[1].Callbacks);
                if (!sync && !sendAll && !isVoid)
                {
                    var initial = Selection(wrong);
                    var diagnostic = Assert.Single(wrong.GetRunResult().Diagnostics);
                    var moved = Compilation("\n\n" + wrongSource);
                    wrong = wrong.RunGenerators(moved);
                    Assert.Equal(initial, Selection(wrong));
                    SelectionReason(wrong, IncrementalStepRunReason.Cached);
                    var current = Assert.Single(wrong.GetRunResult().Diagnostics);
                    Assert.Same(moved.SyntaxTrees[0], current.Location.SourceTree);
                    Assert.Equal(diagnostic.Location.SourceSpan.Start + 2, current.Location.SourceSpan.Start);
                }

                // S01 keeps both wrapper contracts ambiguous, even if this route has a matching one.
                var dual = BindLists(FamilyFixture(Body("DualOpaque"), sync, isVoid), "ZEN0027");
                Assert.Contains("same phase", Assert.Single(dual.GetRunResult().Diagnostics).GetMessage());
                Assert.Empty(Assert.Single(Selection(dual).Pipelines).Stages[1].Callbacks);
            }
    }

    private static string FamilyFixture(string body, bool sync, bool isVoid, bool defaultComposition = false)
    {
        var source = ListFixture(body, defaultComposition) + """
            public sealed class SyncOpaque : ISendWrapper<Ping,int> {
                public int Invoke<N>(Ping r,N n,CancellationToken c) where N:ISendContinuation<Ping,int> => n.Invoke(r,c);
            }
            public sealed class DualOpaque : ISendWrapper<Ping,int>,IAsyncSendWrapper<Ping,int> {
                public int Invoke<N>(Ping r,N n,CancellationToken c) where N:ISendContinuation<Ping,int> => n.Invoke(r,c);
                public ValueTask<int> InvokeAsync<N>(Ping r,N n,CancellationToken c) where N:IAsyncSendContinuation<Ping,int> => n.InvokeAsync(r,c);
            }
            """;
        if (sync) source = source.Replace("IRequest<int>", "ISyncRequest<int>").Replace("IRequestHandler<Ping,int>", "ISyncRequestHandler<Ping,int>")
            .Replace("ValueTask<int> HandleAsync", "int Handle").Replace("=> new(1);", "=> 1;");
        if (isVoid) source = source.Replace("IRequest<int>", "IRequest").Replace("ISyncRequest<int>", "ISyncRequest")
            .Replace("IRequestHandler<Ping,int>", "IRequestHandler<Ping>").Replace("ISyncRequestHandler<Ping,int>", "ISyncRequestHandler<Ping>")
            .Replace("ValueTask<int> HandleAsync", "ValueTask HandleAsync").Replace("int Handle(", "void Handle(")
            .Replace("=> new(1);", "=> default;").Replace("=> 1;", "{}")
            .Replace("ISendWrapper<Ping,int>", "ISendWrapper<Ping>").Replace("IAsyncSendWrapper<Ping,int>", "IAsyncSendWrapper<Ping>")
            .Replace("ISendContinuation<Ping,int>", "ISendContinuation<Ping>").Replace("IAsyncSendContinuation<Ping,int>", "IAsyncSendContinuation<Ping>")
            .Replace("int Invoke<", "void Invoke<").Replace("ValueTask<int> InvokeAsync", "ValueTask InvokeAsync");
        return source;
    }

    [Fact]
    public void Missing_interface_implementation_and_duplicate_marker_remain_compiler_owned_in_lists()
    {
        var source = ListFixture("c.Handlers<Ping>(DispatchFamily.SendAsync,typeof(A)); c.Pipeline<Ping>(Stage.Hook<Missing>());") +
            "public sealed class Missing : ISendRequestTransform<Ping> {}";
        var driver = Driver().RunGeneratorsAndUpdateCompilation(Compilation(source), out var output, out var diagnostics);
        Assert.Empty(diagnostics);
        Assert.Equal("CS0535", Assert.Single(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)).Id);
        Assert.True(Assert.Single(Selection(driver).Pipelines).Stages[0].CompilerOwnsError);
        source = ListFixture("c.Handlers<Ping>(DispatchFamily.SendAsync,typeof(A));").Replace("[ZendiatorHandler] public sealed class A", "[ZendiatorHandler,ZendiatorHandler] public sealed class A");
        driver = Driver().RunGeneratorsAndUpdateCompilation(Compilation(source), out output, out diagnostics);
        Assert.Empty(diagnostics);
        Assert.Equal("CS0579", Assert.Single(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)).Id);
    }
}
