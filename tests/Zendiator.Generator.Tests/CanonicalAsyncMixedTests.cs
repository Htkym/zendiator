using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Xunit;
using Microsoft.Extensions.DependencyInjection;

namespace Zendiator.Generator.Tests;

public sealed class AsyncMixedProbe
{
    public readonly AsyncHookProbe Core = new();
    public readonly List<object> Handlers = [], ReleasedHandlers = [];
    public Exception? HandlerFactoryError, HandlerCleanupError;
    public bool NullHandler, CancelHandlerFactory, CancelHandler;
    public int HookFactoryDisposals, HandlerFactoryDisposals;
    public void CheckToken(CancellationToken t) => Core.CheckToken(t);
    public void HookConstruct(object h, int id) => Core.HookConstruct(h, id);
    public void FactoryConstruct() => Core.Trace.Add("hook-factory.construct");
    public void HandlerFactoryConstruct() => Core.Trace.Add("handler-factory.construct");
    public void HandlerConstruct(object h) { Core.Trace.Add("handler.construct"); Handlers.Add(h); }
    public H Create<H>(object r, Func<H> create) where H : class => Core.Create(r, create);
    public ValueTask<H> CreateAsync<H>(object r, Func<H> create) where H : class => Core.CreateAsync(r, create);
    public H CreateHandler<H>(object r, Func<H> create) where H : class
    {
        Hit("handler.factory", r); if (CancelHandlerFactory) Core.Cancellation.Cancel();
        if (HandlerFactoryError != null) throw HandlerFactoryError; return NullHandler ? null! : create();
    }
    public ValueTask<H> CreateHandlerAsync<H>(object r, Func<H> create) where H : class
    { var h = CreateHandler(r, create); if (Core.Pending != "handler.factory") return new(h); var g = new AsyncOnceGate<H>(h); Core.Gates.Add(g); return g.Task; }
    private void Hit(string phase, object? r)
    { Core.Trace.Add(phase); Core.Calls.Add((phase, r, null, null, AsyncHookProbe.Ambient.Value)); AsyncHookProbe.Ambient.Value = phase; }
    public ValueTask<R> Before<R>(int id, R r, R next) where R : class => Core.Before(id, r, next);
    public ValueTask<int> After(int id, object? r, int v) => Core.After(id, r, v);
    public ValueTask AfterVoid(int id, object? r) => Core.AfterVoid(id, r);
    public ValueTask Observe(int id, object? r, Exception e) => Core.Observe(id, r, e);
    public ValueTask Finally(int id, object? r, SendExitInfo e) => Core.Finally(id, r, e);
    public ValueTask<int> Handle(object r, int v) { if (CancelHandler) Core.Cancellation.Cancel(); return Core.Handle(r, v); }
    public ValueTask HandleVoid(object r) { if (CancelHandler) Core.Cancellation.Cancel(); return Core.HandleVoid(r); }
    public ValueTask DisposeHook(object h, int id) => Core.DisposeHook(h, id);
    public ValueTask DisposeHandler(object h)
    {
        Assert.DoesNotContain(ReleasedHandlers, x => ReferenceEquals(x, h)); ReleasedHandlers.Add(h); Hit("handler.cleanup", null);
        if (HandlerCleanupError != null) throw HandlerCleanupError; if (Core.Pending != "handler.cleanup") return default;
        var g = new AsyncOnceGate<int>(0); Core.Gates.Add(g); return g.Void;
    }
    public void DisposeFactory() { Core.Trace.Add("hook-factory.dispose"); HookFactoryDisposals++; }
    public void DisposeHandlerFactory() { Core.Trace.Add("handler-factory.dispose"); HandlerFactoryDisposals++; }
}

