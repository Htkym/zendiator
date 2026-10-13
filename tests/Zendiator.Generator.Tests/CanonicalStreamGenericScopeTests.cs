using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Zendiator.SourceGenerator;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed class StreamGenericScopeProbe
{
    public readonly List<(object Instance, string Role)> Created = [];
    public readonly List<object> Called = [], Released = [];
    public int SourceDisposals, FactoryCalls;
    internal readonly Zendiator.Tests.Contracts.ContractOnce<bool> PendingGate = new();
    public bool PendingSource;
    public void Create(object instance, string role) => Created.Add((instance, role));
    public void Call(object instance) { Assert.DoesNotContain(Released, r => ReferenceEquals(r, instance)); Called.Add(instance); }
    public void Release(object instance) { Assert.DoesNotContain(Released, r => ReferenceEquals(r, instance)); Released.Add(instance); }
    public IAsyncEnumerable<T> Source<T>(T value) => new Enumerable<T>(this, value);
    private sealed class Enumerable<T>(StreamGenericScopeProbe p, T value) : IAsyncEnumerable<T>
    {
        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken token = default) => new Cursor(p, value);
        private sealed class Cursor(StreamGenericScopeProbe p, T value) : IAsyncEnumerator<T>
        {
            private bool _moved, _disposed;
            public T Current => value;
            public ValueTask<bool> MoveNextAsync() { var result = !_moved; _moved = true; return p.PendingSource && result ? p.PendingGate.Task : new(result); }
            public ValueTask DisposeAsync() { Assert.False(_disposed); _disposed = true; p.SourceDisposals++; return default; }
        }
    }
}

