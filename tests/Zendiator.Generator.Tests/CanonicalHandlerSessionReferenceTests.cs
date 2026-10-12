using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    public sealed record SessionResponseRequest : ISyncRequest<int>;
    public sealed record SessionVoidRequest : ISyncRequest;
    public sealed class HandlerSessionReferenceProbe
    {
        public readonly CanonicalSyncRuntimeProbe Core = new();
        public Exception? FactoryConstructorError, PartialError;
        public bool CancelCleanup;
        public int DependencyCreated, DependencyDisposed, PartialDisposed;
        public readonly List<IDisposable> Borrowed = new();
    }
    public sealed class SessionFactoryDependency(HandlerSessionReferenceProbe probe) : IDisposable
    {
        private readonly HandlerSessionReferenceProbe _probe = Construct(probe);
        private static HandlerSessionReferenceProbe Construct(HandlerSessionReferenceProbe probe)
        { probe.Core.Trace.Add("dependency.ctor"); probe.DependencyCreated++; return probe; }
        public void Dispose() { _probe.Core.Trace.Add("dependency.dispose"); _probe.DependencyDisposed++; }
    }
    private sealed class PartialSessionResource(HandlerSessionReferenceProbe probe) : IDisposable
    {
        private readonly HandlerSessionReferenceProbe _probe = Acquire(probe);
        private static HandlerSessionReferenceProbe Acquire(HandlerSessionReferenceProbe probe)
        { probe.Core.Trace.Add("partial.acquire"); return probe; }
        public void Dispose() { _probe.Core.Trace.Add("partial.dispose"); _probe.PartialDisposed++; }
    }
    public abstract class ReferenceSessionFactory<R, H> : IDisposable where R : class where H : class
    {
        private readonly HandlerSessionReferenceProbe _probe;
        private readonly Func<H> _create;
        protected ReferenceSessionFactory(HandlerSessionReferenceProbe probe, SessionFactoryDependency dependency, Func<H> create)
        {
            _probe = probe; _create = create; probe.Core.Trace.Add("factory.ctor");
            if (probe.FactoryConstructorError != null) throw probe.FactoryConstructorError;
        }
        protected H CreateSession(R request, CancellationToken token, bool borrowed)
        {
            _probe.Core.Factory(this, request, token);
            if (_probe.PartialError != null) { using var partial = new PartialSessionResource(_probe); throw _probe.PartialError; }
            if (_probe.Core.NullSession) return null!;
            var session = _create();
            if (borrowed && session is IDisposable disposable) _probe.Borrowed.Add(disposable);
            return session;
        }
        public void Dispose() => _probe.Core.DisposeFactory();
    }
    public sealed class OwnedReferenceSessionFactory<R, H>(HandlerSessionReferenceProbe probe, SessionFactoryDependency dependency, Func<H> create)
        : ReferenceSessionFactory<R, H>(probe, dependency, create), IOwnedSessionFactory<R, H> where R : class where H : class
    { public H Create(R request, CancellationToken token) => CreateSession(request, token, false); }
    public sealed class BorrowedReferenceSessionFactory<R, H>(HandlerSessionReferenceProbe probe, SessionFactoryDependency dependency, Func<H> create)
        : ReferenceSessionFactory<R, H>(probe, dependency, create), IBorrowedSessionFactory<R, H> where R : class where H : class
    { public H Create(R request, CancellationToken token) => CreateSession(request, token, true); }
    public sealed class ReferenceResponseSession : ISyncRequestHandler<SessionResponseRequest, int>, IDisposable
    {
        private readonly HandlerSessionReferenceProbe _probe;
        public ReferenceResponseSession(HandlerSessionReferenceProbe probe) { _probe = probe; probe.Core.ConstructHandler(this); }
        int ISyncRequestHandler<SessionResponseRequest, int>.Handle(SessionResponseRequest request, CancellationToken token) => _probe.Core.Handle(this, request, token);
        public void Dispose() { if (_probe.CancelCleanup) _probe.Core.Cancellation.Cancel(); _probe.Core.Cleanup(this); }
    }
    public sealed class ReferenceVoidSession : ISyncRequestHandler<SessionVoidRequest>, IDisposable
    {
        private readonly HandlerSessionReferenceProbe _probe;
        public ReferenceVoidSession(HandlerSessionReferenceProbe probe) { _probe = probe; probe.Core.ConstructHandler(this); }
        void ISyncRequestHandler<SessionVoidRequest>.Handle(SessionVoidRequest request, CancellationToken token) => _probe.Core.Handle(this, request, token);
        public void Dispose() { if (_probe.CancelCleanup) _probe.Core.Cancellation.Cancel(); _probe.Core.Cleanup(this); }
    }

    private static IServiceCollection HandlerSessionReferenceServices(HandlerSessionReferenceProbe probe, bool isVoid, bool owned, ServiceLifetime lifetime)
    {
        IServiceCollection services = new ServiceCollection(); services.AddSingleton(probe);
        services.Add(ServiceDescriptor.Describe(typeof(SessionFactoryDependency), typeof(SessionFactoryDependency), lifetime));
        var factory = isVoid ? owned ? typeof(OwnedReferenceSessionFactory<SessionVoidRequest, ReferenceVoidSession>) : typeof(BorrowedReferenceSessionFactory<SessionVoidRequest, ReferenceVoidSession>) :
            owned ? typeof(OwnedReferenceSessionFactory<SessionResponseRequest, ReferenceResponseSession>) : typeof(BorrowedReferenceSessionFactory<SessionResponseRequest, ReferenceResponseSession>);
        services.Add(ServiceDescriptor.Describe(factory, provider => {
            probe.Core.Trace.Add("resolve-factory");
            if (probe.Core.DiError != null) throw probe.Core.DiError;
            if (probe.Core.NullService) return null!;
            var dependency = provider.GetRequiredService<SessionFactoryDependency>();
            return isVoid ? owned ? new OwnedReferenceSessionFactory<SessionVoidRequest, ReferenceVoidSession>(probe, dependency, () => new(probe)) :
                new BorrowedReferenceSessionFactory<SessionVoidRequest, ReferenceVoidSession>(probe, dependency, () => new(probe)) :
                owned ? new OwnedReferenceSessionFactory<SessionResponseRequest, ReferenceResponseSession>(probe, dependency, () => new(probe)) :
                new BorrowedReferenceSessionFactory<SessionResponseRequest, ReferenceResponseSession>(probe, dependency, () => new(probe));
        }, lifetime));
        services.AddScoped<ReferenceResponseHook>(_ => throw new Exception("A zero-Hook session route must not resolve a Hook."));
        services.AddScoped<ReferenceVoidHook>(_ => throw new Exception("A zero-Hook session route must not resolve a Hook."));
        return services;
    }

    private static (object? Result, Exception? Error) InvokeHandlerSessionReference(IServiceProvider provider, bool isVoid, bool owned, object? request, CancellationToken token)
    {
        try
        {
            if (isVoid)
            {
                if (owned) CanonicalSendSyncReference.SendOwnedVoidWithoutHook<SessionVoidRequest, ReferenceVoidSession, OwnedReferenceSessionFactory<SessionVoidRequest, ReferenceVoidSession>>(provider, (SessionVoidRequest)request!, token);
                else CanonicalSendSyncReference.SendBorrowedVoidWithoutHook<SessionVoidRequest, ReferenceVoidSession, BorrowedReferenceSessionFactory<SessionVoidRequest, ReferenceVoidSession>>(provider, (SessionVoidRequest)request!, token);
                return (null, null);
            }
            return (owned ? CanonicalSendSyncReference.SendOwnedResponseWithoutHook<SessionResponseRequest, int, ReferenceResponseSession, OwnedReferenceSessionFactory<SessionResponseRequest, ReferenceResponseSession>>(provider, (SessionResponseRequest)request!, token) :
                CanonicalSendSyncReference.SendBorrowedResponseWithoutHook<SessionResponseRequest, int, ReferenceResponseSession, BorrowedReferenceSessionFactory<SessionResponseRequest, ReferenceResponseSession>>(provider, (SessionResponseRequest)request!, token), null);
        }
        catch (Exception error) { return (null, error); }
    }

    public static IEnumerable<object[]> HandlerSessionReferenceCases()
    {
        foreach (var owned in new[] { false, true }) foreach (var isVoid in new[] { false, true })
        {
            foreach (var scenario in new[] { "success", "null-request", "pre-cancel", "di-error", "di-null", "factory-ctor-error", "factory-error", "factory-oce",
                "factory-null", "handler-ctor-error", "handler-error", "handler-oce", "cancel-in-factory", "cancel-in-handler", "partial-error", "partial-oce" }) yield return [owned, isVoid, scenario];
            if (owned) foreach (var scenario in new[] { "cleanup-error-success", "cleanup-oce-success", "cleanup-error-over-body", "cleanup-oce-over-body",
                "cleanup-error-over-oce", "cleanup-oce-over-oce", "cancel-in-cleanup" }) yield return [owned, isVoid, scenario];
        }
    }

    [Theory]
    [MemberData(nameof(HandlerSessionReferenceCases))]
    public void Canonical_zero_Hook_Handler_session_preserves_transfer_failure_context_and_owner(bool owned, bool isVoid, string scenario)
    {
        var probe = new HandlerSessionReferenceProbe(); var core = probe.Core;
        var ambient = CanonicalSyncRuntimeProbe.Ambient.Value; var culture = CultureInfo.CurrentCulture;
        CanonicalSyncRuntimeProbe.Ambient.Value = "caller"; CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        using var otherToken = new CancellationTokenSource();
        var diError = new Exception("DI identity"); var factoryError = new Exception("factory identity"); var constructorError = new Exception("constructor identity");
        var bodyError = new Exception("body identity"); var cleanupError = new Exception("cleanup identity"); var partialError = new Exception("partial identity");
        var factoryOce = new OperationCanceledException(otherToken.Token); var bodyOce = new OperationCanceledException(otherToken.Token);
        var cleanupOce = new OperationCanceledException(otherToken.Token); var partialOce = new OperationCanceledException(otherToken.Token);
        core.DiError = scenario == "di-error" ? diError : null; core.NullService = scenario == "di-null"; core.NullSession = scenario == "factory-null";
        probe.FactoryConstructorError = scenario == "factory-ctor-error" ? constructorError : null;
        core.ConstructorError = scenario == "handler-ctor-error" ? constructorError : null;
        core.FactoryError = scenario == "factory-error" ? factoryError : scenario == "factory-oce" ? factoryOce : null;
        core.HandlerError = scenario is "handler-error" or "cleanup-error-over-body" or "cleanup-oce-over-body" ? bodyError :
            scenario is "handler-oce" or "cleanup-error-over-oce" or "cleanup-oce-over-oce" ? bodyOce : null;
        core.CleanupError = scenario.StartsWith("cleanup-error", StringComparison.Ordinal) ? cleanupError : scenario.StartsWith("cleanup-oce", StringComparison.Ordinal) ? cleanupOce : null;
        probe.PartialError = scenario == "partial-error" ? partialError : scenario == "partial-oce" ? partialOce : null;
        core.CancelInFactory = scenario == "cancel-in-factory"; core.CancelInHandler = scenario == "cancel-in-handler"; probe.CancelCleanup = scenario == "cancel-in-cleanup";
        if (scenario == "pre-cancel") core.Cancellation.Cancel();
        object? request = scenario == "null-request" ? null : isVoid ? new SessionVoidRequest() : new SessionResponseRequest();
        try
        {
            using var provider = HandlerSessionReferenceServices(probe, isVoid, owned, ServiceLifetime.Scoped).BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            using (var scope = provider.CreateScope())
            {
                var actual = InvokeHandlerSessionReference(scope.ServiceProvider, isVoid, owned, request, core.Cancellation.Token);
                string[] golden = scenario switch {
                    "null-request" or "pre-cancel" => [], "di-error" or "di-null" => ["resolve-factory"],
                    "factory-ctor-error" => ["resolve-factory", "dependency.ctor", "factory.ctor"],
                    "factory-error" or "factory-oce" or "factory-null" => ["resolve-factory", "dependency.ctor", "factory.ctor", "factory"],
                    "partial-error" or "partial-oce" => ["resolve-factory", "dependency.ctor", "factory.ctor", "factory", "partial.acquire", "partial.dispose"],
                    "handler-ctor-error" => ["resolve-factory", "dependency.ctor", "factory.ctor", "factory", "handler.ctor"],
                    _ => owned ? ["resolve-factory", "dependency.ctor", "factory.ctor", "factory", "handler.ctor", "handler", "handler.dispose"] :
                        ["resolve-factory", "dependency.ctor", "factory.ctor", "factory", "handler.ctor", "handler"] };
                Assert.Equal(golden, core.Trace);
                Exception? escaping = scenario switch { "di-error" => diError, "factory-error" => factoryError, "factory-oce" => factoryOce,
                    "factory-ctor-error" or "handler-ctor-error" => constructorError, "handler-error" => bodyError, "handler-oce" => bodyOce,
                    "partial-error" => partialError, "partial-oce" => partialOce,
                    "cleanup-error-success" or "cleanup-error-over-body" or "cleanup-error-over-oce" => cleanupError,
                    "cleanup-oce-success" or "cleanup-oce-over-body" or "cleanup-oce-over-oce" => cleanupOce, _ => null };
                if (escaping != null) Assert.Same(escaping, actual.Error);
                else if (scenario == "null-request") Assert.Equal("request", Assert.IsType<ArgumentNullException>(actual.Error).ParamName);
                else if (scenario == "pre-cancel") Assert.Equal(core.Cancellation.Token, Assert.IsType<OperationCanceledException>(actual.Error).CancellationToken);
                else if (scenario is "di-null" or "factory-null") Assert.IsType<InvalidOperationException>(actual.Error);
                else { Assert.Null(actual.Error); Assert.Equal(isVoid ? null : (object)73, actual.Result); }
                if (escaping is OperationCanceledException oce) Assert.Equal(otherToken.Token, Assert.IsType<OperationCanceledException>(actual.Error).CancellationToken);
                Assert.All(core.Calls, call => { Assert.Same(request, call.Request); Assert.Equal(core.Cancellation.Token, call.Token); });
                var ran = golden.Contains("handler"); var acquired = golden.Contains("factory");
                Assert.Equal(ran ? 1 : 0, core.Created.Count); Assert.Equal(owned && ran ? 1 : 0, core.Released.Count);
                if (ran) Assert.Same(Assert.Single(core.Created), Assert.Single(core.Calls, call => call.Phase == "handler").Receiver);
                if (owned && ran) Assert.Same(Assert.Single(core.Created), Assert.Single(core.Released));
                Assert.Equal(scenario.StartsWith("partial-", StringComparison.Ordinal) ? 1 : 0, probe.PartialDisposed);
                Assert.Equal(0, core.FactoryDisposals); Assert.Equal(0, probe.DependencyDisposed);
                Assert.Equal(ran ? owned ? new[] { "caller", "factory", "handler" } : new[] { "caller", "factory" } : acquired ? new[] { "caller" } : [], core.AmbientObserved);
                Assert.Equal(ran ? owned ? new[] { "", "fr-FR", "es-ES" } : new[] { "", "fr-FR" } : acquired ? new[] { "" } : [], core.CultureObserved);
                Assert.Equal(ran ? owned ? "cleanup" : "handler" : acquired ? "factory" : "caller", CanonicalSyncRuntimeProbe.Ambient.Value);
                Assert.Equal(ran ? owned ? "en-US" : "es-ES" : acquired ? "fr-FR" : "", CultureInfo.CurrentCulture.Name);
                Assert.Equal(scenario is "pre-cancel" or "cancel-in-factory" or "cancel-in-handler" or "cancel-in-cleanup", core.Cancellation.IsCancellationRequested);
                WriteNestedHookEvidence("handler-session-reference", owned + "-" + isVoid + "-" + scenario, new {
                    owned, isVoid, scenario, golden, trace = core.Trace.ToArray(), actual.Result,
                    errorType = actual.Error?.GetType().FullName, originalExceptionPreserved = escaping == null || ReferenceEquals(escaping, actual.Error),
                    nullParameter = (actual.Error as ArgumentNullException)?.ParamName,
                    calls = core.Calls.Select(call => new { call.Phase, requestPreserved = ReferenceEquals(request, call.Request), tokenPreserved = call.Token == core.Cancellation.Token }),
                    createdSessions = core.Created.Count, operationReleasedSessions = core.Released.Count,
                    core.FactoryDisposals, probe.DependencyCreated, probe.DependencyDisposed, probe.PartialDisposed,
                    ambient = CanonicalSyncRuntimeProbe.Ambient.Value, culture = CultureInfo.CurrentCulture.Name,
                    ambientObserved = core.AmbientObserved.ToArray(), cultureObserved = core.CultureObserved.ToArray(), core.Cancellation.IsCancellationRequested
                });
            }
            Assert.Equal(probe.DependencyCreated, probe.DependencyDisposed);
            Assert.Equal(core.Trace.Contains("factory.ctor") && scenario != "factory-ctor-error" ? 1 : 0, core.FactoryDisposals);
            Assert.Equal(owned ? core.Created.Count : 0, core.Released.Count);
            foreach (var borrowed in probe.Borrowed) borrowed.Dispose();
            Assert.Equal(core.Created.Count, core.Released.Count);
        }
        finally { core.Cancellation.Dispose(); CanonicalSyncRuntimeProbe.Ambient.Value = ambient; CultureInfo.CurrentCulture = culture; }
    }

    public static IEnumerable<object[]> HandlerSessionReferenceLifetimeCases()
    {
        foreach (var owned in new[] { false, true }) foreach (var isVoid in new[] { false, true })
            foreach (var lifetime in new[] { ServiceLifetime.Scoped, ServiceLifetime.Transient, ServiceLifetime.Singleton }) yield return [owned, isVoid, lifetime];
    }

    [Theory]
    [MemberData(nameof(HandlerSessionReferenceLifetimeCases))]
    public void Canonical_zero_Hook_Handler_session_keeps_factory_DI_owner_and_session_transfer_distinct(bool owned, bool isVoid, ServiceLifetime lifetime)
    {
        var probe = new HandlerSessionReferenceProbe(); var core = probe.Core;
        var ambient = CanonicalSyncRuntimeProbe.Ambient.Value; var culture = CultureInfo.CurrentCulture;
        try
        {
            using (var provider = HandlerSessionReferenceServices(probe, isVoid, owned, lifetime).BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }))
            {
                for (var s = 0; s < 2; s++)
                {
                    using (var scope = provider.CreateScope())
                    {
                        for (var i = 0; i < 2; i++)
                        {
                            var actual = InvokeHandlerSessionReference(scope.ServiceProvider, isVoid, owned, isVoid ? new SessionVoidRequest() : new SessionResponseRequest(), default);
                            Assert.Null(actual.Error); Assert.Equal(isVoid ? null : (object)73, actual.Result);
                            Assert.Equal(owned ? s * 2 + i + 1 : 0, core.Released.Count);
                            Assert.Equal(lifetime == ServiceLifetime.Singleton ? 0 : s * (lifetime == ServiceLifetime.Scoped ? 1 : 2), core.FactoryDisposals);
                            Assert.Equal(core.FactoryDisposals, probe.DependencyDisposed);
                        }
                    }
                    Assert.Equal(lifetime == ServiceLifetime.Singleton ? 0 : (s + 1) * (lifetime == ServiceLifetime.Scoped ? 1 : 2), core.FactoryDisposals);
                    Assert.Equal(core.FactoryDisposals, probe.DependencyDisposed);
                }
                Assert.Equal(4, core.Created.Count); Assert.Equal(owned ? 4 : 0, core.Released.Count);
            }
            var factories = lifetime == ServiceLifetime.Singleton ? 1 : lifetime == ServiceLifetime.Scoped ? 2 : 4;
            Assert.Equal(factories, core.FactoryDisposals); Assert.Equal(factories, probe.DependencyCreated); Assert.Equal(factories, probe.DependencyDisposed);
            Assert.Equal(factories, core.Trace.Count(t => t == "factory.ctor")); Assert.Equal(4, core.Trace.Count(t => t == "factory"));
            Assert.Equal(4, core.Created.Distinct(ReferenceEqualityComparer.Instance).Count());
            foreach (var borrowed in probe.Borrowed) borrowed.Dispose();
            Assert.Equal(4, core.Released.Count); Assert.All(core.Created, session => Assert.Single(core.Released, item => ReferenceEquals(item, session)));
            WriteNestedHookEvidence("handler-session-reference-lifetimes", owned + "-" + isVoid + "-" + lifetime, new {
                owned, isVoid, lifetime = lifetime.ToString(), trace = core.Trace.ToArray(), factories, core.FactoryDisposals,
                probe.DependencyCreated, probe.DependencyDisposed, createdSessions = core.Created.Count,
                operationReleasedSessions = owned ? 4 : 0, finalOwnerReleasedSessions = core.Released.Count
            });
        }
        finally { core.Cancellation.Dispose(); CanonicalSyncRuntimeProbe.Ambient.Value = ambient; CultureInfo.CurrentCulture = culture; }
    }
}