public sealed partial class GeneratorTests
{
    private sealed record AsyncMixedFixture(AsyncHookFixture Hook, Type HandlerFactory);
    private static readonly ConcurrentDictionary<(string Hook, string Handler, bool Void), Lazy<AsyncMixedFixture>> AsyncMixedFixtures = new();
    private static string AsyncMixedSource(string hookMode, string handlerMode, bool isVoid)
    {
        var source = AsyncHookSource(hookMode, isVoid).Replace("global::Zendiator.Generator.Tests.AsyncHookProbe", "global::Zendiator.Generator.Tests.AsyncMixedProbe")
            .Replace("p.HandlerConstruct();", "p.HandlerConstruct(this);").Replace("_probe.DisposeHandler()", "_probe.DisposeHandler(this)")
            .Replace("typeof(Handler)", "HandlerEntry.Session<Handler,HFactory>()");
        var isAsync = handlerMode.StartsWith("async-", StringComparison.Ordinal); var role = "I" + (isAsync ? "Async" : "") + (handlerMode.EndsWith("owned", StringComparison.Ordinal) ? "Owned" : "Borrowed") + "SessionFactory";
        return source + $$"""
            namespace App {
                public class HFactoryBase:{{role}}<Ping,Handler>,IDisposable {
                    private readonly Probe _p;public HFactoryBase(Probe p){_p=p;p.HandlerFactoryConstruct();}
                    {{(isAsync ? "ValueTask<Handler>" : "Handler")}} {{role}}<Ping,Handler>.{{(isAsync ? "CreateAsync" : "Create")}}(Ping r,CancellationToken t){_p.CheckToken(t);return _p.{{(isAsync ? "CreateHandlerAsync" : "CreateHandler")}}(r,()=>new Handler(_p));}
                    void IDisposable.Dispose()=>_p.DisposeHandlerFactory();
                }
                public sealed class HFactory:HFactoryBase{public HFactory(Probe p):base(p){} }
            }
            """;
    }
    private static AsyncMixedFixture MixedAsyncFixture(string hookMode, string handlerMode, bool isVoid) => AsyncMixedFixtures.GetOrAdd((hookMode, handlerMode, isVoid), key => new(() =>
    {
        var source = AsyncMixedSource(key.Hook, key.Handler, key.Void); var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "AsyncMixed_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        Assert.Empty(diagnostics); Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        using var bytes = new MemoryStream(); var emitted = output.Emit(bytes); Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics)); var sources = driver.GetRunResult().Results.Single().GeneratedSources; AssertCanonicalOutputRoles(sources);
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR"); if (!string.IsNullOrEmpty(evidence))
        {
            var folder = Path.Combine(evidence, "async-mixed-" + key.Hook + "-" + key.Handler + "-" + key.Void); Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, "input.cs"), source);
            foreach (var s in sources) File.WriteAllText(Path.Combine(folder, s.HintName), s.SourceText.ToString()); File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
            File.WriteAllText(Path.Combine(folder, "compile.json"), JsonSerializer.Serialize(new { emitted.Success, productionEnabled = true, candidateOnly = false, warnings = 0, errors = 0, generatorErrors = diagnostics.Length }));
        }
        bytes.Position = 0; var a = AssemblyLoadContext.Default.LoadFromStream(bytes); var bridge = a.GetType("App.Bridge")!; var outer = a.GetType("App.Hook`1")!.MakeGenericType(a.GetType("App.OuterTag")!);
        return new(new(a, a.GetType("App.Generated.Reads.Zendiator")!, a.GetType("App.Ping")!, outer, a.GetType("App.Hook`1")!.MakeGenericType(a.GetType("App.InnerTag")!), a.GetType("App.Handler")!, a.GetType("App.Factory`1")?.MakeGenericType(a.GetType("App.OuterTag")!) ?? outer, bridge.GetMethod("Register")!, bridge.GetMethod("Dispatch")!), a.GetType("App.HFactory")!);
    })).Value;
    private static Type MixedLeaseType(string mode, Type r, Type s, Type factory)
    {
        if (mode == "direct") return typeof(DiReferenceLease<,>).MakeGenericType(r, s);
        return (mode switch { "owned" => typeof(OwnedReferenceLease<,,>), "borrowed" => typeof(BorrowedReferenceLease<,,>), "async-owned" => typeof(AsyncOwnedReferenceLease<,,>), _ => typeof(AsyncBorrowedReferenceLease<,,>) }).MakeGenericType(r, s, factory);
    }
    private static object InvokeAsyncMixed(AsyncMixedFixture f, string hookMode, string handlerMode, bool isVoid, bool generated, IServiceProvider p, object? r, CancellationToken t)
    {
        try
        {
            if (generated) return f.Hook.Dispatch.Invoke(null, [p.GetRequiredService(CanonicalEntryService(f.Hook.Mediator)), r, t])!;
            var args = new List<Type> { f.Hook.Request }; if (!isVoid) args.Add(typeof(int)); args.AddRange([f.Hook.Outer, f.Hook.Handler, MixedLeaseType(hookMode, f.Hook.Request, f.Hook.Outer, f.Hook.Factory), MixedLeaseType(handlerMode, f.Hook.Request, f.Hook.Handler, f.HandlerFactory)]);
            return typeof(CanonicalAsyncMixedReference).GetMethod(isVoid ? "SendVoid" : "SendResponse", BindingFlags.Static | BindingFlags.NonPublic)!.MakeGenericMethod(args.ToArray()).Invoke(null, [p, r, t])!;
        }
        catch (TargetInvocationException e) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException!).Throw(); throw; }
    }
    public static IEnumerable<object[]> AsyncMixedRepresentativeShapes()
    {
        foreach (var hk in new[] { "async-owned", "async-borrowed" }) foreach (var hh in new[] { "async-owned", "async-borrowed" }) foreach (var v in new[] { false, true }) yield return [hk, hh, v];
        yield return ["direct", "owned", false]; yield return ["direct", "borrowed", true];
        yield return ["owned", "async-borrowed", false]; yield return ["borrowed", "owned", true];
        yield return ["direct", "async-owned", true]; yield return ["direct", "async-borrowed", false];
    }
    [Theory]
    [MemberData(nameof(AsyncMixedRepresentativeShapes))]
    public async Task Async_mixed_sessions_standard_API_and_reference_preserve_inner_cleanup_before_Hook_exit(string hookMode, string handlerMode, bool isVoid)
    { var f = MixedAsyncFixture(hookMode, handlerMode, isVoid); foreach (var generated in new[] { false, true }) await RunAsyncMixedCase(f, hookMode, handlerMode, isVoid, generated, "success"); }
    [Theory]
    [InlineData("handler-body-oce")]
    [InlineData("handler-cleanup-over-body")]
    [InlineData("handler-cleanup-oce-over-body")]
    [InlineData("observer-over-inner")]
    [InlineData("finally-over-observer")]
    [InlineData("outer-cleanup-over-finally")]
    [InlineData("outer-cleanup-oce-over-inner")]
    [InlineData("outer-cleanup-after-success")]
    [InlineData("handler-factory-error")]
    [InlineData("handler-factory-oce")]
    [InlineData("handler-factory-null")]
    [InlineData("cancel-handler-factory")]
    [InlineData("cancel-handler")]
    [InlineData("cancel-before")]
    public async Task Async_mixed_sessions_inner_outer_failure_and_cancellation_priority_follow_existing_contract(string scenario)
    {
        var isVoid = scenario is "handler-body-oce" or "handler-cleanup-oce-over-body" or "finally-over-observer" or "outer-cleanup-oce-over-inner" or "handler-factory-oce" or "cancel-handler" or "cancel-before";
        var hookMode = scenario == "handler-factory-error" ? "direct" : "async-owned"; var handlerMode = scenario == "cancel-handler" ? "async-borrowed" : "async-owned";
        var f = MixedAsyncFixture(hookMode, handlerMode, isVoid); foreach (var generated in new[] { false, true }) await RunAsyncMixedCase(f, hookMode, handlerMode, isVoid, generated, scenario);
    }
    [Theory]
    [InlineData(false, "handler.factory")]
    [InlineData(true, "handler")]
    [InlineData(false, "handler.cleanup")]
    [InlineData(true, "h0.cleanup")]
    public async Task Async_mixed_sessions_single_real_await_keeps_nested_lease_cleanup_order_and_consumes_once(bool isVoid, string phase)
    { var f = MixedAsyncFixture("async-owned", "async-owned", isVoid); foreach (var generated in new[] { false, true }) await RunAsyncMixedCase(f, "async-owned", "async-owned", isVoid, generated, "success", phase); }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Async_mixed_sessions_pending_Handler_cleanup_failure_reaches_Hook_and_outer_cleanup(bool isVoid)
    { var f = MixedAsyncFixture("async-owned", "async-owned", isVoid); foreach (var generated in new[] { false, true }) await RunAsyncMixedCase(f, "async-owned", "async-owned", isVoid, generated, "pending-handler-cleanup-oce", "handler.cleanup"); }

    private static async Task RunAsyncMixedCase(AsyncMixedFixture f, string hookMode, string handlerMode, bool isVoid, bool generated, string scenario, string? pending = null)
    {
        var p = new AsyncMixedProbe(); var core = p.Core; core.Pending = pending; var ambient = AsyncHookProbe.Ambient.Value; using var foreign = new CancellationTokenSource();
        var body = new Exception("body"); var inner = new Exception("inner cleanup"); var observer = new Exception("observer"); var final = new Exception("finally"); var outer = new Exception("outer cleanup"); var factory = new Exception("handler factory"); var oce = new OperationCanceledException("identity", foreign.Token);
        var bodyFails = scenario is "handler-body-oce" or "handler-cleanup-over-body" or "handler-cleanup-oce-over-body" or "observer-over-inner" or "finally-over-observer" or "outer-cleanup-over-finally" or "outer-cleanup-oce-over-inner" or "pending-handler-cleanup-oce";
        if (bodyFails) core.Failures["handler"] = scenario == "handler-body-oce" ? oce : body;
        if (scenario is "handler-cleanup-over-body" or "observer-over-inner" or "finally-over-observer" or "outer-cleanup-over-finally" or "outer-cleanup-oce-over-inner") p.HandlerCleanupError = inner;
        if (scenario == "handler-cleanup-oce-over-body") p.HandlerCleanupError = oce;
        if (scenario is "observer-over-inner" or "finally-over-observer" or "outer-cleanup-over-finally") core.Failures["h0.exception"] = observer;
        if (scenario is "finally-over-observer" or "outer-cleanup-over-finally") core.Failures["h0.finally"] = final;
        if (scenario is "outer-cleanup-over-finally" or "outer-cleanup-after-success") core.Failures["h0.cleanup"] = outer;
        if (scenario == "outer-cleanup-oce-over-inner") core.Failures["h0.cleanup"] = oce;
        p.HandlerFactoryError = scenario == "handler-factory-error" ? factory : scenario == "handler-factory-oce" ? oce : null; p.NullHandler = scenario == "handler-factory-null";
        p.CancelHandlerFactory = scenario == "cancel-handler-factory"; p.CancelHandler = scenario == "cancel-handler"; core.CancelPhase = scenario == "cancel-before" ? "h0.before" : null;
        try
        {
            IServiceCollection services = new ServiceCollection(); services.AddSingleton(p); f.Hook.Register.Invoke(null, [services]); Assert.DoesNotContain(services, d => d.ServiceType == f.Hook.Handler); Assert.Contains(services, d => d.ServiceType == f.HandlerFactory && d.Lifetime == ServiceLifetime.Transient);
            if (hookMode != "direct") { Assert.DoesNotContain(services, d => d.ServiceType == f.Hook.Outer); Assert.Contains(services, d => d.ServiceType == f.Hook.Factory && d.Lifetime == ServiceLifetime.Transient); }
            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }); var request = Activator.CreateInstance(f.Hook.Request, [10])!;
            await using (var scope = provider.CreateAsyncScope())
            {
                AsyncHookProbe.Ambient.Value = "caller"; Assert.Throws<ArgumentNullException>(() => InvokeAsyncMixed(f, hookMode, handlerMode, isVoid, generated, scope.ServiceProvider, null, default)); Assert.Empty(core.Trace);
                var task = AsyncTask(InvokeAsyncMixed(f, hookMode, handlerMode, isVoid, generated, scope.ServiceProvider, request, core.Cancellation.Token), isVoid); Assert.Equal("caller", AsyncHookProbe.Ambient.Value);
                if (pending != null) { Assert.False(task.IsCompleted); var gate = Assert.Single(core.Gates); Assert.Equal(0, gate.Consumptions); if (scenario == "pending-handler-cleanup-oce") gate.Fail(oce); else gate.Complete(); }
                Exception? actual = null; try { await task.WaitAsync(TimeSpan.FromSeconds(10)); } catch (Exception e) { actual = e; }
                if (pending != null) Assert.Equal(1, Assert.Single(core.Gates).Consumptions);
                var observed = core.Calls.SingleOrDefault(c => c.Phase == "h0.exception").Error;
                var innerEscape = scenario == "pending-handler-cleanup-oce" ? oce : p.HandlerCleanupError ?? p.HandlerFactoryError ?? core.Failures.GetValueOrDefault("handler");
                if (scenario == "cancel-before" || scenario == "handler-factory-null") Assert.NotNull(observed); else Assert.Same(innerEscape, observed);
                var finalError = core.Failures.GetValueOrDefault("h0.exception") ?? observed; Assert.Same(finalError, core.Calls.Single(c => c.Phase == "h0.finally").Exit!.Value.Error);
                var expected = core.Failures.GetValueOrDefault("h0.cleanup") ?? core.Failures.GetValueOrDefault("h0.finally") ?? finalError;
                Assert.Same(expected, actual); Assert.Equal(actual is OperationCanceledException, task.IsCanceled); Assert.Equal(actual != null && actual is not OperationCanceledException, task.IsFaulted);
                if (ReferenceEquals(actual, oce)) Assert.Equal(foreign.Token, ((OperationCanceledException)actual!).CancellationToken);
                if (scenario == "cancel-before") Assert.Equal(core.Cancellation.Token, ((OperationCanceledException)actual!).CancellationToken);
                if (actual == null && !isVoid) Assert.Equal(12, await (Task<int>)task);
                var handlerOwned = handlerMode.EndsWith("owned", StringComparison.Ordinal); var hookOwned = hookMode.EndsWith("owned", StringComparison.Ordinal);
                Assert.Equal(handlerOwned ? p.Handlers.Count : 0, p.ReleasedHandlers.Count); Assert.Equal(hookOwned ? core.Hooks.Count : 0, core.ReleasedHooks.Count);
                Assert.Equal(ExpectedAsyncMixedTrace(hookMode, handlerMode, scenario), core.Trace);
                if (observed == null) { Assert.True(core.Calls.Single(c => c.Phase == "h0.finally").Exit!.Value.Success); Assert.Equal(handlerOwned ? "handler.cleanup" : "handler", core.Calls.Single(c => c.Phase == "h0.after").Ambient); }
                else Assert.False(core.Calls.Single(c => c.Phase == "h0.finally").Exit!.Value.Success);
                var callbackRequest = core.Calls.Single(c => c.Phase == "h0.finally").Request; Assert.NotSame(request, callbackRequest); Assert.Equal(11, (int)callbackRequest!.GetType().GetProperty("Value")!.GetValue(callbackRequest)!);
                Assert.All(core.Calls.Where(c => c.Phase is "handler.factory" or "handler" or "h0.after" or "h0.exception"), c => Assert.Same(callbackRequest, c.Request));
                WriteNestedHookEvidence("async-mixed-observations", hookMode + "-" + handlerMode + "-" + isVoid + "-" + generated + "-" + scenario + "-" + (pending ?? "complete"), new { hookMode, handlerMode, isVoid, generated, scenario, pending, trace = core.Trace.ToArray(), task.IsCanceled, task.IsFaulted, escapingIdentity = ReferenceEquals(expected, actual), observedError = observed?.Message, exitError = finalError?.Message, operationHandlerReleases = p.ReleasedHandlers.Count, operationHookReleases = core.ReleasedHooks.Count, consumptions = core.Gates.Select(g => g.Consumptions) });
                core.Pending = null; core.Failures.Clear(); p.HandlerCleanupError = null;
            }
            if (!handlerMode.EndsWith("owned", StringComparison.Ordinal)) foreach (var h in p.Handlers) await ((IAsyncDisposable)h).DisposeAsync();
            if (hookMode is "borrowed" or "async-borrowed") foreach (var k in core.Hooks) await ((IAsyncDisposable)k).DisposeAsync();
            Assert.Equal(p.Handlers.Count, p.ReleasedHandlers.Count); Assert.All(p.Handlers, h => Assert.Single(p.ReleasedHandlers, x => ReferenceEquals(x, h)));
            Assert.Equal(core.Hooks.Count, core.ReleasedHooks.Count); Assert.All(core.Hooks, h => Assert.Single(core.ReleasedHooks, x => ReferenceEquals(x, h)));
            var lifetime = ExpectedAsyncMixedTrace(hookMode, handlerMode, scenario).ToList(); if (core.Trace.Contains("handler-factory.construct")) lifetime.Add("handler-factory.dispose");
            if (hookMode == "direct") lifetime.Add("h0.cleanup"); else lifetime.Add("hook-factory.dispose");
            if (!handlerMode.EndsWith("owned", StringComparison.Ordinal)) foreach (var _ in p.Handlers) lifetime.Add("handler.cleanup");
            if (hookMode is "borrowed" or "async-borrowed") lifetime.Add("h0.cleanup"); Assert.Equal(lifetime, core.Trace);
            WriteNestedHookEvidence("async-mixed-ownership", hookMode + "-" + handlerMode + "-" + isVoid + "-" + generated + "-" + scenario + "-" + (pending ?? "complete"), new { hookMode, handlerMode, isVoid, generated, scenario, pending, core.Trace, handlers = p.Handlers.Count, releasedHandlers = p.ReleasedHandlers.Count, hooks = core.Hooks.Count, releasedHooks = core.ReleasedHooks.Count, p.HookFactoryDisposals, p.HandlerFactoryDisposals });
        }
        finally { core.Cancellation.Dispose(); AsyncHookProbe.Ambient.Value = ambient; }
    }
    private static string[] ExpectedAsyncMixedTrace(string hookMode, string handlerMode, string s)
    {
        var list = new List<string>(); if (hookMode != "direct") list.AddRange(["hook-factory.construct", "factory"]); list.AddRange(["h0.construct", "h0.before"]);
        if (s == "cancel-before") list.AddRange(["h0.exception", "h0.finally"]);
        else
        {
            list.AddRange(["handler-factory.construct", "handler.factory"]);
            var factoryFailed = s is "handler-factory-error" or "handler-factory-oce" or "handler-factory-null";
            if (!factoryFailed) { list.AddRange(["handler.construct", "handler"]); if (handlerMode.EndsWith("owned", StringComparison.Ordinal)) list.Add("handler.cleanup"); }
            var bodyFailed = s is not ("success" or "outer-cleanup-after-success" or "cancel-handler-factory" or "cancel-handler");
            list.Add(bodyFailed ? "h0.exception" : "h0.after"); list.Add("h0.finally");
        }
        if (hookMode.EndsWith("owned", StringComparison.Ordinal)) list.Add("h0.cleanup"); return list.ToArray();
    }

    [Fact]
    public void Async_mixed_sessions_two_Hooks_with_Handler_session_keep_diagnostic_and_no_execution()
    {
        var source = AsyncMixedSource("direct", "async-owned", false).Replace("Stage.Hook<Hook<OuterTag>>()", "Stage.Hook<Hook<OuterTag>>(),Stage.Hook<Hook<InnerTag>>()");
        var start = source.IndexOf("public static object Dispatch", StringComparison.Ordinal); source = source.Remove(start, source.IndexOf(';', start) - start + 1);
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "MixedUnsupported"), out var output, out var diagnostics);
        AssertAcceptedSendEmission(driver, diagnostics, global::Zendiator.SourceGenerator.BoundDispatchFamily.SendAsync, false); Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        AssertCanonicalOutputRoles(driver.GetRunResult().Results.Single().GeneratedSources);
        WriteNestedHookEvidence("async-mixed-boundary", "two-hooks", new { source, diagnostics = diagnostics.Select(d => new { d.Id, message = d.GetMessage() }), generatedExecution = true });
    }
    [Theory]
    [InlineData("direct", false)]
    [InlineData("async-owned", true)]
    [InlineData("two", false)]
    public void Async_mixed_sessions_preserve_existing_Hook_generation_only(string mode, bool isVoid) => HookAsyncFixture(mode, isVoid);
}
