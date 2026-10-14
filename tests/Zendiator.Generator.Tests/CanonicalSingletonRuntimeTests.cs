using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Generator.Tests;

// Thread-safe user implementation observations, independent of generated execution state.
public sealed class CanonicalConcurrentSyncProbe
{
    public sealed record Operation(CancellationToken Token, Exception? Error = null, Action? Reenter = null);
    public readonly ConcurrentDictionary<object, Operation> Operations = new(ReferenceEqualityComparer.Instance);
    public readonly ConcurrentQueue<(string Phase, object Request, CancellationToken Token, object Receiver)> Calls = new();
    public readonly ConcurrentQueue<object> Created = new();
    public readonly ConcurrentQueue<object> Released = new();
    public readonly ConcurrentBag<string> Trace = new();
    public Barrier? ConcurrentGate;
    public int FactoryConstructions, FactoryDisposals;
    public Exception? DiError => null;
    public bool NullService => false;
    public bool NullSession => false;

    public void ConstructHandler(object receiver) => Created.Enqueue(receiver);
    public void ConstructFactory() => Interlocked.Increment(ref FactoryConstructions);
    public void Factory(object receiver, object request, CancellationToken token) => Calls.Enqueue(("factory", request, token, receiver));
    public int Handle(object receiver, object request, CancellationToken token)
    {
        Calls.Enqueue(("handler", request, token, receiver));
        if (ConcurrentGate is { } gate && !gate.SignalAndWait(TimeSpan.FromSeconds(20)))
            throw new TimeoutException("Concurrent fixture participants did not overlap.");
        var operation = Operations[request];
        operation.Reenter?.Invoke();
        if (operation.Error is { } error) throw error;
        return 73;
    }
    public void Cleanup(object receiver) => Released.Enqueue(receiver);
    public void DisposeFactory() => Interlocked.Increment(ref FactoryDisposals);
}

public sealed partial class GeneratorTests
{
    private static readonly ConcurrentDictionary<(string Receiver, bool Void), SyncFixture> SingletonFixtures = new();
    private static readonly ConcurrentDictionary<(string Receiver, bool Void), SyncFixture> ConcurrentSingletonFixtures = new();

    private static SyncFixture SingletonFixture(string receiver, bool isVoid) => SingletonFixtures.GetOrAdd((receiver, isVoid), key =>
    {
        var service = key.Receiver == "direct" ? "Handler<string>" : "Factory<string>";
        var source = SyncRuntimeSource(key.Receiver, key.Void).Replace("public static object? ResolveService(IServiceProvider provider)",
            "public static void AddSingletonService(IServiceCollection services)=>services.AddSingleton<" + service + ">(); public static object? ResolveService(IServiceProvider provider)");
        return CompileSyncFixture(key.Receiver, key.Void, sourceOverride: source);
    });

    private static SyncFixture ConcurrentSingletonFixture(string receiver, bool isVoid) => ConcurrentSingletonFixtures.GetOrAdd((receiver, isVoid), key =>
        CompileSyncFixture(key.Receiver, key.Void, sourceOverride: SyncRuntimeSource(key.Receiver, key.Void)
            .Replace("CanonicalSyncRuntimeProbe", "CanonicalConcurrentSyncProbe")));

    public static IEnumerable<object[]> SingletonRegistrationCases()
    {
        foreach (var receiver in new[] { "direct", "borrowed", "owned" })
            foreach (var isVoid in new[] { false, true })
                foreach (var after in new[] { false, true }) yield return [receiver, isVoid, after];
    }

