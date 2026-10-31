using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    public sealed record ReferenceResponseRequest : ISyncRequest<int>;
    public sealed record ReferenceVoidRequest : ISyncRequest;

    public sealed class ReferenceSendProbe
    {
        public static readonly AsyncLocal<string?> Ambient = new();
        public readonly List<string> Phases = new();
        public readonly List<(string Phase, object? Request, CancellationToken Token, string? Ambient, string Culture)> Calls = new();
        public readonly CancellationTokenSource Cancellation = new();
        public Exception? StageConstructorError, HandlerConstructorError, BeforeError, HandlerError, AfterError, ObserverError, FinallyError;
        public object? Replacement;
        public bool TransformRequest, CancelBefore, CancelHandler, CancelAfter, CancelFinally, HookConstructed, HandlerConstructed;
        public int HookDisposals, HandlerDisposals;
        public Exception? ObservedError;
        public SendExitInfo? Exit;

        public static string CultureFor(string phase) => phase switch
        {
            "before" => "fr-FR",
            "handler" => "es-ES",
            "after" => "en-US",
            "onexception" => "ja-JP",
            "finally" => "de-DE",
            _ => ""
        };
        private void Record(string phase, object? request, CancellationToken token)
        {
            Phases.Add(phase);
            Calls.Add((phase, request, token, Ambient.Value, CultureInfo.CurrentCulture.Name));
            Ambient.Value = phase;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(CultureFor(phase));
        }
        public void ConstructHook()
        {
            Phases.Add("hook.ctor");
            if (StageConstructorError is { } error)
                throw error;
            HookConstructed = true;
        }
        public void ConstructHandler()
        {
            Phases.Add("handler.ctor");
            if (HandlerConstructorError is { } error)
                throw error;
            HandlerConstructed = true;
        }
        public object? Before(object request, CancellationToken token)
        {
            Record("before", request, token);
            if (CancelBefore)
                Cancellation.Cancel();
            if (BeforeError is { } error)
                throw error;
            return TransformRequest ? Replacement : request;
        }
        public int Handle(object request, CancellationToken token)
        {
            Record("handler", request, token);
            if (CancelHandler)
                Cancellation.Cancel();
            if (HandlerError is { } error)
                throw error;
            return 41;
        }
        public int After(object request, int response, CancellationToken token)
        {
            Record("after", request, token);
            if (CancelAfter)
                Cancellation.Cancel();
            if (AfterError is { } error)
                throw error;
            return response + 1;
        }
        public void Observe(object? request, Exception error, CancellationToken token)
        {
            Record("onexception", request, token);
            ObservedError = error;
            if (ObserverError is { } replacement)
                throw replacement;
        }
        public void Finish(object? request, SendExitInfo exit, CancellationToken token)
        {
            Record("finally", request, token);
            Exit = exit;
            if (CancelFinally)
                Cancellation.Cancel();
            if (FinallyError is { } error)
                throw error;
        }
    }

    public sealed class ReferenceResponseHook : ISendRequestTransform<ReferenceResponseRequest>, ISendResponseTransform<ReferenceResponseRequest, int>,
        ISendExceptionObserver<ReferenceResponseRequest>, ISendFinallyObserver<ReferenceResponseRequest>, IDisposable
    {
        private readonly ReferenceSendProbe _probe;
        public ReferenceResponseHook(ReferenceSendProbe probe)
        {
            _probe = probe;
            probe.ConstructHook();
        }
        ReferenceResponseRequest ISendRequestTransform<ReferenceResponseRequest>.Before(ReferenceResponseRequest request, CancellationToken token) => (ReferenceResponseRequest)_probe.Before(request, token)!;
        int ISendResponseTransform<ReferenceResponseRequest, int>.After(ReferenceResponseRequest request, int response, CancellationToken token) => _probe.After(request, response, token);
        void ISendExceptionObserver<ReferenceResponseRequest>.OnException(ReferenceResponseRequest request, Exception error, CancellationToken token) => _probe.Observe(request, error, token);
        void ISendFinallyObserver<ReferenceResponseRequest>.Finally(ReferenceResponseRequest request, SendExitInfo exit, CancellationToken token) => _probe.Finish(request, exit, token);
        void IDisposable.Dispose() => _probe.HookDisposals++;
    }
    public sealed class ReferenceVoidHook : ISendRequestTransform<ReferenceVoidRequest>, ISendSuccessObserver<ReferenceVoidRequest>,
        ISendExceptionObserver<ReferenceVoidRequest>, ISendFinallyObserver<ReferenceVoidRequest>, IDisposable
    {
        private readonly ReferenceSendProbe _probe;
        public ReferenceVoidHook(ReferenceSendProbe probe)
        {
            _probe = probe;
            probe.ConstructHook();
        }
        ReferenceVoidRequest ISendRequestTransform<ReferenceVoidRequest>.Before(ReferenceVoidRequest request, CancellationToken token) => (ReferenceVoidRequest)_probe.Before(request, token)!;
        void ISendSuccessObserver<ReferenceVoidRequest>.After(ReferenceVoidRequest request, CancellationToken token) => _probe.After(request, 0, token);
        void ISendExceptionObserver<ReferenceVoidRequest>.OnException(ReferenceVoidRequest request, Exception error, CancellationToken token) => _probe.Observe(request, error, token);
        void ISendFinallyObserver<ReferenceVoidRequest>.Finally(ReferenceVoidRequest request, SendExitInfo exit, CancellationToken token) => _probe.Finish(request, exit, token);
        void IDisposable.Dispose() => _probe.HookDisposals++;
    }
    public sealed class ReferenceResponseHandler : ISyncRequestHandler<ReferenceResponseRequest, int>, IDisposable
    {
        private readonly ReferenceSendProbe _probe;
        public ReferenceResponseHandler(ReferenceSendProbe probe)
        {
            _probe = probe;
            probe.ConstructHandler();
        }
        int ISyncRequestHandler<ReferenceResponseRequest, int>.Handle(ReferenceResponseRequest request, CancellationToken token) => _probe.Handle(request, token);
        void IDisposable.Dispose() => _probe.HandlerDisposals++;
    }
    public sealed class ReferenceVoidHandler : ISyncRequestHandler<ReferenceVoidRequest>, IDisposable
    {
        private readonly ReferenceSendProbe _probe;
        public ReferenceVoidHandler(ReferenceSendProbe probe)
        {
            _probe = probe;
            probe.ConstructHandler();
        }
        void ISyncRequestHandler<ReferenceVoidRequest>.Handle(ReferenceVoidRequest request, CancellationToken token) => _probe.Handle(request, token);
        void IDisposable.Dispose() => _probe.HandlerDisposals++;
    }

    // Fixed contract traces, not inferred from the reference or a generated execution graph.
    public static IEnumerable<object[]> SingleHookReferenceCases()
    {
        var goldens = new (string Scenario, string Phases)[] {
            ("success", "hook.ctor before handler.ctor handler after finally"),
            ("replace-request", "hook.ctor before handler.ctor handler after finally"),
            ("cancel-in-handler", "hook.ctor before handler.ctor handler after finally"),
            ("cancel-in-after", "hook.ctor before handler.ctor handler after finally"),
            ("null-request", ""), ("pre-cancel", ""), ("stage-di-error", "stage.resolve"), ("stage-ctor-error", "hook.ctor"),
            ("before-error", "hook.ctor before onexception finally"), ("before-null", "hook.ctor before onexception finally"),
            ("cancel-in-before", "hook.ctor before onexception finally"),
            ("handler-di-error", "hook.ctor before handler.resolve onexception finally"),
            ("handler-ctor-error", "hook.ctor before handler.ctor onexception finally"),
            ("handler-error", "hook.ctor before handler.ctor handler onexception finally"),
            ("handler-oce", "hook.ctor before handler.ctor handler onexception finally"),
            ("after-error", "hook.ctor before handler.ctor handler after onexception finally"),
            ("after-oce", "hook.ctor before handler.ctor handler after onexception finally"),
            ("observer-error-over-body", "hook.ctor before handler.ctor handler onexception finally"),
            ("finally-error-success", "hook.ctor before handler.ctor handler after finally"),
            ("finally-error-over-body", "hook.ctor before handler.ctor handler onexception finally"),
            ("finally-oce-over-body", "hook.ctor before handler.ctor handler onexception finally"),
            ("finally-error-over-observer", "hook.ctor before handler.ctor handler onexception finally"),
            ("cancel-in-finally", "hook.ctor before handler.ctor handler after finally")
        };
        foreach (var isVoid in new[] { false, true })
            foreach (var (scenario, phases) in goldens)
                yield return [isVoid, scenario, phases];
    }

    [Theory]
    [MemberData(nameof(SingleHookReferenceCases))]
    public void Canonical_single_Hook_SendSync_matches_golden_identity_context_and_DI_ownership(bool isVoid, string scenario, string phases)
    {
        var probe = new ReferenceSendProbe();
        var previousAmbient = ReferenceSendProbe.Ambient.Value;
        var culture = CultureInfo.CurrentCulture;
        ReferenceSendProbe.Ambient.Value = "caller";
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var token = probe.Cancellation.Token;
        Exception body = scenario is "handler-oce" or "after-oce" ? new OperationCanceledException(token) : new Exception("body identity");
        var observer = new Exception("observer identity");
        Exception cleanup = scenario == "finally-oce-over-body" ? new OperationCanceledException(token) : new Exception("finally identity");
        object? request = scenario == "null-request" ? null : isVoid ? new ReferenceVoidRequest() : new ReferenceResponseRequest();
        object replacement = isVoid ? new ReferenceVoidRequest() : new ReferenceResponseRequest();
        probe.TransformRequest = scenario is "replace-request" or "before-null" or "before-error";
        probe.Replacement = scenario == "before-null" ? null : replacement;
        if (scenario == "before-error")
            probe.BeforeError = body;
        if (scenario == "stage-ctor-error")
            probe.StageConstructorError = body;
        if (scenario == "handler-ctor-error")
            probe.HandlerConstructorError = body;
        if (scenario is "handler-error" or "handler-oce" or "observer-error-over-body" or "finally-error-over-body" or "finally-oce-over-body" or "finally-error-over-observer")
            probe.HandlerError = body;
        if (scenario is "after-error" or "after-oce")
            probe.AfterError = body;
        if (scenario is "observer-error-over-body" or "finally-error-over-observer")
            probe.ObserverError = observer;
        if (scenario.StartsWith("finally-", StringComparison.Ordinal))
            probe.FinallyError = cleanup;
        probe.CancelBefore = scenario == "cancel-in-before";
        probe.CancelFinally = scenario == "cancel-in-finally";
        probe.CancelHandler = scenario == "cancel-in-handler";
        probe.CancelAfter = scenario == "cancel-in-after";
        if (scenario == "pre-cancel")
            probe.Cancellation.Cancel();
        try
        {
            IServiceCollection services = new ServiceCollection();
            services.AddSingleton(probe);
            services.AddScoped<ReferenceResponseHook>();
            services.AddScoped<ReferenceVoidHook>();
            services.AddScoped<ReferenceResponseHandler>();
            services.AddScoped<ReferenceVoidHandler>();
            if (scenario is "stage-di-error" or "handler-di-error")
            {
                var type = scenario == "stage-di-error" ? isVoid ? typeof(ReferenceVoidHook) : typeof(ReferenceResponseHook) :
                    isVoid ? typeof(ReferenceVoidHandler) : typeof(ReferenceResponseHandler);
                services.AddScoped(type, _ =>
                {
                    probe.Phases.Add(scenario == "stage-di-error" ? "stage.resolve" : "handler.resolve");
                    throw body;
                });
            }
            using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            using (var scope = provider.CreateScope())
            {
                object? result = null;
                Exception? actual = null;
                try
                {
                    if (isVoid)
                        CanonicalSendSyncReference.SendVoid<ReferenceVoidRequest, ReferenceVoidHook, ReferenceVoidHandler>(scope.ServiceProvider, (ReferenceVoidRequest)request!, token);
                    else
                        result = CanonicalSendSyncReference.SendResponse<ReferenceResponseRequest, int, ReferenceResponseHook, ReferenceResponseHandler>(scope.ServiceProvider, (ReferenceResponseRequest)request!, token);
                }
                catch (Exception error)
                {
                    actual = error;
                }
                Assert.Equal(phases.Split(' ', StringSplitOptions.RemoveEmptyEntries), probe.Phases);
                if (scenario is "success" or "replace-request" or "cancel-in-handler" or "cancel-in-after" or "cancel-in-finally")
                {
                    Assert.Null(actual);
                    Assert.Equal(isVoid ? null : (object)42, result);
                }
                else if (scenario is "null-request" or "before-null")
                    Assert.IsType<ArgumentNullException>(actual);
                else if (scenario is "pre-cancel" or "cancel-in-before")
                    Assert.Equal(token, Assert.IsType<OperationCanceledException>(actual).CancellationToken);
                else
                    Assert.Same(scenario.StartsWith("finally-", StringComparison.Ordinal) ? cleanup : scenario == "observer-error-over-body" ? observer : body, actual);
                var saved = scenario == "before-null" ? null : scenario == "replace-request" ? replacement : request;
                for (var i = 0; i < probe.Calls.Count; i++)
                {
                    var call = probe.Calls[i];
                    Assert.Equal(token, call.Token);
                    Assert.Same(call.Phase == "before" ? request : saved, call.Request);
                    Assert.Equal(i == 0 ? "caller" : probe.Calls[i - 1].Phase, call.Ambient);
                    Assert.Equal(i == 0 ? "" : ReferenceSendProbe.CultureFor(probe.Calls[i - 1].Phase), call.Culture);
                }
                if (probe.Exit is { } exit)
                {
                    Assert.Equal(scenario is "success" or "replace-request" or "cancel-in-handler" or "cancel-in-after" or "cancel-in-finally" or "finally-error-success", exit.Success);
                    Assert.Same(probe.ObserverError ?? probe.ObservedError, exit.Error);
                    if (probe.ObservedError != null && scenario is not ("before-null" or "cancel-in-before"))
                        Assert.Same(body, probe.ObservedError);
                }
                else
                    Assert.DoesNotContain("finally", probe.Phases);
                if (scenario is "before-null" or "cancel-in-before")
                {
                    Assert.NotNull(actual);
                    Assert.Same(actual, probe.ObservedError);
                    Assert.Same(actual, probe.Exit?.Error);
                }
                if (scenario is "null-request" or "pre-cancel")
                {
                    Assert.Null(probe.ObservedError);
                    Assert.Null(probe.Exit);
                }
                Assert.Equal(scenario is "pre-cancel" or "cancel-in-before" or "cancel-in-handler" or "cancel-in-after" or "cancel-in-finally", token.IsCancellationRequested);
                Assert.Equal(probe.Calls.Count == 0 ? "caller" : probe.Calls[^1].Phase, ReferenceSendProbe.Ambient.Value);
                Assert.Equal(0, probe.HookDisposals);
                Assert.Equal(0, probe.HandlerDisposals);
            }
            Assert.Equal(probe.HookConstructed ? 1 : 0, probe.HookDisposals);
            Assert.Equal(probe.HandlerConstructed ? 1 : 0, probe.HandlerDisposals);
        }
        finally
        {
            probe.Cancellation.Dispose();
            ReferenceSendProbe.Ambient.Value = previousAmbient;
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Canonical_zero_Hook_SendSync_resolves_only_the_Handler(bool isVoid)
    {
        var probe = new ReferenceSendProbe();
        var ambient = ReferenceSendProbe.Ambient.Value;
        var culture = CultureInfo.CurrentCulture;
        try
        {
            var services = new ServiceCollection();
            services.AddSingleton(probe);
            services.AddScoped<ReferenceResponseHandler>();
            services.AddScoped<ReferenceVoidHandler>();
            services.AddScoped<ReferenceResponseHook>(_ => throw new Exception("A zero-hook route must not resolve a Hook."));
            services.AddScoped<ReferenceVoidHook>(_ => throw new Exception("A zero-hook route must not resolve a Hook."));
            using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            using (var scope = provider.CreateScope())
            {
                if (isVoid)
                    CanonicalSendSyncReference.SendVoidWithoutHook<ReferenceVoidRequest, ReferenceVoidHandler>(scope.ServiceProvider, new(), default);
                else
                    Assert.Equal(41, CanonicalSendSyncReference.SendResponseWithoutHook<ReferenceResponseRequest, int, ReferenceResponseHandler>(scope.ServiceProvider, new(), default));
                Assert.Equal(new[] { "handler.ctor", "handler" }, probe.Phases);
                Assert.False(probe.HookConstructed);
                Assert.Null(probe.Exit);
                Assert.Equal(0, probe.HookDisposals);
                Assert.Equal(0, probe.HandlerDisposals);
            }
            Assert.Equal(1, probe.HandlerDisposals);
            Assert.Equal(0, probe.HookDisposals);
        }
        finally
        {
            probe.Cancellation.Dispose();
            ReferenceSendProbe.Ambient.Value = ambient;
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Enabled_single_Hook_SendSync_public_API_compiles_and_executes_without_generator_diagnostics(bool isVoid)
    {
        var afterContract = isVoid ? "ISendSuccessObserver<Ping>" : "ISendResponseTransform<Ping,int>";
        var after = isVoid ? "void ISendSuccessObserver<Ping>.After(Ping request,CancellationToken token){}" :
            "int ISendResponseTransform<Ping,int>.After(Ping request,int response,CancellationToken token)=>response;";
        var hook = "public sealed class Hook : ISendRequestTransform<Ping>," + afterContract + ",ISendExceptionObserver<Ping>,ISendFinallyObserver<Ping> { " +
            "Ping ISendRequestTransform<Ping>.Before(Ping request,CancellationToken token)=>request; " + after +
            " void ISendExceptionObserver<Ping>.OnException(Ping request,Exception error,CancellationToken token){} " +
            "void ISendFinallyObserver<Ping>.Finally(Ping request,SendExitInfo exit,CancellationToken token){} } ";
        var source = SyncRuntimeSource("direct", isVoid, emptyPipeline: true)
            .Replace("c.Pipeline<Ping>(DispatchFamily.SendSync);", "c.Pipeline<Ping>(DispatchFamily.SendSync,Stage.Hook<Hook>());")
            .Replace("[ZendiatorComposition", hook + "[ZendiatorComposition");
        var f = CompileSyncFixture("direct", isVoid, emptyPipeline: true, sourceOverride: source);
        var probe = new CanonicalSyncRuntimeProbe();
        var ambient = CanonicalSyncRuntimeProbe.Ambient.Value;
        var culture = CultureInfo.CurrentCulture;
        try
        {
            using var provider = SyncProvider(f, probe, recording: false);
            using var scope = provider.CreateScope();
            var actual = InvokeSync(f, scope.ServiceProvider.GetRequiredService(f.Interface), Activator.CreateInstance(f.Request), default);
            Assert.Null(actual.Error);
            Assert.Equal(isVoid ? null : (object)73, actual.Result);
            Assert.Single(probe.Calls, c => c.Phase == "handler");
        }
        finally
        {
            probe.Cancellation.Dispose();
            CanonicalSyncRuntimeProbe.Ambient.Value = ambient;
            CultureInfo.CurrentCulture = culture;
        }
    }
}