public sealed partial class GeneratorTests
{
    private const string StreamGenericTypes = """
        public sealed record GenericItems<T>(T Value) : IStreamRequest<T>;
        [ZendiatorHandler] public sealed class GenericHandler<T> : IStreamRequestHandler<GenericItems<T>,T>, IDisposable {
            private readonly global::Zendiator.Generator.Tests.StreamGenericScopeProbe p;
            public GenericHandler(global::Zendiator.Generator.Tests.StreamGenericScopeProbe p){this.p=p;p.Create(this,"H");}
            public System.Collections.Generic.IAsyncEnumerable<T> HandleAsync(GenericItems<T> r,CancellationToken c) { p.Call(this);return p.Source(r.Value); }
            public void Dispose()=>p.Release(this);
        }
        public sealed class GenericFirst<T> : IStreamItemFilter<GenericItems<T>,T>, IDisposable {
            private readonly global::Zendiator.Generator.Tests.StreamGenericScopeProbe p;
            public GenericFirst(global::Zendiator.Generator.Tests.StreamGenericScopeProbe p){this.p=p;p.Create(this,"F1");}
            public bool Accept(GenericItems<T> r,T item,CancellationToken c){p.Call(this);return true;}
            public void Dispose()=>p.Release(this);
        }
        public sealed class GenericMap<T> : IStreamItemMap<GenericItems<T>,T>, IDisposable {
            private readonly global::Zendiator.Generator.Tests.StreamGenericScopeProbe p;
            public GenericMap(global::Zendiator.Generator.Tests.StreamGenericScopeProbe p){this.p=p;p.Create(this,"M");}
            public T Map(GenericItems<T> r,T item,CancellationToken c){p.Call(this);return item;}
            public void Dispose()=>p.Release(this);
        }
        public sealed class GenericLast<T> : IStreamItemFilter<GenericItems<T>,T>, IDisposable {
            private readonly global::Zendiator.Generator.Tests.StreamGenericScopeProbe p;
            public GenericLast(global::Zendiator.Generator.Tests.StreamGenericScopeProbe p){this.p=p;p.Create(this,"F2");}
            public bool Accept(GenericItems<T> r,T item,CancellationToken c){p.Call(this);return true;}
            public void Dispose()=>p.Release(this);
        }
        public sealed class GenericWrapper<T> : IStreamWrapper<GenericItems<T>,T>, IDisposable {
            private readonly global::Zendiator.Generator.Tests.StreamGenericScopeProbe p;
            public GenericWrapper(global::Zendiator.Generator.Tests.StreamGenericScopeProbe p){this.p=p;p.Create(this,"W");}
            public System.Collections.Generic.IAsyncEnumerable<T> Wrap<N>(GenericItems<T> r,N n,CancellationToken c) where N:IStreamUpstream<GenericItems<T>,T>{p.Call(this);return n.Open(r,c);}
            public void Dispose()=>p.Release(this);
        }
        """;
    private static string StreamGenericSource(string item, bool wrapper = false, bool local = false)
    {
        var request = "GenericItems<" + item + ">";
        var body = local ? "c.UseLocalHandlers();" : "c.Handlers<" + request + ">(DispatchFamily.Stream,typeof(GenericHandler<>));";
        body += "c.Pipeline<" + request + ">(" + (wrapper ?
            "Stage.Hook(typeof(GenericFirst<>)),Stage.Wrapper(typeof(GenericWrapper<>)),Stage.Hook(typeof(GenericLast<>))" :
            "Stage.Hook(typeof(GenericFirst<>)),Stage.Hook(typeof(GenericMap<>)),Stage.Hook(typeof(GenericLast<>))") + ");";
        return ListFixture(body) + StreamGenericTypes;
    }
    private sealed record StreamGenericFixture(Assembly Assembly, Type Request, Type Item, Type[] Services, MethodInfo Create, CanonicalStreamConnectionKind Kind);
    private static readonly Dictionary<string, StreamGenericFixture> StreamGenericFixtures = [];
    private static StreamGenericFixture StreamGenericFixtureFor(string item, bool wrapper = false, bool local = false)
    {
        var key = item + "-" + wrapper + "-" + local;
        lock (StreamGenericFixtures)
        {
            if (StreamGenericFixtures.TryGetValue(key, out var cached)) return cached;
            var source = StreamGenericSource(item, wrapper, local);
            var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "StreamGeneric_" + key + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
            Assert.Equal("ZEN0037", Assert.Single(diagnostics).Id);
            var connection = Assert.Single(StreamConnections(driver).Routes);
            var expected = item == "int" && !wrapper ? CanonicalStreamConnectionKind.Candidate : CanonicalStreamConnectionKind.CanonicalCandidate;
            Assert.Equal(expected, connection.Kind);
            var route = Assert.Single(Selection(driver).Pipelines);
            Assert.DoesNotContain("<T>", route.Handlers[0].Name); Assert.All(route.Stages, s => Assert.DoesNotContain("<T>", s.Name));
            Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
            using var bytes = new MemoryStream(); var emitted = output.Emit(bytes); Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
            var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
            if (!string.IsNullOrEmpty(evidence))
            {
                var folder = Path.Combine(evidence, "stream-generic", key); Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "input.cs"), source); File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
                foreach (var file in driver.GetRunResult().Results.Single().GeneratedSources) File.WriteAllText(Path.Combine(folder, file.HintName), file.SourceText.ToString());
                File.WriteAllText(Path.Combine(folder, "closure.json"), System.Text.Json.JsonSerializer.Serialize(new { emitted.Success, kind = connection.Kind.ToString(),
                    handlers = route.Handlers.Select(h => h.Name), stages = route.Stages.Select(s => s.Name), candidateOnly = true, productionEnabled = false }));
            }
            bytes.Position = 0; var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes); var t = item == "int" ? typeof(int) : item == "long" ? typeof(long) : typeof(string);
            var names = wrapper ? new[] { "GenericHandler`1", "GenericFirst`1", "GenericWrapper`1", "GenericLast`1" } : new[] { "GenericHandler`1", "GenericFirst`1", "GenericMap`1", "GenericLast`1" };
            var helper = Assert.Single(assembly.GetTypes(), c => c.Name.StartsWith(expected == CanonicalStreamConnectionKind.Candidate ? "__ZendiatorStreamPrototype_" : "__ZendiatorStreamCanonical_", StringComparison.Ordinal));
            var result = new StreamGenericFixture(assembly, assembly.GetType("App.GenericItems`1")!.MakeGenericType(t), t,
                names.Select(n => assembly.GetType("App." + n)!.MakeGenericType(t)).ToArray(), helper.GetMethod("Create", BindingFlags.NonPublic | BindingFlags.Static)!, expected);
            StreamGenericFixtures.Add(key, result); return result;
        }
    }
    private static void StreamGenericEvidence(string label, object result)
    {
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(evidence)) File.WriteAllText(Path.Combine(evidence, "stream-generic", "result-" + label + ".json"), System.Text.Json.JsonSerializer.Serialize(result));
    }
    private static async Task DrainGeneric(StreamGenericFixture f, IServiceProvider provider, object value)
    {
        var request = Activator.CreateInstance(f.Request, [value])!; var stream = f.Create.Invoke(null, [provider, request, default(CancellationToken)])!;
        var method = typeof(GeneratorTests).GetMethod(nameof(DrainGenericTyped), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(f.Item);
        await ((Task)method.Invoke(null, [stream, value])!).WaitAsync(TimeSpan.FromSeconds(5));
    }
    private static async Task DrainGenericTyped<T>(object stream, object value)
    {
        await using var cursor = ((IAsyncEnumerable<T>)stream).GetAsyncEnumerator();
        Assert.True(await cursor.MoveNextAsync()); Assert.Equal((T)value, cursor.Current); Assert.False(await cursor.MoveNextAsync());
    }

    [Theory]
    [InlineData("int", false, false)] [InlineData("long", false, false)] [InlineData("string", false, false)]
    [InlineData("int", true, false)] [InlineData("int", false, true)]
    public async Task Stream_generic_definitions_close_to_typed_fused_or_canonical_routes(string item, bool wrapper, bool local)
    {
        var f = StreamGenericFixtureFor(item, wrapper, local); var p = new StreamGenericScopeProbe(); IServiceCollection collection = new ServiceCollection(); collection.AddSingleton(p);
        foreach (var type in f.Services) collection.AddTransient(type.GetGenericTypeDefinition(), type.GetGenericTypeDefinition());
        using var provider = collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await DrainGeneric(f, provider, item == "string" ? "value" : Convert.ChangeType(7, f.Item)!);
        Assert.Equal(1, p.SourceDisposals); Assert.Empty(p.Released); Assert.Equal(4, p.Created.Count);
        StreamGenericEvidence(item + "-" + wrapper + "-" + local, new { kind = f.Kind.ToString(), created = p.Created.Count, sourceDisposals = p.SourceDisposals, DIReleasedByCursor = p.Released.Count });
    }

    [Theory]
    [InlineData(ServiceLifetime.Transient, false)] [InlineData(ServiceLifetime.Scoped, false)] [InlineData(ServiceLifetime.Singleton, false)]
    [InlineData(ServiceLifetime.Scoped, true)]
    public async Task Stream_generic_cursor_caches_preserve_provider_lifetime_and_DI_ownership(ServiceLifetime lifetime, bool wrapper)
    {
        var f = StreamGenericFixtureFor("int", wrapper); var p = new StreamGenericScopeProbe(); IServiceCollection collection = new ServiceCollection(); collection.AddSingleton(p);
        foreach (var type in f.Services) collection.Add(new(type.GetGenericTypeDefinition(), type.GetGenericTypeDefinition(), lifetime));
        using (var provider = collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }))
        {
            for (var scopeIndex = 0; scopeIndex < 2; scopeIndex++)
            {
                using (var scope = provider.CreateScope())
                {
                    await DrainGeneric(f, scope.ServiceProvider, 5); await DrainGeneric(f, scope.ServiceProvider, 6);
                    Assert.Equal(lifetime == ServiceLifetime.Singleton ? 0 : scopeIndex * (lifetime == ServiceLifetime.Transient ? 8 : 4), p.Released.Count);
                }
                Assert.Equal(lifetime == ServiceLifetime.Singleton ? 0 : (scopeIndex + 1) * (lifetime == ServiceLifetime.Transient ? 8 : 4), p.Released.Count);
            }
            Assert.Equal(lifetime == ServiceLifetime.Singleton ? 4 : lifetime == ServiceLifetime.Scoped ? 8 : 16, p.Created.Count);
        }
        Assert.Equal(p.Created.Count, p.Released.Count); Assert.Equal(4, p.SourceDisposals);
        StreamGenericEvidence("lifetime-" + lifetime + "-" + wrapper, new { lifetime = lifetime.ToString(), wrapper, created = p.Created.Count, released = p.Released.Count, p.SourceDisposals });
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Stream_generic_closed_factory_override_and_disposed_provider_remain_lazy(bool wrapper)
    {
        var f = StreamGenericFixtureFor("int", wrapper); var p = new StreamGenericScopeProbe(); IServiceCollection collection = new ServiceCollection(); collection.AddSingleton(p);
        foreach (var type in f.Services) collection.AddScoped(type.GetGenericTypeDefinition(), type.GetGenericTypeDefinition());
        foreach (var type in f.Services)
            collection.AddScoped(type, _ => { p.FactoryCalls++; return Activator.CreateInstance(type, p)!; });
        using var provider = collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using (var scope = provider.CreateScope())
        { await DrainGeneric(f, scope.ServiceProvider, 5); await DrainGeneric(f, scope.ServiceProvider, 6); Assert.Equal(4, p.FactoryCalls); }
        var disposed = provider.CreateScope(); var request = Activator.CreateInstance(f.Request, [7])!;
        var stream = (IAsyncEnumerable<int>)f.Create.Invoke(null, [disposed.ServiceProvider, request, default(CancellationToken)])!;
        var before = p.Created.Count; disposed.Dispose(); await using var cursor = stream.GetAsyncEnumerator();
        Assert.Equal(before, p.Created.Count);
        var pending = cursor.MoveNextAsync(); Assert.True(pending.IsFaulted);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await pending); Assert.Equal(before, p.Created.Count);
        StreamGenericEvidence("override-disposed-" + wrapper, new { wrapper, p.FactoryCalls, disposedBeforeFirstMove = true, newServices = p.Created.Count - before });
    }

    [Theory]
    [InlineData("unused")] [InlineData("constraints")] [InlineData("inconsistent")] [InlineData("duplicate")]
    public void Stream_generic_unclosable_and_invalid_constraint_inputs_keep_source_diagnostics(string failure)
    {
        var source = StreamGenericSource("int");
        if (failure == "unused") source = source.Replace("Stage.Hook(typeof(GenericFirst<>))", "Stage.Hook(typeof(Unused<,>))") + "public sealed class Unused<T,U>:IStreamItemFilter<GenericItems<T>,T>{public bool Accept(GenericItems<T> r,T item,CancellationToken c)=>true;}";
        if (failure == "constraints") source = source.Replace("Stage.Hook(typeof(GenericFirst<>))", "Stage.Hook(typeof(ClassOnly<>))") + "public sealed class ClassOnly<T>:IStreamItemFilter<GenericItems<T>,T> where T:class {public bool Accept(GenericItems<T> r,T item,CancellationToken c)=>true;}";
        if (failure == "inconsistent") source = source.Replace("Stage.Hook(typeof(GenericFirst<>))", "Stage.Hook(typeof(Inconsistent<>))") + "public sealed class Inconsistent<T>:IStreamItemFilter<GenericItems<T>,string>{public bool Accept(GenericItems<T> r,string item,CancellationToken c)=>true;}";
        if (failure == "duplicate") source = source.Replace("Stage.Hook(typeof(GenericFirst<>))", "Stage.Hook(typeof(GenericFirst<>)),Stage.Hook<GenericFirst<int>>()");
        var run = RunStreamConnection(source, "generic-reject-" + failure);
        var diagnostic = Assert.Single(run.Diagnostics); Assert.Equal("ZEN0035", diagnostic.Id);
        Assert.StartsWith("Stage.Hook", diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan));
        Assert.DoesNotContain(run.Driver.GetRunResult().Results.Single().GeneratedSources, f => f.HintName.StartsWith("Zendiator.Stream", StringComparison.Ordinal));
        if (failure == "duplicate") Assert.Single(diagnostic.AdditionalLocations);
    }
}
