using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Zendiator.SourceGenerator;

namespace Zendiator.Generator.Tests;

public sealed class StreamValueRequestProbe
{
    public readonly StreamGenericScopeProbe Owner = new();
    public readonly List<(string Role, int Marker, object Shared, int Version, long Padding)> Seen = [];
    public void Create(object actor, string role) => Owner.Create(actor, role);
    public void Call(object actor) => Owner.Call(actor);
    public void Release(object actor) => Owner.Release(actor);
    public IAsyncEnumerable<T> Source<T>(T value) => Owner.Source(value);
    public void Record(object actor, int marker, object shared, int version, long padding) =>
        Seen.Add((Owner.Created.Single(c => ReferenceEquals(c.Instance, actor)).Role, marker, shared, version, padding));
}

public sealed partial class GeneratorTests
{
    private static string ValueRequestSource(bool value, bool open, string item, bool wrapper)
    {
        var kind = value ? "struct" : "sealed class";
        var request = $$"""
            public {{kind}} GenericItems<T> : IStreamRequest<T> {
                public T Value; public int Marker; public ValueReference Shared;
                public long Pad0,Pad1,Pad2,Pad3,Pad4,Pad5;
                public GenericItems(T value){Value=value;Marker=7;Shared=new();Pad0=11;Pad1=Pad2=Pad3=Pad4=Pad5=0;}
            }
            public sealed class ValueReference { public int Version; }
            """;
        var types = StreamGenericTypes.Replace("public sealed record GenericItems<T>(T Value) : IStreamRequest<T>;", request)
            .Replace("global::Zendiator.Generator.Tests.StreamGenericScopeProbe", "global::Zendiator.Generator.Tests.StreamValueRequestProbe")
            .Replace("p.Call(this);return p.Source(r.Value);", "p.Call(this);p.Record(this,r.Marker,r.Shared,r.Shared.Version,r.Pad0);r.Marker=107;r.Shared.Version++;return p.Source(r.Value);")
            .Replace("p.Call(this);return true;", "p.Call(this);p.Record(this,r.Marker,r.Shared,r.Shared.Version,r.Pad0);r.Marker=108;return true;")
            .Replace("p.Call(this);return item;", "p.Call(this);p.Record(this,r.Marker,r.Shared,r.Shared.Version,r.Pad0);r.Marker=109;return item;")
            .Replace("p.Call(this);return n.Open(r,c);", "p.Call(this);p.Record(this,r.Marker,r.Shared,r.Shared.Version,r.Pad0);r.Marker+=90;return n.Open(r,c);");
        var input = open ? "GenericItems<>" : "GenericItems<" + item + ">";
        var body = "c.Handlers(typeof(" + input + "),DispatchFamily.Stream,typeof(GenericHandler<>));c.Pipeline(typeof(" + input + "),DispatchFamily.Stream," +
            "Stage.Hook(typeof(GenericFirst<>))," + (wrapper ? "Stage.Wrapper(typeof(GenericWrapper<>))" : "Stage.Hook(typeof(GenericMap<>))") +
            ",Stage.Hook(typeof(GenericLast<>)));";
        return ListFixture(body) + types;
    }
    private sealed record ValueRequestFixture(Assembly Assembly, Type Helper, bool Open, bool Value, bool Wrapper, CanonicalStreamConnectionKind Kind);
    private static readonly Dictionary<string, ValueRequestFixture> ValueRequestFixtures = [];
    private static ValueRequestFixture ValueRequestFixtureFor(bool value, bool open, string item, bool wrapper = false)
    {
        var key = value + "-" + open + "-" + (open ? "T" : item) + "-" + wrapper;
        lock (ValueRequestFixtures)
        {
            if (ValueRequestFixtures.TryGetValue(key, out var cached)) return cached;
            var source = ValueRequestSource(value, open, item, wrapper);
            var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "StreamValue_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
            using var bytes = new MemoryStream(); var emitted = output.Emit(bytes);
            var folder = ValueRequestEvidenceFolder(key);
            if (folder != null)
            {
                File.WriteAllText(Path.Combine(folder, "input.cs"), source); File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
                foreach (var file in driver.GetRunResult().Results.Single().GeneratedSources) File.WriteAllText(Path.Combine(folder, file.HintName), file.SourceText.ToString());
                File.WriteAllText(Path.Combine(folder, "compile.json"), System.Text.Json.JsonSerializer.Serialize(new { emitted.Success,
                    diagnostics = diagnostics.Select(d => new { d.Id, message = d.GetMessage() }), compiler = emitted.Diagnostics.Select(d => new { d.Id, message = d.GetMessage() }) }));
            }
            Assert.Equal("ZEN0037", Assert.Single(diagnostics).Id);
            var connection = Assert.Single(StreamConnections(driver).Routes);
            var expected = !open && item == "int" && !wrapper ? CanonicalStreamConnectionKind.Candidate : CanonicalStreamConnectionKind.CanonicalCandidate;
            Assert.Equal(expected, connection.Kind);
            Assert.Equal(value, Assert.Single(Selection(driver).Pipelines).Handlers[0].StreamEmission!.RequestIsValueType);
            Assert.DoesNotContain(driver.GetRunResult().Results.Single().GeneratedSources, f => f.HintName.StartsWith("Zendiator.Route.", StringComparison.Ordinal) ||
                f.HintName.StartsWith("Zendiator.Composition.", StringComparison.Ordinal) || f.HintName.Contains("Interceptors", StringComparison.Ordinal));
            Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
            Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
            var generated = Assert.Single(driver.GetRunResult().Results.Single().GeneratedSources, f => f.HintName == (connection.Candidate?.HintName ?? connection.Canonical!.HintName)).SourceText.ToString();
            Assert.Equal(!value, generated.Contains("ThrowIfNull(request)", StringComparison.Ordinal));
            bytes.Position = 0; var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
            var helper = Assert.Single(assembly.GetTypes(), t => t.Name.StartsWith(expected == CanonicalStreamConnectionKind.Candidate ? "__ZendiatorStreamPrototype_" : "__ZendiatorStreamCanonical_", StringComparison.Ordinal));
            var fixture = new ValueRequestFixture(assembly, helper, open, value, wrapper, expected);
            var inventory = ValueRequestIl(fixture);
            if (folder != null) File.WriteAllText(Path.Combine(folder, "request-il.json"), System.Text.Json.JsonSerializer.Serialize(inventory));
            Assert.Empty(inventory.Boxes); Assert.NotEmpty(inventory.TypedFields); Assert.NotEmpty(inventory.TypedCalls);
            ValueRequestFixtures.Add(key, fixture); return fixture;
        }
    }
    private static string? ValueRequestEvidenceFolder(string key)
    {
        var root = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        if (string.IsNullOrEmpty(root)) return null;
        var folder = Path.Combine(root, "stream-value-request", key); Directory.CreateDirectory(folder); return folder;
    }
    private sealed record RequestIlInventory(string Scope, string[] Boxes, string[] TypedFields, string[] TypedCalls);
    private static RequestIlInventory ValueRequestIl(ValueRequestFixture fixture)
    {
        // Inspect generated framework types and compiler descendants only. Reflection/harness and user bodies are outside this claim.
        var codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.FieldType == typeof(OpCode))
            .Select(f => (OpCode)f.GetValue(null)!).ToDictionary(o => unchecked((ushort)o.Value));
        var boxes = new List<string>(); var fields = new List<string>(); var calls = new List<string>();
        var request = fixture.Assembly.GetType("App.GenericItems`1")!;
        bool Request(Type type) => type.IsGenericType && type.GetGenericTypeDefinition() == request;
        foreach (var type in fixture.Assembly.GetTypes().Where(t =>
        {
            var owner = t; while (owner.DeclaringType != null) owner = owner.DeclaringType;
            return owner == fixture.Helper;
        }))
        {
            fields.AddRange(type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(f => Request(f.FieldType)).Select(f => type.FullName + "::" + f.Name + ":" + f.FieldType));
            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Cast<MethodBase>()
                .Concat(type.GetConstructors(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)))
            {
                var body = method.GetMethodBody()?.GetILAsByteArray(); if (body == null) continue;
                var context = type.GetGenericArguments(); var parameters = method is MethodInfo { IsGenericMethod: true } m ? m.GetGenericArguments() : Type.EmptyTypes;
                for (var i = 0; i < body.Length;)
                {
                    var at = i; ushort code = body[i++]; if (code == 0xFE) code = (ushort)(0xFE00 | body[i++]); var op = codes[code];
                    var size = op.OperandType switch {
                        OperandType.InlineNone => 0,
                        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                        OperandType.InlineVar => 2,
                        OperandType.InlineI8 or OperandType.InlineR => 8,
                        OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(body, i),
                        _ => 4
                    };
                    if (op == OpCodes.Box && Request(method.Module.ResolveType(BitConverter.ToInt32(body, i), context, parameters)))
                        boxes.Add(type.FullName + "::" + method.Name + ":" + at);
                    if (op == OpCodes.Call || op == OpCodes.Callvirt || op == OpCodes.Newobj)
                    {
                        var called = method.Module.ResolveMethod(BitConverter.ToInt32(body, i), context, parameters)!;
                        if (called.GetParameters().Any(p => Request(p.ParameterType))) calls.Add(type.FullName + "::" + method.Name + ":" + at + "->" + called);
                    }
                    i += size; Assert.True(i <= body.Length);
                }
            }
        }
        return new("generated candidate/compiler descendants;request-specific box opcode and typed storage/calls;not JIT copy elision or total allocations", boxes.ToArray(), fields.Distinct().ToArray(), calls.ToArray());
    }
    private static int ValueRequestSize<T>() => Unsafe.SizeOf<T>();
    private static async Task DrainValueRequest<T>(object stream, object expected, StreamValueRequestProbe p, bool early)
    {
        await using var cursor = ((IAsyncEnumerable<T>)stream).GetAsyncEnumerator();
        var move = cursor.MoveNextAsync();
        if (p.Owner.PendingSource)
        {
            Assert.False(move.IsCompleted); Assert.Single(p.Seen); Assert.Equal("H", p.Seen[0].Role);
            p.Owner.PendingSource = false; p.Owner.PendingGate.Complete(true);
        }
        Assert.True(await move); Assert.Equal((T)expected, cursor.Current);
        if (!early) Assert.False(await cursor.MoveNextAsync());
    }
    private static async Task<string> ObserveValueRequest(ValueRequestFixture f, string itemName, bool reference, bool pending)
    {
        var item = itemName == "int" ? typeof(int) : itemName == "long" ? typeof(long) : typeof(string);
        var requestType = f.Assembly.GetType("App.GenericItems`1")!.MakeGenericType(item);
        var services = new[] { "GenericHandler", "GenericFirst", f.Wrapper ? "GenericWrapper" : "GenericMap", "GenericLast" }
            .Select(n => f.Assembly.GetType("App." + n + "`1")!.MakeGenericType(item)).ToArray();
        var helper = f.Open ? f.Helper.MakeGenericType(item) : f.Helper;
        object value = item == typeof(string) ? "seed" : Convert.ChangeType(7, item);
        var request = Activator.CreateInstance(requestType, [value])!; var shared = requestType.GetField("Shared")!.GetValue(request)!;
        var p = new StreamValueRequestProbe(); p.Owner.PendingSource = pending;
        IServiceCollection collection = new ServiceCollection(); collection.AddSingleton(p);
        foreach (var service in services) collection.AddScoped(service, provider => { p.Owner.FactoryCalls++; return Activator.CreateInstance(service, [provider.GetRequiredService<StreamValueRequestProbe>()])!; });
        using var provider = collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }); using var scope = provider.CreateScope();
        var stream = reference ? typeof(GeneratorTests).GetMethod(f.Wrapper ? nameof(OpenWrapperReference) : nameof(OpenItemReference), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod([requestType, item, .. services]).Invoke(null, [scope.ServiceProvider, request])! :
            helper.GetMethod("Create", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [scope.ServiceProvider, request, default(CancellationToken)])!;
        Assert.Empty(p.Owner.Created);
        // Boxing here belongs to Activator/reflection harness. Mutate its caller copy after framework Create has captured the typed value.
        requestType.GetField("Marker")!.SetValue(request, 999); requestType.GetField("Pad0")!.SetValue(request, 777L);
        var drain = typeof(GeneratorTests).GetMethod(nameof(DrainValueRequest), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(item);
        var drains = f.Wrapper ? 1 : 2;
        for (var i = 0; i < drains; i++) await ((Task)drain.Invoke(null, [stream, value, p, f.Wrapper])!).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(4, p.Owner.FactoryCalls); Assert.Equal(drains, p.Owner.SourceDisposals); Assert.Empty(p.Owner.Released);
        Assert.Equal(pending ? 1 : 0, p.Owner.PendingGate.Consumptions);
        Assert.Equal(drains * 4, p.Seen.Count);
        for (var i = 0; i < drains; i++)
        {
            var seen = p.Seen.Skip(i * 4).Take(4).ToArray();
            Assert.Equal(f.Wrapper ? new[] { "W", "H", "F1", "F2" } : new[] { "H", "F1", "M", "F2" }, seen.Select(s => s.Role));
            Assert.Equal(f.Wrapper ? new[] { 7, 97, 97, 7 } : new[] { 7, 7, 7, 7 }, seen.Select(s => s.Marker));
            Assert.Equal(new[] { i, i + (f.Wrapper ? 0 : 1), i + 1, i + 1 }, seen.Select(s => s.Version));
            Assert.All(seen, s => { Assert.Same(shared, s.Shared); Assert.Equal(11L, s.Padding); });
        }
        scope.Dispose(); Assert.Equal(4, p.Owner.Released.Count);
        var size = (int)typeof(GeneratorTests).GetMethod(nameof(ValueRequestSize), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(requestType).Invoke(null, null)!;
        Assert.True(size >= 64);
        return System.Text.Json.JsonSerializer.Serialize(new { size, drains, p.Owner.FactoryCalls, p.Owner.SourceDisposals, released = p.Owner.Released.Count,
            seen = p.Seen.Select(s => new { s.Role, s.Marker, s.Version, s.Padding, sameShared = ReferenceEquals(shared, s.Shared) }), gateConsumptions = p.Owner.PendingGate.Consumptions });
    }
    [Theory]
    [InlineData(false, "int", false)] [InlineData(false, "long", false)]
    [InlineData(true, "int", false)] [InlineData(true, "string", false)] [InlineData(false, "int", true)]
    public async Task Stream_value_request_typed_storage_copy_replay_and_wrapper_match_reference(bool open, string item, bool wrapper)
    {
        var f = ValueRequestFixtureFor(true, open, item, wrapper); var pending = item == "long";
        var reference = await ObserveValueRequest(f, item, true, pending); var generated = await ObserveValueRequest(f, item, false, pending);
        Assert.Equal(reference, generated);
        var folder = ValueRequestEvidenceFolder("runtime-" + open + "-" + item + "-" + wrapper);
        if (folder != null) File.WriteAllText(Path.Combine(folder, "result.json"), System.Text.Json.JsonSerializer.Serialize(new { reference, generated, candidateOnly = true, productionEnabled = false }));
    }
    [Fact]
    public async Task Stream_value_request_default_is_captured_before_disposed_scope_resolution()
    {
        var f = ValueRequestFixtureFor(true, false, "int"); var request = Activator.CreateInstance(f.Assembly.GetType("App.GenericItems`1")!.MakeGenericType(typeof(int)))!;
        using var provider = new ServiceCollection().BuildServiceProvider(); var scope = provider.CreateScope();
        var stream = (IAsyncEnumerable<int>)f.Helper.GetMethod("Create", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [scope.ServiceProvider, request, default(CancellationToken)])!;
        scope.Dispose(); await using var cursor = stream.GetAsyncEnumerator();
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await cursor.MoveNextAsync());
        var folder = ValueRequestEvidenceFolder("default-disposed");
        if (folder != null) File.WriteAllText(Path.Combine(folder, "result.json"), System.Text.Json.JsonSerializer.Serialize(new { valueDefaultCreateSucceeded = true, failedAtDI = true, candidateOnly = true }));
    }
    [Theory]
    [InlineData("int")] [InlineData("long")]
    public void Stream_value_request_change_preserves_reference_request_null_timing(string item)
    {
        var f = ValueRequestFixtureFor(false, false, item); using var provider = new ServiceCollection().BuildServiceProvider();
        var failure = Assert.Throws<TargetInvocationException>(() => f.Helper.GetMethod("Create", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [provider, null, default(CancellationToken)]));
        Assert.Equal("request", Assert.IsType<ArgumentNullException>(failure.InnerException).ParamName);
        var folder = ValueRequestEvidenceFolder("reference-null-" + item);
        if (folder != null) File.WriteAllText(Path.Combine(folder, "result.json"), System.Text.Json.JsonSerializer.Serialize(new { synchronousArgumentNull = true, paramName = "request", candidateOnly = true }));
    }
}
