using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private sealed class GenericCacheLookupProvider(IServiceProvider provider) : IServiceProvider
    {
        internal readonly ConcurrentDictionary<Type, int> Lookups = new();
        internal Action<Type, object>? AfterResolution;

        public object? GetService(Type serviceType)
        {
            Lookups.AddOrUpdate(serviceType, 1, static (_, count) => count + 1);
            var resolved = provider.GetService(serviceType);
            if (resolved is not null) AfterResolution?.Invoke(serviceType, resolved);
            return resolved;
        }
    }

    private sealed class StableGenericCacheProvider(IReadOnlyDictionary<Type, object> values) : IServiceProvider
    {
        public object? GetService(Type serviceType) => values.TryGetValue(serviceType, out var value) ? value : null;
    }

    private sealed record GenericCacheFixture(Type Mediator, Type[] Handlers, MethodInfo Register,
        Func<IServiceProvider, object> Create, Func<object, int, int, int> Dispatch);

    private static readonly Lazy<GenericCacheFixture> GenericCache = new(() =>
    {
        const string source = """
            using System;using System.Threading;using Zendiator;using Zendiator.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection;using Probe=global::Zendiator.Generator.Tests.MultiLifetimeProbe;
            namespace GenericCache {
                public sealed class A;public sealed class B;public sealed class C;
                public sealed class D;public sealed class E;public sealed class F;
                public sealed record Ping<T>(int Value):ISyncRequest<int>;
                [ZendiatorHandler]public sealed class Handler<T>:ISyncRequestHandler<Ping<T>,int>,IDisposable {
                    private readonly Probe probe;
                    public Handler(Probe probe){this.probe=probe;probe.Construct(this);}
                    public int Handle(Ping<T> request,CancellationToken token)=>probe.Hit(this,request.Value);
                    public void Dispose()=>probe.Release(this);
                }
                [ZendiatorComposition("generic-cache")]public sealed class Composition;
                public static class Bridge {
                    public static IServiceCollection Register(IServiceCollection services)=>services.AddZendiator<Composition>(c=>
                        c.Handlers(typeof(Ping<>),DispatchFamily.SendSync,typeof(Handler<>)));
                    public static object Create(IServiceProvider services)=>new global::GenericCache.Generated.Composition.Zendiator(services);
                    public static int Dispatch(object mediator,int key,int value){
                        var entry=(global::GenericCache.Generated.Composition.IZendiator)mediator;
                        return key switch {
                            0=>entry.Send(new Ping<A>(value)),1=>entry.Send(new Ping<B>(value)),
                            2=>entry.Send(new Ping<C>(value)),3=>entry.Send(new Ping<D>(value)),
                            4=>entry.Send(new Ping<E>(value)),5=>entry.Send(new Ping<F>(value)),
                            _=>throw new ArgumentOutOfRangeException(nameof(key))
                        };
                    }
                }
            }
            """;
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(
            SyncCompilation(source, "GenericServiceCache_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        using var bytes = new MemoryStream();
        var emitted = output.Emit(bytes);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR")))
        {
            var folder = Path.Combine(MultiArtifactRoot, "generated-artifacts", "generic-service-cache");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "input.cs"), source);
            foreach (var generated in driver.GetRunResult().Results.Single().GeneratedSources)
                File.WriteAllText(Path.Combine(folder, generated.HintName), generated.SourceText.ToString());
            File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
        }
        bytes.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var bridge = assembly.GetType("GenericCache.Bridge")!;
        var handler = assembly.GetType("GenericCache.Handler`1")!;
        return new(assembly.GetType("GenericCache.Generated.Composition.Zendiator")!,
            new[] { "A", "B", "C", "D", "E", "F" }.Select(key => handler.MakeGenericType(assembly.GetType("GenericCache." + key)!)).ToArray(),
            bridge.GetMethod("Register")!, bridge.GetMethod("Create")!.CreateDelegate<Func<IServiceProvider, object>>(),
            bridge.GetMethod("Dispatch")!.CreateDelegate<Func<object, int, int, int>>());
    });

    private static void WriteGenericCacheAllocationEvidence(GenericCacheFixture fixture)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR"))) return;
        var probe = new MultiLifetimeProbe();
        IServiceCollection services = new ServiceCollection();
        services.AddSingleton(probe);
        fixture.Register.Invoke(null, [services]);
        using var native = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = native.CreateScope();
        var values = fixture.Handlers.Take(5).ToDictionary(type => type, scope.ServiceProvider.GetRequiredService);
        var stable = new StableGenericCacheProvider(values);
        const int warmupOperations = 512;
        const int measuredOperations = 4096;
        const int sampleCount = 3;
        var samples = new List<object>(4 * sampleCount);
        foreach (var keys in new[] { 0, 1, 4, 5 })
        {
            Exercise(keys, warmupOperations);
            for (var sample = 0; sample < sampleCount; sample++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                Exercise(keys, measuredOperations);
                var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                samples.Add(new { keys, sample, operations = measuredOperations, managedBytes = allocated, bytesPerOperation = (double)allocated / measuredOperations });
            }
        }
        MultiEvidence("generic-service-cache", "allocation-0-1-4-5-keys", new
        {
            operation = "create Mediator and resolve each selected closed generic key once",
            framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            runtimeVersion = Environment.Version.ToString(),
            processArchitecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            processorCount = Environment.ProcessorCount,
            serverGC = System.Runtime.GCSettings.IsServerGC,
            warmupOperations,
            measuredOperations,
            sampleCount,
            providerAndNativeValuesPrebuilt = true,
            timingMeasured = false,
            samples
        });

        void Exercise(int keys, int operations)
        {
            for (var operation = 0; operation < operations; operation++)
            {
                var mediator = (IServiceProvider)fixture.Create(stable);
                for (var key = 0; key < keys; key++)
                    if (!ReferenceEquals(values[fixture.Handlers[key]], mediator.GetService(fixture.Handlers[key])))
                        throw new InvalidOperationException("Generic cache allocation observation changed native service identity.");
                GC.KeepAlive(mediator);
            }
        }
    }

    [Fact]
    public async Task Generic_service_cache_keeps_published_instances_when_activation_spans_promotion()
    {
        var fixture = GenericCache.Value;
        var probe = new MultiLifetimeProbe();
        IServiceCollection services = new ServiceCollection();
        services.AddSingleton(probe);
        fixture.Register.Invoke(null, [services]);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        var scope = provider.CreateScope();
        var tracked = new GenericCacheLookupProvider(scope.ServiceProvider);
        var mediator = fixture.Create(tracked);
        var cache = fixture.Mediator.GetField("_genericServices", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var published = new object[fixture.Handlers.Length];
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var promotionEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        using var promotionRelease = new ManualResetEventSlim();
        Task<int>? stalled = null;
        Task<int>[] promoters = [];
        object? losingActivation = null;
        var fourthActivations = 0;
        var promotingActivations = 0;
        try
        {
            for (var key = 0; key < 3; key++)
            {
                Assert.Equal(key, fixture.Dispatch(mediator, key, key));
                published[key] = probe.Calls[^1];
            }
            tracked.AfterResolution = (type, resolved) =>
            {
                if (type == fixture.Handlers[3] && Interlocked.Increment(ref fourthActivations) == 1)
                {
                    losingActivation = resolved;
                    entered.TrySetResult();
                    Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
                }
                if (type == fixture.Handlers[4] || type == fixture.Handlers[5])
                {
                    if (Interlocked.Increment(ref promotingActivations) == 2) promotionEntered.TrySetResult();
                    Assert.True(promotionRelease.Wait(TimeSpan.FromSeconds(10)));
                }
            };
            stalled = Task.Factory.StartNew(() => fixture.Dispatch(mediator, 3, 30), CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(31, fixture.Dispatch(mediator, 3, 31));
            published[3] = probe.Calls[^1];
            Assert.NotSame(losingActivation, published[3]);
            Assert.NotNull(cache.GetValue(mediator));
            Assert.IsNotType<ConcurrentDictionary<Type, object>>(cache.GetValue(mediator));

            var failure = new InvalidOperationException("native activation before promotion");
            probe.BeforeConstruct = () => throw failure;
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => fixture.Dispatch(mediator, 4, 40)));
            probe.BeforeConstruct = null;
            Assert.IsNotType<ConcurrentDictionary<Type, object>>(cache.GetValue(mediator));
            promoters = new[] { 4, 5 }.Select(key => Task.Factory.StartNew(() => fixture.Dispatch(mediator, key, key * 10 + 1),
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
            await promotionEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsNotType<ConcurrentDictionary<Type, object>>(cache.GetValue(mediator));
            promotionRelease.Set();
            Assert.Equal(new[] { 41, 51 }, await Task.WhenAll(promoters).WaitAsync(TimeSpan.FromSeconds(10)));
            published[4] = Assert.Single(probe.Calls, receiver => receiver.GetType() == fixture.Handlers[4]);
            published[5] = Assert.Single(probe.Calls, receiver => receiver.GetType() == fixture.Handlers[5]);
            Assert.IsType<ConcurrentDictionary<Type, object>>(cache.GetValue(mediator));
            release.Set();
            Assert.Equal(30, await stalled.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Same(published[3], probe.Calls[^1]);
            tracked.AfterResolution = null;

            var lookups = tracked.Lookups.ToDictionary(pair => pair.Key, pair => pair.Value);
            for (var key = 0; key < published.Length; key++)
            {
                Assert.Equal(key + 100, fixture.Dispatch(mediator, key, key + 100));
                Assert.Same(published[key], probe.Calls[^1]);
                Assert.Same(published[key], ((IServiceProvider)mediator).GetService(fixture.Handlers[key]));
                Assert.Equal(lookups[fixture.Handlers[key]], tracked.Lookups[fixture.Handlers[key]]);
            }
            Assert.Equal(7, probe.Created.Count);
            Assert.Empty(probe.Released);
        }
        finally
        {
            release.Set();
            promotionRelease.Set();
            probe.BeforeConstruct = null;
            try
            {
                var pending = stalled is null ? promoters : promoters.Append(stalled);
                await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally { scope.Dispose(); }
        }
        Assert.Equal(probe.Created.Count, probe.Released.Count);
        Assert.All(probe.Created, receiver => Assert.Contains(probe.Released, released => ReferenceEquals(receiver, released)));
        MultiEvidence("generic-service-cache", "promotion-race", new
        {
            created = probe.Created.Count,
            released = probe.Released.Count,
            nativeFailureBeforePromotionRetried = true,
            publishedWinnerSurvivedPromotion = true,
            allClosedKeysRetained = true,
            losingActivationDisposedByNativeDI = true
        });
    }

    [Fact]
    public void Generic_service_cache_retries_null_and_keeps_reentrant_native_winner_across_promotion_and_scopes()
    {
        var fixture = GenericCache.Value;
        var probe = new MultiLifetimeProbe();
        IServiceCollection services = new ServiceCollection();
        services.AddSingleton(probe);
        fixture.Register.Invoke(null, [services]);
        var missing = true;
        services.AddTransient(fixture.Handlers[0], provider => missing ? null! : ActivatorUtilities.CreateInstance(provider, fixture.Handlers[0]));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        object first;
        using (var scope = provider.CreateScope())
        {
            var tracked = new GenericCacheLookupProvider(scope.ServiceProvider);
            var mediator = fixture.Create(tracked);
            Assert.Null(((IServiceProvider)mediator).GetService(fixture.Handlers[0]));
            Assert.Empty(probe.Created);
            missing = false;
            probe.BeforeConstruct = () =>
            {
                probe.BeforeConstruct = null;
                Assert.Equal(11, fixture.Dispatch(mediator, 0, 11));
            };
            try { Assert.Equal(12, fixture.Dispatch(mediator, 0, 12)); }
            finally { probe.BeforeConstruct = null; }
            Assert.Equal(2, probe.Created.Count);
            first = probe.Calls[0];
            Assert.Same(probe.Created[0], first);
            Assert.NotSame(probe.Created[1], first);
            Assert.All(probe.Calls, receiver => Assert.Same(first, receiver));
            for (var key = 1; key <= 4; key++) Assert.Equal(key, fixture.Dispatch(mediator, key, key));
            Assert.Equal(13, fixture.Dispatch(mediator, 0, 13));
            Assert.Same(first, probe.Calls[^1]);
            Assert.Equal(3, tracked.Lookups[fixture.Handlers[0]]);
            Assert.Empty(probe.Released);
        }
        Assert.Equal(6, probe.Released.Count);
        using (var scope = provider.CreateScope())
        {
            var mediator = fixture.Create(scope.ServiceProvider);
            Assert.Equal(21, fixture.Dispatch(mediator, 0, 21));
            Assert.NotSame(first, probe.Calls[^1]);
            Assert.Equal(6, probe.Released.Count);
        }
        Assert.Equal(7, probe.Created.Count);
        Assert.Equal(probe.Created.Count, probe.Released.Count);
        Assert.All(probe.Created, receiver => Assert.Contains(probe.Released, released => ReferenceEquals(receiver, released)));
        MultiEvidence("generic-service-cache", "null-reentrant-and-scopes", new
        {
            created = probe.Created.Count,
            released = probe.Released.Count,
            nullResolutionRetried = true,
            reentrantPublishedWinnerRetained = true,
            nativeScopeOwnershipPreserved = true
        });
        WriteGenericCacheAllocationEvidence(fixture);
    }
}
