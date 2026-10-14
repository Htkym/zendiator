using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Zendiator.Generator.Tests;

public sealed class CapabilityIntegrationProbe : CapabilityProbe
{
    public readonly List<object> CreatedHooks = [], ReleasedHooks = [], CreatedHandlers = [], ReleasedHandlers = [];
    public bool Skip; public Exception? HookCleanupError, HandlerCleanupError; public int HookFactoryDisposals, HandlerFactoryDisposals;
    public H CreateHook<H>(object r, CancellationToken t, Func<H> create) where H : class { Hit("hook.factory", r, t); var h = create(); CreatedHooks.Add(h); return h; }
    public ValueTask<H> CreateHookAsync<H>(object r, CancellationToken t, Func<H> create) where H : class => new(CreateHook(r, t, create));
    public H CreateHandler<H>(object r, CancellationToken t, Func<H> create) where H : class { Hit("handler.factory", r, t); var h = create(); CreatedHandlers.Add(h); return h; }
    public ValueTask<H> CreateHandlerAsync<H>(object r, CancellationToken t, Func<H> create) where H : class => new(CreateHandler(r, t, create));
    public void ReleaseHook(object h) { Assert.DoesNotContain(ReleasedHooks, x => ReferenceEquals(h, x)); ReleasedHooks.Add(h); Trace.Add("hook.cleanup"); if (HookCleanupError != null) throw HookCleanupError; }
    public ValueTask ReleaseHookAsync(object h) { ReleaseHook(h); return default; }
    public void ReleaseHandler(object h) { Assert.DoesNotContain(ReleasedHandlers, x => ReferenceEquals(h, x)); ReleasedHandlers.Add(h); Trace.Add("handler.cleanup"); if (HandlerCleanupError != null) throw HandlerCleanupError; }
    public ValueTask ReleaseHandlerAsync(object h) { ReleaseHandler(h); if (Pending != "handler.cleanup") return default; var g = new AsyncOnceGate<int>(0); Gates.Add(g); return g.Void; }
    public void DisposeKFactory() { Trace.Add("hook-factory.dispose"); HookFactoryDisposals++; }
    public void DisposeHFactory() { Trace.Add("handler-factory.dispose"); HandlerFactoryDisposals++; }
}

