using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed class AdaptiveCacheProbe
{
    public bool Record = true;
    public Action<int>? DuringConstruct;
    public Exception? BeforeError;
    public readonly List<(int Index, object Receiver)> Created = [], Calls = [], BeforeCalls = [];
    public readonly List<object> Released = [];

    public void Construct(object receiver, int index)
    {
        DuringConstruct?.Invoke(index);
        if (Record) lock (Created) Created.Add((index, receiver));
    }
    public void Before(object receiver, int index)
    {
        if (Record) lock (BeforeCalls) BeforeCalls.Add((index, receiver));
        if (BeforeError is { } error) throw error;
    }
    public int Hit(object receiver, int index, int value)
    {
        if (Record) lock (Calls) Calls.Add((index, receiver));
        return value;
    }
    public void Release(object receiver)
    {
        if (!Record) return;
        lock (Released)
        {
            Assert.DoesNotContain(Released, previous => ReferenceEquals(previous, receiver));
            Released.Add(receiver);
        }
    }
}

public sealed partial class GeneratorTests
{
    private sealed record AdaptiveCacheFixture(Type Mediator, Type Hook, Type[] Handlers,
        Func<IServiceCollection, IServiceCollection> Register, Func<IServiceProvider, object> Create,
        Func<AdaptiveCacheProbe, int, object> NewService, Func<object, int, int, int> Send, Action<object, int> Dense);
    private static readonly ConcurrentDictionary<int, Lazy<AdaptiveCacheFixture>> AdaptiveCacheFixtures = new();

