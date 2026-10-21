using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Zendiator.SourceGenerator;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private static readonly string[] AsyncClosureCases = ["closed-explicit", "closed-inferred", "open-constrained", "closed-constraint", "open-contradiction", "undetermined"];
    private const string AsyncClosureTypes = """
        public sealed record AsyncClosureItems<T>(T Value) : IStreamRequest<T>;
        [ZendiatorHandler] public sealed class AsyncClosureHandler<T> : IStreamRequestHandler<AsyncClosureItems<T>,T> {
            public System.Collections.Generic.IAsyncEnumerable<T> HandleAsync(AsyncClosureItems<T> r,CancellationToken c)=>throw new NotImplementedException();
        }
        public sealed class AsyncClosureFirst<TRequest,TItem> : IAsyncStreamItemFilter<TRequest,TItem> {
            public ValueTask<bool> AcceptAsync(TRequest r,TItem item,CancellationToken c)=>new(true);
        }
        public sealed class AsyncClosureMap<TRequest,TItem> : IAsyncStreamItemMap<TRequest,TItem> {
            public ValueTask<TItem> MapAsync(TRequest r,TItem item,CancellationToken c)=>new(item);
        }
        public sealed class AsyncClosureLast<TRequest,TItem> : IStreamItemFilter<TRequest,TItem> {
            public bool Accept(TRequest r,TItem item,CancellationToken c)=>true;
        }
        public sealed class AsyncClosureRefOnly<TRequest,TItem> : IAsyncStreamItemFilter<TRequest,TItem> where TItem:class {
            public ValueTask<bool> AcceptAsync(TRequest r,TItem item,CancellationToken c)=>new(true);
        }
        public sealed class AsyncClosureValueMap<TRequest,TItem> : IAsyncStreamItemMap<TRequest,TItem> where TItem:struct {
            public ValueTask<TItem> MapAsync(TRequest r,TItem item,CancellationToken c)=>new(item);
        }
        public sealed class AsyncClosureUnused<TRequest,TItem,TUnused> : IAsyncStreamItemFilter<TRequest,TItem> {
            public ValueTask<bool> AcceptAsync(TRequest r,TItem item,CancellationToken c)=>new(true);
        }
        """;
    private static string AsyncClosureSource(string id)
    {
        var open = id is "open-constrained" or "open-contradiction";
        var request = open ? "AsyncClosureItems<>" : "AsyncClosureItems<int>";
        var first = "AsyncClosureFirst<,>"; var map = "AsyncClosureMap<,>"; var last = "AsyncClosureLast<,>";
        var types = AsyncClosureTypes;
        if (id == "closed-explicit")
        {
            first = "AsyncClosureFirst<AsyncClosureItems<int>,int>";
            map = "AsyncClosureMap<AsyncClosureItems<int>,int>";
            last = "AsyncClosureLast<AsyncClosureItems<int>,int>";
        }
        if (id == "open-constrained") types = types
            .Replace("IAsyncStreamItemFilter<TRequest,TItem> {", "IAsyncStreamItemFilter<TRequest,TItem> where TItem:class {")
            .Replace("IAsyncStreamItemMap<TRequest,TItem> {", "IAsyncStreamItemMap<TRequest,TItem> where TItem:new() {");
        if (id is "closed-constraint" or "open-contradiction") first = "AsyncClosureRefOnly<,>";
        if (id == "open-contradiction") map = "AsyncClosureValueMap<,>";
        if (id == "undetermined") first = "AsyncClosureUnused<,,>";
        var pipeline = "Stage.Hook(typeof(" + first + ")),Stage.Hook(typeof(" + map + ")),Stage.Hook(typeof(" + last + "))";
        var body = open
            ? "c.Handlers(typeof(" + request + "),DispatchFamily.Stream,typeof(AsyncClosureHandler<>));c.Pipeline(typeof(" + request + "),DispatchFamily.Stream," + pipeline + ");"
            : "c.Handlers<" + request + ">(DispatchFamily.Stream,typeof(AsyncClosureHandler<>));c.Pipeline<" + request + ">(" + pipeline + ");";
        return ListFixture(body) + types;
    }

    [Fact]
    public void Stream_async_stage_type_closure_preserves_typed_completion_and_rejects_invalid_arguments()
    {
        BoundStreamHookEmission[]? explicitClosed = null;
        foreach (var id in AsyncClosureCases)
        {
            var source = AsyncClosureSource(id);
            var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "AsyncClosure_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
            var accepted = id is "closed-explicit" or "closed-inferred" or "open-constrained";
            var route = Assert.Single(Selection(driver).Pipelines);
            var generated = driver.GetRunResult().Results.Single().GeneratedSources;
            using var bytes = new MemoryStream(); var emitted = output.Emit(bytes);
            var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
            if (!string.IsNullOrEmpty(evidence))
            {
                var folder = Path.Combine(evidence, "stream-async-type-closure", id); Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "input.cs"), source); File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
                foreach (var file in generated) File.WriteAllText(Path.Combine(folder, file.HintName), file.SourceText.ToString());
                File.WriteAllText(Path.Combine(folder, "binding.json"), System.Text.Json.JsonSerializer.Serialize(new
                {
                    id,
                    accepted,
                    emitted.Success,
                    route.RequestName,
                    generic = route.StreamGeneric,
                    stages = route.Stages.Select(s => new
                    {
                        s.Name,
                        ownership = s.ServiceOwnership.ToString(),
                        sessions = s.Sessions.Count,
                        typed = s.StreamEmission,
                        callbacks = s.Callbacks.Select(c => new { phase = c.Phase.ToString(), completion = c.Completion.ToString(), c.RequiresSingleConsumption })
                    }),
                    generatorDiagnostics = diagnostics.Select(d => new
                    {
                        d.Id,
                        message = d.GetMessage(),
                        inSource = d.Location.IsInSource,
                        anchor = d.Location.IsInSource ? d.Location.SourceTree!.GetText().ToString(d.Location.SourceSpan) : null
                    }),
                    compilerDiagnostics = emitted.Diagnostics.Select(d => new { d.Id, severity = d.Severity.ToString(), message = d.GetMessage() }),
                    candidateOnly = true,
                    productionEnabled = false,
                    runtimeEnumerations = 0
                }));
            }
            Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
            Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
            if (!accepted)
            {
                Assert.Contains(diagnostics, d => d.Id == "ZEN0035" && d.Severity == DiagnosticSeverity.Error && d.Location.IsInSource);
                var reason = id == "closed-constraint" ? "do not satisfy its generic constraints" :
                    id == "open-contradiction" ? "incompatible reference/value" : "Not every Stream service type argument is determined";
                Assert.Contains(diagnostics, d => d.GetMessage().Contains(reason, StringComparison.Ordinal));
                Assert.DoesNotContain(generated, s => s.HintName.StartsWith("Zendiator.Stream", StringComparison.Ordinal));
                continue;
            }
            // A closed request with only the open Handler member proves internal closure, not public membership.
            if (id is "closed-explicit" or "closed-inferred")
            {
                Assert.Equal("ZEN0037", Assert.Single(diagnostics).Id);
                Assert.DoesNotContain(generated, s => s.HintName.StartsWith("Zendiator.Canonical.Stream.Public.", StringComparison.Ordinal));
            }
            else Assert.Empty(diagnostics);
            Assert.Equal(id == "open-constrained", route.StreamGeneric != null);
            if (id == "open-constrained") Assert.Equal(" where __ZendiatorType0 : class, new()", route.StreamGeneric!.TypeConstraints);
            Assert.Equal(3, route.Stages.Count);
            var completions = new[] { BoundCompletionKind.ValueTask, BoundCompletionKind.ValueTask, BoundCompletionKind.Synchronous };
            var phases = new[] { BoundCallbackPhase.Filter, BoundCallbackPhase.Map, BoundCallbackPhase.Filter };
            for (var i = 0; i < route.Stages.Count; i++)
            {
                var stage = route.Stages[i]; var callback = Assert.Single(stage.Callbacks); var typed = Assert.IsType<BoundStreamHookEmission>(stage.StreamEmission);
                Assert.Equal(phases[i], callback.Phase); Assert.Equal(completions[i], callback.Completion);
                Assert.Equal(callback.Completion, typed.Completion); Assert.Equal(callback.Phase, typed.Phase);
                Assert.Equal(i < 2, callback.RequiresSingleConsumption); Assert.False(typed.HookIsValueType);
                Assert.Equal(FusionOwnership.BorrowedService, stage.ServiceOwnership); Assert.Empty(stage.Sessions);
                Assert.Contains(id == "open-constrained" ? "__ZendiatorType0" : "int", typed.HookName);
            }
            var typedStages = route.Stages.Select(s => s.StreamEmission!).ToArray();
            if (id == "closed-explicit") explicitClosed = typedStages;
            if (id == "closed-inferred") { Assert.NotNull(explicitClosed); Assert.Equal(explicitClosed, typedStages); }
            var connection = Assert.Single(StreamConnections(driver).Routes); Assert.Equal(CanonicalStreamConnectionKind.CanonicalCandidate, connection.Kind);
            var canonical = Assert.Single(generated, s => s.HintName.StartsWith("Zendiator.StreamCanonical.", StringComparison.Ordinal)).SourceText.ToString();
            var canonicalRoot = CSharpSyntaxTree.ParseText(canonical).GetRoot();
            foreach (var method in new[] { "AcceptAsync", "MapAsync" })
            {
                var callback = Assert.Single(canonicalRoot.DescendantNodes().OfType<InvocationExpressionSyntax>(),
                    call => call.Expression is MemberAccessExpressionSyntax member && member.Name.Identifier.ValueText == method);
                var awaited = Assert.Single(callback.Ancestors().OfType<AwaitExpressionSyntax>());
                var configured = Assert.IsType<InvocationExpressionSyntax>(awaited.Expression);
                Assert.Equal("ConfigureAwait", Assert.IsType<MemberAccessExpressionSyntax>(configured.Expression).Name.Identifier.ValueText);
                Assert.True(Assert.Single(configured.ArgumentList.Arguments).Expression.IsKind(SyntaxKind.FalseLiteralExpression));
            }
            Assert.DoesNotContain(generated, s => s.HintName.StartsWith("Zendiator.StreamPrototype.", StringComparison.Ordinal) ||
                s.HintName.StartsWith("Zendiator.Route.", StringComparison.Ordinal) || s.HintName.StartsWith("Zendiator.Composition.", StringComparison.Ordinal));
        }
    }
}
