using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private static readonly ConcurrentDictionary<(bool Owned, bool Void), SyncFixture> HandlerSessionDifferentialFixtures = new();
    private static SyncFixture HandlerSessionDifferentialFixture(bool owned, bool isVoid) => HandlerSessionDifferentialFixtures.GetOrAdd((owned, isVoid), key =>
    {
        var receiver = key.Owned ? "owned" : "borrowed";
        var source = SyncRuntimeSource(receiver, key.Void)
            .Replace("global::Zendiator.Generator.Tests.CanonicalSyncRuntimeProbe", "global::Zendiator.Generator.Tests.GeneratorTests.HandlerSessionReferenceProbe")
            .Replace("probe.Trace.Add", "probe.Core.Trace.Add").Replace("probe.DiError", "probe.Core.DiError").Replace("probe.NullService", "probe.Core.NullService")
            .Replace("probe.ConstructHandler(this)", "probe.Core.ConstructHandler(this)")
            .Replace("_probe.Handle(this,request,token)", "_probe.Core.Handle(this,request,token)")
            .Replace("void IDisposable.Dispose()=>_probe.Cleanup(this);", "void IDisposable.Dispose(){ if (_probe.CancelCleanup) _probe.Core.Cancellation.Cancel(); _probe.Core.Cleanup(this); }")
            .Replace("_probe.DisposeFactory()", "_probe.Core.DisposeFactory()")
            .Replace("public FactoryBase(Probe probe)", "public FactoryBase(Probe probe,global::Zendiator.Generator.Tests.GeneratorTests.SessionFactoryDependency dependency)")
            .Replace("public Factory(Probe probe):base(probe)", "public Factory(Probe probe,global::Zendiator.Generator.Tests.GeneratorTests.SessionFactoryDependency dependency):base(probe,dependency)")
            .Replace("_probe.Factory(this,request,token);", "")
            .Replace("return _probe.NullSession ? null! : new Handler<T>(_probe);", "return _probe.CreateSession(this,request,token,()=>new Handler<T>(_probe)," + (key.Owned ? "false" : "true") + ");")
            .Replace("new Factory<string>(probe)", "new Factory<string>(probe,provider.GetRequiredService<global::Zendiator.Generator.Tests.GeneratorTests.SessionFactoryDependency>())");
        return CompileSyncFixture(receiver, key.Void, sourceOverride: source);
    });

    private static (object? Result, Exception? Error) InvokeCompiledHandlerSessionReference(SyncFixture fixture, IServiceProvider provider,
        bool isVoid, bool owned, object? request, CancellationToken token)
    {
        var handler = fixture.Assembly.GetType("App.Handler`1")!.MakeGenericType(typeof(string));
        var name = owned ? isVoid ? "SendOwnedVoidWithoutHook" : "SendOwnedResponseWithoutHook" :
            isVoid ? "SendBorrowedVoidWithoutHook" : "SendBorrowedResponseWithoutHook";
        var method = typeof(CanonicalSendSyncReference).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(isVoid ? [fixture.Request, handler, fixture.Service] : [fixture.Request, typeof(int), handler, fixture.Service]);
        try
        {
            return (method.Invoke(null, [provider, request, token]), null);
        }
        catch (TargetInvocationException error)
        {
            return (null, error.InnerException);
        }
    }

    [Theory]
    [MemberData(nameof(HandlerSessionReferenceCases))]
    public void Generated_zero_Hook_Handler_session_matches_accepted_reference_and_independent_goldens(bool owned, bool isVoid, string scenario)
    {
        var fixture = HandlerSessionDifferentialFixture(owned, isVoid);
        var reference = RunHandlerSessionScenario(owned, isVoid, scenario, fixture);
        var generated = RunHandlerSessionScenario(owned, isVoid, scenario, fixture, generated: true);
        Assert.Equal(reference, generated);
        WriteNestedHookEvidence("handler-session-differential", owned + "-" + isVoid + "-" + scenario,
            new
            {
                owned,
                isVoid,
                scenario,
                reference = JsonSerializer.Deserialize<JsonElement>(reference),
                generated = JsonSerializer.Deserialize<JsonElement>(generated)
            });
    }

    [Theory]
    [MemberData(nameof(HandlerSessionReferenceLifetimeCases))]
    public void Generated_zero_Hook_Handler_session_captures_DI_dependencies_and_preserves_transfer_lifetimes(bool owned, bool isVoid, ServiceLifetime lifetime)
    {
        var fixture = HandlerSessionDifferentialFixture(owned, isVoid);
        var reference = lifetime == ServiceLifetime.Transient ? null : RunHandlerSessionLifetimes(owned, isVoid, lifetime, fixture);
        var generated = RunHandlerSessionLifetimes(owned, isVoid, lifetime, fixture, generated: true);
        if (reference is not null)
            Assert.Equal(reference, generated);
        WriteNestedHookEvidence("handler-session-differential-lifetimes", owned + "-" + isVoid + "-" + lifetime,
            new
            {
                owned,
                isVoid,
                lifetime = lifetime.ToString(),
                reference = reference is null ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(reference),
                generated = JsonSerializer.Deserialize<JsonElement>(generated)
            });
    }

    public sealed class SharedBorrowedSessionProbe
    {
        public readonly CanonicalConcurrentSyncProbe Core = new();
        private readonly object _gate = new();
        private object? _session;
        public ConcurrentBag<string> Trace => Core.Trace;
        public Exception? DiError => null;
        public bool NullService => false;
        public bool NullSession => false;
        public object GetShared(Func<object> create)
        {
            lock (_gate)
                return _session ??= create();
        }
        public void ReleaseOwner() => ((IDisposable)_session!).Dispose();
        public void ConstructHandler(object receiver) => Core.ConstructHandler(receiver);
        public void ConstructFactory() => Core.ConstructFactory();
        public void Factory(object receiver, object request, CancellationToken token) => Core.Factory(receiver, request, token);
        public int Handle(object receiver, object request, CancellationToken token) => Core.Handle(receiver, request, token);
        public void Cleanup(object receiver) => Core.Cleanup(receiver);
        public void DisposeFactory() => Core.DisposeFactory();
    }
    private static readonly ConcurrentDictionary<bool, SyncFixture> SharedBorrowedFixtures = new();
    private static SyncFixture SharedBorrowedFixture(bool isVoid) => SharedBorrowedFixtures.GetOrAdd(isVoid, key => CompileSyncFixture("borrowed", key,
        sourceOverride: SyncRuntimeSource("borrowed", key)
            .Replace("global::Zendiator.Generator.Tests.CanonicalSyncRuntimeProbe", "global::Zendiator.Generator.Tests.GeneratorTests.SharedBorrowedSessionProbe")
            .Replace("return _probe.NullSession ? null! : new Handler<T>(_probe);", "return (Handler<T>)_probe.GetShared(()=>new Handler<T>(_probe));")));

    public static IEnumerable<object[]> SharedBorrowedSessionCases()
    {
        foreach (var isVoid in new[] { false, true })
            foreach (var lifetime in new[] { ServiceLifetime.Scoped, ServiceLifetime.Transient, ServiceLifetime.Singleton })
                yield return [isVoid, lifetime];
    }
    private static async Task<string> RunSharedBorrowedSession(bool isVoid, ServiceLifetime lifetime, bool parallel, bool generated)
    {
        var fixture = SharedBorrowedFixture(isVoid);
        var probe = new SharedBorrowedSessionProbe();
        var core = probe.Core;
        using var barrier = new Barrier(2);
        if (parallel)
            core.ConcurrentGate = barrier;
        var count = parallel ? 2 : 4;
        var cancellation = Enumerable.Range(0, count).Select(_ => new CancellationTokenSource()).ToArray();
        try
        {
            var requests = Enumerable.Range(0, count).Select(_ => Activator.CreateInstance(fixture.Request)!).ToArray();
            var errors = requests.Select((_, i) => i % 3 == 0 ? null : i % 3 == 1 ? new Exception("body " + i) : new OperationCanceledException(cancellation[i].Token)).ToArray();
            for (var i = 0; i < count; i++)
                Assert.True(core.Operations.TryAdd(requests[i], new(cancellation[i].Token, errors[i])));
            IServiceCollection services = new ServiceCollection();
            services.AddSingleton(probe);
            services.Add(ServiceDescriptor.Describe(fixture.Service, fixture.Service, lifetime));
            Assert.Same(services, fixture.Register.Invoke(null, [services]));
            var snapshots = new List<object>();
            var scopeCleanup = new List<int>();
            using (var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }))
            {
                for (var s = 0; s < (parallel ? 1 : 2); s++)
                {
                    using (var scope = provider.CreateScope())
                    {
                        var mediator = scope.ServiceProvider.GetRequiredService(fixture.Interface);
                        (object? Result, Exception? Error) Invoke(int i) => generated ? InvokeSync(fixture, mediator, requests[i], cancellation[i].Token) :
                            InvokeCompiledHandlerSessionReference(fixture, scope.ServiceProvider, isVoid, false, requests[i], cancellation[i].Token);
                        var indexes = parallel ? new[] { 0, 1 } : new[] { s * 2, s * 2 + 1 };
                        var actual = parallel ? await Task.WhenAll(indexes.Select(i => Task.Factory.StartNew(() => Invoke(i), CancellationToken.None,
                            TaskCreationOptions.LongRunning, TaskScheduler.Default))).WaitAsync(TimeSpan.FromSeconds(30)) : indexes.Select(Invoke).ToArray();
                        for (var j = 0; j < indexes.Length; j++)
                        {
                            var i = indexes[j];
                            Assert.Same(errors[i], actual[j].Error);
                            Assert.Equal(errors[i] != null || isVoid ? null : (object)73, actual[j].Result);
                            var calls = core.Calls.Where(c => ReferenceEquals(c.Request, requests[i])).ToArray();
                            Assert.Equal(new[] { "factory", "handler" }, calls.Select(c => c.Phase));
                            Assert.All(calls, c => Assert.Equal(cancellation[i].Token, c.Token));
                            Assert.Same(Assert.Single(core.Created), calls[1].Receiver);
                            snapshots.Add(new
                            {
                                index = i,
                                actual[j].Result,
                                originalErrorPreserved = ReferenceEquals(errors[i], actual[j].Error),
                                errorType = actual[j].Error?.GetType().FullName,
                                tokenPreserved = calls.All(c => c.Token == cancellation[i].Token),
                                sharedReceiverPreserved = true
                            });
                        }
                        Assert.Empty(core.Released);
                        Assert.Equal(lifetime == ServiceLifetime.Singleton ? 0 : s * (lifetime == ServiceLifetime.Scoped || generated ? 1 : 2), core.FactoryDisposals);
                    }
                    scopeCleanup.Add(core.FactoryDisposals);
                }
                Assert.Empty(core.Released);
                Assert.Single(core.Created);
            }
            var factories = core.FactoryConstructions;
            if (generated && parallel && lifetime == ServiceLifetime.Transient)
                Assert.InRange(factories, 1, count);
            else
                Assert.Equal(lifetime == ServiceLifetime.Singleton ? 1 : lifetime == ServiceLifetime.Scoped || generated ? parallel ? 1 : 2 : count, factories);
            Assert.Equal(factories, core.FactoryDisposals);
            var factoryCalls = core.Calls.Where(c => c.Phase == "factory").ToArray();
            Assert.Equal(count, factoryCalls.Length);
            Assert.Equal(generated ? lifetime == ServiceLifetime.Singleton || parallel ? 1 : 2 : factories,
                factoryCalls.Select(c => c.Receiver).Distinct(ReferenceEqualityComparer.Instance).Count());
            Assert.Empty(core.Released);
            probe.ReleaseOwner();
            Assert.Same(Assert.Single(core.Created), Assert.Single(core.Released));
            return JsonSerializer.Serialize(new
            {
                operations = snapshots,
                scopeCleanup,
                core.FactoryConstructions,
                core.FactoryDisposals,
                createdSessions = core.Created.Count,
                operationReleasedSessions = 0,
                finalOwnerReleasedSessions = core.Released.Count
            });
        }
        finally
        {
            foreach (var source in cancellation)
                source.Dispose();
        }
    }

    [Theory]
    [MemberData(nameof(SharedBorrowedSessionCases))]
    public async Task Generated_Borrowed_Handler_session_reuses_same_instance_without_operation_dispose(bool isVoid, ServiceLifetime lifetime)
    {
        var reference = lifetime == ServiceLifetime.Transient ? null : await RunSharedBorrowedSession(isVoid, lifetime, false, false);
        var generated = await RunSharedBorrowedSession(isVoid, lifetime, false, true);
        if (reference is not null)
            Assert.Equal(reference, generated);
        WriteNestedHookEvidence("handler-session-shared", "reuse-" + isVoid + "-" + lifetime,
            new
            {
                isVoid,
                lifetime = lifetime.ToString(),
                reference = reference is null ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(reference),
                generated = JsonSerializer.Deserialize<JsonElement>(generated)
            });
    }
    [Theory]
    [MemberData(nameof(SharedBorrowedSessionCases))]
    public async Task Generated_Borrowed_shared_Handler_session_preserves_distinct_parallel_operations(bool isVoid, ServiceLifetime lifetime)
    {
        var reference = lifetime == ServiceLifetime.Transient ? null : await RunSharedBorrowedSession(isVoid, lifetime, true, false);
        var generated = await RunSharedBorrowedSession(isVoid, lifetime, true, true);
        if (reference is not null)
            Assert.Equal(reference, generated);
        WriteNestedHookEvidence("handler-session-shared", "parallel-" + isVoid + "-" + lifetime,
            new
            {
                isVoid,
                lifetime = lifetime.ToString(),
                reference = reference is null ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(reference),
                generated = JsonSerializer.Deserialize<JsonElement>(generated)
            });
    }
}