    // Each composition has N unique dependencies: N-1 handlers and one hook shared by every route.
    // The last handler is a mutable struct. Requests stay typed structs in the bridge, including allocation runs.
    private static AdaptiveCacheFixture AdaptiveCacheRuntimeFixture(int dependencies) => AdaptiveCacheFixtures.GetOrAdd(dependencies, count => new(() =>
    {
        var routes = count - 1;
        var source = new StringBuilder("using System;using System.Threading;using Zendiator;using Zendiator.DependencyInjection;using Microsoft.Extensions.DependencyInjection;using Probe=global::Zendiator.Generator.Tests.AdaptiveCacheProbe;namespace Adaptive{");
        for (var i = 0; i < routes; i++)
        {
            source.Append("public readonly record struct R").Append(i).Append("(int Value):ISyncRequest<int>;[ZendiatorHandler]public ");
            if (i == routes - 1)
                source.Append("struct H").Append(i).Append(":ISyncRequestHandler<R").Append(i).Append(",int>,IDisposable{public int State,Disposed;public H").Append(i).Append("(Probe p){State=Disposed=0;}public int Handle(R").Append(i).Append(" r,CancellationToken t)=>r.Value+ ++State;public void Dispose(){Disposed++;}}");
            else
                source.Append("sealed class H").Append(i).Append(":ISyncRequestHandler<R").Append(i).Append(",int>,IDisposable{private readonly Probe _p;public H").Append(i).Append("(Probe p){_p=p;p.Construct(this,").Append(i).Append(");}public int Handle(R").Append(i).Append(" r,CancellationToken t)=>_p.Hit(this,").Append(i).Append(",r.Value);public void Dispose()=>_p.Release(this);}");
        }
        source.Append("public sealed class Shared:");
        source.Append(string.Join(",", Enumerable.Range(0, routes).Select(i => "ISendRequestTransform<R" + i + ">")));
        source.Append(",IDisposable{private readonly Probe _p;public Shared(Probe p){_p=p;p.Construct(this,-1);}");
        for (var i = 0; i < routes; i++)
            source.Append("public R").Append(i).Append(" Before(R").Append(i).Append(" r,CancellationToken t){_p.Before(this,").Append(i).Append(");return r;}");
        source.Append("public void Dispose()=>_p.Release(this);}[ZendiatorComposition(\"adaptive\")]public sealed class Reads;public static class Bridge{public static IServiceCollection Register(IServiceCollection s)=>s.AddZendiator<Reads>(c=>{");
        for (var i = 0; i < routes; i++)
            source.Append("c.Handlers<R").Append(i).Append(">(DispatchFamily.SendSync,typeof(H").Append(i).Append("));c.Pipeline<R").Append(i).Append(">(DispatchFamily.SendSync,Stage.Hook<Shared>());");
        source.Append("});public static object Create(IServiceProvider p)=>new global::Adaptive.Generated.Reads.Zendiator(p);public static object NewService(Probe p,int index)=>index switch{-1=>new Shared(p),");
        for (var i = 0; i < routes; i++) source.Append(i).Append("=>new H").Append(i).Append("(p),");
        source.Append("_=>throw new ArgumentOutOfRangeException(nameof(index))};public static int Send(object m,int index,int value){var z=(global::Adaptive.Generated.Reads.IZendiator)m;return index switch{");
        for (var i = 0; i < routes; i++) source.Append(i).Append("=>z.Send(new R").Append(i).Append("(value)),");
        source.Append("_=>throw new ArgumentOutOfRangeException(nameof(index))};}public static void Dense(object m,int value){");
        for (var i = 0; i < routes; i++) source.Append("Send(m,").Append(i).Append(",value);");
        source.Append("}}}");
        var input = source.ToString();
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(input, "AdaptiveCache_" + count + "_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        var generated = driver.GetRunResult().Results.Single().GeneratedSources;
        var compilerDiagnostics = output.GetDiagnostics();
        using var bytes = new MemoryStream();
        var emitted = output.Emit(bytes);
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(evidence))
        {
            var folder = Path.Combine(evidence, "generated-artifacts", "adaptive-closed-" + count);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "input.cs"), input);
            foreach (var file in generated) File.WriteAllText(Path.Combine(folder, file.HintName), file.SourceText.ToString());
            if (emitted.Success) File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
            File.WriteAllText(Path.Combine(folder, "compile.json"), JsonSerializer.Serialize(new
            {
                output.AssemblyName,
                dependencies = count,
                routes,
                emitted.Success,
                diagnostics = diagnostics.Select(d => new { d.Id, severity = d.Severity.ToString(), message = d.GetMessage() }),
                compiler = compilerDiagnostics.Select(d => new { d.Id, severity = d.Severity.ToString(), message = d.GetMessage() }),
                emit = emitted.Diagnostics.Select(d => new { d.Id, severity = d.Severity.ToString(), message = d.GetMessage() })
            }));
        }
        Assert.Empty(diagnostics);
        Assert.Empty(compilerDiagnostics.Where(d => d.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error));
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        AssertCanonicalOutputRoles(generated, routes: routes);
        bytes.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var bridge = assembly.GetType("Adaptive.Bridge")!;
        return new(assembly.GetType("Adaptive.Generated.Reads.Zendiator")!, assembly.GetType("Adaptive.Shared")!,
            Enumerable.Range(0, routes).Select(i => assembly.GetType("Adaptive.H" + i)!).ToArray(),
            bridge.GetMethod("Register")!.CreateDelegate<Func<IServiceCollection, IServiceCollection>>(),
            bridge.GetMethod("Create")!.CreateDelegate<Func<IServiceProvider, object>>(),
            bridge.GetMethod("NewService")!.CreateDelegate<Func<AdaptiveCacheProbe, int, object>>(),
            bridge.GetMethod("Send")!.CreateDelegate<Func<object, int, int, int>>(),
            bridge.GetMethod("Dense")!.CreateDelegate<Action<object, int>>());
    })).Value;

    private static FieldInfo[] AdaptiveCacheBlocks(AdaptiveCacheFixture fixture) => fixture.Mediator
        .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
        .Where(field => field.FieldType.DeclaringType == fixture.Mediator && field.FieldType.IsClass).ToArray();

    [Fact]
    public async Task Adaptive_closed_cache_shares_cross_block_transient_winners_and_preserves_DI_ownership()
    {
        var fixture = AdaptiveCacheRuntimeFixture(64);
        var probe = new AdaptiveCacheProbe();
        IServiceCollection services = new ServiceCollection();
        services.AddSingleton(probe);
        fixture.Register(services);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService(CanonicalEntryService(fixture.Mediator));
        Assert.Empty(probe.Created);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var constructors = 0;
        Task<int>[] operations = [];
        probe.DuringConstruct = index =>
        {
            if (index != -1) return;
            if (Interlocked.Increment(ref constructors) == 2) entered.TrySetResult();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
        };
        try
        {
            operations = new[] { 0, 40 }.Select((route, i) => Task.Factory.StartNew(
                () => fixture.Send(mediator, route, 11 + i), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            release.Set();
            Assert.Equal(new[] { 11, 12 }, await Task.WhenAll(operations).WaitAsync(TimeSpan.FromSeconds(10)));
            probe.DuringConstruct = null;
            foreach (var route in new[] { 0, 15, 16, 31, 32, 47, 48, 61, 40, 0 }) Assert.Equal(23, fixture.Send(mediator, route, 23));
            Assert.Equal(2, probe.Created.Count(call => call.Index == -1));
            Assert.All(probe.BeforeCalls, call => Assert.Same(probe.BeforeCalls[0].Receiver, call.Receiver));
            foreach (var calls in probe.Calls.GroupBy(call => call.Index)) Assert.All(calls, call => Assert.Same(calls.First().Receiver, call.Receiver));
            Assert.Empty(probe.Released);
            var firstShared = probe.BeforeCalls[0].Receiver;
            await using (var otherScope = provider.CreateAsyncScope())
            {
                var other = otherScope.ServiceProvider.GetRequiredService(CanonicalEntryService(fixture.Mediator));
                Assert.Equal(31, fixture.Send(other, 40, 31));
                Assert.NotSame(firstShared, probe.BeforeCalls[^1].Receiver);
                Assert.NotSame(probe.Calls.First(call => call.Index == 40).Receiver, probe.Calls[^1].Receiver);
            }
            Assert.Equal(2, probe.Released.Count);
        }
        finally
        {
            release.Set();
            probe.DuringConstruct = null;
            try { await Task.WhenAll(operations).WaitAsync(TimeSpan.FromSeconds(10)); }
            finally { await scope.DisposeAsync(); }
        }
        Assert.Equal(probe.Created.Count, probe.Released.Count);
        Assert.All(probe.Created, call => Assert.Contains(probe.Released, released => ReferenceEquals(call.Receiver, released)));
    }

    [Fact]
    public void Adaptive_closed_cache_retries_null_and_failure_and_keeps_reentrant_block_publication()
    {
        var fixture = AdaptiveCacheRuntimeFixture(64);
        var blocks = AdaptiveCacheBlocks(fixture);
        // Pick two typed slots in one still-empty block. The nested resolution must survive outer publication.
        var block = blocks.Last(field => field.FieldType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Count(slot => fixture.Handlers.Contains(slot.FieldType) && !slot.FieldType.IsValueType) >= 2);
        var types = block.FieldType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(slot => slot.FieldType).Where(type => fixture.Handlers.Contains(type) && !type.IsValueType).Take(2).ToArray();
        var outerIndex = Array.IndexOf(fixture.Handlers, types[0]);
        var innerIndex = Array.IndexOf(fixture.Handlers, types[1]);
        var probe = new AdaptiveCacheProbe();
        var failure = new InvalidOperationException("native adaptive activation failure");
        IServiceCollection services = new ServiceCollection();
        services.AddSingleton(probe);
        object mediator = null!;
        object? innerWinner = null, innerOther = null;
        var attempts = 0;
        services.AddTransient(types[0], _ =>
        {
            var attempt = ++attempts;
            if (attempt == 1) return null!;
            if (attempt == 2) throw failure;
            if (attempt == 3)
            {
                innerOther = ((IServiceProvider)mediator).GetService(types[1]);
                innerWinner = ((IServiceProvider)mediator).GetService(types[0]);
            }
            return fixture.NewService(probe, outerIndex);
        });
        fixture.Register(services);
        using (var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }))
        using (var scope = provider.CreateScope())
        {
            mediator = scope.ServiceProvider.GetRequiredService(CanonicalEntryService(fixture.Mediator));
            var resolver = (IServiceProvider)mediator;
            Assert.All(blocks, field => Assert.Null(field.GetValue(mediator)));
            Assert.Null(resolver.GetService(types[0]));
            Assert.Null(block.GetValue(mediator));
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => resolver.GetService(types[0])));
            Assert.Null(block.GetValue(mediator));
            var captured = resolver.GetService(types[0]);
            Assert.Same(innerWinner, captured);
            Assert.NotNull(innerWinner);
            Assert.NotNull(innerOther);
            Assert.Same(innerOther, resolver.GetService(types[1]));
            Assert.Equal(4, attempts);
            Assert.Equal(17, fixture.Send(mediator, outerIndex, 17));
            Assert.Equal(19, fixture.Send(mediator, innerIndex, 19));
            Assert.Same(innerWinner, probe.Calls[0].Receiver);
            Assert.Same(innerOther, probe.Calls[1].Receiver);
            Assert.Equal(2, probe.Created.Count(call => call.Index == outerIndex));
            Assert.Single(probe.Created, call => call.Index == innerIndex);
            Assert.Empty(probe.Released);
        }
        Assert.Equal(probe.Created.Count, probe.Released.Count);
        Assert.All(probe.Created, call => Assert.Contains(probe.Released, released => ReferenceEquals(call.Receiver, released)));
    }

    [Fact]
    public void Adaptive_closed_cache_keeps_original_mutable_box_and_before_failure_lazy()
    {
        var fixture = AdaptiveCacheRuntimeFixture(64);
        var probe = new AdaptiveCacheProbe();
        var index = fixture.Handlers.Length - 1;
        var type = fixture.Handlers[index];
        var box = fixture.NewService(probe, index);
        var resolutions = 0;
        IServiceCollection services = new ServiceCollection();
        services.AddSingleton(probe);
        services.AddSingleton(type, _ => { resolutions++; return box; });
        fixture.Register(services);
        using (var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }))
        using (var scope = provider.CreateScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService(CanonicalEntryService(fixture.Mediator));
            var failure = new InvalidOperationException("stop in Before");
            probe.BeforeError = failure;
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => fixture.Send(mediator, index, 10)));
            Assert.Equal(0, resolutions);
            Assert.Equal(0, type.GetField("State")!.GetValue(box));
            Assert.Single(probe.Created, call => call.Index == -1);
            probe.BeforeError = null;
            Assert.Equal(11, fixture.Send(mediator, index, 10));
            Assert.Equal(12, fixture.Send(mediator, index, 10));
            Assert.Equal(1, resolutions);
            Assert.Equal(2, type.GetField("State")!.GetValue(box));
            Assert.Equal(0, type.GetField("Disposed")!.GetValue(box));
        }
        Assert.Equal(1, type.GetField("Disposed")!.GetValue(box));
    }

    private sealed class AdaptiveCacheProvider(Dictionary<Type, object> values) : IServiceProvider
    {
        public object? GetService(Type type) => values.GetValueOrDefault(type);
    }

    private sealed record AdaptiveCacheAllocation(int Dependencies, int Repetitions, long ConstructionBytesTotal,
        long SparseBytesTotal, long DenseBytesTotal, long WarmDispatchBytesTotal)
    {
        public double ConstructionBytesPerOperation => ConstructionBytesTotal / (double)Repetitions;
        public double SparseBytesPerOperation => SparseBytesTotal / (double)Repetitions;
        public double DenseBytesPerOperation => DenseBytesTotal / (double)Repetitions;
        public double WarmDispatchBytesPerOperation => WarmDispatchBytesTotal / (double)Repetitions;
    }

    private static AdaptiveCacheAllocation AdaptiveCacheAllocations(int dependencies)
    {
        var fixture = AdaptiveCacheRuntimeFixture(dependencies);
        var probe = new AdaptiveCacheProbe { Record = false };
        var values = fixture.Handlers.Select((type, i) => (type, value: fixture.NewService(probe, i))).ToDictionary(pair => pair.type, pair => pair.value);
        values.Add(fixture.Hook, fixture.NewService(probe, -1));
        var provider = new AdaptiveCacheProvider(values);
        const int repetitions = 1024;
        long Construct(int shape)
        {
            void Run()
            {
                var mediator = fixture.Create(provider);
                if (shape == 1) fixture.Send(mediator, 0, 1);
                if (shape == 2) fixture.Dense(mediator, 1);
                GC.KeepAlive(mediator);
            }
            for (var i = 0; i < 32; i++) Run();
            var start = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < repetitions; i++) Run();
            return GC.GetAllocatedBytesForCurrentThread() - start;
        }
        var construction = Construct(0);
        var sparse = Construct(1);
        var dense = Construct(2);
        var warmed = fixture.Create(provider);
        fixture.Dense(warmed, 1);
        for (var i = 0; i < 256; i++) fixture.Send(warmed, i % fixture.Handlers.Length, i);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < repetitions; i++) fixture.Send(warmed, i % fixture.Handlers.Length, i);
        var dispatch = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(warmed);
        return new(dependencies, repetitions, construction, sparse, dense, dispatch);
    }

    [Fact]
    public void Adaptive_closed_cache_construction_and_retained_capture_cost_follow_touched_blocks()
    {
        var small = AdaptiveCacheAllocations(32);
        var large = AdaptiveCacheAllocations(64);
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(evidence))
        {
            Directory.CreateDirectory(evidence);
            File.WriteAllText(Path.Combine(evidence, "adaptive-closed-allocations.json"), JsonSerializer.Serialize(new
            {
                small,
                large,
                constructionWarmup = 32,
                dispatchWarmup = 256,
                denseWarmup = 1,
                metric = "whole per-thread managed construction and capture allocation; no timing or per-type ownership attribution",
                reflectionPerOperation = false,
                timing = false,
                runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                runtimeVersion = Environment.Version.ToString(),
                processArchitecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                osArchitecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
                Environment.Is64BitProcess,
                serverGC = System.Runtime.GCSettings.IsServerGC,
                gcLatencyMode = System.Runtime.GCSettings.LatencyMode.ToString()
            }));
        }
        Assert.Equal(small.ConstructionBytesTotal, small.SparseBytesTotal);
        Assert.Equal(small.ConstructionBytesTotal, small.DenseBytesTotal);
        Assert.True(large.ConstructionBytesTotal < small.ConstructionBytesTotal, JsonSerializer.Serialize(new { small, large }));
        Assert.True(large.ConstructionBytesTotal < large.SparseBytesTotal);
        Assert.True(large.SparseBytesTotal < large.DenseBytesTotal);
        Assert.Equal(0, small.WarmDispatchBytesTotal);
        Assert.Equal(0, large.WarmDispatchBytesTotal);
    }
}
