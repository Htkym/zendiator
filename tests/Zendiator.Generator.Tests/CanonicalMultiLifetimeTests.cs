using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Zendiator.Generator.Tests;

public sealed class MultiLifetimeProbe
{
    public readonly List<object> Calls = [], Created = [], Released = [];
    public int Hit(object receiver, int value) { Calls.Add(receiver); return value; }
    public void Construct(object receiver) => Created.Add(receiver);
    public void Release(object receiver) { Assert.DoesNotContain(Released, r => ReferenceEquals(r, receiver)); Released.Add(receiver); }
}
public sealed partial class GeneratorTests
{
    private sealed class MultiCollection : System.Collections.ObjectModel.Collection<ServiceDescriptor>, IServiceCollection;
    // Controlled third-party contract fixture: every operation reaches GetService; lifetime cache belongs to this provider.
    private sealed class MultiContainer : IServiceProvider, IDisposable
    {
        private readonly IServiceCollection _descriptors; private readonly MultiContainer _root; private readonly Dictionary<Type, object> _cache = [];
        private readonly List<IDisposable> _owned = []; public readonly List<Type> Resolutions = []; private bool _disposed;
        public MultiContainer(IServiceCollection descriptors) { _descriptors = descriptors; _root = this; }
        private MultiContainer(MultiContainer root) { _descriptors = root._descriptors; _root = root; }
        public MultiContainer Child() => new(_root);
        public object? GetService(Type type)
        {
            ObjectDisposedException.ThrowIf(_disposed, this); Resolutions.Add(type); if (type == typeof(IServiceProvider)) return this;
            var d = _descriptors.LastOrDefault(x => x.ServiceType == type); if (d == null) return null; if (d.ImplementationInstance != null) return d.ImplementationInstance;
            var owner = d.Lifetime == ServiceLifetime.Singleton ? _root : this;
            if (d.Lifetime != ServiceLifetime.Transient && owner._cache.TryGetValue(type, out var existing)) return existing;
            object value; if (d.ImplementationFactory != null) value = d.ImplementationFactory(owner);
            else { var constructor = d.ImplementationType!.GetConstructors().Single(); value = constructor.Invoke(constructor.GetParameters().Select(p => owner.GetService(p.ParameterType) ?? throw new InvalidOperationException("Missing " + p.ParameterType)).ToArray()); }
            if (d.Lifetime != ServiceLifetime.Transient) owner._cache.Add(type, value); if (value is IDisposable disposable) owner._owned.Add(disposable); return value;
        }
        public void Dispose() { if (_disposed) return; _disposed = true; foreach (var value in _owned.AsEnumerable().Reverse()) value.Dispose(); }
    }
    private sealed record LifetimeFixture(Assembly Assembly, Type Handler, Type Mediator, Type Request0, Type Request1, MethodInfo Register, MethodInfo Dispatch);
    private static readonly Lazy<LifetimeFixture> LifetimeMulti = new(() =>
    {
        var source = """
            using System;using System.Threading;using System.Threading.Tasks;using Zendiator;using Zendiator.DependencyInjection;using Microsoft.Extensions.DependencyInjection;
            using Probe=global::Zendiator.Generator.Tests.MultiLifetimeProbe;
            namespace Life{
                public sealed record Ping(int Value):ISyncRequest<int>;public sealed record Pong(int Value):IRequest<int>;
                [ZendiatorHandler]public sealed class Handler:ISyncRequestHandler<Ping,int>,IRequestHandler<Pong,int>,IDisposable{
                    private readonly Probe _p;public Handler(Probe p){_p=p;p.Construct(this);}
                    public int Handle(Ping r,CancellationToken t)=>_p.Hit(this,r.Value);public ValueTask<int> HandleAsync(Pong r,CancellationToken t)=>new(_p.Hit(this,r.Value));public void Dispose()=>_p.Release(this);
                }
                [ZendiatorComposition("life")]public sealed class Reads;
                public static class Bridge{
                    public static IServiceCollection Register(IServiceCollection s)=>s.AddZendiator<Reads>(c=>{c.Handlers<Ping>(DispatchFamily.SendSync,typeof(Handler));c.Handlers<Pong>(DispatchFamily.SendAsync,typeof(Handler));c.Pipeline<Pong>(DispatchFamily.SendAsync);c.Pipeline<Ping>(DispatchFamily.SendSync);});
                    public static async Task<int> Dispatch(object m,object r){var mediator=(global::Life.Generated.Reads.IZendiator)m;return r is Ping p?mediator.Send(p):await mediator.SendAsync((Pong)r);}
                }
            }
            """;
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "MultiLifetime_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics); Assert.Empty(diagnostics); Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        using var bytes = new MemoryStream(); var emitted = output.Emit(bytes); Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics)); var sources = driver.GetRunResult().Results.Single().GeneratedSources;
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR"))) { var p = Path.Combine(MultiArtifactRoot, "generated-artifacts", "multi-lifetime"); Directory.CreateDirectory(p); File.WriteAllText(Path.Combine(p, "input.cs"), source); foreach (var s in sources) File.WriteAllText(Path.Combine(p, s.HintName), s.SourceText.ToString()); File.WriteAllBytes(Path.Combine(p, "generated.dll"), bytes.ToArray()); File.WriteAllText(Path.Combine(p, "compile.json"), JsonSerializer.Serialize(new { emitted.Success, productionEnabled = true, candidateOnly = false, warnings = 0, errors = 0, generatorErrors = 0, routes = 2 })); }
        bytes.Position = 0; var a = AssemblyLoadContext.Default.LoadFromStream(bytes); var bridge = a.GetType("Life.Bridge")!; return new(a, a.GetType("Life.Handler")!, a.GetType("Life.Generated.Reads.Zendiator")!, a.GetType("Life.Ping")!, a.GetType("Life.Pong")!, bridge.GetMethod("Register")!, bridge.GetMethod("Dispatch")!);
    });
    [Theory]
    [InlineData(ServiceLifetime.Transient)]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Singleton)]
    public async Task Multi_route_custom_collection_provider_preserve_registered_lifetime_and_DI_ownership(ServiceLifetime lifetime)
    {
        var f = LifetimeMulti.Value; var p = new MultiLifetimeProbe(); IServiceCollection collection = new MultiCollection(); collection.AddSingleton(p); collection.Add(new(f.Handler, f.Handler, lifetime)); Assert.Same(collection, f.Register.Invoke(null, [collection])); Assert.Single(collection, d => d.ServiceType == f.Handler);
        using (var root = new MultiContainer(collection))
        {
            for (var scopeIndex = 0; scopeIndex < 2; scopeIndex++)
            {
                using (var scope = root.Child()) { var mediator = scope.GetService(f.Mediator)!; for (var route = 0; route < 2; route++) { var value = scopeIndex * 2 + route + 5; var request = Activator.CreateInstance(route == 0 ? f.Request0 : f.Request1, [value])!; Assert.Equal(value, await ((Task<int>)f.Dispatch.Invoke(null, [mediator, request])!).WaitAsync(TimeSpan.FromSeconds(10))); } Assert.DoesNotContain(p.Released, x => p.Calls.Skip(scopeIndex * 2).Any(c => ReferenceEquals(c, x))); Assert.DoesNotContain(typeof(IServiceScopeFactory), scope.Resolutions); Assert.Equal(2, scope.Resolutions.Count(t => t == f.Handler)); }
                Assert.Equal(lifetime == ServiceLifetime.Singleton ? 0 : lifetime == ServiceLifetime.Scoped ? scopeIndex + 1 : (scopeIndex + 1) * 2, p.Released.Count);
            }
            Assert.Equal(lifetime == ServiceLifetime.Transient ? 4 : lifetime == ServiceLifetime.Scoped ? 2 : 1, p.Created.Count); if (lifetime == ServiceLifetime.Scoped) { Assert.Same(p.Calls[0], p.Calls[1]); Assert.Same(p.Calls[2], p.Calls[3]); Assert.NotSame(p.Calls[0], p.Calls[2]); }
            if (lifetime == ServiceLifetime.Singleton) Assert.All(p.Calls, c => Assert.Same(p.Calls[0], c));
        }
        Assert.Equal(p.Created.Count, p.Released.Count); MultiEvidence("multi-lifetime", lifetime.ToString(), new { lifetime = lifetime.ToString(), operations = 4, created = p.Created.Count, released = p.Released.Count, registrationCount = collection.Count(d => d.ServiceType == f.Handler), operationCleanup = 0, implicitScopeCreations = 0 });
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Multi_route_owned_sessions_use_custom_provider_and_borrow_its_scope(bool async)
    {
        var f = MultiRuntimeFixture("mixed"); var route = async ? f.B : f.A; var p = new ManagedShapeProbe(); try
        {
            IServiceCollection collection = new MultiCollection(); collection.AddSingleton(p); f.Register.Invoke(null, [collection]); using var root = new MultiContainer(collection); using var scope = root.Child(); var mediator = scope.GetService(f.Mediator)!; var request = Activator.CreateInstance(route.Request, [5])!; var result = route.Dispatch.Invoke(null, [mediator, request, p.Token.Token])!;
            if (async) { var task = (Task)result.GetType().GetMethod("AsTask")!.Invoke(result, null)!; await task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.Equal(17, ((int[])task.GetType().GetProperty("Result")!.GetValue(task)!)[0]); } else Assert.Equal(7, Convert.ToInt32(route.Result.GetProperty("A")!.GetValue(result)));
            Assert.Single(p.Created); Assert.Single(p.Released); Assert.DoesNotContain(typeof(IServiceScopeFactory), scope.Resolutions); Assert.Equal(new[] { "before", "factory", "body", "dispose", "after", "finally" }, p.Trace); MultiEvidence("multi-custom-owned", async.ToString(), new { async, created = p.Created.Count, released = p.Released.Count, p.Trace, providerBorrowed = true });
        }
        finally { p.Token.Dispose(); }
    }
}
