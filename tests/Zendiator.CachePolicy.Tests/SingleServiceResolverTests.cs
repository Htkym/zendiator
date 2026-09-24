using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Zendiator.DependencyInjection;

namespace Zendiator.CachePolicy.Tests;

public sealed class SingleServiceResolverTests
{
    private static IServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddScoped<ZendiatorServiceResolver>();
        services.AddScoped<ZendiatorSingleServiceResolver<Probe>>();
        return services;
    }

    private static (Func<Probe> Get, object Resolver) Resolve(IServiceProvider provider, bool specialized)
    {
        if (specialized)
        {
            var resolver = provider.GetRequiredService<ZendiatorSingleServiceResolver<Probe>>();
            return (resolver.GetRequiredService, resolver);
        }
        var general = provider.GetRequiredService<ZendiatorServiceResolver>();
        return (general.GetRequiredService<Probe>, general);
    }

    [Theory]
    [InlineData(false, ServiceLifetime.Scoped)]
    [InlineData(true, ServiceLifetime.Scoped)]
    [InlineData(false, ServiceLifetime.Transient)]
    [InlineData(true, ServiceLifetime.Transient)]
    [InlineData(false, ServiceLifetime.Singleton)]
    [InlineData(true, ServiceLifetime.Singleton)]
    public void Capture_preserves_dependency_lifetime_and_disposal(bool specialized, ServiceLifetime lifetime)
    {
        var services = Services();
        var calls = 0;
        services.Add(ServiceDescriptor.Describe(typeof(Probe), _ => { calls++; return new Probe(41); }, lifetime));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var capture = Resolve(scope.ServiceProvider, specialized);
        Assert.Equal(0, calls);
        var value = capture.Get();
        Assert.Equal(41, value.Id);
        Assert.Same(value, capture.Get());
        Assert.Equal(1, calls);
        Assert.Equal(0, value.DisposeCalls);
        scope.Dispose();
        Assert.Equal(lifetime == ServiceLifetime.Singleton ? 0 : 1, value.DisposeCalls);
        provider.Dispose();
        Assert.Equal(1, value.DisposeCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Same_service_factory_reentrancy_keeps_the_first_published_value(bool specialized)
    {
        var services = Services();
        Func<Probe>? get = null;
        Probe? inner = null;
        var calls = 0;
        services.AddTransient<Probe>(_ =>
        {
            var id = ++calls;
            if (id == 1) inner = get!();
            return new Probe(id);
        });
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        get = Resolve(scope.ServiceProvider, specialized).Get;
        var outer = get();
        Assert.Equal(1, outer.Id);
        Assert.Equal(2, inner!.Id);
        Assert.Same(inner, get());
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_first_use_captures_one_transient(bool specialized)
    {
        var services = Services();
        var calls = 0;
        services.AddTransient<Probe>(_ => new Probe(Interlocked.Increment(ref calls)));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var capture = Resolve(scope.ServiceProvider, specialized);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            await start.Task;
            return capture.Get();
        })).ToArray();
        start.SetResult();
        var results = await Task.WhenAll(tasks);
        Assert.All(results, result => Assert.Same(results[0], result));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Concurrent_waiters_retry_after_a_failed_single_service_activation()
    {
        var services = Services();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var calls = 0;
        services.AddTransient<Probe>(_ =>
        {
            var id = Interlocked.Increment(ref calls);
            if (id == 1)
            {
                entered.SetResult();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
                throw new FormatException("activation");
            }
            return new Probe(id);
        });
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var capture = scope.ServiceProvider.GetRequiredService<ZendiatorSingleServiceResolver<Probe>>();
        var first = Task.Run(() => Assert.Throws<FormatException>(capture.GetRequiredService));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;
        var waiters = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            if (Interlocked.Increment(ref started) == 8) ready.SetResult();
            return capture.GetRequiredService();
        })).ToArray();
        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            release.Set();
            await first.WaitAsync(TimeSpan.FromSeconds(10));
            var results = await Task.WhenAll(waiters).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.All(results, result => Assert.Same(results[0], result));
            Assert.Equal(2, calls);
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public async Task Separate_single_service_resolvers_initialize_independently()
    {
        var services = Services();
        var bothEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var entered = 0;
        services.AddTransient<Probe>(_ =>
        {
            var id = Interlocked.Increment(ref entered);
            if (id == 2) bothEntered.SetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            return new Probe(id);
        });
        using var provider = services.BuildServiceProvider();
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();
        var first = new ZendiatorSingleServiceResolver<Probe>(firstScope.ServiceProvider);
        var second = new ZendiatorSingleServiceResolver<Probe>(secondScope.ServiceProvider);
        var firstTask = Task.Run(first.GetRequiredService);
        var secondTask = Task.Run(second.GetRequiredService);
        try
        {
            await bothEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            release.Set();
            Assert.NotSame(await firstTask.WaitAsync(TimeSpan.FromSeconds(10)),
                await secondTask.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally
        {
            release.Set();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Failed_activation_is_not_captured(bool specialized)
    {
        var services = Services();
        var calls = 0;
        services.AddTransient<Probe>(_ => ++calls == 1 ? throw new FormatException("activation") : new Probe(2));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var capture = Resolve(scope.ServiceProvider, specialized);
        Assert.Throws<FormatException>(() => capture.Get());
        var second = capture.Get();
        Assert.Same(second, capture.Get());
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task External_monitor_does_not_block_single_service_initialization()
    {
        var services = Services();
        services.AddTransient<Probe>(_ => new Probe(41));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var capture = Resolve(scope.ServiceProvider, true);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var owner = Task.Run(() =>
        {
            lock (capture.Resolver)
            {
                entered.SetResult();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Initialization used the public monitor.");
            }
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try { Assert.Equal(41, capture.Get().Id); }
        finally
        {
            release.Set();
            await owner;
        }
    }

    [Fact]
    public void Single_service_construction_stays_within_72_bytes()
    {
        using var provider = Services().BuildServiceProvider();
        var retained = new ZendiatorSingleServiceResolver<Probe>[1024];
        _ = new ZendiatorSingleServiceResolver<Probe>(provider);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < retained.Length; i++) retained[i] = new(provider);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.InRange(allocated, 1, 72L * retained.Length);
        GC.KeepAlive(retained);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Warm_capture_allocates_nothing(bool specialized)
    {
        var services = Services();
        services.AddScoped<Probe>(_ => new Probe(41));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var capture = Resolve(scope.ServiceProvider, specialized);
        var first = capture.Get();
        for (var i = 0; i < 1024; i++) capture.Get();
        Probe? last = null;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1024; i++) last = capture.Get();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Same(first, last);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void Constructors_preserve_null_argument_guards()
    {
        Assert.Equal("provider", Assert.Throws<ArgumentNullException>(() => new ZendiatorSingleServiceResolver<Probe>(null!)).ParamName);
    }

    [Fact]
    public void Typed_capture_reserves_its_slot_before_a_factory_resolves_another_type()
    {
        var services = Services();
        services.AddScoped<SlotSecond>();
        services.AddScoped<SlotFirst>(provider =>
        {
            _ = provider.GetRequiredService<ZendiatorServiceResolver>().GetRequiredService<SlotSecond>();
            return new();
        });
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var capture = new ZendiatorSingleServiceResolver<SlotFirst>(scope.ServiceProvider);
        _ = capture.GetRequiredService();

        static int Slot(Type type) => (int)typeof(ZendiatorServiceResolver)
            .GetNestedType("ServiceSlot`1", System.Reflection.BindingFlags.NonPublic)!
            .MakeGenericType(type).GetField("Index", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(null)!;

        Assert.True(Slot(typeof(SlotFirst)) < Slot(typeof(SlotSecond)));
    }

    private sealed class SlotFirst;
    private sealed class SlotSecond;

    private sealed class Probe(int id) : IDisposable
    {
        internal int Id => id;
        internal int DisposeCalls;
        public void Dispose() => Interlocked.Increment(ref DisposeCalls);
    }
}
