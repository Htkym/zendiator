using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Zendiator.SourceGenerator;
using Zendiator.Tests.Contracts;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private static readonly string[] TypedIntegrationInputs = ["open-session", "large-wrapper-session", "factory-undetermined", "constraint-conflict", "interval-mismatch"];
    private sealed record TypedOwnedFixture(bool Open, Assembly Assembly, Type Request, Type SourceItem, Type FinalItem, Type[] Receivers, Type[] Factories, Type[] FactoryDefinitions, MethodInfo Create, Type Helper);
    private sealed record TypedOwnedResult(long[] Values, string[] Events, string[] Created, int Starts, int Moves, int Reads, int SourceDisposals, int SessionDisposals, int FactoryDisposals, int Enumerations);
    private static string TypedIntegrationSource(string id)
    {
        var open = id is "open-session" or "factory-undetermined" or "constraint-conflict";
        if (open)
        {
            var source = ListFixture("c.Handlers(typeof(OpenRequest<>),DispatchFamily.Stream,HandlerEntry.Session(typeof(OpenHandler<>),typeof(OpenHandlerFactory<>)));" +
                "c.StreamPipeline(typeof(OpenRequest<>),typeof(OpenFinal<>),Stage.Hook(typeof(OpenMap<,>),typeof(OpenMapFactory<,>)));") + """
                public sealed class OpenPayload { public long Value {get;}=7; }
                public sealed record OpenRequest<T>(T Value):IStreamRequest<T>;
                public sealed record OpenFinal<T>(T Value);
                [ZendiatorHandler] public sealed class OpenHandler<T>(global::Zendiator.Generator.Tests.CanonicalStreamTypedIntegrationProbe p,int id):IStreamRequestHandler<OpenRequest<T>,T>,IAsyncDisposable where T:class,new() {
                    public System.Collections.Generic.IAsyncEnumerable<T> HandleAsync(OpenRequest<T> r,CancellationToken c)=>p.Start(r,r.Value,c);
                    public ValueTask DisposeAsync()=>p.DisposeSession("H",id);
                }
                public sealed class OpenMap<R,T>(global::Zendiator.Generator.Tests.CanonicalStreamTypedIntegrationProbe p,int id):IStreamItemMap<R,T,OpenFinal<T>>,IAsyncDisposable {
                    public OpenFinal<T> Map(R r,T item,CancellationToken c)=>new(p.MapOpen(r!,item,c));
                    public ValueTask DisposeAsync()=>p.DisposeSession("M",id);
                }
                public sealed class OpenHandlerFactory<T>(global::Zendiator.Generator.Tests.CanonicalStreamTypedIntegrationProbe p):IOwnedSessionFactory<OpenRequest<T>,OpenHandler<T>>,IDisposable where T:class,new() {
                    public OpenHandler<T> Create(OpenRequest<T> r,CancellationToken c)=>p.Create("H",r,c,id=>new OpenHandler<T>(p,id));
                    public void Dispose()=>p.DisposeFactory("H");
                }
                public sealed class OpenMapFactory<R,T>(global::Zendiator.Generator.Tests.CanonicalStreamTypedIntegrationProbe p):IOwnedSessionFactory<R,OpenMap<R,T>> {
                    public OpenMap<R,T> Create(R r,CancellationToken c)=>p.Create("M",r!,c,id=>new OpenMap<R,T>(p,id));
                }
                """;
            // The normal factory is disposable; the two rejection mutations remain legal C# consumers.
            source = source.Replace("IOwnedSessionFactory<R,OpenMap<R,T>> {", "IOwnedSessionFactory<R,OpenMap<R,T>>,IDisposable {")
                .Replace("public OpenMap<R,T> Create(R r", "public void Dispose()=>p.DisposeFactory(\"M\"); public OpenMap<R,T> Create(R r");
            if (id == "factory-undetermined") source = source.Replace("OpenMapFactory<R,T>", "OpenMapFactory<R,T,X>").Replace("typeof(OpenMapFactory<,>)", "typeof(OpenMapFactory<,,>)");
            if (id == "constraint-conflict") source = source.Replace("IOwnedSessionFactory<R,OpenMap<R,T>>,IDisposable {", "IOwnedSessionFactory<R,OpenMap<R,T>>,IDisposable where T:struct {");
            return source;
        }
        var item = id == "interval-mismatch" ? "int" : "LargeItem";
        return ListFixture("c.Handlers<BigRequest>(DispatchFamily.Stream,HandlerEntry.Session<BigHandler,BigHandlerFactory>());" +
            "c.StreamPipeline<BigRequest,LargeItem>(Stage.Hook<ToLarge,ToLargeFactory>(),Stage.Wrapper<BigWrapper,BigWrapperFactory>(),Stage.Hook<LastLarge,LastLargeFactory>());") + """
            public sealed record BigRequest:IStreamRequest<int>;
            [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential,Size=128)]
            public readonly struct LargeItem(long value){ public long Value {get;}=value; }
            [ZendiatorHandler] public sealed class BigHandler(global::Zendiator.Generator.Tests.CanonicalStreamTypedIntegrationProbe p,int id):IStreamRequestHandler<BigRequest,int>,IAsyncDisposable {
                public System.Collections.Generic.IAsyncEnumerable<int> HandleAsync(BigRequest r,CancellationToken c)=>p.Start(r,7,c);
                public ValueTask DisposeAsync()=>p.DisposeSession("H",id);
            }
            public sealed class ToLarge(global::Zendiator.Generator.Tests.CanonicalStreamTypedIntegrationProbe p,int id):IStreamItemMap<BigRequest,int,LargeItem>,IAsyncDisposable {
                public LargeItem Map(BigRequest r,int item,CancellationToken c)=>new(p.MapLarge(r,item,c));
                public ValueTask DisposeAsync()=>p.DisposeSession("A",id);
            }
            public sealed class LastLarge(global::Zendiator.Generator.Tests.CanonicalStreamTypedIntegrationProbe p,int id):IAsyncStreamItemMap<BigRequest,LargeItem,LargeItem>,IAsyncDisposable {
                public ValueTask<LargeItem> MapAsync(BigRequest r,LargeItem item,CancellationToken c)=>new(new LargeItem(p.MapFinal(r,item.Value,c)));
                public ValueTask DisposeAsync()=>p.DisposeSession("B",id);
            }
            """ + "public sealed class BigWrapper(global::Zendiator.Generator.Tests.CanonicalStreamTypedIntegrationProbe p,int id):IStreamWrapper<BigRequest," + item + ">,IAsyncDisposable {" +
            "public System.Collections.Generic.IAsyncEnumerable<" + item + "> Wrap<N>(BigRequest r,N n,CancellationToken c) where N:IStreamUpstream<BigRequest," + item + "> {p.Wrap(r,c);return n.Open(r,c);}" +
            "public ValueTask DisposeAsync()=>p.DisposeSession(\"W\",id);}" +
            string.Concat(new[] { ("H", "BigHandler", "BigHandlerFactory"), ("A", "ToLarge", "ToLargeFactory"), ("W", "BigWrapper", "BigWrapperFactory"), ("B", "LastLarge", "LastLargeFactory") }.Select(x =>
                "public sealed class " + x.Item3 + "(global::Zendiator.Generator.Tests.CanonicalStreamTypedIntegrationProbe p):IOwnedSessionFactory<BigRequest," + x.Item2 + ">,IDisposable {" +
                "public " + x.Item2 + " Create(BigRequest r,CancellationToken c)=>p.Create(\"" + x.Item1 + "\",r,c,id=>new " + x.Item2 + "(p,id)); public void Dispose()=>p.DisposeFactory(\"" + x.Item1 + "\");}"));
    }

    [Fact]
    public async Task Stream_typed_generic_session_and_large_wrapper_routes_preserve_ports_owners_and_item_IL()
    {
        var fixtures = new List<(string Id, TypedOwnedFixture Fixture)>();
        foreach (var id in TypedIntegrationInputs)
        {
            var source = TypedIntegrationSource(id); var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "TypedIntegration_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
            var route = Assert.Single(Selection(driver).Pipelines); var generated = driver.GetRunResult().Results.Single().GeneratedSources; using var bytes = new MemoryStream(); var emitted = output.Emit(bytes);
            var folder = TypedIntegrationFolder(id);
            if (folder != null)
            {
                File.WriteAllText(Path.Combine(folder, "input.cs"), source); File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
                foreach (var file in generated) File.WriteAllText(Path.Combine(folder, file.HintName), file.SourceText.ToString());
                File.WriteAllText(Path.Combine(folder, "binding.json"), System.Text.Json.JsonSerializer.Serialize(new
                {
                    id,
                    emitted.Success,
                    generic = route.StreamGeneric,
                    flow = route.StreamItemFlow,
                    handlerSessions = route.Handlers.Select(h => h.Sessions),
                    stageSessions = route.Stages.Select(s => s.Sessions),
                    diagnostics = diagnostics.Select(d => new { d.Id, severity = d.Severity.ToString(), inSource = d.Location.IsInSource, anchor = d.Location.IsInSource ? d.Location.SourceTree!.GetText().ToString(d.Location.SourceSpan) : null }),
                    compiler = emitted.Diagnostics.Select(d => new { d.Id, severity = d.Severity.ToString(), message = d.GetMessage() }),
                    productionEnabled = false
                }));
            }
            Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error)); Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
            if (id is "factory-undetermined" or "constraint-conflict" or "interval-mismatch")
            {
                var expected = id == "interval-mismatch" ? "ZEN0026" : "ZEN0035"; var anchor = id == "interval-mismatch" ? "Stage.Wrapper" : "Stage.Hook";
                Assert.Contains(diagnostics, d => d.Id == expected && d.Severity == DiagnosticSeverity.Error && d.Location.IsInSource &&
                    d.Location.SourceTree!.GetText().ToString(d.Location.SourceSpan).StartsWith(anchor, StringComparison.Ordinal));
                Assert.DoesNotContain(generated, s => s.HintName.StartsWith("Zendiator.Stream", StringComparison.Ordinal)); continue;
            }
            Assert.Empty(diagnostics);
            Assert.True(Graph(driver).IsValid);
            var flow = Assert.IsType<BoundStreamItemFlow>(route.StreamItemFlow); Assert.Equal(route.Result, flow.Final.Identity);
            Assert.Equal(flow.Stages[flow.Stages.Count - 1].Output.Identity, flow.Final.Identity);
            Assert.Single(generated, s => s.HintName.StartsWith("Zendiator.StreamCanonical.", StringComparison.Ordinal));
            bytes.Position = 0; var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes); var open = id == "open-session";
            var helper = Assert.Single(assembly.GetTypes(), t => t.Name.StartsWith("__ZendiatorStreamCanonical_", StringComparison.Ordinal));
            Type request, input, final; Type[] receivers, factories, definitions;
            if (open)
            {
                Assert.Equal(" where __ZendiatorType0 : class, new()", route.StreamGeneric!.TypeConstraints);
                input = assembly.GetType("App.OpenPayload")!; request = assembly.GetType("App.OpenRequest`1")!.MakeGenericType(input); final = assembly.GetType("App.OpenFinal`1")!.MakeGenericType(input);
                receivers = [assembly.GetType("App.OpenHandler`1")!.MakeGenericType(input), assembly.GetType("App.OpenMap`2")!.MakeGenericType(request, input)];
                definitions = [assembly.GetType("App.OpenHandlerFactory`1")!, assembly.GetType("App.OpenMapFactory`2")!];
                factories = [definitions[0].MakeGenericType(input), definitions[1].MakeGenericType(request, input)]; helper = helper.MakeGenericType(input);
                Assert.Contains("OpenFinal<__ZendiatorType0>", flow.Final.Name);
            }
            else
            {
                input = typeof(int); request = assembly.GetType("App.BigRequest")!; final = assembly.GetType("App.LargeItem")!;
                receivers = new[] { "BigHandler", "ToLarge", "BigWrapper", "LastLarge" }.Select(n => assembly.GetType("App." + n)!).ToArray();
                factories = new[] { "BigHandlerFactory", "ToLargeFactory", "BigWrapperFactory", "LastLargeFactory" }.Select(n => assembly.GetType("App." + n)!).ToArray(); definitions = factories;
                Assert.Equal("int", flow.Source.Name); Assert.All(flow.Stages.Skip(1), s => Assert.Contains("LargeItem", s.Input.Name)); Assert.Equal(128, Marshal.SizeOf(final));
            }
            var f = new TypedOwnedFixture(open, assembly, request, input, final, receivers, factories, definitions, helper.GetMethod("Create", BindingFlags.NonPublic | BindingFlags.Static)!, helper);
            InspectTypedOwnedIL(id, f); fixtures.Add((id, f));
        }
        Assert.Equal(2, fixtures.Count);
        foreach (var (id, fixture) in fixtures)
        {
            var reference = await RunTypedOwned(fixture, true); var canonical = await RunTypedOwned(fixture, false);
            SaveTypedIntegration(id + "/comparison.json", new { reference, canonical, productionEnabled = false });
            Assert.Equal(reference.Values, canonical.Values); Assert.Equal(reference.Events, canonical.Events); Assert.Equal(reference.Created, canonical.Created);
            Assert.Equal(reference with { Values = canonical.Values, Events = canonical.Events, Created = canonical.Created }, canonical);
            Assert.Equal(new long[] { fixture.Open ? 7 : 701 }, canonical.Values); Assert.Equal(fixture.Open ? 2 : 4, canonical.SessionDisposals); Assert.Equal(fixture.Open ? 2 : 4, canonical.FactoryDisposals);
            Assert.Equal(1, canonical.Starts); Assert.Equal(2, canonical.Moves); Assert.Equal(1, canonical.Reads); Assert.Equal(1, canonical.SourceDisposals);
        }
    }

    private static async Task<TypedOwnedResult> RunTypedOwned(TypedOwnedFixture f, bool reference)
    {
        var payload = f.Open ? Activator.CreateInstance(f.SourceItem) : null;
        var request = Activator.CreateInstance(f.Request, f.Open ? new[] { payload } : Array.Empty<object>())!; var p = new CanonicalStreamTypedIntegrationProbe { Request = request };
        var services = new ServiceCollection();
        if (f.Open) services.AddSingleton(p);
        var roles = f.Open ? new[] { "H", "M" } : new[] { "H", "A", "W", "B" };
        for (var i = 0; i < f.Factories.Length; i++)
        {
            var closed = f.Factories[i]; var role = roles[i]; var definition = f.FactoryDefinitions[i];
            if (f.Open) services.AddScoped(definition);
            else services.AddScoped(closed, _ => { p.Resolve(role); return Activator.CreateInstance(closed, p)!; });
        }
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope(); object stream;
        if (reference)
        {
            var method = typeof(CanonicalStreamTypedOwnedReference).GetMethod(f.Open ? "Map" : "MapWrapperMap")!;
            var arguments = f.Open ? new[] { f.Request, f.SourceItem, f.FinalItem }.Concat(f.Receivers).Concat(f.Factories).ToArray()
                : new[] { f.Request, f.SourceItem, f.FinalItem, f.FinalItem }.Concat(f.Receivers).Concat(f.Factories).ToArray();
            stream = method.MakeGenericMethod(arguments).Invoke(null, [scope.ServiceProvider, request])!;
        }
        else stream = f.Create.Invoke(null, [scope.ServiceProvider, request, default(CancellationToken)])!;
        Assert.Empty(p.Events);
        var values = (long[])await (Task<long[]>)typeof(GeneratorTests).GetMethod(nameof(EnumerateTypedOwned), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(f.FinalItem).Invoke(null, [stream, f.Open, payload!])!;
        Assert.Equal(0, p.FactoryDisposals); Assert.Equal(p.Instances.Count, p.Instances.Distinct(ReferenceEqualityComparer.Instance).Count());
        var expected = f.Open ? new[] { "M:1", "H:2" } : new[] { "B:1", "W:2", "A:3", "H:4" };
        Assert.Equal(expected, p.Created.Select(x => x.Role + ":" + x.Id));
        Assert.Equal(f.Open ? new[] { "Session.Dispose:H:2", "Session.Dispose:M:1" } : new[] { "Session.Dispose:H:4", "Session.Dispose:A:3", "Session.Dispose:W:2", "Session.Dispose:B:1" }, p.Events.Where(e => e.StartsWith("Session.Dispose:", StringComparison.Ordinal)));
        var before = p.SessionDisposals; await scope.DisposeAsync(); Assert.Equal(before, p.SessionDisposals);
        return new(values, [.. p.Events], p.Created.Select(x => x.Role + ":" + x.Id).ToArray(), p.Starts, p.Moves, p.Reads, p.SourceDisposals, p.SessionDisposals, p.FactoryDisposals, f.Open ? 1 : 2);
    }
    private static async Task<long[]> EnumerateTypedOwned<T>(object stream, bool open, object? payload)
    {
        var cursor = ((IAsyncEnumerable<T>)stream).GetAsyncEnumerator(); Assert.True(await cursor.MoveNextAsync()); var item = cursor.Current;
        var value = typeof(T).GetProperty("Value")!.GetValue(item);
        if (open) { Assert.Same(payload, value); value = value!.GetType().GetProperty("Value")!.GetValue(value); }
        Assert.False(await cursor.MoveNextAsync()); await cursor.DisposeAsync(); await cursor.DisposeAsync(); return [Convert.ToInt64(value)];
    }
    private static void InspectTypedOwnedIL(string id, TypedOwnedFixture f)
    {
        var cursors = f.Helper.GetNestedTypes(BindingFlags.NonPublic).Where(t => t.Name.StartsWith("Cursor", StringComparison.Ordinal))
            .Select(t => t.ContainsGenericParameters ? t.MakeGenericType(f.Helper.GetGenericArguments()) : t).ToArray();
        Assert.Equal(f.Open ? 1 : 2, cursors.Length); var fields = new List<object>(); var methods = new List<string>(); var boxing = new List<string>();
        var codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Where(x => x.FieldType == typeof(OpCode)).Select(x => (OpCode)x.GetValue(null)!).ToDictionary(x => unchecked((ushort)x.Value));
        foreach (var cursor in cursors)
        {
            var current = cursor.GetField("_current", BindingFlags.Instance | BindingFlags.NonPublic)!.FieldType; Assert.Equal(f.FinalItem, current);
            var source = cursor.GetField("_source", BindingFlags.Instance | BindingFlags.NonPublic)!.FieldType.GetGenericArguments()[0];
            Assert.True(source == f.SourceItem || !f.Open && source == f.FinalItem);
            fields.Add(new { cursor = cursor.Name, current = current.ToString(), source = source.ToString(), currentBytes = current.IsValueType ? Marshal.SizeOf(current) : IntPtr.Size });
            foreach (var unbound in new[] { cursor }.Concat(cursor.GetNestedTypes(BindingFlags.NonPublic)))
            {
                var type = unbound.ContainsGenericParameters ? unbound.MakeGenericType(f.Helper.GetGenericArguments()) : unbound;
                Assert.DoesNotContain(type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public), x => (x.Name.StartsWith("<item", StringComparison.Ordinal) || x.Name == "_current") && x.FieldType == typeof(object));
                foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    var body = method.GetMethodBody()?.GetILAsByteArray(); if (body == null) continue; methods.Add(type.Name + "::" + method.Name);
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
                            var item = method.Module.ResolveType(BitConverter.ToInt32(body, i), type.GetGenericArguments(), Type.EmptyTypes);
                            if (item == f.SourceItem || item == f.FinalItem) boxing.Add(type.Name + "::" + method.Name + ":" + at);
                        }
                        i += size; Assert.True(i <= body.Length);
                    }
                }
            }
        }
        Assert.Empty(boxing); Assert.NotEmpty(methods); SaveTypedIntegration(id + "/il.json", new { fields, methods, boxing, scope = "selected generated cursors and compiler state machines;producer/test reflection/DI casts excluded;not total allocation or JIT-copy measurement" });
    }
    private static string? TypedIntegrationFolder(string name)
    {
        var root = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR"); if (string.IsNullOrEmpty(root)) return null;
        var folder = Path.Combine(root, "typed-integration", name); Directory.CreateDirectory(folder); return folder;
    }
    private static void SaveTypedIntegration(string name, object value)
    {
        var slash = name.LastIndexOf('/'); var folder = TypedIntegrationFolder(name[..slash]); if (folder != null) File.WriteAllText(Path.Combine(folder, name[(slash + 1)..]), System.Text.Json.JsonSerializer.Serialize(value));
    }
}