public sealed partial class GeneratorTests
{
    private sealed record CapabilityIntegrationFixture(Assembly Assembly, Type Mediator, Type Request, MethodInfo Register, MethodInfo Dispatch, MethodInfo Reference);
    private static readonly ConcurrentDictionary<(bool Async, bool Void, string Pattern, string Hook, string Handler, bool Wrapper), Lazy<CapabilityIntegrationFixture>> IntegrationFixtures = new();
    private static string CapabilityIntegrationSource(bool async, bool isVoid, string pattern, string hookMode, string handlerMode, bool wrapper)
    {
        var source = CapabilitySource(async, isVoid, pattern, false); source = source[..source.IndexOf("namespace App {public static class Oracle", StringComparison.Ordinal)];
        source = source.Replace("global::Zendiator.Generator.Tests.CapabilityProbe", "global::Zendiator.Generator.Tests.CapabilityIntegrationProbe");
        if (hookMode != "direct") source = source.Replace("Stage.Hook<Hook0>()", "Stage.Hook<Hook0,KFactory>()");
        if (handlerMode != "direct") source = source.Replace("typeof(Handler)", "HandlerEntry.Session<Handler,HFactory>()");
        var disposable = async ? "IAsyncDisposable" : "IDisposable";
        if (hookMode != "direct") source = source.Replace("public sealed class Hook0:", "public sealed class Hook0:" + disposable + ",").Replace("private readonly Probe _p;public Hook0", (async ? "public ValueTask DisposeAsync()=>_p.ReleaseHookAsync(this);" : "public void Dispose()=>_p.ReleaseHook(this);") + "private readonly Probe _p;public Hook0");
        if (handlerMode != "direct") source = source.Replace(",IDisposable{", "," + disposable + "{").Replace("public void Dispose()=>_p.Dispose();", async ? "public ValueTask DisposeAsync()=>_p.ReleaseHandlerAsync(this);" : "public void Dispose()=>_p.ReleaseHandler(this);");
        string Factory(string mode, string type, string name, bool hook)
        {
            if (mode == "direct") return ""; var a = mode.StartsWith("async-", StringComparison.Ordinal); var role = "I" + (a ? "Async" : "") + (mode.EndsWith("owned", StringComparison.Ordinal) ? "Owned" : "Borrowed") + "SessionFactory";
            return $"namespace App{{public sealed class {name}:{role}<Ping,{type}>,IDisposable{{private readonly Probe _p;public {name}(Probe p){{_p=p;}}public {(a ? "ValueTask<" + type + ">" : type)} {(a ? "CreateAsync" : "Create")}(Ping r,CancellationToken t)=>_p.Create{(hook ? "Hook" : "Handler")}{(a ? "Async" : "")}(r,t,()=>new {type}(_p));public void Dispose()=>_p.Dispose{(hook ? "K" : "H")}Factory();}}}}";
        }
        source += Factory(hookMode, "Hook0", "KFactory", true) + Factory(handlerMode, "Handler", "HFactory", false);
        if (wrapper)
        {
            source = source.Replace("Stage.Hook<Hook0>()", "Stage.Hook<Hook0>(),Stage.Wrapper<W>(),Stage.Hook<Hook1>()"); var next = async ? "IAsyncSendContinuation" : "ISendContinuation"; var res = isVoid ? "" : ",int"; var call = async ? "InvokeAsync" : "Invoke"; var ret = async ? (isVoid ? "ValueTask" : "ValueTask<int>") : (isVoid ? "void" : "int");
            source += $$"""
                namespace App{public sealed class W:{{(async ? "IAsyncSendWrapper" : "ISendWrapper")}}<Ping{{res}}>{
                    private readonly Probe _p;public W(Probe p){_p=p;}
                    public {{(async ? "async " : "")}}{{ret}} {{call}}<TNext>(Ping r,TNext next,CancellationToken t)where TNext:{{next}}<Ping{{res}}>{
                        _p.Trace.Add("wrapper.before");if(_p.Skip){_p.Trace.Add("wrapper.skip");{{(isVoid ? "return;" : "return 91;")}}}
                        {{(isVoid ? "" : "var a=")}}{{(async ? "await " : "")}}next.{{call}}(new Ping(10),t){{(async ? ".ConfigureAwait(false)" : "")}};
                        {{(isVoid ? "" : "var b=")}}{{(async ? "await " : "")}}next.{{call}}(new Ping(20),t){{(async ? ".ConfigureAwait(false)" : "")}};
                        _p.Trace.Add("wrapper.after");{{(isVoid ? "" : "return a+b;")}}
                    }
                } }
                """;
        }
        return source + CanonicalCapabilityReference.Source(async, isVoid, pattern[0].ToString(), pattern[1].ToString(), pattern[2].ToString(), pattern[3].ToString(), false, hookMode, handlerMode, wrapper);
    }
    private static CapabilityIntegrationFixture IntegrationFixture(bool async, bool isVoid, string pattern, string hook, string handler, bool wrapper) => IntegrationFixtures.GetOrAdd((async, isVoid, pattern, hook, handler, wrapper), k => new(() =>
    {
        var source = CapabilityIntegrationSource(k.Async, k.Void, k.Pattern, k.Hook, k.Handler, k.Wrapper); var d = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "Integration_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics); Assert.Empty(diagnostics); Assert.Empty(output.GetDiagnostics().Where(x => x.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)); using var bytes = new MemoryStream(); var emitted = output.Emit(bytes); Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics)); var sources = d.GetRunResult().Results.Single().GeneratedSources; AssertCanonicalOutputRoles(sources);
        if (k.Pattern[3] == '-') { var body = CanonicalRouteSource(sources); Assert.DoesNotContain("var success", body); Assert.DoesNotContain("global::System.Exception? error", body); }
        var e = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR"); if (!string.IsNullOrEmpty(e)) { var p = Path.Combine(e, "integration-" + k.Async + "-" + k.Void + "-" + k.Pattern + "-" + k.Hook + "-" + k.Handler + "-" + k.Wrapper); Directory.CreateDirectory(p); File.WriteAllText(Path.Combine(p, "input.cs"), source); foreach (var s in sources) File.WriteAllText(Path.Combine(p, s.HintName), s.SourceText.ToString()); File.WriteAllBytes(Path.Combine(p, "generated.dll"), bytes.ToArray()); File.WriteAllText(Path.Combine(p, "compile.json"), JsonSerializer.Serialize(new { emitted.Success, productionEnabled = true, candidateOnly = false, warnings = 0, errors = 0, generatorErrors = diagnostics.Length })); }
        bytes.Position = 0; var a = AssemblyLoadContext.Default.LoadFromStream(bytes); var b = a.GetType("App.Bridge")!; return new(a, a.GetType("App.Generated.Reads.Zendiator")!, a.GetType("App.Ping")!, b.GetMethod("Register")!, b.GetMethod("Dispatch")!, a.GetType("App.Oracle")!.GetMethod("Send")!);
    })).Value;
    public static IEnumerable<object[]> CapabilityIntegrationShapes()
    {
        foreach (var v in new[] { false, true })
        {
            yield return [false, v, "s--s", "owned", "owned", false]; yield return [false, v, "--ss", "borrowed", "owned", false];
            yield return [true, v, "saas", "async-owned", "async-owned", false]; yield return [true, v, "a--s", "async-owned", "async-borrowed", false];
            yield return [true, v, "--as", "async-borrowed", "async-owned", false]; yield return [true, v, "---a", "async-borrowed", "async-borrowed", false];
            yield return [true, v, "--a-", "owned", "async-owned", false]; yield return [false, v, "-s-s", "direct", "owned", true];
            yield return [true, v, "saas", "direct", "async-owned", true]; yield return [true, v, "---s", "direct", "async-borrowed", true];
        }
    }
    [Theory]
    [MemberData(nameof(CapabilityIntegrationShapes))]
    public async Task Integrated_capabilities_session_and_Wrapper_API_preserve_missing_phase_state_and_nested_ownership(bool async, bool isVoid, string pattern, string hookMode, string handlerMode, bool wrapper)
    { var f = IntegrationFixture(async, isVoid, pattern, hookMode, handlerMode, wrapper); foreach (var generated in new[] { false, true }) await RunIntegration(f, async, isVoid, pattern, hookMode, handlerMode, wrapper, generated, "success"); }
    [Theory]
    [InlineData(false, "handler-cleanup")]
    [InlineData(true, "hook-cleanup")]
    [InlineData(false, "observer")]
    [InlineData(true, "finally")]
    [InlineData(false, "oce")]
    public async Task Integrated_capabilities_failure_priority_preserves_inner_observation_and_outer_cleanup(bool isVoid, string scenario)
    { var f = IntegrationFixture(true, isVoid, "saas", "async-owned", "async-owned", false); foreach (var generated in new[] { false, true }) await RunIntegration(f, true, isVoid, "saas", "async-owned", "async-owned", false, generated, scenario); }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Integrated_capabilities_Wrapper_zero_next_does_not_acquire_inner_handler_session(bool isVoid)
    { var f = IntegrationFixture(true, isVoid, "saas", "direct", "async-owned", true); foreach (var generated in new[] { false, true }) await RunIntegration(f, true, isVoid, "saas", "direct", "async-owned", true, generated, "skip"); }
    [Theory]
    [InlineData(false, "handler.cleanup")]
    [InlineData(true, "h0.after")]
    public async Task Integrated_capabilities_owned_lease_or_callback_real_wait_preserves_phase_order_and_single_consumption(bool isVoid, string pending)
    { var f = IntegrationFixture(true, isVoid, "saas", "async-owned", "async-owned", false); foreach (var generated in new[] { false, true }) await RunIntegration(f, true, isVoid, "saas", "async-owned", "async-owned", false, generated, "success", pending); }

