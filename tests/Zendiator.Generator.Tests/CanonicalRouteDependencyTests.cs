using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed class RouteDependencyProbe
{
    public static readonly AsyncLocal<string?> Ambient = new();
    public readonly ConcurrentQueue<(int Role, object Receiver)> Created = new(), Before = new(), Handled = new();
    public readonly ConcurrentQueue<object> Released = new();
    public readonly ConcurrentQueue<string> Trace = new();
    public Action<int>? DuringConstruction;
    public Exception? OuterError;

    public void Construct(int role, object receiver)
    {
        Created.Enqueue((role, receiver));
        Trace.Enqueue("construct:" + role);
        DuringConstruction?.Invoke(role);
    }

    public ValueTask<T> Transform<T>(int role, object receiver, T request, CancellationToken token)
    {
        Before.Enqueue((role, receiver));
        Trace.Enqueue("before:" + role);
        if ((role is 0 or 5) && OuterError is { } failure)
            throw failure;
        return new(request);
    }

    public ValueTask<int> Handle(int role, object receiver, int value)
    {
        Handled.Enqueue((role, receiver));
        Trace.Enqueue("handle:" + role);
        return new(value);
    }

    public void Release(object receiver)
    {
        Assert.DoesNotContain(Released, previous => ReferenceEquals(previous, receiver));
        Released.Enqueue(receiver);
    }
}

public sealed partial class GeneratorTests
{
    private sealed record RouteDependencyFixture(Type Mediator, Type Outer, Type Handler, Type SyncBefore,
        Type[] Carriers, string Generated, Action<IServiceCollection> Register,
        Func<object, int, CancellationToken, ValueTask<int>> Ping,
        Func<object, int, CancellationToken, ValueTask<int>> Pong,
        Func<object, int, int> Plain, Func<object, int, IAsyncEnumerable<int>> Stream,
        Func<RouteDependencyProbe, int, object> New);

    private static readonly ConcurrentDictionary<string, Lazy<RouteDependencyFixture>> RouteDependencyFixtures = new();

