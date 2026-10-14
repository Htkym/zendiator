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
    private sealed record AsyncWrapperFixture(Type Request, Type[] Services, Type Bridge, MethodInfo Start, MethodInfo Create);
    private sealed record AsyncWrapperResult(int[] Values, StreamSeedEvent[] Events, int Starts, int Moves, int Reads,
        int SourceDisposals, int Opens, int UpstreamDisposals, int WrapperDisposals, int WrapperFinals,
        int UpstreamReads, int WrapperReads, int FilterConsumptions, int MapConsumptions, bool FilterPending, bool MapPending);
    [Fact]
    public async Task Stream_async_filter_opaque_wrapper_async_map_preserves_segments_pending_and_cursor_ownership()
    {
        var fixture = CompileAsyncWrapper(); var reference = await RunAsyncWrapper(fixture, "reference"); var canonical = await RunAsyncWrapper(fixture, "canonical");
        SaveAsyncWrapper("comparison.json", new { reference, canonical, cases = 1, consumerCompilations = 1, productionEnabled = false });
        Assert.Equal(reference.Values, canonical.Values); Assert.Equal(reference.Events, canonical.Events);
        Assert.Equal(reference with { Values = canonical.Values, Events = canonical.Events }, canonical);
        Assert.Equal(new[] { 20 }, canonical.Values); Assert.Equal(1, canonical.Starts); Assert.Equal(2, canonical.Moves); Assert.Equal(1, canonical.Reads);
        Assert.Equal(1, canonical.SourceDisposals); Assert.Equal(1, canonical.Opens); Assert.Equal(1, canonical.UpstreamDisposals);
        Assert.Equal(1, canonical.WrapperDisposals); Assert.Equal(1, canonical.WrapperFinals); Assert.Equal(1, canonical.UpstreamReads); Assert.Equal(1, canonical.WrapperReads);
        Assert.Equal(1, canonical.FilterConsumptions); Assert.Equal(1, canonical.MapConsumptions); Assert.True(canonical.FilterPending); Assert.True(canonical.MapPending);
        var e = canonical.Events;
        var order = new[] { ("Filter", "F1"), ("Release", "F1"), ("WrapperReplace", "W"), ("Map", "Map"), ("Release", "Map"), ("ConsumerYield", "Consumer"),
            ("InnerDisposeEnd", "H"), ("CursorDispose", "Upstream"), ("WrapperFinally", "W"), ("CursorDispose", "Wrapper") };
        var indices = order.Select(x => Array.FindIndex(e, v => v.Kind == x.Item1 && v.Stage == x.Item2)).ToArray();
        Assert.All(indices, i => Assert.True(i >= 0)); Assert.Equal(indices.OrderBy(i => i), indices);
    }
    private static AsyncWrapperFixture CompileAsyncWrapper()
    {
        var source = ListFixture("c.Handlers<AsyncWrapperRequest>(DispatchFamily.Stream,typeof(AsyncWrapperHandler));" +
            "c.Pipeline<AsyncWrapperRequest>(Stage.Hook<AsyncWrapperFilter>(),Stage.Wrapper<AsyncWrapperOpaque>(),Stage.Hook<AsyncWrapperMap>());") + """
            public sealed record AsyncWrapperRequest : IStreamRequest<int>;
            [ZendiatorHandler] public sealed class AsyncWrapperHandler(global::Zendiator.Generator.Tests.CanonicalAsyncWrapperProbe p):IStreamRequestHandler<AsyncWrapperRequest,int> {
                public System.Collections.Generic.IAsyncEnumerable<int> HandleAsync(AsyncWrapperRequest r,CancellationToken c)=>p.Start(r,c);
            }
            public sealed class AsyncWrapperFilter(global::Zendiator.Generator.Tests.CanonicalAsyncWrapperProbe p):IAsyncStreamItemFilter<AsyncWrapperRequest,int> {
                public ValueTask<bool> AcceptAsync(AsyncWrapperRequest r,int item,CancellationToken c)=>p.First(r,item,c);
            }
            public sealed class AsyncWrapperMap(global::Zendiator.Generator.Tests.CanonicalAsyncWrapperProbe p):IAsyncStreamItemMap<AsyncWrapperRequest,int> {
                public ValueTask<int> MapAsync(AsyncWrapperRequest r,int item,CancellationToken c)=>p.Map(r,item,c);
            }
            public sealed class AsyncWrapperOpaque(global::Zendiator.Generator.Tests.CanonicalAsyncWrapperProbe p):IStreamWrapper<AsyncWrapperRequest,int> {
                public System.Collections.Generic.IAsyncEnumerable<int> Wrap<N>(AsyncWrapperRequest r,N n,CancellationToken c) where N:IStreamUpstream<AsyncWrapperRequest,int> {
                    p.Wrap(r,c);return p.ObserveWrapper(Run(r,n,c));
                }
                private async System.Collections.Generic.IAsyncEnumerable<int> Run<N>(AsyncWrapperRequest r,N n,
                    [global::System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken c) where N:IStreamUpstream<AsyncWrapperRequest,int> {
                    try { await foreach(var item in p.ObserveOpen(r,n,c).ConfigureAwait(false)) yield return p.Replace(r,item,c); }
                    finally { p.Finally(r,c); }
                }
            }
            public sealed class AsyncWrapperBridge(System.IServiceProvider services) {
                public System.Collections.Generic.IAsyncEnumerable<int> Start(AsyncWrapperRequest r,CancellationToken c)=>
                    Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<AsyncWrapperHandler>(services).HandleAsync(r,c);
            }
            """;
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "AsyncWrapper_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        Assert.Equal("ZEN0037", Assert.Single(diagnostics).Id); Assert.Equal(CanonicalStreamConnectionKind.CanonicalCandidate, Assert.Single(StreamConnections(driver).Routes).Kind);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error));
        using var bytes = new MemoryStream(); var emitted = output.Emit(bytes); Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        var folder = AsyncWrapperFolder();
        if (folder != null)
        {
            File.WriteAllText(Path.Combine(folder, "input.cs"), source); File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
            foreach (var file in driver.GetRunResult().Results.Single().GeneratedSources) File.WriteAllText(Path.Combine(folder, file.HintName), file.SourceText.ToString());
        }
        SaveAsyncWrapper("compile.json", new { emitted.Success, consumerCompilations = 1, productionEnabled = false });
        bytes.Position = 0; var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes); var bridge = assembly.GetType("App.AsyncWrapperBridge")!;
        return new(assembly.GetType("App.AsyncWrapperRequest")!, new[] { "AsyncWrapperHandler", "AsyncWrapperFilter", "AsyncWrapperOpaque", "AsyncWrapperMap" }.Select(n => assembly.GetType("App." + n)!).ToArray(),
            bridge, bridge.GetMethod("Start")!, Assert.Single(assembly.GetTypes(), t => t.Name.StartsWith("__ZendiatorStreamCanonical_", StringComparison.Ordinal)).GetMethod("Create", BindingFlags.NonPublic | BindingFlags.Static)!);
    }
    private static async Task<AsyncWrapperResult> RunAsyncWrapper(AsyncWrapperFixture fixture, string backend)
    {
        var request = Activator.CreateInstance(fixture.Request)!; var p = new CanonicalAsyncWrapperProbe { Request = request };
        var collection = new ServiceCollection(); var roles = new[] { "H", "F1", "W", "Map" };
        for (var i = 0; i < fixture.Services.Length; i++) { var type = fixture.Services[i]; var role = roles[i]; collection.AddTransient(type, _ => { p.Resolve(role); return Activator.CreateInstance(type, p)!; }); }
        using var provider = collection.BuildServiceProvider(); IAsyncEnumerable<int> stream;
        if (backend == "reference")
        {
            var bridge = Activator.CreateInstance(fixture.Bridge, provider)!;
            var start = fixture.Start.CreateDelegate(typeof(Func<,,>).MakeGenericType(fixture.Request, typeof(CancellationToken), typeof(IAsyncEnumerable<int>)), bridge);
            stream = (IAsyncEnumerable<int>)typeof(CanonicalStreamWrapperReference).GetMethod("AsyncFilterWrapperMap")!
                .MakeGenericMethod(fixture.Request, typeof(int), fixture.Services[1], fixture.Services[2], fixture.Services[3]).Invoke(null, [provider, request, start, default(CancellationToken)])!;
        }
        else stream = (IAsyncEnumerable<int>)fixture.Create.Invoke(null, [provider, request, default(CancellationToken)])!;
        var cursor = stream.GetAsyncEnumerator(); Assert.Empty(p.Events); var move = cursor.MoveNextAsync().AsTask();
        await p.FilterEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)); var filterPending = !move.IsCompleted; Assert.True(filterPending);
        Assert.Equal(0, p.FilterGate.Consumptions); Assert.DoesNotContain(p.Events, e => e.Kind is "Map" or "WrapperReplace");
        p.Event("Release", "F1", result: true); p.FilterGate.Complete(true);
        await p.MapEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)); var mapPending = !move.IsCompleted; Assert.True(mapPending);
        Assert.Equal(1, p.FilterGate.Consumptions); Assert.Equal(0, p.MapGate.Consumptions);
        p.Event("Release", "Map", output: 20); p.MapGate.Complete(20);
        Assert.True(await move.WaitAsync(TimeSpan.FromSeconds(5))); var values = new[] { cursor.Current }; _ = cursor.Current;
        p.Event("ConsumerYield", "Consumer", output: values[0]); Assert.False(await cursor.MoveNextAsync()); await cursor.DisposeAsync(); await cursor.DisposeAsync();
        var result = new AsyncWrapperResult(values, [.. p.Events], p.Starts, p.Moves, p.Reads, p.SourceDisposals, p.Opens, p.UpstreamDisposals,
            p.WrapperDisposals, p.WrapperFinals, p.UpstreamReads, p.WrapperReads, p.FilterGate.Consumptions, p.MapGate.Consumptions, filterPending, mapPending);
        SaveAsyncWrapper(backend + ".json", result); return result;
    }
    private static string? AsyncWrapperFolder()
    {
        var root = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR"); if (string.IsNullOrEmpty(root)) return null;
        var folder = Path.Combine(root, "async-wrapper"); Directory.CreateDirectory(folder); return folder;
    }
    private static void SaveAsyncWrapper(string name, object value)
    { var folder = AsyncWrapperFolder(); if (folder != null) File.WriteAllText(Path.Combine(folder, name), System.Text.Json.JsonSerializer.Serialize(value)); }
}
