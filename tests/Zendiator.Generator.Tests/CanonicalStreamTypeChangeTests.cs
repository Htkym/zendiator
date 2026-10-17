using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Zendiator.SourceGenerator;
using Zendiator.Tests.Contracts;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private static readonly string[] TypeChangeInputs = ["success", "input-mismatch", "wrapper-boundary", "nullable-mismatch", "final-mismatch"];
    private sealed record TypeChangeFixture(Assembly Assembly, Type Request, Type[] Services, Type Bridge, MethodInfo Start, MethodInfo Create);
    private sealed record TypeChangeResult(long[] Values, string[] Trace, int Starts, int Moves, int Reads, int Disposals, int SyncCalls, int AsyncCalls, int Consumptions, bool Pending);
    private static string TypeChangeSource(string id)
    {
        var final = id == "final-mismatch" ? "bool" : "long";
        var mid = id == "wrapper-boundary" ? "Stage.Wrapper<WrongBoundary>()," : "";
        var source = ListFixture("c.Handlers<ChangeRequest>(DispatchFamily.Stream,typeof(ChangeHandler));c.StreamPipeline<ChangeRequest," + final +
            ">(Stage.Hook(typeof(ToReference<>))," + mid + "Stage.Hook<ToFinal>());") + """
            public sealed record ChangeRequest : IStreamRequest<int>;
            [ZendiatorHandler] public sealed class ChangeHandler(global::Zendiator.Generator.Tests.CanonicalStreamTypeChangeProbe p) : IStreamRequestHandler<ChangeRequest,int> {
                public System.Collections.Generic.IAsyncEnumerable<int> HandleAsync(ChangeRequest r,CancellationToken c)=>p.Start(r,c);
            }
            public sealed class ToReference<T>(global::Zendiator.Generator.Tests.CanonicalStreamTypeChangeProbe p) : IStreamItemMap<ChangeRequest,T,string?> where T:struct {
                public string? Map(ChangeRequest r,T item,CancellationToken c)=>p.FirstText(r,item.ToString(),c);
            }
            public sealed class ToFinal(global::Zendiator.Generator.Tests.CanonicalStreamTypeChangeProbe p) : IAsyncStreamItemMap<ChangeRequest,string?,long> {
                public ValueTask<long> MapAsync(ChangeRequest r,string? item,CancellationToken c)=>p.Last(r,item,c);
            }
            public sealed class WrongBoundary:IStreamWrapper<ChangeRequest,int> {
                public System.Collections.Generic.IAsyncEnumerable<int> Wrap<N>(ChangeRequest r,N n,CancellationToken c) where N:IStreamUpstream<ChangeRequest,int> =>n.Open(r,c);
            }
            public sealed class ChangeBridge(System.IServiceProvider services) {
                public System.Collections.Generic.IAsyncEnumerable<int> Start(ChangeRequest r,CancellationToken c)=>
                    Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<ChangeHandler>(services).HandleAsync(r,c);
            }
            """;
        if (id == "input-mismatch") source = source.Replace("IAsyncStreamItemMap<ChangeRequest,string?,long>", "IAsyncStreamItemMap<ChangeRequest,int,long>")
            .Replace("MapAsync(ChangeRequest r,string? item,CancellationToken c)=>p.Last(r,item,c);", "MapAsync(ChangeRequest r,int item,CancellationToken c){_ = p; return new(70L);}");
        if (id == "nullable-mismatch") source = source.Replace("IAsyncStreamItemMap<ChangeRequest,string?,long>", "IAsyncStreamItemMap<ChangeRequest,string,long>")
            .Replace("MapAsync(ChangeRequest r,string? item", "MapAsync(ChangeRequest r,string item");
        return source;
    }
    [Fact]
    public async Task Stream_type_changing_maps_validate_ports_generate_typed_current_and_match_reference()
    {
        TypeChangeFixture? fixture = null;
        foreach (var id in TypeChangeInputs)
        {
            var source = TypeChangeSource(id); var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "TypeChange_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
            using var bytes = new MemoryStream(); var emitted = output.Emit(bytes); var route = Assert.Single(Selection(driver).Pipelines);
            var generated = driver.GetRunResult().Results.Single().GeneratedSources;
            var folder = TypeChangeFolder(id);
            if (folder != null)
            {
                File.WriteAllText(Path.Combine(folder, "input.cs"), source); File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
                foreach (var file in generated) File.WriteAllText(Path.Combine(folder, file.HintName), file.SourceText.ToString());
                File.WriteAllText(Path.Combine(folder, "binding.json"), System.Text.Json.JsonSerializer.Serialize(new
                {
                    id,
                    emitted.Success,
                    flow = route.StreamItemFlow,
                    diagnostics = diagnostics.Select(d => new
                    {
                        d.Id,
                        severity = d.Severity.ToString(),
                        message = d.GetMessage(),
                        inSource = d.Location.IsInSource,
                        anchor = d.Location.IsInSource ? d.Location.SourceTree!.GetText().ToString(d.Location.SourceSpan) : null
                    }),
                    compiler = emitted.Diagnostics.Select(d => new { d.Id, severity = d.Severity.ToString(), message = d.GetMessage() }),
                    productionEnabled = false
                }));
            }
            Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)); Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
            if (id != "success")
            {
                var expected = id == "wrapper-boundary" ? "Stage.Wrapper" : id == "final-mismatch" ? "c.StreamPipeline" : "Stage.Hook<ToFinal>";
                Assert.Contains(diagnostics, d => d.Id == "ZEN0026" && d.Severity == DiagnosticSeverity.Error && d.Location.IsInSource &&
                    d.Location.SourceTree!.GetText().ToString(d.Location.SourceSpan).StartsWith(expected, StringComparison.Ordinal));
                Assert.DoesNotContain(generated, s => s.HintName.StartsWith("Zendiator.Stream", StringComparison.Ordinal)); continue;
            }
            Assert.Empty(diagnostics);
            var flow = Assert.IsType<BoundStreamItemFlow>(route.StreamItemFlow); Assert.Equal("int", flow.Source.Name); Assert.Equal("long", flow.Final.Name);
            Assert.Equal(new[] { "int", "string?" }, flow.Stages.Select(s => s.Input.Name)); Assert.Equal(new[] { "string?", "long" }, flow.Stages.Select(s => s.Output.Name));
            Assert.Equal(flow.Final.Identity, route.Result); Assert.Contains("ToReference<int>", route.Stages[0].Name);
            Assert.Equal(CanonicalStreamConnectionKind.CanonicalCandidate, Assert.Single(StreamConnections(driver).Routes).Kind);
            var text = Assert.Single(generated, s => s.HintName.StartsWith("Zendiator.StreamCanonical.", StringComparison.Ordinal)).SourceText.ToString();
            Assert.Contains("IAsyncEnumerator<int>", text); Assert.Contains("IAsyncEnumerator<long>", text); Assert.Contains("private long _current", text);
            Assert.Contains("var item1 = _hook0.Map(request, item0, _token)", text); Assert.Contains("var item2 = (await _hook1.MapAsync(request, item1, _token).ConfigureAwait(false))", text);
            bytes.Position = 0; var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes); var request = assembly.GetType("App.ChangeRequest")!;
            var bridge = assembly.GetType("App.ChangeBridge")!; var helper = Assert.Single(assembly.GetTypes(), t => t.Name.StartsWith("__ZendiatorStreamCanonical_", StringComparison.Ordinal));
            fixture = new(assembly, request, [assembly.GetType("App.ChangeHandler")!, assembly.GetType("App.ToReference`1")!.MakeGenericType(typeof(int)), assembly.GetType("App.ToFinal")!],
                bridge, bridge.GetMethod("Start")!, helper.GetMethod("Create", BindingFlags.NonPublic | BindingFlags.Static)!);
            InspectTypeChangeIL(helper);
        }
        Assert.NotNull(fixture); var reference = await RunTypeChange(fixture, true); var canonical = await RunTypeChange(fixture, false);
        SaveTypeChange("comparison.json", new { reference, canonical, consumerCompilations = 1, runtimeEnumerations = 2, productionEnabled = false });
        Assert.Equal(reference.Values, canonical.Values); Assert.Equal(reference.Trace, canonical.Trace); Assert.Equal(reference with { Values = canonical.Values, Trace = canonical.Trace }, canonical);
        Assert.Equal(new long[] { 70 }, canonical.Values); Assert.Equal(1, canonical.Starts); Assert.Equal(2, canonical.Moves); Assert.Equal(1, canonical.Reads); Assert.Equal(1, canonical.Disposals);
        Assert.Equal(1, canonical.SyncCalls); Assert.Equal(1, canonical.AsyncCalls); Assert.Equal(1, canonical.Consumptions); Assert.True(canonical.Pending);
    }
    private static async Task<TypeChangeResult> RunTypeChange(TypeChangeFixture f, bool reference)
    {
        var request = Activator.CreateInstance(f.Request)!; var p = new CanonicalStreamTypeChangeProbe { Request = request }; var collection = new ServiceCollection(); var roles = new[] { "H", "Sync", "Async" };
        for (var i = 0; i < f.Services.Length; i++) { var type = f.Services[i]; var role = roles[i]; collection.AddTransient(type, _ => { p.Resolve(role); return Activator.CreateInstance(type, p)!; }); }
        using var provider = collection.BuildServiceProvider(); IAsyncEnumerable<long> stream;
        if (reference)
        {
            var bridge = Activator.CreateInstance(f.Bridge, provider)!; var start = f.Start.CreateDelegate(typeof(Func<,,>).MakeGenericType(f.Request, typeof(CancellationToken), typeof(IAsyncEnumerable<int>)), bridge);
            stream = (IAsyncEnumerable<long>)typeof(CanonicalStreamTypeChangingReference).GetMethod("MapAsyncMap")!
                .MakeGenericMethod(f.Request, typeof(int), typeof(string), typeof(long), f.Services[1], f.Services[2]).Invoke(null, [provider, request, start, default(CancellationToken)])!;
        }
        else stream = (IAsyncEnumerable<long>)f.Create.Invoke(null, [provider, request, default(CancellationToken)])!;
        var cursor = stream.GetAsyncEnumerator(); Assert.Empty(p.Trace); var move = cursor.MoveNextAsync().AsTask(); await p.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var pending = !move.IsCompleted; Assert.True(pending); Assert.Equal(0, p.Gate.Consumptions); Assert.Equal(1, p.Reads);
        p.Trace.Add("Release:70"); p.Gate.Complete(70); Assert.True(await move.WaitAsync(TimeSpan.FromSeconds(5))); var values = new[] { cursor.Current }; _ = cursor.Current; Assert.Equal(1, p.Reads);
        Assert.False(await cursor.MoveNextAsync()); await cursor.DisposeAsync(); await cursor.DisposeAsync();
        return new(values, [.. p.Trace], p.Starts, p.Moves, p.Reads, p.Disposals, p.SyncCalls, p.AsyncCalls, p.Gate.Consumptions, pending);
    }
    private static void InspectTypeChangeIL(Type helper)
    {
        var cursor = Assert.Single(helper.GetNestedTypes(BindingFlags.NonPublic), t => t.Name.StartsWith("Cursor", StringComparison.Ordinal));
        Assert.Equal(typeof(long), cursor.GetField("_current", BindingFlags.NonPublic | BindingFlags.Instance)!.FieldType);
        Assert.Equal(typeof(IAsyncEnumerator<int>), cursor.GetField("_source", BindingFlags.NonPublic | BindingFlags.Instance)!.FieldType);
        var codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)!).ToDictionary(c => unchecked((ushort)c.Value));
        var inspected = new List<string>(); var itemBoxing = new List<string>();
        foreach (var type in new[] { cursor }.Concat(cursor.GetNestedTypes(BindingFlags.NonPublic)))
            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                var body = method.GetMethodBody()?.GetILAsByteArray(); if (body == null) continue; inspected.Add(type.FullName + "::" + method.Name);
                for (var i = 0; i < body.Length;)
                {
                    var at = i; ushort code = body[i++]; if (code == 0xFE) code = (ushort)(0xFE00 | body[i++]); var op = codes[code];
                    var size = op.OperandType switch
                    {
                        OperandType.InlineNone => 0,
                        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                        OperandType.InlineVar => 2,
                        OperandType.InlineI8 or OperandType.InlineR => 8,
                        OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(body, i),
                        _ => 4
                    };
                    if (op == OpCodes.Box || op == OpCodes.Unbox_Any)
                    {
                        var boxed = method.Module.ResolveType(BitConverter.ToInt32(body, i), type.GetGenericArguments(), Type.EmptyTypes);
                        if (boxed == typeof(int) || boxed == typeof(long) || boxed == typeof(string)) itemBoxing.Add(type.FullName + "::" + method.Name + ":" + at);
                    }
                    i += size; Assert.True(i <= body.Length);
                }
                Assert.DoesNotContain(type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly),
                    f => (f.Name.StartsWith("<item", StringComparison.Ordinal) || f.Name == "_current") && f.FieldType == typeof(object));
            }
        Assert.NotEmpty(inspected); Assert.Empty(itemBoxing); SaveTypeChange("il.json", new { inspected, itemBoxing, currentType = "System.Int64", currentBytes = sizeof(long), sourceItem = "System.Int32", scope = "selected generated cursor and its compiler state machines;DI receiver casts excluded" });
    }
    private static string? TypeChangeFolder(string label)
    {
        var root = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR"); if (string.IsNullOrEmpty(root)) return null;
        var folder = Path.Combine(root, "type-change", label); Directory.CreateDirectory(folder); return folder;
    }
    private static void SaveTypeChange(string name, object data)
    { var folder = TypeChangeFolder("results"); if (folder != null) File.WriteAllText(Path.Combine(folder, name), System.Text.Json.JsonSerializer.Serialize(data)); }
}
