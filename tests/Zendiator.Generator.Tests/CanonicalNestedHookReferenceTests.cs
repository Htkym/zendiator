using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    public sealed class OuterHookTag;
    public sealed class InnerHookTag;
    public sealed record NestedResponseRequest(string Name) : ISyncRequest<int>;
    public sealed record NestedVoidRequest(string Name) : ISyncRequest;

    public sealed class NestedSendProbe
    {
        public static readonly AsyncLocal<string?> Ambient = new();
        public readonly List<string> Phases = new();
        public readonly List<(string Phase, object? Request, CancellationToken Token, string? Ambient, string Culture)> Calls = new();
        public readonly Dictionary<string, Exception> Errors = new();
        public readonly Dictionary<string, Exception> Observed = new();
        public readonly Dictionary<string, SendExitInfo> Exits = new();
        public readonly Dictionary<string, object?> Replacements = new();
        public readonly HashSet<string> CancelAt = new();
        public readonly Dictionary<string, int> Disposals = new();
        public readonly HashSet<string> Constructed = new();
        public readonly CancellationTokenSource Cancellation = new();

        public void Construct(string owner)
        { Phases.Add(owner + ".ctor"); Fail(owner + ".ctor"); Constructed.Add(owner); }
        private void Fail(string phase) { if (Errors.TryGetValue(phase, out var error)) throw error; }
        public void Record(string phase, object? request, CancellationToken token)
        {
            Phases.Add(phase); Calls.Add((phase, request, token, Ambient.Value, CultureInfo.CurrentCulture.Name));
            Ambient.Value = phase;
            var suffix = phase.Contains('.') ? phase[(phase.IndexOf('.') + 1)..] : phase;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(ReferenceSendProbe.CultureFor(suffix));
            if (CancelAt.Contains(phase)) Cancellation.Cancel();
        }
        public object? Before(string owner, object request, CancellationToken token)
        { Record(owner + ".before", request, token); Fail(owner + ".before"); return Replacements.TryGetValue(owner, out var value) ? value : request; }
        public int Handle(object request, CancellationToken token)
        { Record("handler", request, token); Fail("handler"); return 41; }
        public int After(string owner, object request, int response, CancellationToken token)
        { Record(owner + ".after", request, token); Fail(owner + ".after"); return response + 1; }
        public void Observe(string owner, object? request, Exception error, CancellationToken token)
        { Record(owner + ".onexception", request, token); Observed.Add(owner, error); Fail(owner + ".onexception"); }
        public void Finish(string owner, object? request, SendExitInfo exit, CancellationToken token)
        { Record(owner + ".finally", request, token); Exits.Add(owner, exit); Fail(owner + ".finally"); }
        public void DisposeService(string owner) => Disposals[owner] = Disposals.GetValueOrDefault(owner) + 1;
    }

    public sealed class NestedResponseHook<TTag> : ISendRequestTransform<NestedResponseRequest>, ISendResponseTransform<NestedResponseRequest, int>,
        ISendExceptionObserver<NestedResponseRequest>, ISendFinallyObserver<NestedResponseRequest>, IDisposable
    {
        private readonly NestedSendProbe _probe;
        private readonly string _owner = typeof(TTag) == typeof(OuterHookTag) ? "A" : "B";
        public NestedResponseHook(NestedSendProbe probe) { _probe = probe; probe.Construct(_owner); }
        NestedResponseRequest ISendRequestTransform<NestedResponseRequest>.Before(NestedResponseRequest request, CancellationToken token) => (NestedResponseRequest)_probe.Before(_owner, request, token)!;
        int ISendResponseTransform<NestedResponseRequest, int>.After(NestedResponseRequest request, int response, CancellationToken token) => _probe.After(_owner, request, response, token);
        void ISendExceptionObserver<NestedResponseRequest>.OnException(NestedResponseRequest request, Exception error, CancellationToken token) => _probe.Observe(_owner, request, error, token);
        void ISendFinallyObserver<NestedResponseRequest>.Finally(NestedResponseRequest request, SendExitInfo exit, CancellationToken token) => _probe.Finish(_owner, request, exit, token);
        void IDisposable.Dispose() => _probe.DisposeService(_owner);
    }
    public sealed class NestedVoidHook<TTag> : ISendRequestTransform<NestedVoidRequest>, ISendSuccessObserver<NestedVoidRequest>,
        ISendExceptionObserver<NestedVoidRequest>, ISendFinallyObserver<NestedVoidRequest>, IDisposable
    {
        private readonly NestedSendProbe _probe;
        private readonly string _owner = typeof(TTag) == typeof(OuterHookTag) ? "A" : "B";
        public NestedVoidHook(NestedSendProbe probe) { _probe = probe; probe.Construct(_owner); }
        NestedVoidRequest ISendRequestTransform<NestedVoidRequest>.Before(NestedVoidRequest request, CancellationToken token) => (NestedVoidRequest)_probe.Before(_owner, request, token)!;
        void ISendSuccessObserver<NestedVoidRequest>.After(NestedVoidRequest request, CancellationToken token) => _probe.After(_owner, request, 0, token);
        void ISendExceptionObserver<NestedVoidRequest>.OnException(NestedVoidRequest request, Exception error, CancellationToken token) => _probe.Observe(_owner, request, error, token);
        void ISendFinallyObserver<NestedVoidRequest>.Finally(NestedVoidRequest request, SendExitInfo exit, CancellationToken token) => _probe.Finish(_owner, request, exit, token);
        void IDisposable.Dispose() => _probe.DisposeService(_owner);
    }
    public sealed class NestedResponseHandler : ISyncRequestHandler<NestedResponseRequest, int>, IDisposable
    {
        private readonly NestedSendProbe _probe;
        public NestedResponseHandler(NestedSendProbe probe) { _probe = probe; probe.Construct("handler"); }
        int ISyncRequestHandler<NestedResponseRequest, int>.Handle(NestedResponseRequest request, CancellationToken token) => _probe.Handle(request, token);
        void IDisposable.Dispose() => _probe.DisposeService("handler");
    }
    public sealed class NestedVoidHandler : ISyncRequestHandler<NestedVoidRequest>, IDisposable
    {
        private readonly NestedSendProbe _probe;
        public NestedVoidHandler(NestedSendProbe probe) { _probe = probe; probe.Construct("handler"); }
        void ISyncRequestHandler<NestedVoidRequest>.Handle(NestedVoidRequest request, CancellationToken token) => _probe.Handle(request, token);
        void IDisposable.Dispose() => _probe.DisposeService("handler");
    }

    public static IEnumerable<object[]> NestedHookReferenceCases()
    {
        var normal = "A.ctor A.before B.ctor B.before handler.ctor handler B.after B.finally A.after A.finally";
        var body = "A.ctor A.before B.ctor B.before handler.ctor handler B.onexception B.finally A.onexception A.finally";
        var goldens = new (string Scenario, string Phases)[] {
            ("success", normal), ("replace-both", normal), ("null-request", ""), ("pre-cancel", ""),
            ("A-di-error", "A.resolve"), ("A-ctor-error", "A.ctor"),
            ("A-before-error", "A.ctor A.before A.onexception A.finally"),
            ("A-before-null", "A.ctor A.before A.onexception A.finally"),
            ("A-before-cancel", "A.ctor A.before A.onexception A.finally"),
            ("B-di-error", "A.ctor A.before B.resolve A.onexception A.finally"),
            ("B-ctor-error", "A.ctor A.before B.ctor A.onexception A.finally"),
            ("B-before-error", "A.ctor A.before B.ctor B.before B.onexception B.finally A.onexception A.finally"),
            ("B-before-null", "A.ctor A.before B.ctor B.before B.onexception B.finally A.onexception A.finally"),
            ("B-before-cancel", "A.ctor A.before B.ctor B.before B.onexception B.finally A.onexception A.finally"),
            ("handler-di-error", "A.ctor A.before B.ctor B.before handler.resolve B.onexception B.finally A.onexception A.finally"),
            ("handler-ctor-error", "A.ctor A.before B.ctor B.before handler.ctor B.onexception B.finally A.onexception A.finally"),
            ("handler-error", body), ("handler-oce", body),
            ("B-after-error", "A.ctor A.before B.ctor B.before handler.ctor handler B.after B.onexception B.finally A.onexception A.finally"),
            ("A-after-error", "A.ctor A.before B.ctor B.before handler.ctor handler B.after B.finally A.after A.onexception A.finally"),
            ("B-observer-error", body), ("A-observer-error", body),
            ("B-finally-error-success", "A.ctor A.before B.ctor B.before handler.ctor handler B.after B.finally A.onexception A.finally"),
            ("B-finally-error-over-body", body), ("B-finally-oce-over-body", body), ("A-finally-error-over-B-error", body), ("cascade-replacements", body),
            ("handler-cancel-success", normal), ("B-after-cancel-success", normal), ("B-finally-cancel-success", normal),
            ("A-after-cancel-success", normal), ("A-finally-cancel-success", normal)
        };
        foreach (var isVoid in new[] { false, true }) foreach (var (scenario, phases) in goldens) yield return [isVoid, scenario, phases];
    }

    [Theory]
    [MemberData(nameof(NestedHookReferenceCases))]
    public void Canonical_two_Hooks_SendSync_preserves_saved_state_and_nested_unwind(bool isVoid, string scenario, string golden)
    {
        var probe = new NestedSendProbe(); var ambient = NestedSendProbe.Ambient.Value; var culture = CultureInfo.CurrentCulture;
        NestedSendProbe.Ambient.Value = "caller"; CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        object? request = scenario == "null-request" ? null : isVoid ? new NestedVoidRequest("input") : new NestedResponseRequest("input");
        object outer = isVoid ? new NestedVoidRequest("outer") : new NestedResponseRequest("outer");
        object inner = isVoid ? new NestedVoidRequest("inner") : new NestedResponseRequest("inner");
        var token = probe.Cancellation.Token;
        Exception body = scenario == "handler-oce" ? new OperationCanceledException(token) : new Exception("body identity");
        var bObserver = new Exception("B observer"); var aObserver = new Exception("A observer");
        Exception bFinally = scenario == "B-finally-oce-over-body" ? new OperationCanceledException(token) : new Exception("B finally");
        var aFinally = new Exception("A finally");
        if (scenario is "replace-both" or "B-before-error" or "B-before-null") probe.Replacements["A"] = outer;
        if (scenario == "replace-both") probe.Replacements["B"] = inner;
        if (scenario == "A-before-null") probe.Replacements["A"] = null;
        if (scenario == "B-before-null") probe.Replacements["B"] = null;
        var directFailure = scenario switch { "A-ctor-error" => "A.ctor", "B-ctor-error" => "B.ctor", "handler-ctor-error" => "handler.ctor",
            "A-before-error" => "A.before", "B-before-error" => "B.before", "B-after-error" => "B.after", "A-after-error" => "A.after", _ => null };
        if (directFailure != null) probe.Errors[directFailure] = body;
        if (scenario is "handler-error" or "handler-oce" or "B-observer-error" or "A-observer-error" or "B-finally-error-over-body" or "B-finally-oce-over-body" or "A-finally-error-over-B-error" or "cascade-replacements") probe.Errors["handler"] = body;
        if (scenario is "B-observer-error" or "cascade-replacements") probe.Errors["B.onexception"] = bObserver;
        if (scenario is "A-observer-error" or "cascade-replacements") probe.Errors["A.onexception"] = aObserver;
        if (scenario is "B-finally-error-success" or "B-finally-error-over-body" or "B-finally-oce-over-body" or "A-finally-error-over-B-error" or "cascade-replacements") probe.Errors["B.finally"] = bFinally;
        if (scenario is "A-finally-error-over-B-error" or "cascade-replacements") probe.Errors["A.finally"] = aFinally;
        var cancelPhase = scenario switch { "A-before-cancel" => "A.before", "B-before-cancel" => "B.before", "handler-cancel-success" => "handler",
            "B-after-cancel-success" => "B.after", "B-finally-cancel-success" => "B.finally", "A-after-cancel-success" => "A.after", "A-finally-cancel-success" => "A.finally", _ => null };
        if (cancelPhase != null) probe.CancelAt.Add(cancelPhase);
        if (scenario == "pre-cancel") probe.Cancellation.Cancel();
        try
        {
            IServiceCollection services = new ServiceCollection(); services.AddSingleton(probe);
            services.AddScoped<NestedResponseHook<OuterHookTag>>(); services.AddScoped<NestedResponseHook<InnerHookTag>>(); services.AddScoped<NestedResponseHandler>();
            services.AddScoped<NestedVoidHook<OuterHookTag>>(); services.AddScoped<NestedVoidHook<InnerHookTag>>(); services.AddScoped<NestedVoidHandler>();
            if (scenario.EndsWith("-di-error", StringComparison.Ordinal))
            {
                var owner = scenario[..scenario.IndexOf('-')];
                var type = owner == "A" ? isVoid ? typeof(NestedVoidHook<OuterHookTag>) : typeof(NestedResponseHook<OuterHookTag>) :
                    owner == "B" ? isVoid ? typeof(NestedVoidHook<InnerHookTag>) : typeof(NestedResponseHook<InnerHookTag>) : isVoid ? typeof(NestedVoidHandler) : typeof(NestedResponseHandler);
                services.AddScoped(type, _ => { probe.Phases.Add(owner + ".resolve"); throw body; });
            }
            using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            using (var scope = provider.CreateScope())
            {
                object? result = null; Exception? actual = null;
                try
                {
                    if (isVoid) CanonicalSendSyncReference.SendTwoHooksVoid<NestedVoidRequest, NestedVoidHook<OuterHookTag>, NestedVoidHook<InnerHookTag>, NestedVoidHandler>(scope.ServiceProvider, (NestedVoidRequest)request!, token);
                    else result = CanonicalSendSyncReference.SendTwoHooksResponse<NestedResponseRequest, int, NestedResponseHook<OuterHookTag>, NestedResponseHook<InnerHookTag>, NestedResponseHandler>(scope.ServiceProvider, (NestedResponseRequest)request!, token);
                }
                catch (Exception error) { actual = error; }
                Assert.Equal(golden.Split(' ', StringSplitOptions.RemoveEmptyEntries), probe.Phases);
                var savedA = probe.Replacements.TryGetValue("A", out var a) && scenario != "A-before-error" ? a : request;
                var savedB = probe.Replacements.TryGetValue("B", out var b) && scenario != "B-before-error" ? b : savedA;
                for (var i = 0; i < probe.Calls.Count; i++)
                {
                    var call = probe.Calls[i]; Assert.Equal(token, call.Token);
                    Assert.Same(call.Phase == "A.before" ? request : call.Phase == "B.before" ? savedA : call.Phase.StartsWith("A.", StringComparison.Ordinal) ? savedA : savedB, call.Request);
                    Assert.Equal(i == 0 ? "caller" : probe.Calls[i - 1].Phase, call.Ambient);
                    var previous = i == 0 ? null : probe.Calls[i - 1].Phase;
                    Assert.Equal(previous == null ? "" : ReferenceSendProbe.CultureFor(previous.Contains('.') ? previous[(previous.IndexOf('.') + 1)..] : previous), call.Culture);
                }
                if (scenario is "success" or "replace-both" || scenario.EndsWith("-cancel-success", StringComparison.Ordinal))
                { Assert.Null(actual); Assert.Equal(isVoid ? null : (object)43, result); }
                else if (scenario is "null-request" or "A-before-null" or "B-before-null") Assert.IsType<ArgumentNullException>(actual);
                else if (scenario is "pre-cancel" or "A-before-cancel" or "B-before-cancel") Assert.Equal(token, Assert.IsType<OperationCanceledException>(actual).CancellationToken);
                else Assert.Same(probe.Errors.GetValueOrDefault("A.finally") ?? probe.Errors.GetValueOrDefault("A.onexception") ?? probe.Errors.GetValueOrDefault("B.finally") ?? probe.Errors.GetValueOrDefault("B.onexception") ?? body, actual);
                Exception? expectedB = scenario switch
                {
                    "B-before-null" or "B-before-cancel" => actual,
                    "B-before-error" or "handler-di-error" or "handler-ctor-error" or "handler-error" or "handler-oce" or "B-after-error" or
                    "B-observer-error" or "A-observer-error" or "B-finally-error-over-body" or "B-finally-oce-over-body" or
                    "A-finally-error-over-B-error" or "cascade-replacements" => body,
                    _ => null
                };
                Exception? expectedA = scenario switch
                {
                    "A-before-null" or "A-before-cancel" or "B-before-null" or "B-before-cancel" => actual,
                    "B-finally-error-success" or "B-finally-error-over-body" or "B-finally-oce-over-body" or
                    "A-finally-error-over-B-error" or "cascade-replacements" => bFinally,
                    "B-observer-error" => bObserver,
                    "A-before-error" or "B-di-error" or "B-ctor-error" or "B-before-error" or "handler-di-error" or "handler-ctor-error" or
                    "handler-error" or "handler-oce" or "B-after-error" or "A-after-error" or "A-observer-error" => body,
                    _ => null
                };
                if (expectedB == null) Assert.False(probe.Observed.ContainsKey("B"));
                else { Assert.True(probe.Observed.TryGetValue("B", out var observedB)); Assert.Same(expectedB, observedB); }
                if (expectedA == null) Assert.False(probe.Observed.ContainsKey("A"));
                else { Assert.True(probe.Observed.TryGetValue("A", out var observedA)); Assert.Same(expectedA, observedA); }
                if (probe.Exits.TryGetValue("B", out var exitB))
                {
                    Assert.Equal(expectedB == null, exitB.Success);
                    Assert.Same(scenario is "B-observer-error" or "cascade-replacements" ? bObserver : expectedB, exitB.Error);
                }
                if (probe.Exits.TryGetValue("A", out var exitA))
                {
                    Assert.Equal(expectedA == null, exitA.Success);
                    Assert.Same(scenario is "A-observer-error" or "cascade-replacements" ? aObserver : expectedA, exitA.Error);
                }
                if (scenario is "A-before-null" or "A-before-cancel" or "B-before-null" or "B-before-cancel")
                {
                    Assert.NotNull(actual); Assert.Same(actual, probe.Observed["A"]); Assert.Same(actual, probe.Exits["A"].Error);
                    if (probe.Observed.TryGetValue("B", out var seen)) { Assert.Same(actual, seen); Assert.Same(actual, probe.Exits["B"].Error); }
                }
                Assert.Empty(probe.Disposals);
                Assert.Equal(probe.Calls.Count == 0 ? "caller" : probe.Calls[^1].Phase, NestedSendProbe.Ambient.Value);
                Assert.Equal(cancelPhase != null || scenario == "pre-cancel", token.IsCancellationRequested);
            }
            Assert.Equal(probe.Constructed.Order(), probe.Disposals.Keys.Order()); Assert.All(probe.Disposals.Values, count => Assert.Equal(1, count));
        }
        finally { probe.Cancellation.Dispose(); NestedSendProbe.Ambient.Value = ambient; CultureInfo.CurrentCulture = culture; }
    }

    [Theory]
    [InlineData(false, ServiceLifetime.Scoped)] [InlineData(true, ServiceLifetime.Scoped)]
    [InlineData(false, ServiceLifetime.Transient)] [InlineData(true, ServiceLifetime.Transient)]
    [InlineData(false, ServiceLifetime.Singleton)] [InlineData(true, ServiceLifetime.Singleton)]
    public void Canonical_two_Hooks_SendSync_leaves_service_cleanup_to_scope_or_root(bool isVoid, ServiceLifetime lifetime)
    {
        var probe = new NestedSendProbe(); var ambient = NestedSendProbe.Ambient.Value; var culture = CultureInfo.CurrentCulture;
        try
        {
            IServiceCollection services = new ServiceCollection(); services.AddSingleton(probe);
            var types = isVoid ? new[] { typeof(NestedVoidHook<OuterHookTag>), typeof(NestedVoidHook<InnerHookTag>), typeof(NestedVoidHandler) } :
                new[] { typeof(NestedResponseHook<OuterHookTag>), typeof(NestedResponseHook<InnerHookTag>), typeof(NestedResponseHandler) };
            foreach (var type in types) services.Add(ServiceDescriptor.Describe(type, type, lifetime));
            using (var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }))
            {
                for (var s = 0; s < 2; s++)
                {
                    using (var scope = provider.CreateScope())
                    {
                        for (var i = 0; i < 2; i++)
                        {
                            probe.Observed.Clear(); probe.Exits.Clear();
                            if (isVoid) CanonicalSendSyncReference.SendTwoHooksVoid<NestedVoidRequest, NestedVoidHook<OuterHookTag>, NestedVoidHook<InnerHookTag>, NestedVoidHandler>(scope.ServiceProvider, new("input"), default);
                            else Assert.Equal(43, CanonicalSendSyncReference.SendTwoHooksResponse<NestedResponseRequest, int, NestedResponseHook<OuterHookTag>, NestedResponseHook<InnerHookTag>, NestedResponseHandler>(scope.ServiceProvider, new("input"), default));
                            foreach (var owner in new[] { "A", "B", "handler" })
                                Assert.Equal(s * (lifetime == ServiceLifetime.Singleton ? 0 : lifetime == ServiceLifetime.Scoped ? 1 : 2), probe.Disposals.GetValueOrDefault(owner));
                        }
                    }
                    foreach (var owner in new[] { "A", "B", "handler" })
                        Assert.Equal((s + 1) * (lifetime == ServiceLifetime.Singleton ? 0 : lifetime == ServiceLifetime.Scoped ? 1 : 2), probe.Disposals.GetValueOrDefault(owner));
                }
                foreach (var owner in new[] { "A", "B", "handler" })
                    Assert.Equal(lifetime == ServiceLifetime.Singleton ? 1 : lifetime == ServiceLifetime.Scoped ? 2 : 4, probe.Phases.Count(p => p == owner + ".ctor"));
            }
            foreach (var owner in new[] { "A", "B", "handler" })
                Assert.Equal(lifetime == ServiceLifetime.Singleton ? 1 : lifetime == ServiceLifetime.Scoped ? 2 : 4, probe.Disposals[owner]);
        }
        finally { probe.Cancellation.Dispose(); NestedSendProbe.Ambient.Value = ambient; CultureInfo.CurrentCulture = culture; }
    }
}