    private static string RouteDependencySource(string shape)
    {
        var eagerHook = shape == "sync-eager-hook";
        var eagerHandler = shape == "sync-eager-handler";
        var eligible = shape == "eligible" || eagerHook || eagerHandler;
        var mixed = shape == "mixed";
        var singleReceiver = shape == "one-receiver";
        var syncBefore = shape == "sync-before" || eagerHook || eagerHandler || mixed;
        var outerKind = shape == "value-hook" ? "struct" : "sealed class";
        var pipeline = shape == "no-hook" ? "" : singleReceiver
            ? "c.Pipeline<Ping>(DispatchFamily.SendAsync,Stage.Hook<Handler>());" : syncBefore
            ? "c.Pipeline<Ping>(DispatchFamily.SendAsync,Stage.Hook<SyncBefore>());"
            : "c.Pipeline<Ping>(DispatchFamily.SendAsync,Stage.Hook<Outer>(),Stage.Hook<Middle>(),Stage.Hook<Inner>());";
        var second = eligible
            ? "c.Handlers<Pong>(DispatchFamily.SendAsync,typeof(OtherHandler));c.Pipeline<Pong>(DispatchFamily.SendAsync,Stage.Hook<Outer>());"
            : mixed
            ? "c.Handlers<Plain>(DispatchFamily.SendSync,typeof(Handler));c.Handlers<Items>(DispatchFamily.Stream,typeof(SyncBefore));c.Pipeline<Items>(DispatchFamily.Stream);"
            : "";
        var eager = eagerHook ? ",EagerDependencies=new[]{typeof(SyncBefore)}" : eagerHandler ? ",EagerDependencies=new[]{typeof(Handler)}" : "";
        string Callbacks(string request, int role) => $$"""
            public ValueTask<{{request}}> BeforeAsync({{request}} r,CancellationToken t)=>_p.Transform({{role}},this,r,t);
            public ValueTask<int> AfterAsync({{request}} r,int value,CancellationToken t)=>new(value);
            public ValueTask OnExceptionAsync({{request}} r,Exception error,CancellationToken t)=>default;
            public ValueTask FinallyAsync({{request}} r,SendExitInfo exit,CancellationToken t)=>default;
            """;
        string Contracts(string request) => "IAsyncSendRequestTransform<" + request + ">,IAsyncSendResponseTransform<" + request + ",int>,IAsyncSendExceptionObserver<" + request + ">,IAsyncSendFinallyObserver<" + request + ">";
        string Hook(string name, int role) => $$"""
            public sealed class {{name}}:{{Contracts("Ping")}},IDisposable {
                private readonly Probe _p;public {{name}}(Probe p){_p=p;p.Construct({{role}},this);}
                {{Callbacks("Ping", role)}}
                public void Dispose()=>_p.Release(this);
            }
            """;
        return $$"""
            using System;using System.Collections.Generic;using System.Threading;using System.Threading.Tasks;using Zendiator;using Zendiator.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection;using Probe=global::Zendiator.Generator.Tests.RouteDependencyProbe;
            namespace RouteCapture {
                public sealed record Ping(int Value):IRequest<int>;
                public sealed record Pong(int Value):IRequest<int>;
                public sealed record Plain(int Value):ISyncRequest<int>;
                public sealed record Items(int Value):IStreamRequest<int>;
                public sealed class Cursor(int value):IAsyncEnumerator<int> {
                    private bool _moved;public int Current=>value;
                    public ValueTask<bool> MoveNextAsync(){var next=!_moved;_moved=true;return new(next);}
                    public ValueTask DisposeAsync()=>default;
                }
                public {{outerKind}} Outer:{{Contracts("Ping")}},{{Contracts("Pong")}},IDisposable {
                    private readonly Probe _p;public Outer(Probe p){_p=p;p.Construct(0,this);}
                    {{Callbacks("Ping", 0)}}{{Callbacks("Pong", 0)}}
                    public void Dispose()=>_p.Release(this);
                }
                {{Hook("Middle", 1)}}{{Hook("Inner", 2)}}
                {{(mixed ? "[Handler]" : "")}}public sealed class SyncBefore:ISendRequestTransform<Ping>,IDisposable{{(mixed ? ",IStreamSourceHandler<Items,int,Cursor>" : "")}} {
                    private readonly Probe _p;public SyncBefore(Probe p){_p=p;p.Construct(5,this);}
                    public Ping Before(Ping r,CancellationToken t)=>_p.Transform(5,this,r,t).GetAwaiter().GetResult();
                    {{(mixed ? "public ValueTask<Cursor> OpenAsync(Items r,CancellationToken t){_p.Handle(5,this,r.Value).GetAwaiter().GetResult();return new(new Cursor(r.Value));}" : "")}}
                    public void Dispose()=>_p.Release(this);
                }
                [Handler]public sealed class Handler:IRequestHandler<Ping,int>,IDisposable{{(mixed ? ",ISyncRequestHandler<Plain,int>" : "")}}{{(singleReceiver ? ",ISendRequestTransform<Ping>" : "")}} {
                    private readonly Probe _p;public Handler(Probe p){_p=p;p.Construct(3,this);}
                    public ValueTask<int> HandleAsync(Ping r,CancellationToken t)=>_p.Handle(3,this,r.Value);
                    {{(mixed ? "public int Handle(Plain r,CancellationToken t)=>_p.Handle(3,this,r.Value).GetAwaiter().GetResult();" : "")}}
                    {{(singleReceiver ? "public Ping Before(Ping r,CancellationToken t)=>_p.Transform(3,this,r,t).GetAwaiter().GetResult();" : "")}}
                    public void Dispose()=>_p.Release(this);
                }
                [Handler]public sealed class OtherHandler:IRequestHandler<Pong,int>,IDisposable {
                    private readonly Probe _p;public OtherHandler(Probe p){_p=p;p.Construct(4,this);}
                    public ValueTask<int> HandleAsync(Pong r,CancellationToken t)=>_p.Handle(4,this,r.Value);
                    public void Dispose()=>_p.Release(this);
                }
                [ZendiatorComposition("route-capture"{{eager}})]public sealed class Routes;
                public static class Bridge {
                    public static void Register(IServiceCollection s)=>s.AddZendiator<Routes>(c=>{
                        c.Handlers<Ping>(DispatchFamily.SendAsync,typeof(Handler));{{pipeline}}{{second}}
                    });
                    public static ValueTask<int> Ping(object m,int value,CancellationToken token)=>((global::RouteCapture.Generated.Routes.IZendiator)m).SendAsync(new Ping(value),token);
                    public static ValueTask<int> Pong(object m,int value,CancellationToken token)=>{{(eligible ? "((global::RouteCapture.Generated.Routes.IZendiator)m).SendAsync(new Pong(value),token)" : "throw new NotSupportedException()")}};
                    public static int Plain(object m,int value)=>{{(mixed ? "((global::RouteCapture.Generated.Routes.IZendiator)m).Send(new Plain(value))" : "throw new NotSupportedException()")}};
                    public static IAsyncEnumerable<int> Stream(object m,int value)=>{{(mixed ? "((global::RouteCapture.Generated.Routes.IZendiator)m).StreamAsync(new Items(value))" : "throw new NotSupportedException()")}};
                    public static object New(Probe p,int role)=>role switch {0=>new Outer(p),1=>new Middle(p),2=>new Inner(p),3=>new Handler(p),4=>new OtherHandler(p),5=>new SyncBefore(p),_=>throw new ArgumentOutOfRangeException(nameof(role))};
                }
            }
            """;
    }

