using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Zendiator.SourceGenerator;
using Zendiator.Tests.Contracts;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private static readonly string[] OpenAsyncRuntimeCases = ["chain", "cancel-filter", "cancel-map"];
    private sealed record OpenAsyncRuntimeFixture(Type Request, Type[] Definitions, Type[] Services, Type Bridge, MethodInfo Start, MethodInfo Create)
    {
        public Action<IServiceCollection>? Register { get; init; }
        public Func<IServiceProvider, int, int>? Send { get; init; }
    }
    private sealed record OpenAsyncRuntimeResult(int[] Values, StreamSeedEvent[] Events, OpenAsyncServiceEvent[] Services,
        int Starts, int Moves, int Reads, int Disposals, int LastCalls, int FilterConsumptions, int MapConsumptions,
        bool FirstPending, bool MapPending, bool Canceled, bool Faulted, bool ErrorTokenMatches, bool BorrowedAliveBeforeScopeEnd, int ScopeDisposals);

    [Fact]
    public async Task Stream_open_async_DI_pending_chain_and_cancellation_match_reference_and_scope_ownership()
    {
        var fixture = CompileOpenAsyncRuntime();
        foreach (var id in OpenAsyncRuntimeCases)
        {
            var reference = await RunOpenAsyncRuntime(fixture, "reference", id);
            var canonical = await RunOpenAsyncRuntime(fixture, "canonical", id);
            SaveOpenAsyncRuntime(id + ".json", new { id, reference, canonical, productionEnabled = false });
            Assert.Equal(reference.Values, canonical.Values); Assert.Equal(reference.Events, canonical.Events); Assert.Equal(reference.Services, canonical.Services);
            Assert.Equal(reference with { Values = canonical.Values, Events = canonical.Events, Services = canonical.Services }, canonical);
            Assert.Equal(id == "chain" ? new[] { 2 } : [], canonical.Values);
            Assert.Equal(1, canonical.Starts); Assert.Equal(1, canonical.Reads); Assert.Equal(1, canonical.Disposals);
            Assert.Equal(id == "chain" ? 2 : 1, canonical.Moves); Assert.Equal(id == "chain" ? 1 : 0, canonical.LastCalls);
            Assert.Equal(1, canonical.FilterConsumptions); Assert.Equal(id == "cancel-filter" ? 0 : 1, canonical.MapConsumptions);
            Assert.True(canonical.FirstPending); Assert.Equal(id != "cancel-filter", canonical.MapPending);
            Assert.Equal(id != "chain", canonical.Canceled); Assert.False(canonical.Faulted); Assert.True(canonical.ErrorTokenMatches);
            Assert.True(canonical.BorrowedAliveBeforeScopeEnd); Assert.Equal(id == "chain" ? 4 : id == "cancel-filter" ? 2 : 3, canonical.ScopeDisposals);
        }
    }
    private static string OpenAsyncRuntimeSource()
    {
        return ListFixture("c.Handlers(typeof(OpenAsyncRequest<>),DispatchFamily.Stream,typeof(OpenAsyncHandler<>));" +
            "c.Pipeline(typeof(OpenAsyncRequest<>),DispatchFamily.Stream,Stage.Hook(typeof(OpenAsyncFirst<,>)),Stage.Hook(typeof(OpenAsyncMap<,>)),Stage.Hook(typeof(OpenAsyncLast<,>)));") + """
            public sealed record OpenAsyncRequest<T>(T Value) : IStreamRequest<T>;
            [ZendiatorHandler] public sealed class OpenAsyncHandler<T> : IStreamRequestHandler<OpenAsyncRequest<T>,T>, IDisposable where T:class,new() {
                private readonly global::Zendiator.Generator.Tests.CanonicalOpenAsyncRuntimeProbe<T> p;
                public OpenAsyncHandler(global::Zendiator.Generator.Tests.CanonicalOpenAsyncRuntimeProbe<T> p){this.p=p;p.Created(this,"H");}
                public System.Collections.Generic.IAsyncEnumerable<T> HandleAsync(OpenAsyncRequest<T> r,CancellationToken c)=>p.Start(this,r,c);
                public void Dispose()=>p.Released(this);
            }
            public sealed class OpenAsyncFirst<R,T> : IAsyncStreamItemFilter<R,T>, IDisposable where T:class,new() {
                private readonly global::Zendiator.Generator.Tests.CanonicalOpenAsyncRuntimeProbe<T> p;
                public OpenAsyncFirst(global::Zendiator.Generator.Tests.CanonicalOpenAsyncRuntimeProbe<T> p){this.p=p;p.Created(this,"F1");}
                public ValueTask<bool> AcceptAsync(R r,T item,CancellationToken c)=>p.First(this,r!,item,c);
                public void Dispose()=>p.Released(this);
            }
            public sealed class OpenAsyncMap<R,T> : IAsyncStreamItemMap<R,T>, IDisposable where T:class,new() {
                private readonly global::Zendiator.Generator.Tests.CanonicalOpenAsyncRuntimeProbe<T> p;
                public OpenAsyncMap(global::Zendiator.Generator.Tests.CanonicalOpenAsyncRuntimeProbe<T> p){this.p=p;p.Created(this,"Map");}
                public ValueTask<T> MapAsync(R r,T item,CancellationToken c)=>p.Map(this,r!,item,c);
                public void Dispose()=>p.Released(this);
            }
            public sealed class OpenAsyncLast<R,T> : IStreamItemFilter<R,T>, IDisposable where T:class,new() {
                private readonly global::Zendiator.Generator.Tests.CanonicalOpenAsyncRuntimeProbe<T> p;
                public OpenAsyncLast(global::Zendiator.Generator.Tests.CanonicalOpenAsyncRuntimeProbe<T> p){this.p=p;p.Created(this,"F2");}
                public bool Accept(R r,T item,CancellationToken c)=>p.Last(this,r!,item,c);
                public void Dispose()=>p.Released(this);
            }
            public sealed class OpenAsyncBridge<T>(System.IServiceProvider services) where T:class,new() {
                public System.Collections.Generic.IAsyncEnumerable<T> Start(OpenAsyncRequest<T> r,CancellationToken c)=>
                    Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<OpenAsyncHandler<T>>(services).HandleAsync(r,c);
            }
            """;
    }

    private static OpenAsyncRuntimeFixture CompileOpenAsyncRuntime()
    {
        var source = OpenAsyncRuntimeSource();
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "OpenAsyncRuntime_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        Assert.Empty(diagnostics); var route = Assert.Single(Selection(driver).Pipelines);
        Assert.Equal(" where __ZendiatorType0 : class, new()", route.StreamGeneric!.TypeConstraints);
        Assert.Equal(CanonicalStreamConnectionKind.CanonicalCandidate, Assert.Single(StreamConnections(driver).Routes).Kind);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error));
        using var bytes = new MemoryStream(); var emitted = output.Emit(bytes); Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        var folder = OpenAsyncRuntimeFolder();
        if (folder != null)
        {
            File.WriteAllText(Path.Combine(folder, "input.cs"), source); File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
            foreach (var file in driver.GetRunResult().Results.Single().GeneratedSources) File.WriteAllText(Path.Combine(folder, file.HintName), file.SourceText.ToString());
        }
        SaveOpenAsyncRuntime("compile.json", new { emitted.Success, consumerCompilations = 1, generic = route.StreamGeneric, productionEnabled = false });
        bytes.Position = 0; var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes); var item = typeof(OpenAsyncRuntimeItem);
        var request = assembly.GetType("App.OpenAsyncRequest`1")!.MakeGenericType(item);
        var definitions = new[] { "OpenAsyncHandler`1", "OpenAsyncFirst`2", "OpenAsyncMap`2", "OpenAsyncLast`2" }.Select(n => assembly.GetType("App." + n)!).ToArray();
        var services = definitions.Select((t, i) => t.MakeGenericType(i == 0 ? [item] : [request, item])).ToArray();
        var bridge = assembly.GetType("App.OpenAsyncBridge`1")!.MakeGenericType(item);
        var helper = Assert.Single(assembly.GetTypes(), t => t.Name.StartsWith("__ZendiatorStreamCanonical_", StringComparison.Ordinal)).MakeGenericType(item);
        return new(request, definitions, services, bridge, bridge.GetMethod("Start")!, helper.GetMethod("Create", BindingFlags.NonPublic | BindingFlags.Static)!);
    }
    private static async Task<OpenAsyncRuntimeResult> RunOpenAsyncRuntime(OpenAsyncRuntimeFixture fixture, string backend, string id)
    {
        using var api = new CancellationTokenSource(); using var enumeration = new CancellationTokenSource();
        var input = new OpenAsyncRuntimeItem { Number = 1 }; var mapped = new OpenAsyncRuntimeItem { Number = 2 };
        var request = Activator.CreateInstance(fixture.Request, input)!;
        var p = new CanonicalOpenAsyncRuntimeProbe<OpenAsyncRuntimeItem>
        {
            Request = request,
            Input = input,
            Mapped = mapped,
            Distractor = new() { Number = 9 },
            SourceCurrent = input
        };
        var collection = new ServiceCollection(); collection.AddSingleton(p);
        if (fixture.Register == null)
            foreach (var type in fixture.Definitions) collection.AddScoped(type, type);
        else
        {
            fixture.Register(collection); var count = collection.Count; fixture.Register(collection);
            Assert.Equal(count, collection.Count);
            Assert.All(fixture.Definitions, type => Assert.Equal(ServiceLifetime.Scoped, Assert.Single(collection, d => d.ServiceType == type).Lifetime));
        }
        using var provider = collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }); using var scope = provider.CreateScope();
        if (fixture.Send != null) Assert.Equal(42, fixture.Send(scope.ServiceProvider, 41));
        IAsyncEnumerable<OpenAsyncRuntimeItem> stream;
        if (backend == "reference")
        {
            var bridge = Activator.CreateInstance(fixture.Bridge, scope.ServiceProvider)!;
            var start = fixture.Start.CreateDelegate(typeof(Func<,,>).MakeGenericType(fixture.Request, typeof(CancellationToken), typeof(IAsyncEnumerable<OpenAsyncRuntimeItem>)), bridge);
            stream = (IAsyncEnumerable<OpenAsyncRuntimeItem>)typeof(CanonicalStreamAsyncItemReference).GetMethod("FilterMapFilter")!
                .MakeGenericMethod(fixture.Request, typeof(OpenAsyncRuntimeItem), fixture.Services[1], fixture.Services[2], fixture.Services[3])
                .Invoke(null, [scope.ServiceProvider, request, start, api.Token])!;
        }
        else stream = (IAsyncEnumerable<OpenAsyncRuntimeItem>)fixture.Create.Invoke(null, [scope.ServiceProvider, request, api.Token])!;
        var cursor = stream.GetAsyncEnumerator(enumeration.Token); Assert.Empty(p.ServiceEvents); Assert.Equal(0, p.Starts);
        var move = cursor.MoveNextAsync().AsTask(); await p.FilterEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var firstPending = !move.IsCompleted; Assert.True(firstPending); Assert.Equal(0, p.FilterGate.Consumptions);
        Assert.DoesNotContain(p.ServiceEvents, e => e.Role is "Map" or "F2"); Assert.Equal(1, p.Reads);
        Assert.NotEqual(api.Token, p.ExecutionToken); Assert.NotEqual(enumeration.Token, p.ExecutionToken);
        p.SourceCurrent = p.Distractor; // A second source.Current read would lose the original in-flight item.
        if (id == "cancel-filter") { api.Cancel(); p.Event("Cancel", "Consumer"); }
        p.Event("Release", "F1", result: true); p.FilterGate.Complete(true);
        var mapPending = false;
        if (id != "cancel-filter")
        {
            await p.MapEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)); mapPending = !move.IsCompleted; Assert.True(mapPending);
            Assert.Equal(1, p.FilterGate.Consumptions); Assert.Equal(0, p.MapGate.Consumptions); Assert.Equal(1, p.Reads);
            Assert.DoesNotContain(p.ServiceEvents, e => e.Role == "F2");
            if (id == "cancel-map") { api.Cancel(); p.Event("Cancel", "Consumer"); }
            p.Event("Release", "Map", output: 2); p.MapGate.Complete(mapped);
        }
        var values = new List<int>(); Exception? error = null; var errorTokenMatches = true;
        try
        {
            if (await move.WaitAsync(TimeSpan.FromSeconds(5)))
            {
                Assert.Same(mapped, cursor.Current); values.Add(cursor.Current.Number); _ = cursor.Current; Assert.Equal(1, p.Reads);
                p.Event("ConsumerYield", "Consumer", output: 2); Assert.False(await cursor.MoveNextAsync());
            }
        }
        catch (OperationCanceledException caught)
        {
            error = caught; errorTokenMatches = caught.CancellationToken == p.ExecutionToken; Assert.True(errorTokenMatches);
            Assert.True(p.ExecutionToken.IsCancellationRequested); Assert.False(enumeration.IsCancellationRequested);
            p.Event("ConsumerCatch", "Consumer", callbackToken: true, error: caught);
        }
        if (id == "chain") Assert.Null(error); else Assert.IsAssignableFrom<OperationCanceledException>(error);
        await cursor.DisposeAsync(); await cursor.DisposeAsync(); Assert.Equal(1, p.Disposals);
        if (fixture.Send != null) Assert.Equal(10, fixture.Send(scope.ServiceProvider, 9));
        var alive = !p.ServiceEvents.Any(e => e.Kind == "Disposed"); Assert.True(alive);
        var created = p.ServiceEvents.Where(e => e.Kind == "Created").ToArray();
        Assert.All(p.ServiceEvents.Where(e => e.Kind == "Called"), e => Assert.Contains(created, c => c.Service == e.Service && c.Role == e.Role));
        scope.Dispose(); var disposed = p.ServiceEvents.Where(e => e.Kind == "Disposed").ToArray();
        Assert.Equal(created.Reverse().Select(e => e.Service), disposed.Select(e => e.Service));
        return new([.. values], [.. p.Events], [.. p.ServiceEvents], p.Starts, p.Moves, p.Reads, p.Disposals, p.LastCalls,
            p.FilterGate.Consumptions, p.MapGate.Consumptions, firstPending, mapPending, move.IsCanceled, move.IsFaulted, errorTokenMatches, alive, disposed.Length);
    }
    private static string? OpenAsyncRuntimeFolder()
    {
        var root = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR"); if (string.IsNullOrEmpty(root)) return null;
        var folder = Path.Combine(root, "open-async-runtime"); Directory.CreateDirectory(folder); return folder;
    }
    private static void SaveOpenAsyncRuntime(string name, object value)
    { var folder = OpenAsyncRuntimeFolder(); if (folder != null) File.WriteAllText(Path.Combine(folder, name), System.Text.Json.JsonSerializer.Serialize(value)); }
}
