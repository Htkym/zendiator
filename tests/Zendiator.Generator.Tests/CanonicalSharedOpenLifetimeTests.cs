using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private sealed class SharedOpenLookupProvider(IServiceProvider provider) : IServiceProvider
    {
        internal readonly Dictionary<Type, int> Lookups = [];

        public object? GetService(Type serviceType)
        {
            Lookups.TryGetValue(serviceType, out var count);
            Lookups[serviceType] = count + 1;
            return provider.GetService(serviceType);
        }
    }

    private sealed record SharedOpenLifetimeFixture(Type Mediator, Type FirstHandler, Type SecondHandler,
        MethodInfo Register, Func<object, bool, bool, int, Task<int>> Dispatch);

    private static readonly Lazy<SharedOpenLifetimeFixture> SharedOpenLifetime = new(() =>
    {
        const string source = """
            using System;using System.Threading;using System.Threading.Tasks;
            using Zendiator;using Zendiator.DependencyInjection;using Microsoft.Extensions.DependencyInjection;
            using Probe=global::Zendiator.Generator.Tests.MultiLifetimeProbe;
            namespace SharedOpen {
                public sealed class First;
                public sealed class Second;
                public sealed record Ping<T>(int Value):IRequest<int>;
                public sealed record Pong<T>(int Value):IRequest<int>;
                [ZendiatorHandler]public sealed class Handler<T>:IRequestHandler<Ping<T>,int>,
                    IRequestHandler<Pong<T>,int>,IDisposable {
                    private readonly Probe probe;
                    public Handler(Probe probe){this.probe=probe;probe.Construct(this);}
                    public ValueTask<int> HandleAsync(Ping<T> request,CancellationToken token)=>new(probe.Hit(this,request.Value));
                    public ValueTask<int> HandleAsync(Pong<T> request,CancellationToken token)=>new(probe.Hit(this,request.Value));
                    public void Dispose()=>probe.Release(this);
                }
                [ZendiatorComposition("shared-open")]public sealed class Composition;
                public static class Bridge {
                    public static IServiceCollection Register(IServiceCollection services)=>services.AddZendiator<Composition>(c=>{
                        c.Handlers(typeof(Ping<>),DispatchFamily.SendAsync,typeof(Handler<>));
                        c.Handlers(typeof(Pong<>),DispatchFamily.SendAsync,typeof(Handler<>));
                    });
                    public static Task<int> Dispatch(object mediator,bool pong,bool second,int value){
                        var entry=(global::SharedOpen.Generated.Composition.IZendiator)mediator;
                        if(second)return pong?entry.SendAsync(new Pong<Second>(value)).AsTask():entry.SendAsync(new Ping<Second>(value)).AsTask();
                        return pong?entry.SendAsync(new Pong<First>(value)).AsTask():entry.SendAsync(new Ping<First>(value)).AsTask();
                    }
                }
            }
            """;
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(
            SyncCompilation(source, "SharedOpenLifetime_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        using var bytes = new MemoryStream();
        var emitted = output.Emit(bytes);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR")))
        {
            var folder = Path.Combine(MultiArtifactRoot, "generated-artifacts", "shared-open-lifetime");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "input.cs"), source);
            foreach (var generated in driver.GetRunResult().Results.Single().GeneratedSources)
                File.WriteAllText(Path.Combine(folder, generated.HintName), generated.SourceText.ToString());
            File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
        }
        bytes.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var handler = assembly.GetType("SharedOpen.Handler`1")!;
        var bridge = assembly.GetType("SharedOpen.Bridge")!;
        return new(assembly.GetType("SharedOpen.Generated.Composition.Zendiator")!,
            handler.MakeGenericType(assembly.GetType("SharedOpen.First")!),
            handler.MakeGenericType(assembly.GetType("SharedOpen.Second")!), bridge.GetMethod("Register")!,
            bridge.GetMethod("Dispatch")!.CreateDelegate<Func<object, bool, bool, int, Task<int>>>());
    });

    [Fact]
    public async Task Shared_open_handler_retries_native_failure_and_reuses_only_the_same_closed_type_and_scope()
    {
        var fixture = SharedOpenLifetime.Value;
        var probe = new MultiLifetimeProbe();
        IServiceCollection services = new ServiceCollection();
        services.AddSingleton(probe);
        fixture.Register.Invoke(null, [services]);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        object first;
        using (var scope = provider.CreateScope())
        {
            var tracked = new SharedOpenLookupProvider(scope.ServiceProvider);
            var mediator = Activator.CreateInstance(fixture.Mediator, [tracked])!;
            Assert.Empty(probe.Created);
            var failure = new InvalidOperationException("first native constructor failure");
            probe.BeforeConstruct = () => throw failure;
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
            {
                _ = fixture.Dispatch(mediator, false, false, 10);
            }));
            Assert.Empty(probe.Created);
            Assert.Empty(probe.Calls);
            probe.BeforeConstruct = null;

            // Retry through the other open request, then revisit the failed request's closed key.
            Assert.Equal(11, await fixture.Dispatch(mediator, true, false, 11).WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(12, await fixture.Dispatch(mediator, false, false, 12).WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(21, await fixture.Dispatch(mediator, false, true, 21).WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(22, await fixture.Dispatch(mediator, true, true, 22).WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(2, tracked.Lookups[fixture.FirstHandler]);
            Assert.Equal(1, tracked.Lookups[fixture.SecondHandler]);
            Assert.Equal(2, probe.Created.Count);
            Assert.Equal(4, probe.Calls.Count);
            first = probe.Created[0];
            Assert.Equal(fixture.FirstHandler, first.GetType());
            Assert.Equal(fixture.SecondHandler, probe.Created[1].GetType());
            Assert.Same(first, probe.Calls[0]);
            Assert.Same(first, probe.Calls[1]);
            Assert.Same(probe.Created[1], probe.Calls[2]);
            Assert.Same(probe.Created[1], probe.Calls[3]);
            Assert.Empty(probe.Released);
        }
        Assert.Equal(2, probe.Released.Count);
        Assert.All(probe.Created, receiver => Assert.Contains(receiver, probe.Released));
        using (var scope = provider.CreateScope())
        {
            var tracked = new SharedOpenLookupProvider(scope.ServiceProvider);
            var mediator = Activator.CreateInstance(fixture.Mediator, [tracked])!;
            Assert.Equal(31, await fixture.Dispatch(mediator, false, false, 31).WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(1, tracked.Lookups[fixture.FirstHandler]);
            Assert.NotSame(first, probe.Calls[^1]);
            Assert.Equal(3, probe.Created.Count);
            Assert.Equal(2, probe.Released.Count);
        }
        Assert.Equal(3, probe.Released.Count);
        MultiEvidence("shared-open-lifetime", "retry-and-sharing", new
        {
            created = probe.Created.Count,
            released = probe.Released.Count,
            calls = probe.Calls.Count,
            failedResolutionRetried = true,
            sameClosedTypeSharedAcrossRoutes = true,
            closedTypesAndScopesSeparated = true
        });
    }
}