    private static RouteDependencyFixture RouteDependencyRuntimeFixture(string shape = "eligible") => RouteDependencyFixtures.GetOrAdd(shape, key => new(() =>
    {
        var input = RouteDependencySource(key);
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(input, "RouteDependency_" + key + "_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        var generated = driver.GetRunResult().Results.Single().GeneratedSources;
        using var bytes = new MemoryStream();
        var emitted = output.Emit(bytes);
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(evidence))
        {
            var folder = Path.Combine(evidence, "route-dependency-" + key);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "input.cs"), input);
            foreach (var file in generated)
                File.WriteAllText(Path.Combine(folder, file.HintName), file.SourceText.ToString());
            if (emitted.Success)
                File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
            File.WriteAllText(Path.Combine(folder, "compile.json"), JsonSerializer.Serialize(new
            {
                candidateOnly = true,
                emitted.Success,
                diagnostics = diagnostics.Select(d => new { d.Id, message = d.GetMessage() }),
                compiler = output.GetDiagnostics().Select(d => new { d.Id, severity = d.Severity.ToString(), message = d.GetMessage() }),
                emit = emitted.Diagnostics.Select(d => new { d.Id, message = d.GetMessage() })
            }));
        }
        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error));
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        if (key != "mixed")
            AssertCanonicalOutputRoles(generated, routes: key == "eligible" || key.StartsWith("sync-eager-", StringComparison.Ordinal) ? 2 : 1);
        bytes.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var mediator = assembly.GetType("RouteCapture.Generated.Routes.Zendiator")!;
        var bridge = assembly.GetType("RouteCapture.Bridge")!;
        return new(mediator, assembly.GetType("RouteCapture.Outer")!, assembly.GetType("RouteCapture.Handler")!, assembly.GetType("RouteCapture.SyncBefore")!,
            mediator.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).Where(t => t.Name.StartsWith("RouteDependencies_", StringComparison.Ordinal)).ToArray(),
            string.Join("\n", generated.Select(s => s.SourceText.ToString())),
            bridge.GetMethod("Register")!.CreateDelegate<Action<IServiceCollection>>(),
            bridge.GetMethod("Ping")!.CreateDelegate<Func<object, int, CancellationToken, ValueTask<int>>>(),
            bridge.GetMethod("Pong")!.CreateDelegate<Func<object, int, CancellationToken, ValueTask<int>>>(),
            bridge.GetMethod("Plain")!.CreateDelegate<Func<object, int, int>>(),
            bridge.GetMethod("Stream")!.CreateDelegate<Func<object, int, IAsyncEnumerable<int>>>(),
            bridge.GetMethod("New")!.CreateDelegate<Func<RouteDependencyProbe, int, object>>());
    })).Value;

    private static IServiceCollection RouteDependencyServices(RouteDependencyFixture fixture, RouteDependencyProbe probe)
    {
        IServiceCollection services = new ServiceCollection();
        services.AddSingleton(probe);
        fixture.Register(services);
        return services;
    }

    private static object RouteDependencyMediator(RouteDependencyFixture f, IServiceProvider provider) => provider.GetRequiredService(CanonicalEntryService(f.Mediator));

    private static void AssertRouteDependencyDisposal(RouteDependencyProbe p)
    {
        Assert.Equal(p.Created.Count, p.Released.Count);
        Assert.All(p.Created, item => Assert.Single(p.Released, receiver => ReferenceEquals(receiver, item.Receiver)));
    }

    [Fact]
    public async Task Route_dependencies_capture_eagerly_per_route_and_DI_owns_unused_receivers()
    {
        var f = RouteDependencyRuntimeFixture();
        var p = new RouteDependencyProbe();
        using (var provider = RouteDependencyServices(f, p).BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }))
        using (var scope = provider.CreateScope())
        {
            var mediator = RouteDependencyMediator(f, scope.ServiceProvider);
            Assert.Empty(p.Created);
            Assert.Equal(10, await f.Ping(mediator, 10, default));
            Assert.Equal(new[] { "construct:0", "construct:1", "construct:2", "construct:3", "before:0" }, p.Trace.Take(5));
            var firstOuter = p.Before.First(c => c.Role == 0).Receiver;
            var firstHandler = Assert.Single(p.Handled).Receiver;
            Assert.Equal(11, await f.Ping(mediator, 11, default));
            Assert.Equal(4, p.Created.Count);
            Assert.All(p.Handled, call => Assert.Same(firstHandler, call.Receiver));
            Assert.Equal(12, await f.Pong(mediator, 12, default));
            Assert.NotSame(firstOuter, p.Before.Last(c => c.Role == 0).Receiver);
            var explicitCapture = ((IServiceProvider)mediator).GetService(f.Outer);
            Assert.NotNull(explicitCapture);
            Assert.NotSame(explicitCapture, ((IServiceProvider)mediator).GetService(f.Outer));
            Assert.DoesNotContain(p.Before, call => ReferenceEquals(explicitCapture, call.Receiver));
            Assert.Empty(p.Released);
        }
        AssertRouteDependencyDisposal(p);

        var stopped = new RouteDependencyProbe { OuterError = new InvalidOperationException("cold outer Before failure") };
        using (var provider = RouteDependencyServices(f, stopped).BuildServiceProvider())
        using (var scope = provider.CreateScope())
        {
            var mediator = RouteDependencyMediator(f, scope.ServiceProvider);
            Assert.Same(stopped.OuterError, await Assert.ThrowsAsync<InvalidOperationException>(() => f.Ping(mediator, 1, default).AsTask()));
            Assert.Equal(4, stopped.Created.Count);
            Assert.Single(stopped.Before);
            Assert.Empty(stopped.Handled);
            Assert.Empty(stopped.Released);
        }
        AssertRouteDependencyDisposal(stopped);

        foreach (var lifetime in new[] { ServiceLifetime.Scoped, ServiceLifetime.Singleton })
        {
            var shared = new RouteDependencyProbe();
            var services = RouteDependencyServices(f, shared);
            services.Add(ServiceDescriptor.Describe(f.Outer, f.Outer, lifetime));
            using (var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }))
            using (var scope = provider.CreateScope())
            {
                var mediator = RouteDependencyMediator(f, scope.ServiceProvider);
                Assert.Equal(20, await f.Ping(mediator, 20, default));
                Assert.Equal(21, await f.Pong(mediator, 21, default));
                var outer = shared.Before.First(call => call.Role == 0).Receiver;
                Assert.All(shared.Before.Where(call => call.Role == 0), call => Assert.Same(outer, call.Receiver));
                Assert.Same(outer, ((IServiceProvider)mediator).GetService(f.Outer));
                Assert.Empty(shared.Released);
            }
            AssertRouteDependencyDisposal(shared);
        }
    }

    [Fact]
    public async Task Route_dependencies_do_not_publish_null_or_replace_an_outer_construction_failure_with_a_reentrant_winner()
    {
        var f = RouteDependencyRuntimeFixture();
        var nullProbe = new RouteDependencyProbe();
        var attempts = 0;
        var services = RouteDependencyServices(f, nullProbe);
        services.AddTransient(f.Outer, _ => ++attempts == 1 ? null! : f.New(nullProbe, 0));
        using (var provider = services.BuildServiceProvider())
        using (var scope = provider.CreateScope())
        {
            var mediator = RouteDependencyMediator(f, scope.ServiceProvider);
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => f.Ping(mediator, 1, default).AsTask());
            Assert.Contains(f.Outer.FullName!, failure.Message, StringComparison.Ordinal);
            Assert.Empty(nullProbe.Before);
            Assert.Empty(nullProbe.Handled);
            Assert.Equal(2, await f.Ping(mediator, 2, default));
            Assert.Equal(2, attempts);
            Assert.Equal(3, await f.Ping(mediator, 3, default));
            Assert.Equal(2, attempts);
            // Native DI may obtain later constructor arguments before the carrier rejects null.
            Assert.Empty(nullProbe.Released);
        }
        AssertRouteDependencyDisposal(nullProbe);

        var p = new RouteDependencyProbe();
        object mediatorForFactory = null!;
        var nativeCalls = 0;
        var outerFailure = new InvalidOperationException("outer graph fails after nested winner");
        services = RouteDependencyServices(f, p);
        services.AddTransient(f.Outer, _ =>
        {
            if (Interlocked.Increment(ref nativeCalls) == 1)
            {
                var inner = f.Ping(mediatorForFactory, 40, default);
                Assert.True(inner.IsCompletedSuccessfully);
                Assert.Equal(40, inner.GetAwaiter().GetResult());
                throw outerFailure;
            }
            return f.New(p, 0);
        });
        using (var provider = services.BuildServiceProvider())
        using (var scope = provider.CreateScope())
        {
            mediatorForFactory = RouteDependencyMediator(f, scope.ServiceProvider);
            Assert.Same(outerFailure, await Assert.ThrowsAsync<InvalidOperationException>(() => f.Ping(mediatorForFactory, 41, default).AsTask()));
            var innerHandler = Assert.Single(p.Handled).Receiver;
            Assert.Equal(42, await f.Ping(mediatorForFactory, 42, default));
            Assert.Equal(2, nativeCalls);
            Assert.All(p.Handled, call => Assert.Same(innerHandler, call.Receiver));
            Assert.Empty(p.Released);
        }
        AssertRouteDependencyDisposal(p);
    }

    [Fact]
    public async Task Route_dependencies_concurrent_first_calls_invoke_one_completed_winner_and_DI_disposes_losers()
    {
        var f = RouteDependencyRuntimeFixture();
        var p = new RouteDependencyProbe();
        using var entered = new CountdownEvent(2);
        using var release = new ManualResetEventSlim();
        var services = RouteDependencyServices(f, p);
        services.AddTransient(f.Outer, _ =>
        {
            entered.Signal();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
            return f.New(p, 0);
        });
        using (var provider = services.BuildServiceProvider())
        using (var scope = provider.CreateScope())
        {
            var mediator = RouteDependencyMediator(f, scope.ServiceProvider);
            Task<int>[] calls = [];
            try
            {
                calls = new[] { 51, 52 }.Select(value => Task.Factory.StartNew(
                    () => f.Ping(mediator, value, default).AsTask(), CancellationToken.None,
                    TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap()).ToArray();
                Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
                release.Set();
                Assert.Equal(new[] { 51, 52 }, await Task.WhenAll(calls).WaitAsync(TimeSpan.FromSeconds(10)));
                var winner = p.Before.First(call => call.Role == 0).Receiver;
                Assert.All(p.Before.Where(call => call.Role == 0), call => Assert.Same(winner, call.Receiver));
                var handler = p.Handled.First().Receiver;
                Assert.All(p.Handled, call => Assert.Same(handler, call.Receiver));
                Assert.Equal(2, p.Created.Count(call => call.Role == 0));
                Assert.Empty(p.Released);
            }
            finally
            {
                release.Set();
                await Task.WhenAll(calls).WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
        AssertRouteDependencyDisposal(p);
    }

    [Fact]
    public async Task Route_dependencies_stay_inside_async_cancellation_and_caller_context_boundaries()
    {
        var f = RouteDependencyRuntimeFixture();
        var p = new RouteDependencyProbe();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using (var provider = RouteDependencyServices(f, p).BuildServiceProvider())
        using (var scope = provider.CreateScope())
        {
            var mediator = RouteDependencyMediator(f, scope.ServiceProvider);
            var operation = f.Ping(mediator, 1, cancellation.Token);
            var task = operation.AsTask();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
            Assert.True(task.IsCanceled);
            Assert.Empty(p.Created);
        }

        using var duringConstruction = new CancellationTokenSource();
        p = new RouteDependencyProbe
        {
            DuringConstruction = role =>
        {
            if (role == 0)
                duringConstruction.Cancel();
        }
        };
        using (var provider = RouteDependencyServices(f, p).BuildServiceProvider())
        using (var scope = provider.CreateScope())
        {
            var mediator = RouteDependencyMediator(f, scope.ServiceProvider);
            var task = f.Ping(mediator, 2, duringConstruction.Token).AsTask();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
            Assert.True(task.IsCanceled);
            Assert.Empty(p.Handled);
            // Native constructor injection does not promise cancellation between arguments.
        }
        AssertRouteDependencyDisposal(p);

        p = new RouteDependencyProbe();
        var failure = new InvalidOperationException("native construction failure");
        var services = RouteDependencyServices(f, p);
        services.AddTransient(f.Outer, _ =>
        {
            RouteDependencyProbe.Ambient.Value = "factory";
            throw failure;
        });
        var prior = RouteDependencyProbe.Ambient.Value;
        try
        {
            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            var mediator = RouteDependencyMediator(f, scope.ServiceProvider);
            RouteDependencyProbe.Ambient.Value = "caller";
            var operation = f.Ping(mediator, 3, default);
            Assert.Equal("caller", RouteDependencyProbe.Ambient.Value);
            Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => operation.AsTask()));
            Assert.Equal("caller", RouteDependencyProbe.Ambient.Value);
            Assert.Empty(p.Before);
        }
        finally
        {
            RouteDependencyProbe.Ambient.Value = prior;
        }
    }

    [Fact]
    public void Route_dependencies_only_cover_supported_closed_reference_hook_routes()
    {
        var eligible = RouteDependencyRuntimeFixture();
        Assert.Equal(2, eligible.Carriers.Length);
        Assert.All(eligible.Carriers, carrier => Assert.Single(carrier.GetConstructors()));
        IServiceCollection services = new ServiceCollection();
        eligible.Register(services);
        Assert.All(eligible.Carriers, carrier => Assert.Contains(services, descriptor => descriptor.ServiceType == carrier &&
            descriptor.ImplementationType == carrier && descriptor.Lifetime == ServiceLifetime.Transient));
        Assert.Single(RouteDependencyRuntimeFixture("sync-before").Carriers);
        foreach (var shape in new[] { "no-hook", "value-hook", "one-receiver" })
        {
            var fallback = RouteDependencyRuntimeFixture(shape);
            Assert.Empty(fallback.Carriers);
            Assert.DoesNotContain("RouteDependencies_", fallback.Generated, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Synchronous_before_dependencies_are_constructed_together_validated_and_owned_by_DI()
    {
        var f = RouteDependencyRuntimeFixture("sync-before");
        var p = new RouteDependencyProbe();
        var services = RouteDependencyServices(f, p);
        services.Remove(Assert.Single(services, descriptor => descriptor.ServiceType == f.Handler));
        var failure = Assert.Throws<AggregateException>(() => services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true }));
        Assert.Contains(f.Handler.FullName!, failure.Message, StringComparison.Ordinal);
        Assert.Empty(p.Created);
        using (var provider = RouteDependencyServices(f, p).BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }))
        using (var scope = provider.CreateScope())
        {
            var mediator = RouteDependencyMediator(f, scope.ServiceProvider);
            Assert.Empty(p.Created);
            Assert.Equal(10, await f.Ping(mediator, 10, default));
            Assert.Equal(new[] { "construct:5", "construct:3", "before:5", "handle:3" }, p.Trace);
            Assert.Equal(11, await f.Ping(mediator, 11, default));
            Assert.Equal(2, p.Created.Count);
            Assert.Same(p.Before.First().Receiver, p.Before.Last().Receiver);
            Assert.Same(p.Handled.First().Receiver, p.Handled.Last().Receiver);
            Assert.Empty(p.Released);
        }
        AssertRouteDependencyDisposal(p);
    }

    [Fact]
    public async Task Carrier_routes_keep_direct_Send_and_Stream_captures_for_shared_receiver_types()
    {
        var f = RouteDependencyRuntimeFixture("mixed");
        var p = new RouteDependencyProbe();
        using (var provider = RouteDependencyServices(f, p).BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }))
        using (var scope = provider.CreateScope())
        {
            var mediator = RouteDependencyMediator(f, scope.ServiceProvider);
            var directHandler = ((IServiceProvider)mediator).GetService(f.Handler);
            var directHook = ((IServiceProvider)mediator).GetService(f.SyncBefore);
            Assert.NotNull(directHandler);
            Assert.NotNull(directHook);
            Assert.Equal(20, f.Plain(mediator, 20));
            Assert.Same(directHandler, p.Handled.Last().Receiver);
            var items = new List<int>();
            await foreach (var item in f.Stream(mediator, 21))
                items.Add(item);
            Assert.Equal(new[] { 21 }, items);
            Assert.Same(directHook, p.Handled.Last().Receiver);
            Assert.Equal(22, await f.Ping(mediator, 22, default));
            var carrierHook = p.Before.Last().Receiver;
            var carrierHandler = p.Handled.Last().Receiver;
            Assert.NotSame(directHook, carrierHook);
            Assert.NotSame(directHandler, carrierHandler);
            Assert.Equal(23, await f.Ping(mediator, 23, default));
            Assert.Same(carrierHook, p.Before.Last().Receiver);
            Assert.Same(carrierHandler, p.Handled.Last().Receiver);
            Assert.Same(directHandler, ((IServiceProvider)mediator).GetService(f.Handler));
            Assert.Same(directHook, ((IServiceProvider)mediator).GetService(f.SyncBefore));
            Assert.Equal(4, p.Created.Count);
            Assert.Empty(p.Released);
        }
        AssertRouteDependencyDisposal(p);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Explicit_eager_synchronous_route_keeps_injected_identity_and_other_routes_use_carriers(bool eagerHook)
    {
        var f = RouteDependencyRuntimeFixture(eagerHook ? "sync-eager-hook" : "sync-eager-handler");
        var p = new RouteDependencyProbe { OuterError = new InvalidOperationException("eager Before failure") };
        using (var provider = RouteDependencyServices(f, p).BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }))
        using (var scope = provider.CreateScope())
        {
            var mediator = RouteDependencyMediator(f, scope.ServiceProvider);
            var injected = Assert.Single(p.Created);
            Assert.Equal(eagerHook ? 5 : 3, injected.Role);
            Assert.Single(f.Carriers);
            Assert.Same(p.OuterError, Assert.Throws<InvalidOperationException>(() => { _ = f.Ping(mediator, 29, default); }));
            Assert.Equal(eagerHook ? 1 : 2, p.Created.Count);
            Assert.Empty(p.Handled);
            p.OuterError = null;
            Assert.Equal(30, await f.Ping(mediator, 30, default));
            Assert.Equal(31, await f.Ping(mediator, 31, default));
            Assert.Equal(2, p.Created.Count);
            Assert.Same(injected.Receiver, eagerHook ? p.Before.First().Receiver : p.Handled.First().Receiver);
            Assert.Same(injected.Receiver, ((IServiceProvider)mediator).GetService(eagerHook ? f.SyncBefore : f.Handler));
            Assert.Equal(32, await f.Pong(mediator, 32, default));
            Assert.Equal(4, p.Created.Count);
            Assert.Empty(p.Released);
        }
        AssertRouteDependencyDisposal(p);
    }
}
