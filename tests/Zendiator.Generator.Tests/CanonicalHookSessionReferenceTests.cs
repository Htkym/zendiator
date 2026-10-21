using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    public sealed class HookSessionReferenceProbe
    {
        public readonly ReferenceSendProbe Core = new();
        public Exception? DiError, FactoryConstructorError, FactoryError, PartialError, DisposeError;
        public bool NullFactory, NullSession, CancelFactory, CancelDispose;
        public int Dependencies, DependencyDisposals, FactoryDisposals, PartialDisposals;
        public readonly List<object> Created = new(), Released = new(), HookReceivers = new();
        public readonly List<IDisposable> Borrowed = new();
        public readonly List<(string Phase, object Receiver, object? Request, CancellationToken Token, string? Ambient, string Culture)> Calls = new();
        public H Create<H>(object factory, object request, CancellationToken token, Func<H> create, bool borrowed) where H : class, IDisposable
        {
            Record("factory", factory, request, token);
            if (CancelFactory) Core.Cancellation.Cancel();
            if (FactoryError is { } error) throw error;
            if (PartialError is { } partialError) { using var partial = new HookFactoryPartialResource(this); throw partialError; }
            if (NullSession) return null!;
            var hook = create(); Created.Add(hook);
            if (borrowed) Borrowed.Add(hook);
            return hook;
        }
        public void Release(object hook)
        {
            Record("session.dispose", hook, null, default); Released.Add(hook); Core.HookDisposals++;
            if (CancelDispose) Core.Cancellation.Cancel();
            if (DisposeError is { } error) throw error;
        }
        private void Record(string phase, object receiver, object? request, CancellationToken token)
        {
            Core.Phases.Add(phase); Calls.Add((phase, receiver, request, token, ReferenceSendProbe.Ambient.Value, CultureInfo.CurrentCulture.Name));
            ReferenceSendProbe.Ambient.Value = phase;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(phase == "factory" ? "it-IT" : "ko-KR");
        }
    }
    public sealed class HookFactoryDependency : IDisposable
    {
        private readonly HookSessionReferenceProbe _probe;
        public HookFactoryDependency(HookSessionReferenceProbe probe) { _probe = probe; probe.Core.Phases.Add("dependency.ctor"); probe.Dependencies++; }
        public void Dispose() { _probe.Core.Phases.Add("dependency.dispose"); _probe.DependencyDisposals++; }
    }
    private sealed class HookFactoryPartialResource : IDisposable
    {
        private readonly HookSessionReferenceProbe _probe;
        public HookFactoryPartialResource(HookSessionReferenceProbe probe) { _probe = probe; probe.Core.Phases.Add("partial.acquire"); }
        public void Dispose() { _probe.Core.Phases.Add("partial.dispose"); _probe.PartialDisposals++; }
    }
    public abstract class HookReferenceFactory<R, H> : IDisposable where R : class where H : class, IDisposable
    {
        private readonly HookSessionReferenceProbe _probe;
        private readonly Func<H> _create;
        protected HookReferenceFactory(HookSessionReferenceProbe probe, HookFactoryDependency dependency, Func<H> create)
        { _probe = probe; _create = create; probe.Core.Phases.Add("factory.ctor"); if (probe.FactoryConstructorError is { } error) throw error; }
        protected H CreateSession(R request, CancellationToken token, bool borrowed) => _probe.Create(this, request, token, _create, borrowed);
        public void Dispose() { _probe.Core.Phases.Add("factory.dispose"); _probe.FactoryDisposals++; }
    }
    public sealed class OwnedHookReferenceFactory<R, H>(HookSessionReferenceProbe probe, HookFactoryDependency dependency, Func<H> create)
        : HookReferenceFactory<R, H>(probe, dependency, create), IOwnedSessionFactory<R, H> where R : class where H : class, IDisposable
    { public H Create(R request, CancellationToken token) => CreateSession(request, token, false); }
    public sealed class BorrowedHookReferenceFactory<R, H>(HookSessionReferenceProbe probe, HookFactoryDependency dependency, Func<H> create)
        : HookReferenceFactory<R, H>(probe, dependency, create), IBorrowedSessionFactory<R, H> where R : class where H : class, IDisposable
    { public H Create(R request, CancellationToken token) => CreateSession(request, token, true); }

    // These wrappers are user fixtures. They forward callbacks to the existing single-Hook fixture;
    // the handwritten orchestration itself uses typed contracts and no factory delegates.
    public sealed class ResponseHookSession : ISendRequestTransform<ReferenceResponseRequest>, ISendResponseTransform<ReferenceResponseRequest, int>,
        ISendExceptionObserver<ReferenceResponseRequest>, ISendFinallyObserver<ReferenceResponseRequest>, IDisposable
    {
        private readonly HookSessionReferenceProbe _probe;
        private readonly ReferenceResponseHook _body;
        public ResponseHookSession(HookSessionReferenceProbe probe) { _probe = probe; _body = new(probe.Core); }
        ReferenceResponseRequest ISendRequestTransform<ReferenceResponseRequest>.Before(ReferenceResponseRequest request, CancellationToken token)
        { _probe.HookReceivers.Add(this); return ((ISendRequestTransform<ReferenceResponseRequest>)_body).Before(request, token); }
        int ISendResponseTransform<ReferenceResponseRequest, int>.After(ReferenceResponseRequest request, int response, CancellationToken token)
        { _probe.HookReceivers.Add(this); return ((ISendResponseTransform<ReferenceResponseRequest, int>)_body).After(request, response, token); }
        void ISendExceptionObserver<ReferenceResponseRequest>.OnException(ReferenceResponseRequest request, Exception error, CancellationToken token)
        { _probe.HookReceivers.Add(this); ((ISendExceptionObserver<ReferenceResponseRequest>)_body).OnException(request, error, token); }
        void ISendFinallyObserver<ReferenceResponseRequest>.Finally(ReferenceResponseRequest request, SendExitInfo exit, CancellationToken token)
        { _probe.HookReceivers.Add(this); ((ISendFinallyObserver<ReferenceResponseRequest>)_body).Finally(request, exit, token); }
        public void Dispose() => _probe.Release(this);
    }
    public sealed class VoidHookSession : ISendRequestTransform<ReferenceVoidRequest>, ISendSuccessObserver<ReferenceVoidRequest>,
        ISendExceptionObserver<ReferenceVoidRequest>, ISendFinallyObserver<ReferenceVoidRequest>, IDisposable
    {
        private readonly HookSessionReferenceProbe _probe;
        private readonly ReferenceVoidHook _body;
        public VoidHookSession(HookSessionReferenceProbe probe) { _probe = probe; _body = new(probe.Core); }
        ReferenceVoidRequest ISendRequestTransform<ReferenceVoidRequest>.Before(ReferenceVoidRequest request, CancellationToken token)
        { _probe.HookReceivers.Add(this); return ((ISendRequestTransform<ReferenceVoidRequest>)_body).Before(request, token); }
        void ISendSuccessObserver<ReferenceVoidRequest>.After(ReferenceVoidRequest request, CancellationToken token)
        { _probe.HookReceivers.Add(this); ((ISendSuccessObserver<ReferenceVoidRequest>)_body).After(request, token); }
        void ISendExceptionObserver<ReferenceVoidRequest>.OnException(ReferenceVoidRequest request, Exception error, CancellationToken token)
        { _probe.HookReceivers.Add(this); ((ISendExceptionObserver<ReferenceVoidRequest>)_body).OnException(request, error, token); }
        void ISendFinallyObserver<ReferenceVoidRequest>.Finally(ReferenceVoidRequest request, SendExitInfo exit, CancellationToken token)
        { _probe.HookReceivers.Add(this); ((ISendFinallyObserver<ReferenceVoidRequest>)_body).Finally(request, exit, token); }
        public void Dispose() => _probe.Release(this);
    }

    private static IServiceCollection HookSessionServices(HookSessionReferenceProbe probe, bool owned, bool isVoid, ServiceLifetime lifetime, SyncFixture? fixture = null)
    {
        IServiceCollection services = new ServiceCollection(); services.AddSingleton(probe); services.AddSingleton(probe.Core);
        services.Add(ServiceDescriptor.Describe(typeof(HookFactoryDependency), typeof(HookFactoryDependency), lifetime));
        services.Add(ServiceDescriptor.Describe(typeof(ReferenceResponseHandler), typeof(ReferenceResponseHandler), lifetime));
        services.Add(ServiceDescriptor.Describe(typeof(ReferenceVoidHandler), typeof(ReferenceVoidHandler), lifetime));
        var factory = fixture?.Service ?? (isVoid ? owned ? typeof(OwnedHookReferenceFactory<ReferenceVoidRequest, VoidHookSession>) : typeof(BorrowedHookReferenceFactory<ReferenceVoidRequest, VoidHookSession>) :
            owned ? typeof(OwnedHookReferenceFactory<ReferenceResponseRequest, ResponseHookSession>) : typeof(BorrowedHookReferenceFactory<ReferenceResponseRequest, ResponseHookSession>));
        Func<IServiceProvider, object> resolve = fixture != null ? fixture.ResolveService.CreateDelegate<Func<IServiceProvider, object>>() : provider =>
        {
            probe.Core.Phases.Add("factory.resolve"); if (probe.DiError is { } error) throw error; if (probe.NullFactory) return null!;
            var dependency = provider.GetRequiredService<HookFactoryDependency>();
            return isVoid ? owned ? new OwnedHookReferenceFactory<ReferenceVoidRequest, VoidHookSession>(probe, dependency, () => new(probe)) :
                new BorrowedHookReferenceFactory<ReferenceVoidRequest, VoidHookSession>(probe, dependency, () => new(probe)) :
                owned ? new OwnedHookReferenceFactory<ReferenceResponseRequest, ResponseHookSession>(probe, dependency, () => new(probe)) :
                new BorrowedHookReferenceFactory<ReferenceResponseRequest, ResponseHookSession>(probe, dependency, () => new(probe));
        };
        services.Add(ServiceDescriptor.Describe(factory, resolve, lifetime));
        services.AddScoped<ResponseHookSession>(_ => throw new Exception("Resolve the factory, not its returned Hook."));
        services.AddScoped<VoidHookSession>(_ => throw new Exception("Resolve the factory, not its returned Hook."));
        if (fixture != null)
        {
            services.Add(ServiceDescriptor.Describe(fixture.Assembly.GetType("App.Handler`1")!.MakeGenericType(typeof(string)), fixture.Assembly.GetType("App.Handler`1")!.MakeGenericType(typeof(string)), lifetime));
            services.AddScoped(fixture.Assembly.GetType("App.Hook`1")!.MakeGenericType(typeof(string)), _ => throw new Exception("Resolve the factory, not the compiled Hook."));
            Assert.Same(services, fixture.Register.Invoke(null, [services]));
        }
        return services;
    }
    private static (object? Result, Exception? Error) InvokeHookSessionReference(IServiceProvider provider, bool owned, bool isVoid, object? request, CancellationToken token)
    {
        try
        {
            if (isVoid)
            {
                if (owned) CanonicalSendSyncReference.SendOwnedHookVoid<ReferenceVoidRequest, VoidHookSession, ReferenceVoidHandler, OwnedHookReferenceFactory<ReferenceVoidRequest, VoidHookSession>>(provider, (ReferenceVoidRequest)request!, token);
                else CanonicalSendSyncReference.SendBorrowedHookVoid<ReferenceVoidRequest, VoidHookSession, ReferenceVoidHandler, BorrowedHookReferenceFactory<ReferenceVoidRequest, VoidHookSession>>(provider, (ReferenceVoidRequest)request!, token);
                return (null, null);
            }
            return (owned ? CanonicalSendSyncReference.SendOwnedHookResponse<ReferenceResponseRequest, int, ResponseHookSession, ReferenceResponseHandler, OwnedHookReferenceFactory<ReferenceResponseRequest, ResponseHookSession>>(provider, (ReferenceResponseRequest)request!, token) :
                CanonicalSendSyncReference.SendBorrowedHookResponse<ReferenceResponseRequest, int, ResponseHookSession, ReferenceResponseHandler, BorrowedHookReferenceFactory<ReferenceResponseRequest, ResponseHookSession>>(provider, (ReferenceResponseRequest)request!, token), null);
        }
        catch (Exception error) { return (null, error); }
    }

    // The existing literal single-Hook traces are independent goldens, not traces of an execution.
    public static IEnumerable<object[]> HookSessionReferenceCases()
    {
        const string prefix = "factory.resolve dependency.ctor factory.ctor factory ";
        foreach (var data in SingleHookReferenceCases()) foreach (var owned in new[] { false, true })
        {
            var scenario = (string)data[1]; var body = (string)data[2];
            var golden = scenario is "null-request" or "pre-cancel" ? "" : scenario == "stage-di-error" ? "factory.resolve" : prefix + body;
            if (owned && body.Contains("before", StringComparison.Ordinal)) golden += " session.dispose";
            yield return [owned, data[0], scenario, golden];
        }
        var acquisitions = new (string Scenario, string Golden)[] {
            ("factory-di-null", "factory.resolve"), ("factory-ctor-error", "factory.resolve dependency.ctor factory.ctor"),
            ("factory-error", prefix.TrimEnd()), ("factory-oce", prefix.TrimEnd()), ("factory-null", prefix.TrimEnd()),
            ("cancel-in-factory", prefix + "hook.ctor before onexception finally"),
            ("partial-error", prefix + "partial.acquire partial.dispose"), ("partial-oce", prefix + "partial.acquire partial.dispose")
        };
        foreach (var owned in new[] { false, true }) foreach (var isVoid in new[] { false, true }) foreach (var (scenario, phases) in acquisitions)
            yield return [owned, isVoid, scenario, phases + (owned && scenario == "cancel-in-factory" ? " session.dispose" : "")];
        foreach (var isVoid in new[] { false, true }) foreach (var scenario in new[] {
            "dispose-error-success", "dispose-oce-success", "dispose-error-over-body", "dispose-error-over-observer",
            "dispose-error-over-finally", "dispose-oce-over-body", "cancel-in-dispose" })
        {
            var success = scenario is "dispose-error-success" or "dispose-oce-success" or "cancel-in-dispose";
            yield return [true, isVoid, scenario, prefix + (success ? "hook.ctor before handler.ctor handler after finally session.dispose" : "hook.ctor before handler.ctor handler onexception finally session.dispose")];
        }
    }

    [Theory]
    [MemberData(nameof(HookSessionReferenceCases))]
    public void Canonical_single_Hook_session_preserves_acquisition_callback_unwind_and_owner(bool owned, bool isVoid, string scenario, string phases)
        => RunHookSessionScenario(owned, isVoid, scenario, phases);

    private static string RunHookSessionScenario(bool owned, bool isVoid, string scenario, string phases, SyncFixture? fixture = null, bool generated = false)
    {
        var probe = new HookSessionReferenceProbe(); var core = probe.Core;
        var ambient = ReferenceSendProbe.Ambient.Value; var culture = CultureInfo.CurrentCulture;
        ReferenceSendProbe.Ambient.Value = "caller"; CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        using var otherCancellation = new CancellationTokenSource(); var token = core.Cancellation.Token;
        Exception body = scenario is "handler-oce" or "after-oce" ? new OperationCanceledException(otherCancellation.Token) : new Exception("body identity");
        var observer = new Exception("observer identity");
        Exception final = scenario == "finally-oce-over-body" ? new OperationCanceledException(otherCancellation.Token) : new Exception("Finally identity");
        Exception dispose = scenario.StartsWith("dispose-oce", StringComparison.Ordinal) ? new OperationCanceledException(otherCancellation.Token) : new Exception("Dispose identity");
        Exception factoryError = scenario is "factory-oce" or "partial-oce" ? new OperationCanceledException(otherCancellation.Token) : new Exception("factory identity");
        object? request = scenario == "null-request" ? null : fixture != null ? Activator.CreateInstance(fixture.Request) : isVoid ? new ReferenceVoidRequest() : new ReferenceResponseRequest();
        object replacement = fixture != null ? Activator.CreateInstance(fixture.Request)! : isVoid ? new ReferenceVoidRequest() : new ReferenceResponseRequest();
        core.TransformRequest = scenario is "replace-request" or "before-null" or "before-error"; core.Replacement = scenario == "before-null" ? null : replacement;
        core.BeforeError = scenario == "before-error" ? body : null; core.StageConstructorError = scenario == "stage-ctor-error" ? body : null;
        core.HandlerConstructorError = scenario == "handler-ctor-error" ? body : null;
        core.HandlerError = scenario is "handler-error" or "handler-oce" or "observer-error-over-body" or "finally-error-over-body" or "finally-oce-over-body" or "finally-error-over-observer" or
            "dispose-error-over-body" or "dispose-error-over-observer" or "dispose-error-over-finally" or "dispose-oce-over-body" ? body : null;
        core.AfterError = scenario is "after-error" or "after-oce" ? body : null;
        core.ObserverError = scenario is "observer-error-over-body" or "finally-error-over-observer" or "dispose-error-over-observer" ? observer : null;
        core.FinallyError = scenario.StartsWith("finally-", StringComparison.Ordinal) || scenario == "dispose-error-over-finally" ? final : null;
        core.CancelBefore = scenario == "cancel-in-before"; core.CancelHandler = scenario == "cancel-in-handler";
        core.CancelAfter = scenario == "cancel-in-after"; core.CancelFinally = scenario == "cancel-in-finally";
        probe.DiError = scenario == "stage-di-error" ? body : null; probe.NullFactory = scenario == "factory-di-null";
        probe.FactoryConstructorError = scenario == "factory-ctor-error" ? factoryError : null;
        probe.FactoryError = scenario is "factory-error" or "factory-oce" ? factoryError : null; probe.NullSession = scenario == "factory-null";
        probe.PartialError = scenario is "partial-error" or "partial-oce" ? factoryError : null;
        probe.CancelFactory = scenario == "cancel-in-factory"; probe.CancelDispose = scenario == "cancel-in-dispose";
        probe.DisposeError = scenario.StartsWith("dispose-", StringComparison.Ordinal) ? dispose : null;
        if (scenario == "pre-cancel") core.Cancellation.Cancel();
        try
        {
            var services = HookSessionServices(probe, owned, isVoid, ServiceLifetime.Scoped, fixture);
            if (scenario == "handler-di-error") services.AddScoped(fixture != null ? fixture.Assembly.GetType("App.Handler`1")!.MakeGenericType(typeof(string)) : isVoid ? typeof(ReferenceVoidHandler) : typeof(ReferenceResponseHandler),
                _ => { core.Phases.Add("handler.resolve"); throw body; });
            string snapshot = "";
            using (var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }))
            {
                using (var scope = provider.CreateScope())
                {
                    var actual = fixture == null ? InvokeHookSessionReference(scope.ServiceProvider, owned, isVoid, request, token) :
                        generated ? InvokeSync(fixture, scope.ServiceProvider.GetRequiredService(fixture.Interface), request, token) :
                        InvokeCompiledHookSessionReference(fixture, scope.ServiceProvider, owned, isVoid, request, token);
                    var golden = phases.Split(' ', StringSplitOptions.RemoveEmptyEntries); Assert.Equal(golden, core.Phases);
                    var escaping = probe.DisposeError ?? core.FinallyError ?? core.ObserverError ?? probe.FactoryConstructorError ?? probe.FactoryError ?? probe.PartialError ??
                        core.StageConstructorError ?? core.BeforeError ?? core.HandlerConstructorError ?? core.HandlerError ?? core.AfterError ?? probe.DiError ?? (scenario == "handler-di-error" ? body : null);
                    if (escaping != null) Assert.Same(escaping, actual.Error);
                    else if (scenario is "null-request" or "before-null") Assert.Equal(scenario == "null-request" ? "request" : "saved", Assert.IsType<ArgumentNullException>(actual.Error).ParamName);
                    else if (scenario is "pre-cancel" or "cancel-in-before" or "cancel-in-factory") Assert.Equal(token, Assert.IsType<OperationCanceledException>(actual.Error).CancellationToken);
                    else if (scenario is "factory-null" or "factory-di-null") Assert.IsType<InvalidOperationException>(actual.Error);
                    else { Assert.Null(actual.Error); Assert.Equal(isVoid ? null : (object)42, actual.Result); }
                    if (escaping is OperationCanceledException) Assert.Equal(otherCancellation.Token, Assert.IsType<OperationCanceledException>(actual.Error).CancellationToken);
                    var hasCallbacks = golden.Contains("before"); var enteredFactory = golden.Contains("factory");
                    var successfulBody = scenario is "success" or "replace-request" or "cancel-in-handler" or "cancel-in-after" or "cancel-in-finally" or "finally-error-success" or
                        "dispose-error-success" or "dispose-oce-success" or "cancel-in-dispose";
                    // Expected body/guard error is selected from the scenario, independently of ObservedError.
                    Exception? expectedObserved = scenario is "before-null" or "cancel-in-before" or "cancel-in-factory" ? actual.Error :
                        hasCallbacks && !successfulBody ? body : null;
                    Assert.Same(expectedObserved, core.ObservedError);
                    if (hasCallbacks)
                    { var exit = Assert.IsType<SendExitInfo>(core.Exit); Assert.Equal(successfulBody, exit.Success); Assert.Same(core.ObserverError ?? expectedObserved, exit.Error); }
                    else Assert.Null(core.Exit);
                    var saved = scenario == "before-null" ? null : scenario == "replace-request" ? replacement : request;
                    for (var i = 0; i < core.Calls.Count; i++)
                    {
                        var call = core.Calls[i]; Assert.Equal(token, call.Token); Assert.Same(call.Phase == "before" ? request : saved, call.Request);
                        Assert.Equal(i == 0 ? "factory" : core.Calls[i - 1].Phase, call.Ambient);
                        Assert.Equal(i == 0 ? "it-IT" : ReferenceSendProbe.CultureFor(core.Calls[i - 1].Phase), call.Culture);
                    }
                    foreach (var call in probe.Calls.Where(c => c.Phase == "factory"))
                    { Assert.Same(request, call.Request); Assert.Equal(token, call.Token); Assert.Equal("caller", call.Ambient); Assert.Equal("", call.Culture); }
                    foreach (var call in probe.Calls.Where(c => c.Phase == "session.dispose"))
                    { Assert.Same(Assert.Single(probe.Created), call.Receiver); Assert.Equal("finally", call.Ambient); Assert.Equal("de-DE", call.Culture); }
                    Assert.Equal(hasCallbacks ? 1 : 0, probe.Created.Count); Assert.Equal(owned && hasCallbacks ? 1 : 0, probe.Released.Count);
                    Assert.All(probe.HookReceivers, receiver => Assert.Same(Assert.Single(probe.Created), receiver));
                    if (owned && hasCallbacks) Assert.Same(Assert.Single(probe.Created), Assert.Single(probe.Released));
                    Assert.Equal(scenario.StartsWith("partial-", StringComparison.Ordinal) ? 1 : 0, probe.PartialDisposals);
                    Assert.Equal(0, core.HandlerDisposals); Assert.Equal(0, probe.FactoryDisposals); Assert.Equal(0, probe.DependencyDisposals);
                    Assert.Equal(scenario is "pre-cancel" or "cancel-in-before" or "cancel-in-factory" or "cancel-in-handler" or "cancel-in-after" or "cancel-in-finally" or "cancel-in-dispose", token.IsCancellationRequested);
                    Assert.Equal(owned && hasCallbacks ? "session.dispose" : hasCallbacks ? "finally" : enteredFactory ? "factory" : "caller", ReferenceSendProbe.Ambient.Value);
                    Assert.Equal(owned && hasCallbacks ? "ko-KR" : hasCallbacks ? "de-DE" : enteredFactory ? "it-IT" : "", CultureInfo.CurrentCulture.Name);
                    var observation = new
                    {
                        owned,
                        isVoid,
                        scenario,
                        golden,
                        trace = core.Phases.ToArray(),
                        actual.Result,
                        errorType = actual.Error?.GetType().FullName,
                        exceptionIdentityPreserved = escaping == null || ReferenceEquals(escaping, actual.Error),
                        observerIdentityPreserved = ReferenceEquals(expectedObserved, core.ObservedError),
                        success = core.Exit?.Success,
                        exitErrorIdentityPreserved = ReferenceEquals(core.ObserverError ?? expectedObserved, core.Exit?.Error),
                        calls = core.Calls.Select(call => new { call.Phase, effectiveRequestPreserved = ReferenceEquals(call.Phase == "before" ? request : saved, call.Request), tokenPreserved = call.Token == token, call.Ambient, call.Culture }),
                        sessionCalls = probe.Calls.Select(call => new { call.Phase, call.Ambient, call.Culture }),
                        createdSessions = probe.Created.Count,
                        operationReleasedSessions = probe.Released.Count,
                        core.HandlerDisposals,
                        probe.FactoryDisposals,
                        probe.DependencyDisposals,
                        probe.PartialDisposals,
                        ambient = ReferenceSendProbe.Ambient.Value,
                        culture = CultureInfo.CurrentCulture.Name,
                        token.IsCancellationRequested
                    };
                    snapshot = JsonSerializer.Serialize(observation);
                    if (fixture == null) WriteNestedHookEvidence("hook-session-reference", owned + "-" + isVoid + "-" + scenario, observation);
                }
                Assert.Equal(core.HandlerConstructed ? 1 : 0, core.HandlerDisposals);
                Assert.Equal(goldenFactoryCaptured(scenario) ? 1 : 0, probe.FactoryDisposals);
                Assert.Equal(scenario is "null-request" or "pre-cancel" or "stage-di-error" or "factory-di-null" ? 0 : 1, probe.DependencyDisposals);
            }
            Assert.Equal(probe.Dependencies, probe.DependencyDisposals);
            Assert.Equal(owned ? probe.Created.Count : 0, probe.Released.Count);
            foreach (var hook in probe.Borrowed) hook.Dispose();
            Assert.Equal(probe.Created.Count, probe.Released.Count);
            Assert.All(probe.Created, hook => Assert.Single(probe.Released, item => ReferenceEquals(item, hook)));
            return JsonSerializer.Serialize(new
            {
                operation = JsonSerializer.Deserialize<JsonElement>(snapshot),
                finalTrace = core.Phases.ToArray(),
                core.HandlerDisposals,
                probe.FactoryDisposals,
                probe.DependencyDisposals,
                finalOwnerReleasedSessions = probe.Released.Count
            });
        }
        finally { core.Cancellation.Dispose(); ReferenceSendProbe.Ambient.Value = ambient; CultureInfo.CurrentCulture = culture; }
        static bool goldenFactoryCaptured(string scenario) => scenario is not ("null-request" or "pre-cancel" or "stage-di-error" or "factory-di-null" or "factory-ctor-error");
    }

    public static IEnumerable<object[]> HookSessionReferenceLifetimeCases()
    {
        foreach (var owned in new[] { false, true }) foreach (var isVoid in new[] { false, true })
            foreach (var lifetime in new[] { ServiceLifetime.Scoped, ServiceLifetime.Transient, ServiceLifetime.Singleton }) yield return [owned, isVoid, lifetime];
    }
    [Theory]
    [MemberData(nameof(HookSessionReferenceLifetimeCases))]
    public void Canonical_single_Hook_session_keeps_factory_Handler_DI_and_session_owners_distinct(bool owned, bool isVoid, ServiceLifetime lifetime)
        => RunHookSessionLifetimes(owned, isVoid, lifetime);

    private static string RunHookSessionLifetimes(bool owned, bool isVoid, ServiceLifetime lifetime, SyncFixture? fixture = null, bool generated = false)
    {
        var probe = new HookSessionReferenceProbe(); var core = probe.Core;
        var ambient = ReferenceSendProbe.Ambient.Value; var culture = CultureInfo.CurrentCulture;
        try
        {
            using (var provider = HookSessionServices(probe, owned, isVoid, lifetime, fixture).BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }))
            {
                for (var s = 0; s < 2; s++)
                {
                    using (var scope = provider.CreateScope())
                    {
                        for (var i = 0; i < 2; i++)
                        {
                            object request = fixture != null ? Activator.CreateInstance(fixture.Request)! : isVoid ? new ReferenceVoidRequest() : new ReferenceResponseRequest();
                            var actual = fixture == null ? InvokeHookSessionReference(scope.ServiceProvider, owned, isVoid, request, default) :
                                generated ? InvokeSync(fixture, scope.ServiceProvider.GetRequiredService(fixture.Interface), request, default) :
                                InvokeCompiledHookSessionReference(fixture, scope.ServiceProvider, owned, isVoid, request, default);
                            Assert.Null(actual.Error); Assert.Equal(isVoid ? null : (object)42, actual.Result);
                            Assert.Equal(owned ? s * 2 + i + 1 : 0, core.HookDisposals);
                            var previous = lifetime == ServiceLifetime.Singleton ? 0 : s * (lifetime == ServiceLifetime.Scoped || generated ? 1 : 2);
                            Assert.Equal(previous, core.HandlerDisposals); Assert.Equal(previous, probe.FactoryDisposals); Assert.Equal(previous, probe.DependencyDisposals);
                        }
                    }
                    var captured = lifetime == ServiceLifetime.Singleton ? 0 : (s + 1) * (lifetime == ServiceLifetime.Scoped || generated ? 1 : 2);
                    Assert.Equal(captured, core.HandlerDisposals); Assert.Equal(captured, probe.FactoryDisposals); Assert.Equal(captured, probe.DependencyDisposals);
                }
                Assert.Equal(4, probe.Created.Count); Assert.Equal(owned ? 4 : 0, probe.Released.Count);
            }
            var services = lifetime == ServiceLifetime.Singleton ? 1 : lifetime == ServiceLifetime.Scoped || generated ? 2 : 4;
            Assert.Equal(services, core.HandlerDisposals); Assert.Equal(services, probe.FactoryDisposals); Assert.Equal(services, probe.DependencyDisposals);
            Assert.Equal(services, probe.Dependencies); Assert.Equal(services, core.Phases.Count(p => p == "handler.ctor"));
            Assert.Equal(services, probe.Calls.Where(c => c.Phase == "factory").Select(c => c.Receiver).Distinct(ReferenceEqualityComparer.Instance).Count());
            Assert.Equal(4, probe.Created.Distinct(ReferenceEqualityComparer.Instance).Count());
            foreach (var hook in probe.Borrowed) hook.Dispose();
            Assert.Equal(4, probe.Released.Count); Assert.All(probe.Created, hook => Assert.Single(probe.Released, item => ReferenceEquals(item, hook)));
            var observation = new
            {
                owned,
                isVoid,
                lifetime = lifetime.ToString(),
                trace = core.Phases.ToArray(),
                services,
                core.HandlerDisposals,
                probe.FactoryDisposals,
                probe.Dependencies,
                probe.DependencyDisposals,
                createdSessions = probe.Created.Count,
                operationReleasedSessions = owned ? 4 : 0,
                finalOwnerReleasedSessions = probe.Released.Count
            };
            if (fixture == null) WriteNestedHookEvidence("hook-session-reference-lifetimes", owned + "-" + isVoid + "-" + lifetime, observation);
            return JsonSerializer.Serialize(observation);
        }
        finally { core.Cancellation.Dispose(); ReferenceSendProbe.Ambient.Value = ambient; CultureInfo.CurrentCulture = culture; }
    }
}
