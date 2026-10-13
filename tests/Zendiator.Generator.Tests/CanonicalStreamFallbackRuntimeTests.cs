using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Zendiator.SourceGenerator;
using Zendiator.Tests.Contracts;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed class CanonicalStreamFallbackProbe
{
    public static readonly AsyncLocal<string?> Ambient = new();
    public int[] Input = [-1, 1, 2];
    public string? WrapperMode;
    public object? Outer;
    public CancellationToken ReplacementToken;
    public CancellationToken ApiToken;
    public Action<CancellationToken>? ValidateStartToken;
    public Exception? GetEnumeratorError, MoveError, DisposeError;
    public CancellationTokenSource? CancelInFirst;
    public bool CancelAndAccept;
    public CancellationToken SourceToken => _sourceToken;
    public Action<string, object?, CancellationToken>? Observe;
    public Action<CanonicalStreamProbeEvent>? CanonicalEvent;
    public Exception? FirstError;
    public Func<bool, ValueTask<bool>>? NativeMove;
    public Func<ValueTask>? NativeDispose;
    public int NativeMovePendingReturns, NativeDisposePendingReturns;
    public bool Context;
    public readonly List<string> Trace = [];
    public int Starts, Moves, Reads, Disposals, Effects, BeforeResolves, AfterResolves, WrapperFinals;
    internal readonly ContractOnce<bool> MoveGate = new(), DisposeGate = new();
    internal bool Pending, PendingFailure;
    internal readonly Exception SourceError = new FormatException("source"), CleanupError = new InvalidOperationException("cleanup");
    private readonly Dictionary<object, CancellationToken> _opens = new(ReferenceEqualityComparer.Instance);
    private object? _sourceRequest;
    private CancellationToken _sourceToken, _wrapperToken;
    private void EmitCanonical(string kind, string stage, object? request, CancellationToken token,
        int? sequence = null, object? input = null, object? output = null, bool? result = null, Exception? error = null)
        => CanonicalEvent?.Invoke(new(kind, stage, request, token, sequence, input, output, result, error));
    public void Resolve(string role)
    {
        Trace.Add("resolve:" + role + "@" + Ambient.Value);
        Observe?.Invoke("factory:" + role, null, default);
        if (role == "F1") { BeforeResolves++; if (Context) Ambient.Value = "factory"; }
        if (role == "F2") AfterResolves++;
        EmitCanonical("Resolve", role, Outer, ApiToken);
    }
    private void Record(string phase, object request, CancellationToken token, object? item, bool identity, CancellationToken expected)
    {
        var id = request.GetType().GetProperty("Id")!.GetValue(request);
        Trace.Add(phase + ":" + id + ":" + item + ":" + identity + ":" + token.Equals(expected) + "@" + Ambient.Value);
        Observe?.Invoke(phase, request, token);
        Assert.True(identity); Assert.Equal(expected, token);
    }
    private static T Value<T>(long value) => (T)Convert.ChangeType(value, typeof(T), System.Globalization.CultureInfo.InvariantCulture)!;
    public bool First<T>(object request, T item, CancellationToken token)
    {
        Record("F1", request, token, item, ReferenceEquals(request, _sourceRequest), _sourceToken);
        if (Context) Ambient.Value = "callback";
        if (FirstError != null)
        {
            EmitCanonical("Throw", "F1", request, token, Reads - 1, input: item, error: FirstError);
            throw FirstError;
        }
        bool accepted;
        if (CancelInFirst != null) { CancelInFirst.Cancel(); accepted = CancelAndAccept; }
        else accepted = Convert.ToInt64(item) > 0;
        EmitCanonical("Filter", "F1", request, token, Reads - 1, input: item, result: accepted);
        return accepted;
    }
    public T Map<T>(object request, T item, CancellationToken token)
    {
        Record("Map", request, token, item, ReferenceEquals(request, _sourceRequest), _sourceToken); Effects++;
        T mapped;
        try
        {
            mapped = Value<T>(Convert.ToInt64(item) * 2);
        }
        catch (Exception error)
        {
            EmitCanonical("Throw", "Map", request, token, Reads - 1, input: item, error: error);
            throw;
        }
        EmitCanonical("Map", "Map", request, token, Reads - 1, input: item, output: mapped);
        return mapped;
    }
    public bool Last<T>(object request, T item, CancellationToken token)
    {
        Record("F2", request, token, item, ReferenceEquals(request, WrapperMode == null ? _sourceRequest : Outer),
            WrapperMode == null ? _sourceToken : _wrapperToken);
        var accepted = WrapperMode == null ? Convert.ToInt64(item) < 4 : Convert.ToInt64(item) != 20;
        EmitCanonical("Filter", "F2", request, token, Reads - 1, input: item, result: accepted);
        return accepted;
    }
    public IAsyncEnumerable<T> Start<T>(object request, CancellationToken token)
    {
        ValidateStartToken?.Invoke(token);
        var expected = WrapperMode == null ? ValidateStartToken == null ? ApiToken : token : _opens[request];
        Record("H", request, token, null, WrapperMode == null ? ReferenceEquals(request, Outer) : _opens.ContainsKey(request), expected);
        Starts++; _sourceRequest = request; _sourceToken = token;
        EmitCanonical("HandlerStart", "H", request, token);
        return new Source<T>(this, request, token);
    }
    public void Wrap(object request, CancellationToken token)
    {
        _wrapperToken = token; Record("W.wrap", request, token, null, ReferenceEquals(request, Outer), ApiToken);
        if (Context) Ambient.Value = "wrapper-sync";
    }
    public void Open(object request, CancellationToken token)
    { _opens.Add(request, token); Record("W.open", request, token, null, true, token); }
    public void WrapperEvent(string phase, object request, CancellationToken token, int? item = null)
    {
        Record(phase, request, token, item, ReferenceEquals(request, Outer), _wrapperToken);
        if (Context && phase == "W.before") Ambient.Value = "wrapper-async";
        if (phase == "W.finally") WrapperFinals++;
    }
    private sealed class Source<T>(CanonicalStreamFallbackProbe p, object request, CancellationToken start)
        : IAsyncEnumerable<T>, IAsyncEnumerator<T>
    {
        private int _index = -1;
        private CancellationToken _token;
        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken token = default)
        { _token = token; p.Record("Get", request, token, null, true, start); if (p.GetEnumeratorError != null) throw p.GetEnumeratorError; return this; }
        public T Current
        { get { p.Reads++; var value = Value<T>(p.Input[_index]); p.Record("Current", request, _token, value, true, start);
            p.EmitCanonical("InnerCurrent", "H", request, _token, _index, output: value); return value; } }
        public ValueTask<bool> MoveNextAsync()
        {
            p.Moves++; _index++; p.Record("Move", request, _token, null, true, start);
            p.EmitCanonical("InnerMoveNext", "H", request, _token, _index);
            if (p.NativeMove != null)
            {
                var move = p.NativeMove(_index < p.Input.Length);
                if (!move.IsCompleted) p.NativeMovePendingReturns++;
                p.Observe?.Invoke("native-move.return", request, _token); return move;
            }
            if (p.MoveError != null) return ValueTask.FromException<bool>(p.MoveError);
            return p.Pending && _index == 0 ? p.MoveGate.Task : new(_index < p.Input.Length);
        }
        public ValueTask DisposeAsync()
        {
            p.Disposals++; p.Record("H.finally", request, _token, null, true, start);
            p.EmitCanonical("InnerDisposeStart", "H", request, _token);
            if (p.NativeDispose != null)
            {
                var dispose = p.NativeDispose();
                if (!dispose.IsCompleted) p.NativeDisposePendingReturns++;
                p.Observe?.Invoke("native-dispose.return", request, _token); return dispose;
            }
            if (p.DisposeError != null)
            {
                p.EmitCanonical("InnerDisposeThrow", "H", request, _token, error: p.DisposeError);
                return ValueTask.FromException(p.DisposeError);
            }
            if (p.PendingFailure) return ValueTask.FromException(p.CleanupError);
            if (p.Pending) return p.DisposeGate.Void;
            p.EmitCanonical("InnerDisposeEnd", "H", request, _token);
            return default;
        }
    }
}