    [Theory]
    [MemberData(nameof(SingletonRegistrationCases))]
    public void Generated_SendSync_preserves_Singleton_before_or_after_registration_across_scopes(string receiver, bool isVoid, bool after)
    {
        var f = SingletonFixture(receiver, isVoid); var probe = new CanonicalSyncRuntimeProbe();
        var ambient = CanonicalSyncRuntimeProbe.Ambient.Value; var culture = CultureInfo.CurrentCulture;
        try
        {
            IServiceCollection services = new ServiceCollection(); services.AddSingleton(probe);
            var register = f.Assembly.GetType("App.Bridge")!.GetMethod("AddSingletonService")!;
            if (!after) register.Invoke(null, [services]);
            Assert.Same(services, f.Register.Invoke(null, [services]));
            if (after) register.Invoke(null, [services]);
            Assert.Equal(ServiceLifetime.Singleton, services.Last(d => d.ServiceType == f.Service).Lifetime);
            using (var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }))
            {
                object? firstMediator = null;
                for (var s = 0; s < 2; s++)
                {
                    using (var scope = provider.CreateScope())
                    {
                        var mediator = scope.ServiceProvider.GetRequiredService(f.Interface);
                        if (s == 0) firstMediator = mediator; else Assert.NotSame(firstMediator, mediator);
                        for (var i = 0; i < 2; i++)
                        {
                            var actual = InvokeSync(f, mediator, Activator.CreateInstance(f.Request), default);
                            Assert.Null(actual.Error); Assert.Equal(isVoid ? null : (object)73, actual.Result);
                        }
                    }
                    Assert.Equal(receiver == "owned" ? (s + 1) * 2 : 0, probe.Released.Count);
                    Assert.Equal(0, probe.FactoryDisposals);
                }
                var calls = probe.Calls.Where(c => c.Phase == (receiver == "direct" ? "handler" : "factory")).ToArray();
                Assert.Equal(4, calls.Length); Assert.All(calls, c => Assert.Same(calls[0].Receiver, c.Receiver));
                Assert.Equal(receiver == "direct" ? 1 : 4, probe.Created.Count);
                if (receiver != "direct") Assert.Single(probe.Trace, t => t == "factory.ctor");
            }
            Assert.Equal(receiver == "direct" ? 1 : receiver == "owned" ? 4 : 0, probe.Released.Count);
            Assert.Equal(receiver == "direct" ? 0 : 1, probe.FactoryDisposals);
        }
        finally { CanonicalSyncRuntimeProbe.Ambient.Value = ambient; CultureInfo.CurrentCulture = culture; probe.Cancellation.Dispose(); }
    }

    public static IEnumerable<object[]> SingletonMediatorCases()
    {
        foreach (var receiver in new[] { "direct", "borrowed", "owned" })
            foreach (var role in new[] { "mediator", "interface" })
                foreach (var lifetime in new[] { ServiceLifetime.Singleton, ServiceLifetime.Scoped, ServiceLifetime.Transient })
                    yield return [receiver, role, lifetime];
    }

    [Theory]
    [MemberData(nameof(SingletonMediatorCases))]
    public void Generated_Singleton_mediator_uses_root_dependency_ownership_and_ValidateScopes(string receiver, string role, ServiceLifetime lifetime)
    {
        var f = SyncRuntimeFixture(receiver, false); var probe = new CanonicalSyncRuntimeProbe();
        var ambient = CanonicalSyncRuntimeProbe.Ambient.Value; var culture = CultureInfo.CurrentCulture;
        try
        {
            IServiceCollection services = new ServiceCollection(); services.AddSingleton(probe);
            services.Add(ServiceDescriptor.Describe(f.Service, f.Service, lifetime));
            services.Add(ServiceDescriptor.Singleton(role == "mediator" ? f.Mediator : f.Interface, f.Mediator));
            Assert.Same(services, f.Register.Invoke(null, [services]));
            using (var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }))
            {
                object? firstMediator = null;
                for (var s = 0; s < 2; s++)
                {
                    using (var scope = provider.CreateScope())
                    {
                        var mediator = scope.ServiceProvider.GetRequiredService(f.Interface);
                        if (s == 0) firstMediator = mediator; else Assert.Same(firstMediator, mediator);
                        for (var i = 0; i < 2; i++)
                        {
                            var actual = InvokeSync(f, mediator, Activator.CreateInstance(f.Request), default);
                            if (lifetime == ServiceLifetime.Scoped)
                                Assert.Contains("scoped", Assert.IsType<InvalidOperationException>(actual.Error).Message, StringComparison.OrdinalIgnoreCase);
                            else { Assert.Null(actual.Error); Assert.Equal(73, actual.Result); }
                        }
                    }
                    Assert.Equal(receiver == "owned" && lifetime != ServiceLifetime.Scoped ? (s + 1) * 2 : 0, probe.Released.Count);
                    Assert.Equal(0, probe.FactoryDisposals);
                }
                if (lifetime == ServiceLifetime.Scoped) { Assert.Empty(probe.Calls); Assert.Empty(probe.Created); Assert.Empty(probe.Trace); }
                else
                {
                    var receivers = probe.Calls.Where(c => c.Phase == (receiver == "direct" ? "handler" : "factory")).Select(c => c.Receiver).ToArray();
                    Assert.Equal(lifetime == ServiceLifetime.Singleton ? 1 : 4, receivers.Distinct(ReferenceEqualityComparer.Instance).Count());
                }
            }
            var resolved = lifetime == ServiceLifetime.Scoped ? 0 : lifetime == ServiceLifetime.Singleton ? 1 : 4;
            Assert.Equal(receiver == "direct" ? resolved : receiver == "owned" && resolved != 0 ? 4 : 0, probe.Released.Count);
            Assert.Equal(receiver == "direct" ? 0 : resolved, probe.FactoryDisposals);
        }
        finally { CanonicalSyncRuntimeProbe.Ambient.Value = ambient; CultureInfo.CurrentCulture = culture; probe.Cancellation.Dispose(); }
    }

    [Theory]
    [InlineData("direct", false)]
    [InlineData("borrowed", false)]
    [InlineData("owned", false)]
    [InlineData("direct", true)]
    [InlineData("borrowed", true)]
    [InlineData("owned", true)]
    public void Generated_SendSync_distinguishes_external_instance_from_container_captured_delegate(string receiver, bool external)
    {
        var f = SyncRuntimeFixture(receiver, false); var probe = new CanonicalSyncRuntimeProbe();
        var ambient = CanonicalSyncRuntimeProbe.Ambient.Value; var culture = CultureInfo.CurrentCulture;
        object? externalService = null;
        try
        {
            IServiceCollection services = new ServiceCollection(); services.AddSingleton(probe);
            if (external) { externalService = Activator.CreateInstance(f.Service, probe)!; services.Add(ServiceDescriptor.Singleton(f.Service, externalService)); }
            else services.Add(ServiceDescriptor.Singleton(f.Service, provider => Activator.CreateInstance(f.Service, provider.GetRequiredService<CanonicalSyncRuntimeProbe>())!));
            Assert.Same(services, f.Register.Invoke(null, [services]));
            using (var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }))
            {
                using (var scope = provider.CreateScope())
                {
                    var mediator = scope.ServiceProvider.GetRequiredService(f.Interface);
                    for (var i = 0; i < 2; i++) Assert.Null(InvokeSync(f, mediator, Activator.CreateInstance(f.Request), default).Error);
                }
                Assert.Equal(receiver == "owned" ? 2 : 0, probe.Released.Count); Assert.Equal(0, probe.FactoryDisposals);
            }
            Assert.Equal(receiver == "owned" ? 2 : receiver == "direct" && !external ? 1 : 0, probe.Released.Count);
            Assert.Equal(receiver != "direct" && !external ? 1 : 0, probe.FactoryDisposals);
            if (externalService is IDisposable owner) { owner.Dispose(); externalService = null; }
            Assert.Equal(receiver == "direct" ? 1 : receiver == "owned" ? 2 : 0, probe.Released.Count);
            Assert.Equal(receiver == "direct" ? 0 : 1, probe.FactoryDisposals);
        }
        finally { (externalService as IDisposable)?.Dispose(); CanonicalSyncRuntimeProbe.Ambient.Value = ambient; CultureInfo.CurrentCulture = culture; probe.Cancellation.Dispose(); }
    }

    [Theory]
    [InlineData("direct")]
    [InlineData("owned")]
    public void Generated_standard_implementation_type_constructor_failure_preserves_identity_without_session_cleanup(string receiver)
    {
        var f = SyncRuntimeFixture(receiver, false); var error = new Exception("standard implementation-type constructor identity");
        var probe = new CanonicalSyncRuntimeProbe { ConstructorError = error };
        using (var provider = SyncProvider(f, probe, recording: false))
        {
            using (var scope = provider.CreateScope())
            {
                var mediator = scope.ServiceProvider.GetRequiredService(f.Interface);
                var actual = InvokeSync(f, mediator, Activator.CreateInstance(f.Request), default);
                Assert.Same(error, actual.Error);
                Assert.Equal(new[] { receiver == "direct" ? "handler.ctor" : "factory.ctor" }, probe.Trace);
                Assert.Empty(probe.Calls); Assert.Empty(probe.Created); Assert.Empty(probe.Released); Assert.Equal(0, probe.FactoryDisposals);
            }
            Assert.Empty(probe.Released); Assert.Equal(0, probe.FactoryDisposals);
        }
        Assert.Empty(probe.Released); Assert.Equal(0, probe.FactoryDisposals); probe.Cancellation.Dispose();
    }

    private static ServiceProvider ConcurrentSingletonProvider(SyncFixture f, CanonicalConcurrentSyncProbe probe)
    {
        IServiceCollection services = new ServiceCollection(); services.AddSingleton(probe);
        services.Add(ServiceDescriptor.Singleton(f.Service, f.Service));
        services.Add(ServiceDescriptor.Singleton(f.Interface, f.Mediator));
        Assert.Same(services, f.Register.Invoke(null, [services]));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    [Theory]
    [InlineData("direct", false)]
    [InlineData("borrowed", false)]
    [InlineData("owned", false)]
    [InlineData("direct", true)]
    [InlineData("borrowed", true)]
    [InlineData("owned", true)]
    public async Task Generated_Singleton_SendSync_keeps_parallel_request_token_exception_and_session_identity(string receiver, bool isVoid)
    {
        var f = ConcurrentSingletonFixture(receiver, isVoid); var probe = new CanonicalConcurrentSyncProbe();
        using var barrier = new Barrier(8); probe.ConcurrentGate = barrier;
        var cancellation = Enumerable.Range(0, 8).Select(_ => new CancellationTokenSource()).ToArray();
        try
        {
            var requests = Enumerable.Range(0, 8).Select(_ => Activator.CreateInstance(f.Request)!).ToArray();
            var errors = requests.Select((_, i) => i % 3 == 0 ? null : i % 3 == 1 ? new Exception("operation " + i) : new OperationCanceledException(cancellation[i].Token)).ToArray();
            for (var i = 0; i < 8; i++) Assert.True(probe.Operations.TryAdd(requests[i], new(cancellation[i].Token, errors[i])));
            using (var provider = ConcurrentSingletonProvider(f, probe))
            {
                var mediator = provider.GetRequiredService(f.Interface);
                var tasks = requests.Select((request, i) => Task.Factory.StartNew(() => InvokeSync(f, mediator, request, cancellation[i].Token),
                    CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
                var actual = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));
                for (var i = 0; i < 8; i++)
                {
                    Assert.Same(errors[i], actual[i].Error);
                    Assert.Equal(errors[i] != null || isVoid ? null : (object)73, actual[i].Result);
                    var call = Assert.Single(probe.Calls, c => c.Phase == "handler" && ReferenceEquals(c.Request, requests[i]));
                    Assert.Equal(cancellation[i].Token, call.Token);
                    if (receiver != "direct") Assert.Equal(cancellation[i].Token,
                        Assert.Single(probe.Calls, c => c.Phase == "factory" && ReferenceEquals(c.Request, requests[i])).Token);
                }
                var handlers = probe.Calls.Where(c => c.Phase == "handler").Select(c => c.Receiver).ToArray();
                Assert.Equal(receiver == "direct" ? 1 : 8, handlers.Distinct(ReferenceEqualityComparer.Instance).Count());
                Assert.Equal(receiver == "direct" ? 1 : 8, probe.Created.Count);
                Assert.Equal(receiver == "owned" ? 8 : 0, probe.Released.Count); Assert.Equal(0, probe.FactoryDisposals);
                if (receiver == "owned") Assert.All(handlers, handler => Assert.Single(probe.Released, x => ReferenceEquals(x, handler)));
                if (receiver != "direct")
                {
                    Assert.Equal(1, probe.FactoryConstructions);
                    Assert.Single(probe.Calls.Where(c => c.Phase == "factory").Select(c => c.Receiver).Distinct(ReferenceEqualityComparer.Instance));
                }
            }
            Assert.Equal(receiver == "direct" ? 1 : receiver == "owned" ? 8 : 0, probe.Released.Count);
            Assert.Equal(receiver == "direct" ? 0 : 1, probe.FactoryDisposals);
        }
        finally { foreach (var source in cancellation) source.Dispose(); }
    }

    [Theory]
    [InlineData("direct", false)]
    [InlineData("borrowed", false)]
    [InlineData("owned", false)]
    [InlineData("direct", true)]
    [InlineData("borrowed", true)]
    [InlineData("owned", true)]
    public void Generated_Singleton_SendSync_reentry_keeps_inner_failure_and_cleanup_separate_from_outer(string receiver, bool isVoid)
    {
        var f = ConcurrentSingletonFixture(receiver, isVoid); var probe = new CanonicalConcurrentSyncProbe();
        using var outerToken = new CancellationTokenSource(); using var innerToken = new CancellationTokenSource();
        var outer = Activator.CreateInstance(f.Request)!; var inner = Activator.CreateInstance(f.Request)!;
        var error = new Exception("inner identity");
        using (var provider = ConcurrentSingletonProvider(f, probe))
        {
            var mediator = provider.GetRequiredService(f.Interface);
            Assert.True(probe.Operations.TryAdd(inner, new(innerToken.Token, error)));
            Assert.True(probe.Operations.TryAdd(outer, new(outerToken.Token, Reenter: () =>
            {
                var result = InvokeSync(f, mediator, inner, innerToken.Token); Assert.Same(error, result.Error);
                Assert.Equal(receiver == "owned" ? 1 : 0, probe.Released.Count);
            })));
            var actual = InvokeSync(f, mediator, outer, outerToken.Token);
            Assert.Null(actual.Error); Assert.Equal(isVoid ? null : (object)73, actual.Result);
            var outerCall = Assert.Single(probe.Calls, c => c.Phase == "handler" && ReferenceEquals(c.Request, outer));
            var innerCall = Assert.Single(probe.Calls, c => c.Phase == "handler" && ReferenceEquals(c.Request, inner));
            Assert.Equal(outerToken.Token, outerCall.Token); Assert.Equal(innerToken.Token, innerCall.Token);
            if (receiver == "direct") Assert.Same(outerCall.Receiver, innerCall.Receiver);
            else
            {
                Assert.NotSame(outerCall.Receiver, innerCall.Receiver);
                var factories = probe.Calls.Where(c => c.Phase == "factory").ToArray(); Assert.Equal(2, factories.Length);
                Assert.Same(factories[0].Receiver, factories[1].Receiver);
                Assert.Equal(outerToken.Token, factories[0].Token); Assert.Equal(innerToken.Token, factories[1].Token);
            }
            if (receiver == "owned") Assert.Equal(new[] { innerCall.Receiver, outerCall.Receiver }, probe.Released.ToArray());
            else Assert.Empty(probe.Released);
            Assert.Equal(0, probe.FactoryDisposals);
        }
        Assert.Equal(receiver == "direct" ? 1 : receiver == "owned" ? 2 : 0, probe.Released.Count);
        Assert.Equal(receiver == "direct" ? 0 : 1, probe.FactoryDisposals);
    }
}
