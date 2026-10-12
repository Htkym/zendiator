using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    [Theory]
    [InlineData(false, "default")] [InlineData(true, "default")]
    [InlineData(false, "static")] [InlineData(true, "static")]
    [InlineData(false, "default-static")] [InlineData(true, "default-static")]
    [InlineData(false, "inferred")] [InlineData(true, "inferred")]
    public void Enabled_two_Hooks_public_registration_forms_compile_and_match_reference(bool isVoid, string form)
    {
        var named = form is not ("default" or "default-static"); var extension = form is not ("static" or "default-static");
        var source = NestedHookBackendSource(isVoid);
        if (!named) source = source.Replace("services.AddZendiator<Reads>", "services.AddZendiator").Replace("App.Generated.Reads", "Zendiator.Generated.Default");
        if (!extension) source = source.Replace("services.AddZendiator" + (named ? "<Reads>" : "") + "(c=>",
            "global::Zendiator.DependencyInjection.ZendiatorServiceCollectionExtensions.AddZendiator" + (named ? "<Reads>" : "") + "(services,c=>");
        if (form == "inferred") source = source.Replace("c.Pipeline<Ping>(DispatchFamily.SendSync,", "c.Pipeline<Ping>(");
        var fixture = CompileSyncFixture("direct", isVoid, named, extension, sourceOverride: source);
        const string golden = "A.ctor A.before B.ctor B.before handler.ctor handler B.after B.finally A.after A.finally";
        var reference = RunNestedHookScenario(isVoid, "success", golden, fixture);
        var generated = RunNestedHookScenario(isVoid, "success", golden, fixture, generated: true);
        Assert.Equal(reference, generated);
        WriteNestedHookEvidence("nested-hook-api", (isVoid ? "void-" : "response-") + form,
            new { isVoid, form, named, extension, reference = JsonSerializer.Deserialize<JsonElement>(reference), generated = JsonSerializer.Deserialize<JsonElement>(generated) });
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Unaccepted_async_two_Hooks_public_API_keeps_ZEN0037_and_emits_no_backend(bool isVoid)
    {
        var source = NestedHookBackendSource(isVoid, "async").Replace("ISyncRequest", "IRequest").Replace("DispatchFamily.SendSync", "DispatchFamily.SendAsync");
        source = isVoid ? source.Replace("void IRequestHandler<Ping>.Handle(Ping request,CancellationToken token) { _probe.Handle(request,token); }",
            "global::System.Threading.Tasks.ValueTask IRequestHandler<Ping>.HandleAsync(Ping request,CancellationToken token) { _probe.Handle(request,token); return default; }") :
            source.Replace("int IRequestHandler<Ping,int>.Handle(Ping request,CancellationToken token) { return _probe.Handle(request,token); }",
                "global::System.Threading.Tasks.ValueTask<int> IRequestHandler<Ping,int>.HandleAsync(Ping request,CancellationToken token)=>global::System.Threading.Tasks.ValueTask.FromResult(_probe.Handle(request,token));");
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "UnacceptedAsyncNestedHook" + isVoid), out var output, out var diagnostics);
        Assert.Equal("ZEN0037", Assert.Single(diagnostics).Id);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        Assert.DoesNotContain(driver.GetRunResult().Results.Single().GeneratedSources, s => s.HintName.StartsWith("Zendiator.Canonical", StringComparison.Ordinal));
        WriteNestedHookEvidence("nested-hook-async-boundaries", isVoid ? "void" : "response",
            new { isVoid, source, diagnostics = diagnostics.Select(d => new { d.Id, message = d.GetMessage() }), generatedSources = 0 });
    }

    public sealed class NestedOverlapOperation(string name, Type requestType, CancellationToken token)
    {
        public readonly string Name = name;
        public readonly object Input = Activator.CreateInstance(requestType, [name])!;
        public readonly object Outer = Activator.CreateInstance(requestType, [name + ".outer"])!;
        public readonly object Inner = Activator.CreateInstance(requestType, [name + ".inner"])!;
        public readonly CancellationToken Token = token;
        public readonly ConcurrentQueue<(string Phase, object Request, CancellationToken Token)> Calls = new();
        public readonly ConcurrentDictionary<string, Exception> Observed = new();
        public readonly ConcurrentDictionary<string, SendExitInfo> Exits = new();
        public Exception? BodyError, InnerFinallyError;
        public Barrier? Gate;
        public Action? Reenter;
    }

    // Thread-safe user callbacks for testing generated per-operation locals, not a runtime synchronization guarantee.
    public sealed class NestedOverlappingProbe
    {
        private readonly ConcurrentDictionary<object, NestedOverlapOperation> _operations = new(ReferenceEqualityComparer.Instance);
        public readonly ConcurrentDictionary<string, int> Constructions = new();
        public readonly ConcurrentDictionary<string, int> Disposals = new();
        public readonly ConcurrentQueue<string> Trace = new();
        public void Add(NestedOverlapOperation operation)
        { foreach (var request in new[] { operation.Input, operation.Outer, operation.Inner }) Assert.True(_operations.TryAdd(request, operation)); }
        public void Construct(string owner) => Constructions.AddOrUpdate(owner, 1, (_, count) => count + 1);
        public void DisposeService(string owner) => Disposals.AddOrUpdate(owner, 1, (_, count) => count + 1);
        private NestedOverlapOperation Record(string phase, object request, CancellationToken token)
        {
            var operation = _operations[request]; operation.Calls.Enqueue((phase, request, token)); Trace.Enqueue(operation.Name + "." + phase);
            return operation;
        }
        public object Before(string owner, object request, CancellationToken token)
        { var operation = Record(owner + ".before", request, token); return owner == "A" ? operation.Outer : operation.Inner; }
        public int After(string owner, object request, int response, CancellationToken token)
        { Record(owner + ".after", request, token); return response + 1; }
        public int Handle(object request, CancellationToken token)
        {
            var operation = Record("handler", request, token);
            if (operation.Gate != null && !operation.Gate.SignalAndWait(TimeSpan.FromSeconds(10))) throw new TimeoutException("The two Sends did not overlap at the handler.");
            Interlocked.Exchange(ref operation.Reenter, null)?.Invoke();
            if (operation.BodyError != null) throw operation.BodyError;
            return 41;
        }
        public void Observe(string owner, object request, Exception error, CancellationToken token)
        { var operation = Record(owner + ".onexception", request, token); Assert.True(operation.Observed.TryAdd(owner, error)); }
        public void Finish(string owner, object request, SendExitInfo exit, CancellationToken token)
        {
            var operation = Record(owner + ".finally", request, token); Assert.True(operation.Exits.TryAdd(owner, exit));
            if (owner == "B" && operation.InnerFinallyError != null) throw operation.InnerFinallyError;
        }
    }

    private static readonly ConcurrentDictionary<bool, SyncFixture> OverlappingNestedHookFixtures = new();
    private static SyncFixture OverlappingNestedHookFixture(bool isVoid) => OverlappingNestedHookFixtures.GetOrAdd(isVoid,
        key => CompileSyncFixture("direct", key, sourceOverride: NestedHookBackendSource(key).Replace(
            "GeneratorTests.NestedSendProbe;", "GeneratorTests.NestedOverlappingProbe;")));

    private static object AssertNestedOverlapOperation(NestedOverlapOperation operation, (object? Result, Exception? Error) actual, bool isVoid)
    {
        var bError = operation.BodyError; var aError = operation.InnerFinallyError ?? bError;
        var golden = bError != null ? "A.before B.before handler B.onexception B.finally A.onexception A.finally" :
            operation.InnerFinallyError != null ? "A.before B.before handler B.after B.finally A.onexception A.finally" :
            "A.before B.before handler B.after B.finally A.after A.finally";
        var calls = operation.Calls.ToArray(); Assert.Equal(golden.Split(' '), calls.Select(c => c.Phase));
        Assert.Same(aError, actual.Error); Assert.Equal(aError != null || isVoid ? null : (object)43, actual.Result);
        foreach (var call in calls)
        {
            Assert.Equal(operation.Token, call.Token);
            Assert.Same(call.Phase == "A.before" ? operation.Input : call.Phase == "B.before" || call.Phase.StartsWith("A.", StringComparison.Ordinal) ? operation.Outer : operation.Inner, call.Request);
        }
        foreach (var (owner, error) in new[] { ("A", aError), ("B", bError) })
        {
            if (error == null) Assert.False(operation.Observed.ContainsKey(owner));
            else { Assert.True(operation.Observed.TryGetValue(owner, out var observed)); Assert.Same(error, observed); }
            Assert.True(operation.Exits.TryGetValue(owner, out var exit)); Assert.Equal(error == null, exit.Success); Assert.Same(error, exit.Error);
        }
        string RequestRole(object request) => ReferenceEquals(request, operation.Input) ? "input" : ReferenceEquals(request, operation.Outer) ? "outer" : "inner";
        string? ErrorRole(Exception? error) => error == null ? null : ReferenceEquals(error, operation.BodyError) ? "body" :
            ReferenceEquals(error, operation.InnerFinallyError) ? "inner-finally" : "unexpected";
        return new { operation.Name, actual.Result, error = ErrorRole(actual.Error), errorType = actual.Error?.GetType().FullName,
            calls = calls.Select(c => new { c.Phase, request = RequestRole(c.Request), tokenPreserved = c.Token == operation.Token }),
            observed = operation.Observed.OrderBy(p => p.Key).Select(p => new { owner = p.Key, error = ErrorRole(p.Value) }),
            exits = operation.Exits.OrderBy(p => p.Key).Select(p => new { owner = p.Key, p.Value.Success, error = ErrorRole(p.Value.Error) }) };
    }

    private static async Task<string> RunOverlappingNestedHookEngine(bool isVoid, ServiceLifetime lifetime, bool generated, bool reentrant, bool variant)
    {
        var fixture = OverlappingNestedHookFixture(isVoid); var probe = new NestedOverlappingProbe();
        using var firstToken = new CancellationTokenSource(); using var secondToken = new CancellationTokenSource();
        using var barrier = new Barrier(2);
        var first = new NestedOverlapOperation("outer", fixture.Request, firstToken.Token);
        var second = new NestedOverlapOperation("inner", fixture.Request, secondToken.Token);
        var operations = new[] { first, second }; foreach (var operation in operations) probe.Add(operation);
        if (reentrant)
        {
            second.InnerFinallyError = new Exception("inner Finally identity");
            if (variant) first.BodyError = second.InnerFinallyError;
        }
        else
        {
            first.Gate = second.Gate = barrier;
            if (variant) { first.BodyError = new OperationCanceledException(firstToken.Token); second.InnerFinallyError = new Exception("second Finally identity"); }
            else second.BodyError = new Exception("second body identity");
        }
        IServiceCollection services = new ServiceCollection(); services.AddSingleton(probe);
        foreach (var type in NestedHookFixtureTypes(fixture)) services.Add(ServiceDescriptor.Describe(type, type, lifetime));
        Assert.Same(services, fixture.Register.Invoke(null, [services]));
        object[] snapshots;
        using (var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }))
        {
            using (var scope = provider.CreateScope())
            {
                var mediator = scope.ServiceProvider.GetRequiredService(fixture.Interface);
                (object? Result, Exception? Error) Invoke(NestedOverlapOperation operation) => generated ? InvokeSync(fixture, mediator, operation.Input, operation.Token) :
                    InvokeNestedHookReference(fixture, scope.ServiceProvider, operation.Input, operation.Token, isVoid);
                (object? Result, Exception? Error)[] actual;
                if (reentrant)
                {
                    (object? Result, Exception? Error) inner = default;
                    first.Reenter = () => { inner = Invoke(second); Assert.Same(second.InnerFinallyError, inner.Error); if (variant) throw inner.Error!; };
                    var outer = Invoke(first); actual = [outer, inner];
                    var outerEnd = variant ? "outer.B.onexception outer.B.finally outer.A.onexception outer.A.finally" : "outer.B.after outer.B.finally outer.A.after outer.A.finally";
                    Assert.Equal(("outer.A.before outer.B.before outer.handler inner.A.before inner.B.before inner.handler inner.B.after inner.B.finally inner.A.onexception inner.A.finally " + outerEnd).Split(' '), probe.Trace);
                }
                else
                {
                    var tasks = operations.Select(operation => Task.Factory.StartNew(() => Invoke(operation), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
                    actual = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));
                }
                snapshots = operations.Select((operation, i) => AssertNestedOverlapOperation(operation, actual[i], isVoid)).ToArray();
                Assert.Empty(probe.Disposals);
                foreach (var owner in new[] { "A", "B", "handler" }) Assert.Equal(lifetime == ServiceLifetime.Transient ? 2 : 1, probe.Constructions.GetValueOrDefault(owner));
            }
            foreach (var owner in new[] { "A", "B", "handler" }) Assert.Equal(lifetime == ServiceLifetime.Singleton ? 0 : lifetime == ServiceLifetime.Transient ? 2 : 1, probe.Disposals.GetValueOrDefault(owner));
        }
        foreach (var owner in new[] { "A", "B", "handler" }) Assert.Equal(lifetime == ServiceLifetime.Transient ? 2 : 1, probe.Disposals.GetValueOrDefault(owner));
        return JsonSerializer.Serialize(new { operations = snapshots, trace = reentrant ? probe.Trace.ToArray() : null,
            constructions = probe.Constructions.OrderBy(p => p.Key), disposals = probe.Disposals.OrderBy(p => p.Key) });
    }

    [Theory]
    [InlineData(false, ServiceLifetime.Scoped)] [InlineData(true, ServiceLifetime.Scoped)]
    [InlineData(false, ServiceLifetime.Transient)] [InlineData(true, ServiceLifetime.Transient)]
    [InlineData(false, ServiceLifetime.Singleton)] [InlineData(true, ServiceLifetime.Singleton)]
    public async Task Enabled_two_Hooks_parallel_Sends_keep_operation_state_separate(bool isVoid, ServiceLifetime lifetime)
    {
        foreach (var variant in new[] { false, true })
        {
            var reference = await RunOverlappingNestedHookEngine(isVoid, lifetime, false, false, variant);
            var generated = await RunOverlappingNestedHookEngine(isVoid, lifetime, true, false, variant);
            Assert.Equal(reference, generated);
            WriteNestedHookEvidence("nested-hook-overlap", "parallel-" + isVoid + "-" + lifetime + "-" + variant,
                new { isVoid, lifetime = lifetime.ToString(), variant, reference = JsonSerializer.Deserialize<JsonElement>(reference), generated = JsonSerializer.Deserialize<JsonElement>(generated) });
        }
    }

    [Theory]
    [InlineData(false, ServiceLifetime.Scoped)] [InlineData(true, ServiceLifetime.Scoped)]
    [InlineData(false, ServiceLifetime.Transient)] [InlineData(true, ServiceLifetime.Transient)]
    [InlineData(false, ServiceLifetime.Singleton)] [InlineData(true, ServiceLifetime.Singleton)]
    public async Task Enabled_two_Hooks_reentry_keeps_inner_failure_separate_from_outer(bool isVoid, ServiceLifetime lifetime)
    {
        foreach (var variant in new[] { false, true })
        {
            var reference = await RunOverlappingNestedHookEngine(isVoid, lifetime, false, true, variant);
            var generated = await RunOverlappingNestedHookEngine(isVoid, lifetime, true, true, variant);
            Assert.Equal(reference, generated);
            WriteNestedHookEvidence("nested-hook-overlap", "reentry-" + isVoid + "-" + lifetime + "-" + variant,
                new { isVoid, lifetime = lifetime.ToString(), variant, reference = JsonSerializer.Deserialize<JsonElement>(reference), generated = JsonSerializer.Deserialize<JsonElement>(generated) });
        }
    }
}