public sealed partial class GeneratorTests
{
    private sealed record FallbackFixture(Assembly Assembly, Type Request, Type Item, Type[] Services, Type Bridge,
        MethodInfo Start, MethodInfo Canonical, MethodInfo? Fused, bool Wrapper);
    private static readonly Dictionary<string, FallbackFixture> FallbackFixtures = [];
    private static FallbackFixture FallbackFixtureFor(string shape)
    {
        lock (FallbackFixtures)
        {
            if (FallbackFixtures.TryGetValue(shape, out var cached)) return cached;
            var item = shape is "long" or "string" ? shape : "int";
            var wrapper = shape == "wrapper";
            var pipeline = wrapper ? "Stage.Hook<Positive>(),Stage.Wrapper<StreamOpaque>(),Stage.Hook<Small>()" :
                shape == "order" ? "Stage.Hook<DoubleItem>(),Stage.Hook<Positive>(),Stage.Hook<Small>()" :
                "Stage.Hook<Positive>(),Stage.Hook<DoubleItem>(),Stage.Hook<Small>()";
            var source = StreamGraphFixture(pipeline)
                .Replace("IStreamRequest<int>", "IStreamRequest<" + item + ">")
                .Replace("<Items,int>", "<Items," + item + ">")
                .Replace("IAsyncEnumerable<int>", "IAsyncEnumerable<" + item + ">")
                .Replace("int item", item + " item").Replace("public int Map", "public " + item + " Map")
                .Replace("record Items :", "record Items(int Id) :")
                .Replace("public sealed class ItemsHandler :", "public sealed class ItemsHandler(global::Zendiator.Generator.Tests.CanonicalStreamFallbackProbe p) :")
                .Replace("HandleAsync(Items r,CancellationToken c)=>throw new NotImplementedException();", "HandleAsync(Items r,CancellationToken c)=>p.Start<" + item + ">(r,c);")
                .Replace("public sealed class Positive :", "public sealed class Positive(global::Zendiator.Generator.Tests.CanonicalStreamFallbackProbe p) :")
                .Replace("=>item>0;", "=>p.First(r,item,c);")
                .Replace("public sealed class DoubleItem :", "public sealed class DoubleItem(global::Zendiator.Generator.Tests.CanonicalStreamFallbackProbe p) :")
                .Replace("=>item*2;", "=>p.Map(r,item,c);")
                .Replace("public sealed class Small :", "public sealed class Small(global::Zendiator.Generator.Tests.CanonicalStreamFallbackProbe p) :")
                .Replace("=>item<4;", "=>p.Last(r,item,c);");
            if (wrapper) source = source.Replace("""
                public sealed class StreamOpaque : IStreamWrapper<Items,int> {
                    public System.Collections.Generic.IAsyncEnumerable<int> Wrap<N>(Items r,N n,CancellationToken c)
                        where N : IStreamUpstream<Items,int> => n.Open(r,c);
                }
                """, """
                public sealed class StreamOpaque(global::Zendiator.Generator.Tests.CanonicalStreamFallbackProbe p) : IStreamWrapper<Items,int> {
                    public System.Collections.Generic.IAsyncEnumerable<int> Wrap<N>(Items r,N n,CancellationToken c) where N : IStreamUpstream<Items,int> {
                        p.Wrap(r,c);
                        if(p.WrapperMode=="direct") { var changed=new Items(9); p.Open(changed,p.ReplacementToken); return n.Open(changed,p.ReplacementToken); }
                        return Run(r,n,c);
                    }
                    private async System.Collections.Generic.IAsyncEnumerable<int> Run<N>(Items r,N n,
                        [global::System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken c) where N : IStreamUpstream<Items,int> {
                        p.WrapperEvent("W.before",r,c);
                        try {
                            await Task.CompletedTask;
                            if(p.WrapperMode=="empty") yield break;
                            if(p.WrapperMode=="alternative") { yield return 7; yield break; }
                            for(var i=0;i<(p.WrapperMode=="two-open"?2:1);i++) {
                                var changed=new Items(2+i); p.Open(changed,c);
                                await foreach(var value in n.Open(changed,c).ConfigureAwait(false)) { p.WrapperEvent("W.item",r,c,value); yield return value; }
                            }
                        } finally { p.WrapperEvent("W.finally",r,c); }
                    }
                }
                """);
            // The unused Wrapper still has to compile when the item type changes.
            source += $$"""
                public sealed class FallbackBridge(System.IServiceProvider services) {
                    public System.Collections.Generic.IAsyncEnumerable<{{item}}> Start(Items r,CancellationToken t)=>
                        Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<ItemsHandler>(services).HandleAsync(r,t);
                }
                """;
            var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "Fallback_" + shape + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
            Assert.Equal("ZEN0037", Assert.Single(diagnostics).Id);
            var backend = Assert.IsType<CanonicalRouteBackend>(Assert.Single(driver.GetRunResult().Results.Single()
                .TrackedSteps["CanonicalRouteBackendEntry"].SelectMany(s => s.Outputs)).Value);
            CanonicalSourceFile? forced = null;
            if (backend.StreamCandidate != null)
            {
                // Comparison-only canonical lowering of the same input; production still selects fused first.
                forced = CanonicalStreamFallbackEmitter.Emit(CanonicalStreamFallbackProjection.Create(backend.Route)!);
                output = output.AddSyntaxTrees(CSharpSyntaxTree.ParseText(forced.Source, SyncParseOptions));
            }
            Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
            using var bytes = new MemoryStream(); var emitted = output.Emit(bytes);
            var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
            if (!string.IsNullOrEmpty(evidence))
            {
                var folder = Path.Combine(evidence, "stream-fallback", shape); Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "input.cs"), source);
                foreach (var file in driver.GetRunResult().Results.Single().GeneratedSources) File.WriteAllText(Path.Combine(folder, file.HintName), file.SourceText.ToString());
                if (forced != null) File.WriteAllText(Path.Combine(folder, forced.HintName), forced.Source);
                File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
                File.WriteAllText(Path.Combine(folder, "compile.json"), System.Text.Json.JsonSerializer.Serialize(new
                { emitted.Success, forcedCanonicalForComparison = forced != null, candidateOnly = true, productionEnabled = false,
                    diagnostics = emitted.Diagnostics.Select(d => new { d.Id, severity = d.Severity.ToString(), message = d.GetMessage() }) }));
            }
            Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics)); bytes.Position = 0;
            var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
            var services = (wrapper ? new[] { "ItemsHandler", "Positive", "StreamOpaque", "Small" } : new[] { "ItemsHandler", "Positive", "DoubleItem", "Small" })
                .Select(n => assembly.GetType("App." + n)!).ToArray();
            var bridge = assembly.GetType("App.FallbackBridge")!;
            var canonical = Assert.Single(assembly.GetTypes(), t => t.Name.StartsWith("__ZendiatorStreamCanonical_", StringComparison.Ordinal)).GetMethod("Create", BindingFlags.NonPublic | BindingFlags.Static)!;
            var fused = assembly.GetTypes().SingleOrDefault(t => t.Name.StartsWith("__ZendiatorStreamPrototype_", StringComparison.Ordinal))?.GetMethod("Create", BindingFlags.NonPublic | BindingFlags.Static);
            var fixture = new FallbackFixture(assembly, assembly.GetType("App.Items")!, item == "long" ? typeof(long) : item == "string" ? typeof(string) : typeof(int), services, bridge, bridge.GetMethod("Start")!, canonical, fused, wrapper);
            FallbackFixtures.Add(shape, fixture); return fixture;
        }
    }
    private sealed record FallbackResult(string[] Values, string[] Trace, int Starts, int Moves, int Reads, int Disposals,
        int Effects, int BeforeResolves, int AfterResolves, int WrapperFinals, int MoveConsumptions, int DisposeConsumptions,
        string Error, bool Canceled, bool Faulted);
    private static async Task<FallbackResult> RunFallback<T>(FallbackFixture f, string backend, string? wrapperMode = null, bool pending = false, bool failure = false, bool context = false,
        Action<string, object?, CancellationToken>? observe = null)
    {
        using var api = new CancellationTokenSource(); using var replacement = new CancellationTokenSource();
        var p = new CanonicalStreamFallbackProbe { WrapperMode = wrapperMode, ReplacementToken = replacement.Token, ApiToken = api.Token,
            Pending = pending, PendingFailure = failure, Context = context, Observe = observe, Input = pending || wrapperMode == "two-open" ? [1] : [-1, 1, 2] };
        var request = Activator.CreateInstance(f.Request, [1])!; p.Outer = request;
        var before = CanonicalStreamFallbackProbe.Ambient.Value; CanonicalStreamFallbackProbe.Ambient.Value = "caller";
        var collection = new ServiceCollection(); var roles = f.Wrapper ? new[] { "H", "F1", "W", "F2" } : new[] { "H", "F1", "Map", "F2" };
        for (var i = 0; i < f.Services.Length; i++)
        { var type = f.Services[i]; var role = roles[i]; collection.AddTransient(type, _ => { p.Resolve(role); return Activator.CreateInstance(type, p)!; }); }
        using var services = collection.BuildServiceProvider(); IAsyncEnumerable<T> stream;
        if (backend == "reference")
        {
            var bridge = Activator.CreateInstance(f.Bridge, services)!;
            var start = f.Start.CreateDelegate(typeof(Func<,,>).MakeGenericType(f.Request, typeof(CancellationToken), typeof(IAsyncEnumerable<T>)), bridge);
            var reference = f.Wrapper ? typeof(CanonicalStreamWrapperReference).GetMethod("FilterWrapperFilter")! : typeof(CanonicalStreamItemReference).GetMethod("FilterMapFilter")!;
            stream = (IAsyncEnumerable<T>)reference.MakeGenericMethod(f.Request, typeof(T), f.Services[1], f.Services[2], f.Services[3])
                .Invoke(null, [services, request, start, api.Token, null])!;
        }
        else stream = (IAsyncEnumerable<T>)(backend == "fused" ? f.Fused! : f.Canonical).Invoke(null, [services, request, api.Token])!;
        var cursor = stream.GetAsyncEnumerator(); var values = new List<string>(); Exception? error = null; bool canceled = false, faulted = false;
        try
        {
            Assert.Equal(0, p.Starts); Assert.Empty(p.Trace);
            for (var i = 0; i < 5; i++)
            {
                var move = cursor.MoveNextAsync(); var task = move.AsTask();
                if (pending && i == 0)
                {
                    Assert.False(task.IsCompleted); Assert.Equal(0, p.Reads); Assert.Equal(0, p.MoveGate.Consumptions);
                    Assert.Throws<InvalidOperationException>(() => cursor.MoveNextAsync());
                    Assert.Throws<InvalidOperationException>(() => cursor.DisposeAsync());
                    Assert.Throws<InvalidOperationException>(() => cursor.Current);
                    if (failure) p.MoveGate.Fail(p.SourceError); else p.MoveGate.Complete(true);
                }
                try
                {
                    var hasItem = await task.WaitAsync(TimeSpan.FromSeconds(5));
                    Assert.Equal("caller", CanonicalStreamFallbackProbe.Ambient.Value);
                    if (!hasItem) break; values.Add(Convert.ToString(cursor.Current, System.Globalization.CultureInfo.InvariantCulture)!);
                    var reads = p.Reads; _ = cursor.Current; Assert.Equal(reads, p.Reads);
                }
                catch (Exception e) { error = e; canceled = task.IsCanceled; faulted = task.IsFaulted; break; }
                if (pending || wrapperMode == "early-break") break;
            }
            var dispose = cursor.DisposeAsync().AsTask();
            if (pending && !failure) { Assert.False(dispose.IsCompleted); Assert.Equal(0, p.DisposeGate.Consumptions); p.DisposeGate.Complete(true); }
            await dispose.WaitAsync(TimeSpan.FromSeconds(5)); await cursor.DisposeAsync();
            if (pending) { Assert.Equal(1, p.MoveGate.Consumptions); Assert.Equal(failure ? 0 : 1, p.DisposeGate.Consumptions); }
            if (failure) Assert.Same(p.CleanupError, error); else Assert.Null(error);
            return new([.. values], [.. p.Trace], p.Starts, p.Moves, p.Reads, p.Disposals, p.Effects, p.BeforeResolves, p.AfterResolves,
                p.WrapperFinals, p.MoveGate.Consumptions, p.DisposeGate.Consumptions, error == null ? "none" : ReferenceEquals(error, p.CleanupError) ? "cleanup" : "unexpected", canceled, faulted);
        }
        finally { CanonicalStreamFallbackProbe.Ambient.Value = before; }
    }
    private static void FallbackEvidence(string label, params FallbackResult[] results)
    {
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(evidence)) File.WriteAllText(Path.Combine(evidence, "stream-fallback", "observations-" + label + ".json"),
            System.Text.Json.JsonSerializer.Serialize(new { label, results, candidateOnly = true, productionEnabled = false }));
    }
    private static void EqualFallback(FallbackResult expected, FallbackResult actual)
    { Assert.Equal(expected.Values, actual.Values); Assert.Equal(expected.Trace, actual.Trace); Assert.Equal(expected with { Values = actual.Values, Trace = actual.Trace }, actual); }

    [Theory]
    [InlineData("long")] [InlineData("string")]
    public async Task Stream_fallback_closed_item_representatives_match_reference_and_selected_context(string shape)
    {
        var f = FallbackFixtureFor(shape);
        var reference = shape == "long" ? await RunFallback<long>(f, "reference", context: true) : await RunFallback<string>(f, "reference");
        var canonical = shape == "long" ? await RunFallback<long>(f, "canonical", context: true) : await RunFallback<string>(f, "canonical");
        FallbackEvidence(shape, reference, canonical); EqualFallback(reference, canonical);
        Assert.Equal(new[] { "2" }, canonical.Values); Assert.Equal(1, canonical.Disposals);
    }
    [Fact]
    public async Task Stream_fallback_selected_shape_comparison_keeps_fused_priority_and_matches_canonical_and_reference()
    {
        var f = FallbackFixtureFor("int"); Assert.NotNull(f.Fused);
        var reference = await RunFallback<int>(f, "reference"); var canonical = await RunFallback<int>(f, "canonical"); var fused = await RunFallback<int>(f, "fused");
        FallbackEvidence("selected-triple", reference, canonical, fused); EqualFallback(reference, canonical); EqualFallback(canonical, fused);
    }
    [Fact]
    public async Task Stream_fallback_map_before_filter_keeps_declared_effects_and_cached_current()
    {
        var f = FallbackFixtureFor("order"); Assert.Null(f.Fused);
        var canonical = await RunFallback<int>(f, "canonical"); FallbackEvidence("map-first", canonical);
        Assert.Equal(new[] { "2" }, canonical.Values); Assert.Equal(3, canonical.Effects);
        Assert.Equal(4, canonical.Moves); Assert.Equal(3, canonical.Reads); Assert.Equal(1, canonical.Disposals);
        Assert.True(Array.FindIndex(canonical.Trace, t => t.StartsWith("Map:1:-1:", StringComparison.Ordinal)) <
            Array.FindIndex(canonical.Trace, t => t.StartsWith("F1:1:-2:", StringComparison.Ordinal)));
    }
    [Theory]
    [InlineData("empty")] [InlineData("alternative")] [InlineData("two-open")] [InlineData("direct")] [InlineData("early-break")]
    public async Task Stream_fallback_opaque_wrapper_representatives_match_reference_and_preserve_ownership(string mode)
    {
        var f = FallbackFixtureFor("wrapper"); Assert.Null(f.Fused);
        var reference = await RunFallback<int>(f, "reference", mode); var canonical = await RunFallback<int>(f, "canonical", mode);
        FallbackEvidence("wrapper-" + mode, reference, canonical); EqualFallback(reference, canonical);
        Assert.Equal(mode is "empty" or "alternative" ? 0 : mode == "two-open" ? 2 : 1, canonical.Starts);
        Assert.Equal(canonical.Starts, canonical.Disposals);
        Assert.Equal(mode == "empty" ? 0 : 1, canonical.AfterResolves);
        if (mode == "two-open") { Assert.Equal(2, canonical.BeforeResolves); Assert.Equal(new[] { "1", "1" }, canonical.Values); }
        if (mode == "direct") Assert.Equal(0, canonical.WrapperFinals);
        if (mode == "early-break") { Assert.Equal(new[] { "1" }, canonical.Values); Assert.Equal(2, canonical.Moves); }
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Stream_fallback_real_pending_results_consume_once_and_keep_cleanup_error_priority(bool failure)
    {
        var f = FallbackFixtureFor("int");
        var reference = await RunFallback<int>(f, "reference", pending: true, failure: failure);
        var canonical = await RunFallback<int>(f, "canonical", pending: true, failure: failure);
        FallbackEvidence("pending-" + failure, reference, canonical); EqualFallback(reference, canonical);
        Assert.Equal(1, canonical.Disposals); Assert.Equal(failure, canonical.Faulted); Assert.False(canonical.Canceled);
    }
    [Fact]
    public void Stream_fallback_value_receiver_remains_diagnosed_outside_this_backend()
    {
        var run = RunStreamConnection(StreamGraphFixture().Replace("sealed class ItemsHandler", "struct ItemsHandler"), "value-receiver-pending");
        var connection = Assert.Single(StreamConnections(run.Driver).Routes);
        Assert.Equal(CanonicalStreamConnectionKind.CanonicalPending, connection.Kind); Assert.Null(connection.Canonical); Assert.Null(connection.Candidate);
        Assert.Equal("ZEN0037", Assert.Single(run.Diagnostics).Id);
        Assert.DoesNotContain(run.Driver.GetRunResult().Results.Single().GeneratedSources, f => f.HintName.StartsWith("Zendiator.StreamCanonical.", StringComparison.Ordinal));
    }
}
