using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private static readonly ConcurrentDictionary<bool, SyncFixture> SingleHookBackendFixtures = new();

    public sealed class ExternalFourPhaseHook<R, T> : ISendRequestTransform<R>, ISendResponseTransform<R, T>, ISendExceptionObserver<R>, ISendFinallyObserver<R>
    {
        public R Before(R request, CancellationToken token) => request;
        public T After(R request, T response, CancellationToken token) => response;
        public void OnException(R request, Exception error, CancellationToken token) { }
        public void Finally(R request, SendExitInfo exit, CancellationToken token) { }
    }
    public sealed class ExternalVoidFourPhaseHook<R> : ISendRequestTransform<R>, ISendSuccessObserver<R>, ISendExceptionObserver<R>, ISendFinallyObserver<R>
    {
        public R Before(R request, CancellationToken token) => request;
        public void After(R request, CancellationToken token) { }
        public void OnException(R request, Exception error, CancellationToken token) { }
        public void Finally(R request, SendExitInfo exit, CancellationToken token) { }
    }

    private static string SingleHookBackendSource(bool isVoid, string shape = "eligible")
    {
        var capabilities = new List<string>(); var methods = new List<string>();
        if (shape != "missing-before") { capabilities.Add("ISendRequestTransform<Ping>"); methods.Add("Ping ISendRequestTransform<Ping>.Before(Ping request,CancellationToken token)=>(Ping)_probe.Before(request,token)!;"); }
        if (shape != "missing-after")
        {
            capabilities.Add(isVoid ? "ISendSuccessObserver<Ping>" : "ISendResponseTransform<Ping,int>");
            methods.Add(isVoid ? "void ISendSuccessObserver<Ping>.After(Ping request,CancellationToken token)=>_probe.After(request,0,token);" :
                "int ISendResponseTransform<Ping,int>.After(Ping request,int response,CancellationToken token)=>_probe.After(request,response,token);");
        }
        if (shape != "missing-exception") { capabilities.Add("ISendExceptionObserver<Ping>"); methods.Add("void ISendExceptionObserver<Ping>.OnException(Ping request,Exception error,CancellationToken token)=>_probe.Observe(request,error,token);"); }
        if (shape != "missing-finally") { capabilities.Add("ISendFinallyObserver<Ping>"); methods.Add("void ISendFinallyObserver<Ping>.Finally(Ping request,SendExitInfo exit,CancellationToken token)=>_probe.Finish(request,exit,token);"); }
        var hook = "public " + (shape == "struct" ? "struct" : "class") + " HookBase<T> : " + string.Join(",", capabilities) + ",IDisposable where T:class { " +
            "private readonly Probe _probe; public HookBase(Probe probe) { _probe=probe; probe.ConstructHook(); } " + string.Join(" ", methods) + " void IDisposable.Dispose()=>_probe.HookDisposals++; }";
        if (shape != "struct") hook += " public sealed class Hook<T> : HookBase<T> where T:class { public Hook(Probe probe):base(probe){} }";
        var stageType = shape == "struct" ? "HookBase<string>" : "Hook<string>";
        var stageEntry = "Stage.Hook<" + stageType + ">()";
        var handlerEntry = "typeof(Handler<string>)";
        if (shape == "two-hooks") { hook += " public sealed class Hook2<T> : HookBase<T> where T:class { public Hook2(Probe probe):base(probe){} }"; stageEntry += ",Stage.Hook<Hook2<string>>()"; }
        if (shape == "handler-owned") { hook += " public sealed class HandlerFactory : IOwnedSessionFactory<Ping,Handler<string>> { public Handler<string> Create(Ping request,CancellationToken token)=>throw new NotImplementedException(); }"; handlerEntry = "HandlerEntry.Session<Handler<string>,HandlerFactory>()"; }
        if (shape == "hook-owned") { hook += " public sealed class HookFactory : IOwnedSessionFactory<Ping,Hook<string>> { public Hook<string> Create(Ping request,CancellationToken token)=>throw new NotImplementedException(); }"; stageEntry = "Stage.Hook<Hook<string>,HookFactory>()"; }
        if (shape == "wrapper")
        {
            hook += isVoid ? " public sealed class Wrapper : ISendWrapper<Ping> { public void Invoke<TNext>(Ping request,TNext next,CancellationToken token) where TNext:ISendContinuation<Ping> =>next.Invoke(request,token); }" :
                " public sealed class Wrapper : ISendWrapper<Ping,int> { public int Invoke<TNext>(Ping request,TNext next,CancellationToken token) where TNext:ISendContinuation<Ping,int> =>next.Invoke(request,token); }";
            stageEntry = "Stage.Wrapper<Wrapper>()";
        }
        if (shape == "external") stageEntry = isVoid ? "Stage.Hook<global::Zendiator.Generator.Tests.GeneratorTests.ExternalVoidFourPhaseHook<Ping>>()" :
            "Stage.Hook<global::Zendiator.Generator.Tests.GeneratorTests.ExternalFourPhaseHook<Ping,int>>()";
        var result = isVoid ? "" : ",int";
        return $$"""
            using System; using System.Threading; using Zendiator; using Zendiator.DependencyInjection; using Microsoft.Extensions.DependencyInjection;
            using Probe=global::Zendiator.Generator.Tests.GeneratorTests.ReferenceSendProbe;
            namespace App {
                public sealed record Ping : ISyncRequest{{(isVoid ? "" : "<int>")}};
                [ZendiatorHandler] public sealed partial class Handler<T> : ISyncRequestHandler<Ping{{result}}>,IDisposable where T:class {
                    private readonly Probe _probe; public Handler(Probe probe) { _probe=probe; probe.ConstructHandler(); }
                    {{(isVoid ? "void" : "int")}} ISyncRequestHandler<Ping{{result}}>.Handle(Ping request,CancellationToken token) { {{(isVoid ? "" : "return ")}}_probe.Handle(request,token); }
                    void IDisposable.Dispose()=>_probe.HandlerDisposals++;
                }
                {{hook}}
                [ZendiatorComposition("reads")] public sealed class Reads;
                public static class Bridge {
                    public static IServiceCollection Register(IServiceCollection services)=>services.AddZendiator<Reads>(c=>{
                        c.Handlers<Ping>(DispatchFamily.SendSync,{{handlerEntry}}); c.Pipeline<Ping>(DispatchFamily.SendSync,{{stageEntry}});
                    });
                    public static object? ResolveService(IServiceProvider provider)=>new Handler<string>(provider.GetRequiredService<Probe>());
                    {{(shape == "eligible" ? "public static object? Dispatch(object mediator,object? request,CancellationToken token) { " + (isVoid ? "" : "return ") + "((global::App.Generated.Reads.IZendiator)mediator).Send((Ping)request!,token); " + (isVoid ? "return null;" : "") + " }" : "")}}
                }
            }
            """;
    }

    private static SyncFixture SingleHookBackendFixture(bool isVoid) => SingleHookBackendFixtures.GetOrAdd(isVoid,
        key => CompileSyncFixture("direct", key, sourceOverride: SingleHookBackendSource(key)));

    private static (object? Result, Exception? Error) InvokeSingleHookReference(SyncFixture f, IServiceProvider provider, object? request, CancellationToken token, bool isVoid)
    {
        var hook = f.Assembly.GetType("App.Hook`1")!.MakeGenericType(typeof(string));
        var method = typeof(CanonicalSendSyncReference).GetMethod(isVoid ? "SendVoid" : "SendResponse", BindingFlags.Static | BindingFlags.NonPublic)!;
        method = method.MakeGenericMethod(isVoid ? [f.Request, hook, f.Service] : [f.Request, typeof(int), hook, f.Service]);
        try { return (method.Invoke(null, [provider, request, token]), null); }
        catch (TargetInvocationException error) { return (null, error.InnerException); }
    }

    private static string RunSingleHookEngine(SyncFixture f, bool isVoid, string scenario, string golden, bool generated)
    {
        var probe = new ReferenceSendProbe(); var ambient = ReferenceSendProbe.Ambient.Value; var culture = CultureInfo.CurrentCulture;
        ReferenceSendProbe.Ambient.Value = "caller"; CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var token = probe.Cancellation.Token;
        Exception body = scenario is "handler-oce" or "after-oce" ? new OperationCanceledException(token) : new Exception("body identity");
        var observer = new Exception("observer identity");
        Exception cleanup = scenario == "finally-oce-over-body" ? new OperationCanceledException(token) : new Exception("finally identity");
        object? request = scenario == "null-request" ? null : Activator.CreateInstance(f.Request);
        var replacement = Activator.CreateInstance(f.Request)!;
        probe.TransformRequest = scenario is "replace-request" or "before-null" or "before-error"; probe.Replacement = scenario == "before-null" ? null : replacement;
        if (scenario == "before-error") probe.BeforeError = body;
        if (scenario == "stage-ctor-error") probe.StageConstructorError = body;
        if (scenario == "handler-ctor-error") probe.HandlerConstructorError = body;
        if (scenario is "handler-error" or "handler-oce" or "observer-error-over-body" or "finally-error-over-body" or "finally-oce-over-body" or "finally-error-over-observer") probe.HandlerError = body;
        if (scenario is "after-error" or "after-oce") probe.AfterError = body;
        if (scenario is "observer-error-over-body" or "finally-error-over-observer") probe.ObserverError = observer;
        if (scenario.StartsWith("finally-", StringComparison.Ordinal)) probe.FinallyError = cleanup;
        probe.CancelBefore = scenario == "cancel-in-before"; probe.CancelHandler = scenario == "cancel-in-handler";
        probe.CancelAfter = scenario == "cancel-in-after"; probe.CancelFinally = scenario == "cancel-in-finally";
        if (scenario == "pre-cancel") probe.Cancellation.Cancel();
        string? ErrorRole(Exception? error) => error == null ? null : ReferenceEquals(error, body) ? "body" : ReferenceEquals(error, observer) ? "observer" :
            ReferenceEquals(error, cleanup) ? "finally" : error is ArgumentNullException ? "guard-null" : error is OperationCanceledException ? "guard-cancel" : "unexpected:" + error.GetType().FullName;
        string? RequestRole(object? value) => value == null ? null : ReferenceEquals(value, request) ? "input" : ReferenceEquals(value, replacement) ? "replacement" : "unexpected";
        try
        {
            IServiceCollection services = new ServiceCollection(); services.AddSingleton(probe);
            var hook = f.Assembly.GetType("App.Hook`1")!.MakeGenericType(typeof(string));
            if (scenario is "stage-di-error" or "handler-di-error") services.AddScoped(scenario == "stage-di-error" ? hook : f.Service,
                _ => { probe.Phases.Add(scenario == "stage-di-error" ? "stage.resolve" : "handler.resolve"); throw body; });
            Assert.Same(services, f.Register.Invoke(null, [services]));
            using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            string snapshot;
            using (var scope = provider.CreateScope())
            {
                var actual = generated ? InvokeSync(f, scope.ServiceProvider.GetRequiredService(f.Interface), request, token) :
                    InvokeSingleHookReference(f, scope.ServiceProvider, request, token, isVoid);
                Assert.Equal(golden.Split(' ', StringSplitOptions.RemoveEmptyEntries), probe.Phases);
                if (scenario is "before-null" or "cancel-in-before")
                { Assert.NotNull(actual.Error); Assert.Same(actual.Error, probe.ObservedError); Assert.Same(actual.Error, probe.Exit?.Error); }
                if (scenario is "null-request" or "pre-cancel") { Assert.Null(probe.ObservedError); Assert.Null(probe.Exit); }
                Assert.Equal(0, probe.HookDisposals); Assert.Equal(0, probe.HandlerDisposals);
                snapshot = JsonSerializer.Serialize(new { phases = probe.Phases.ToArray(), calls = probe.Calls.Select(c => new {
                    c.Phase, request = RequestRole(c.Request), tokenPreserved = c.Token == token, c.Ambient, c.Culture }),
                    actual.Result, error = ErrorRole(actual.Error), errorType = actual.Error?.GetType().FullName,
                    nullParameter = (actual.Error as ArgumentNullException)?.ParamName,
                    cancellationTokenPreserved = actual.Error is not OperationCanceledException oce || oce.CancellationToken == token,
                    observedError = ErrorRole(probe.ObservedError), exitSuccess = probe.Exit?.Success, exitError = ErrorRole(probe.Exit?.Error),
                    callerAmbient = ReferenceSendProbe.Ambient.Value, callerCulture = CultureInfo.CurrentCulture.Name, token.IsCancellationRequested });
            }
            Assert.Equal(probe.HookConstructed ? 1 : 0, probe.HookDisposals); Assert.Equal(probe.HandlerConstructed ? 1 : 0, probe.HandlerDisposals);
            return snapshot;
        }
        finally { probe.Cancellation.Dispose(); ReferenceSendProbe.Ambient.Value = ambient; CultureInfo.CurrentCulture = culture; }
    }

    [Theory]
    [MemberData(nameof(SingleHookReferenceCases))]
    public void Enabled_single_Hook_SendSync_matches_accepted_reference_and_golden(bool isVoid, string scenario, string golden)
    {
        var f = SingleHookBackendFixture(isVoid);
        var reference = RunSingleHookEngine(f, isVoid, scenario, golden, false);
        var generated = RunSingleHookEngine(f, isVoid, scenario, golden, true);
        Assert.Equal(reference, generated);
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(evidence))
        {
            var folder = Path.Combine(Path.GetDirectoryName(evidence)!, "hook-differential"); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, (isVoid ? "void-" : "response-") + scenario + ".json"),
                JsonSerializer.Serialize(new { isVoid, scenario, golden, reference = JsonSerializer.Deserialize<JsonElement>(reference), generated = JsonSerializer.Deserialize<JsonElement>(generated) }));
        }
    }

    public static IEnumerable<object[]> SingleHookLifetimeCases()
    {
        foreach (var isVoid in new[] { false, true }) foreach (var hook in new[] { ServiceLifetime.Scoped, ServiceLifetime.Transient, ServiceLifetime.Singleton })
            foreach (var handler in new[] { ServiceLifetime.Scoped, ServiceLifetime.Transient, ServiceLifetime.Singleton }) yield return [isVoid, hook, handler];
    }

    private static string RunSingleHookLifetimes(SyncFixture f, bool isVoid, ServiceLifetime hookLifetime, ServiceLifetime handlerLifetime, bool generated)
    {
        var probe = new ReferenceSendProbe(); var ambient = ReferenceSendProbe.Ambient.Value; var culture = CultureInfo.CurrentCulture;
        try
        {
            IServiceCollection services = new ServiceCollection(); services.AddSingleton(probe);
            var hook = f.Assembly.GetType("App.Hook`1")!.MakeGenericType(typeof(string));
            services.Add(ServiceDescriptor.Describe(hook, hook, hookLifetime)); services.Add(ServiceDescriptor.Describe(f.Service, f.Service, handlerLifetime));
            Assert.Same(services, f.Register.Invoke(null, [services]));
            var cleanup = new List<(int Hook, int Handler)>();
            using (var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }))
            {
                for (var s = 0; s < 2; s++)
                {
                    using (var scope = provider.CreateScope())
                    {
                        var mediator = generated ? scope.ServiceProvider.GetRequiredService(f.Interface) : null;
                        for (var i = 0; i < 2; i++)
                        {
                            var actual = generated ? InvokeSync(f, mediator!, Activator.CreateInstance(f.Request), default) :
                                InvokeSingleHookReference(f, scope.ServiceProvider, Activator.CreateInstance(f.Request), default, isVoid);
                            Assert.Null(actual.Error); Assert.Equal(isVoid ? null : (object)42, actual.Result);
                            Assert.Equal(s * (hookLifetime == ServiceLifetime.Transient ? 2 : hookLifetime == ServiceLifetime.Scoped ? 1 : 0), probe.HookDisposals);
                            Assert.Equal(s * (handlerLifetime == ServiceLifetime.Transient ? 2 : handlerLifetime == ServiceLifetime.Scoped ? 1 : 0), probe.HandlerDisposals);
                        }
                    }
                    cleanup.Add((probe.HookDisposals, probe.HandlerDisposals));
                }
                Assert.Equal(hookLifetime == ServiceLifetime.Singleton ? 1 : hookLifetime == ServiceLifetime.Scoped ? 2 : 4, probe.Phases.Count(p => p == "hook.ctor"));
                Assert.Equal(handlerLifetime == ServiceLifetime.Singleton ? 1 : handlerLifetime == ServiceLifetime.Scoped ? 2 : 4, probe.Phases.Count(p => p == "handler.ctor"));
            }
            Assert.Equal(hookLifetime == ServiceLifetime.Singleton ? 1 : hookLifetime == ServiceLifetime.Scoped ? 2 : 4, probe.HookDisposals);
            Assert.Equal(handlerLifetime == ServiceLifetime.Singleton ? 1 : handlerLifetime == ServiceLifetime.Scoped ? 2 : 4, probe.HandlerDisposals);
            return JsonSerializer.Serialize(new { phases = probe.Phases.ToArray(), scopeCleanup = cleanup.Select(c => new { c.Hook, c.Handler }), probe.HookDisposals, probe.HandlerDisposals });
        }
        finally { probe.Cancellation.Dispose(); ReferenceSendProbe.Ambient.Value = ambient; CultureInfo.CurrentCulture = culture; }
    }

    [Theory]
    [MemberData(nameof(SingleHookLifetimeCases))]
    public void Enabled_single_Hook_SendSync_preserves_DI_lifetimes_against_reference(bool isVoid, ServiceLifetime hookLifetime, ServiceLifetime handlerLifetime)
    {
        var f = SingleHookBackendFixture(isVoid);
        Assert.Equal(RunSingleHookLifetimes(f, isVoid, hookLifetime, handlerLifetime, false), RunSingleHookLifetimes(f, isVoid, hookLifetime, handlerLifetime, true));
    }

    [Theory]
    [InlineData(false, "missing-before")] [InlineData(true, "missing-before")]
    [InlineData(false, "missing-after")] [InlineData(true, "missing-after")]
    [InlineData(false, "missing-exception")] [InlineData(true, "missing-exception")]
    [InlineData(false, "missing-finally")] [InlineData(true, "missing-finally")]
    [InlineData(false, "two-hooks")] [InlineData(true, "two-hooks")]
    [InlineData(false, "struct")] [InlineData(true, "struct")]
    [InlineData(false, "handler-owned")] [InlineData(true, "handler-owned")]
    [InlineData(false, "hook-owned")] [InlineData(true, "hook-owned")]
    [InlineData(false, "wrapper")] [InlineData(true, "wrapper")]
    [InlineData(false, "external")] [InlineData(true, "external")]
    public void Single_Hook_shape_boundary_enables_accepted_two_Hooks_and_blocks_other_shapes(bool isVoid, string shape)
    {
        var source = SingleHookBackendSource(isVoid, shape);
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "UnsupportedHook_" + shape + isVoid), out var output, out var diagnostics);
        if (shape == "two-hooks") Assert.Empty(diagnostics);
        else Assert.Equal("ZEN0037", Assert.Single(diagnostics).Id);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        var sources = driver.GetRunResult().Results.Single().GeneratedSources;
        if (shape is "two-hooks" or "hook-owned") Assert.Equal(2, sources.Length);
        else Assert.DoesNotContain(sources, s => s.HintName.StartsWith("Zendiator.Canonical", StringComparison.Ordinal));
    }

    [Fact]
    public void Enabled_single_Hook_body_edits_cache_callable_source_but_capability_changes_remove_backend()
    {
        var source = SingleHookBackendSource(false);
        var before = SyncCompilation(source, "HookIncremental"); var driver = SyncDriver().RunGenerators(before);
        Assert.Empty(driver.GetRunResult().Diagnostics);
        var body = before.ReplaceSyntaxTree(before.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(source.Replace("_probe.Before(request,token)!", "_probe.Before(request, token)!"), SyncParseOptions));
        driver = driver.RunGenerators(body);
        Assert.Empty(driver.GetRunResult().Diagnostics);
        Assert.All(driver.GetRunResult().Results.Single().TrackedSteps["CanonicalSyncSourceEmission"].SelectMany(s => s.Outputs),
            output => Assert.True(output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged));
        var capability = body.ReplaceSyntaxTree(body.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(SingleHookBackendSource(false, "missing-before"), SyncParseOptions));
        driver = driver.RunGenerators(capability);
        Assert.Equal("ZEN0037", Assert.Single(driver.GetRunResult().Diagnostics).Id);
        Assert.DoesNotContain(driver.GetRunResult().Results.Single().GeneratedSources, s => s.HintName.StartsWith("Zendiator.Canonical", StringComparison.Ordinal));
    }
}