    private static async Task RunIntegration(CapabilityIntegrationFixture f, bool async, bool isVoid, string pattern, string hookMode, string handlerMode, bool wrapper, bool generated, string scenario, string? pending = null)
    {
        var p = new CapabilityIntegrationProbe { Skip = scenario == "skip", Pending = pending }; var body = new Exception("body"); var inner = new Exception("inner"); var outer = new Exception("outer"); var observer = new Exception("observer"); var final = new Exception("finally"); using var foreign = new CancellationTokenSource(); var oce = new OperationCanceledException("identity", foreign.Token);
        if (scenario != "success" && scenario != "skip") p.Failures["handler"] = body;
        if (scenario is "handler-cleanup" or "observer" or "finally" or "hook-cleanup") p.HandlerCleanupError = inner; if (scenario == "hook-cleanup") p.HookCleanupError = outer; if (scenario == "observer") p.Failures["h0.exception"] = observer; if (scenario == "finally") p.Failures["h0.finally"] = final; if (scenario == "oce") p.HandlerCleanupError = oce;
        try
        {
            IServiceCollection services = new ServiceCollection(); services.AddSingleton(p); f.Register.Invoke(null, [services]); await using var provider = services.BuildServiceProvider(); var request = Activator.CreateInstance(f.Request, [10])!; object? value = null; Exception? error = null;
            await using (var scope = provider.CreateAsyncScope())
            {
                object? Invoke() { try { return generated ? f.Dispatch.Invoke(null, [scope.ServiceProvider.GetRequiredService(f.Mediator), request, p.Cancellation.Token]) : f.Reference.Invoke(null, [scope.ServiceProvider, request, p.Cancellation.Token]); } catch (TargetInvocationException e) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException!).Throw(); throw; } }
                if (async) { var task = AsyncTask(Invoke()!, isVoid); if (pending != null) { Assert.False(task.IsCompleted); var g = Assert.Single(p.Gates); Assert.Equal(0, g.Consumptions); g.Complete(); } try { await task.WaitAsync(TimeSpan.FromSeconds(10)); if (!isVoid) value = await (Task<int>)task; } catch (Exception e) { error = e; } Assert.Equal(error is OperationCanceledException, task.IsCanceled); Assert.Equal(error != null && error is not OperationCanceledException, task.IsFaulted); } else { try { value = Invoke(); } catch (Exception e) { error = e; } }
                Assert.All(p.Gates, g => Assert.Equal(1, g.Consumptions));
                var expected = scenario switch { "handler-cleanup" => inner, "hook-cleanup" => outer, "observer" => observer, "finally" => final, "oce" => oce, _ => null }; Assert.Same(expected, error);
                if (error == null && !isVoid) { var count = wrapper ? 2 : 1; var input = wrapper ? 30 : 10; Assert.Equal(scenario == "skip" ? 91 + (pattern[1] != '-' ? 1 : 0) : input + (pattern[0] != '-' ? count : 0) + (pattern[1] != '-' ? count : 0) + (wrapper && pattern[1] != '-' ? 1 : 0), value); }
                var ownH = handlerMode.EndsWith("owned", StringComparison.Ordinal); var ownK = hookMode.EndsWith("owned", StringComparison.Ordinal); Assert.Equal(ownH ? p.CreatedHandlers.Count : 0, p.ReleasedHandlers.Count); Assert.Equal(ownK ? p.CreatedHooks.Count : 0, p.ReleasedHooks.Count);
                if (scenario is "handler-cleanup" or "hook-cleanup" or "observer" or "finally" or "oce") { Assert.Same(scenario == "oce" ? oce : inner, p.Calls.Single(c => c.Phase == "h0.exception").Error); if (pattern[3] != '-') Assert.Same(scenario == "observer" ? observer : scenario == "oce" ? oce : inner, p.Calls.Single(c => c.Phase == "h0.finally").Exit!.Value.Error); }
                if (wrapper) { Assert.Equal(scenario == "skip" ? 0 : 2, p.CreatedHandlers.Count); Assert.Equal(scenario == "skip" ? 0 : 2, p.Trace.Count(x => x == "handler")); }
                if (ownH && p.Trace.Contains("handler.cleanup")) { var index = p.Trace.IndexOf("handler.cleanup"); var after = p.Trace.FindIndex(index, x => x.EndsWith(".after", StringComparison.Ordinal) || x.EndsWith(".exception", StringComparison.Ordinal)); if (after >= 0) Assert.True(index < after); }
                if (ownK && pattern[3] != '-') Assert.True(p.Trace.IndexOf("h0.finally") < p.Trace.IndexOf("hook.cleanup"));
                Assert.Equal(IntegrationExpectedPhases(pattern, hookMode, handlerMode, wrapper, scenario), p.Trace);
                WriteNestedHookEvidence("integration-observations", async + "-" + isVoid + "-" + pattern + "-" + hookMode + "-" + handlerMode + "-" + wrapper + "-" + generated + "-" + scenario + "-" + (pending ?? "complete"), new { async, isVoid, pattern, hookMode, handlerMode, wrapper, generated, scenario, pending, value, error = error?.Message, trace = p.Trace.ToArray(), operationHookReleases = p.ReleasedHooks.Count, operationHandlerReleases = p.ReleasedHandlers.Count, createdHooks = p.CreatedHooks.Count, createdHandlers = p.CreatedHandlers.Count, consumptions = p.Gates.Select(g => g.Consumptions) }); p.HookCleanupError = null; p.HandlerCleanupError = null; p.Failures.Clear(); p.Pending = null;
            }
            foreach (var h in p.CreatedHandlers.Where(h => !p.ReleasedHandlers.Any(x => ReferenceEquals(x, h)))) { if (async) await ((IAsyncDisposable)h).DisposeAsync(); else ((IDisposable)h).Dispose(); }
            foreach (var h in p.CreatedHooks.Where(h => !p.ReleasedHooks.Any(x => ReferenceEquals(x, h)))) { if (async) await ((IAsyncDisposable)h).DisposeAsync(); else ((IDisposable)h).Dispose(); }
            Assert.Equal(p.CreatedHandlers.Count, p.ReleasedHandlers.Count); Assert.Equal(p.CreatedHooks.Count, p.ReleasedHooks.Count); WriteNestedHookEvidence("integration-lifetime", async + "-" + isVoid + "-" + pattern + "-" + hookMode + "-" + handlerMode + "-" + wrapper + "-" + generated + "-" + scenario + "-" + (pending ?? "complete"), new { async, isVoid, pattern, hookMode, handlerMode, wrapper, generated, scenario, pending, p.Trace, createdHooks = p.CreatedHooks.Count, releasedHooks = p.ReleasedHooks.Count, createdHandlers = p.CreatedHandlers.Count, releasedHandlers = p.ReleasedHandlers.Count, p.HookFactoryDisposals, p.HandlerFactoryDisposals });
        }
        finally { p.Cancellation.Dispose(); }
    }
    private static string[] IntegrationExpectedPhases(string pattern, string hook, string handler, bool wrapper, string scenario)
    {
        var result = new List<string>(); if (hook != "direct") result.Add("hook.factory"); if (pattern[0] != '-') result.Add("h0.before");
        void Inner(string id) { if (wrapper && pattern[0] != '-') result.Add(id + ".before"); if (handler != "direct") result.Add("handler.factory"); result.Add("handler"); if (handler.EndsWith("owned", StringComparison.Ordinal)) result.Add("handler.cleanup"); if (scenario == "success" && pattern[1] != '-') result.Add(id + ".after"); if (scenario != "success" && pattern[2] != '-') result.Add(id + ".exception"); if (pattern[3] != '-') result.Add(id + ".finally"); }
        if (wrapper) { result.Add("wrapper.before"); if (scenario == "skip") result.Add("wrapper.skip"); else { Inner("h1"); Inner("h1"); result.Add("wrapper.after"); } if (pattern[1] != '-') result.Add("h0.after"); if (pattern[3] != '-') result.Add("h0.finally"); }
        else Inner("h0"); if (hook.EndsWith("owned", StringComparison.Ordinal)) result.Add("hook.cleanup"); return result.ToArray();
    }
    [Fact]
    public void Integrated_capabilities_preserve_accepted_DI_partial_and_complete_session_and_Wrapper_generation_only()
    { CapabilityRuntimeFixture(true, false, "saas", false); MixedAsyncFixture("async-owned", "async-owned", false); WrapperRuntimeFixture(true, false, "async-owned", "both"); }
}
