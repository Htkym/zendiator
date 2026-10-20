using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ComposedApp.Generated.App;
using Microsoft.Extensions.DependencyInjection;
using Zendiator;
using Zendiator.DependencyInjection;

namespace ComposedApp;

public static class Program
{
    public static async Task Main()
    {
        var services = new ServiceCollection();
        services.AddScoped<IInventory, InMemoryInventory>();
        services.AddSingleton<IEntityStore<Product>>(new ProductStore(new("p-7", "Keyboard")));
        AppServices.Add(services);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();

        Product product = await mediator.SendAsync(new Lookup<Product>("p-7"));
        await mediator.SendAsync(new RefreshInventory(3));
        var sum = mediator.Send(new Sum<Product>(20, 21));
        var count = await mediator.SendAsync(new CachedCount<Product>("current"));
        var collected = mediator.SendAll(new CollectCounts<Product>(1));
        var read = await mediator.SendAllAsync(new ReadCounts<Product>(1));
        var numbers = new List<int>();
        await foreach (var item in mediator.StreamAsync(new ReadNumbers(6))) numbers.Add(item);

        Console.WriteLine($"Product: {product.Name}");
        Console.WriteLine($"Sum: {sum}");
        Console.WriteLine($"Cached count: {count}");
        Console.WriteLine($"SendAll: {string.Join(", ", collected)}");
        Console.WriteLine($"SendAllAsync: {string.Join(", ", read)}");
        Console.WriteLine($"StreamAsync: {string.Join(", ", numbers)}");
    }
}

[ZendiatorComposition("composedApp")]
public sealed class App;

public static class AppServices
{
    public static IServiceCollection Add(IServiceCollection services) => services.AddZendiator<App>(configuration =>
    {
        configuration.Handlers(typeof(Lookup<>), DispatchFamily.SendAsync, typeof(LookupHandler<>));
        configuration.Handlers<RefreshInventory>(DispatchFamily.SendAsync, typeof(RefreshInventoryHandler));
        configuration.Handlers(typeof(Sum<>), DispatchFamily.SendSync, typeof(SumHandler<>));
        configuration.Pipeline(typeof(Sum<>), DispatchFamily.SendSync, Stage.Hook(typeof(SumHook<>)));
        configuration.Handlers(typeof(CachedCount<>), DispatchFamily.SendAsync, typeof(CachedCountHandler<>));
        configuration.Pipeline(typeof(CachedCount<>), DispatchFamily.SendAsync, Stage.Wrapper(typeof(CountCache<>)));
        configuration.Handlers(typeof(CollectCounts<>), DispatchFamily.SendAllSync, typeof(CachedCollector<>), typeof(WarehouseCollector<>));
        configuration.Pipeline(typeof(CollectCounts<>), DispatchFamily.SendAllSync, Stage.Hook(typeof(CollectHook<>)));
        configuration.Handlers(typeof(ReadCounts<>), DispatchFamily.SendAllAsync, typeof(PrimaryReader<>), typeof(ReplicaReader<>));
        configuration.Pipeline(typeof(ReadCounts<>), DispatchFamily.SendAllAsync, Stage.Hook(typeof(ReadHook<>)));
        configuration.Handlers<ReadNumbers>(DispatchFamily.Stream, typeof(ReadNumbersHandler));
        configuration.Pipeline<ReadNumbers>(DispatchFamily.Stream, Stage.Hook<KeepEven>(), Stage.Hook<TimesTen>(), Stage.Hook<KeepAtMostForty>());
    });
}

public interface IEntity { string Id { get; } }
public sealed record Product(string Id, string Name) : IEntity;
public sealed record Lookup<T>(string Id) : IRequest<T> where T : class;
public interface IEntityStore<T> where T : class, IEntity
{
    ValueTask<T> FindAsync(string id, CancellationToken token);
}
public sealed class ProductStore(Product product) : IEntityStore<Product>
{
    public ValueTask<Product> FindAsync(string id, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return product.Id == id ? new(product) : ValueTask.FromException<Product>(new KeyNotFoundException(id));
    }
}
[ZendiatorHandler]
public sealed class LookupHandler<T>(IEntityStore<T> store) : IRequestHandler<Lookup<T>, T> where T : class, IEntity
{
    public ValueTask<T> HandleAsync(Lookup<T> request, CancellationToken token) => store.FindAsync(request.Id, token);
}

public interface IInventory
{
    int CachedCount { get; }
    int WarehouseCount { get; }
    ValueTask RefreshAsync(int count, CancellationToken token);
    ValueTask<int> ReadPrimaryAsync(CancellationToken token);
    ValueTask<int> ReadReplicaAsync(CancellationToken token);
}
public sealed class InMemoryInventory : IInventory
{
    private int _count = 2;
    public int CachedCount => _count;
    public int WarehouseCount => _count + 1;
    public ValueTask RefreshAsync(int count, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        _count = count;
        return ValueTask.CompletedTask;
    }
    public ValueTask<int> ReadPrimaryAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return new(_count);
    }
    public ValueTask<int> ReadReplicaAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return new(_count + 1);
    }
}
public sealed record RefreshInventory(int Count) : IRequest;
[ZendiatorHandler]
public sealed class RefreshInventoryHandler(IInventory inventory) : IRequestHandler<RefreshInventory>
{
    public ValueTask HandleAsync(RefreshInventory request, CancellationToken token) => inventory.RefreshAsync(request.Count, token);
}

public sealed record Sum<TMarker>(int Left, int Right) : ISyncRequest<int> where TMarker : class;
[ZendiatorHandler]
public sealed class SumHandler<TMarker> : ISyncRequestHandler<Sum<TMarker>, int> where TMarker : class
{
    public int Handle(Sum<TMarker> request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return request.Left + request.Right;
    }
}
public sealed class SumHook<TMarker> : ISendRequestTransform<Sum<TMarker>>, ISendResponseTransform<Sum<TMarker>, int>, ISendExceptionObserver<Sum<TMarker>>, ISendFinallyObserver<Sum<TMarker>> where TMarker : class
{
    public Sum<TMarker> Before(Sum<TMarker> request, CancellationToken token) => request with { Right = request.Right + 1 };
    public int After(Sum<TMarker> request, int response, CancellationToken token) => response;
    public void OnException(Sum<TMarker> request, Exception error, CancellationToken token) { }
    public void Finally(Sum<TMarker> request, SendExitInfo exit, CancellationToken token) { }
}

public sealed record CachedCount<TMarker>(string Key) : IRequest<int> where TMarker : class;
public sealed class TransientCountException : Exception;
[ZendiatorHandler]
public sealed class CachedCountHandler<TMarker>(IInventory inventory) : IRequestHandler<CachedCount<TMarker>, int> where TMarker : class
{
    public ValueTask<int> HandleAsync(CachedCount<TMarker> request, CancellationToken token) => inventory.ReadPrimaryAsync(token);
}
public sealed class CountCache<TMarker> : IAsyncSendWrapper<CachedCount<TMarker>, int> where TMarker : class
{
    private readonly Dictionary<string, int> _values = new();
    public async ValueTask<int> InvokeAsync<TNext>(CachedCount<TMarker> request, TNext next, CancellationToken token)
        where TNext : IAsyncSendContinuation<CachedCount<TMarker>, int>
    {
        if (_values.TryGetValue(request.Key, out var cached)) return cached;
        int result;
        try { result = await next.InvokeAsync(request, token).ConfigureAwait(false); }
        catch (TransientCountException) when (!token.IsCancellationRequested)
        {
            result = await next.InvokeAsync(request, token).ConfigureAwait(false);
        }
        _values.Add(request.Key, result);
        return result;
    }
}

public sealed record CollectCounts<TMarker>(int Offset) : ISyncMultiRequest<int> where TMarker : class;
[ZendiatorHandler]
public sealed class CachedCollector<TMarker>(IInventory inventory) : ISyncRequestHandler<CollectCounts<TMarker>, int> where TMarker : class
{
    public int Handle(CollectCounts<TMarker> request, CancellationToken token) { token.ThrowIfCancellationRequested(); return inventory.CachedCount + request.Offset; }
}
[ZendiatorHandler]
public sealed class WarehouseCollector<TMarker>(IInventory inventory) : ISyncRequestHandler<CollectCounts<TMarker>, int> where TMarker : class
{
    public int Handle(CollectCounts<TMarker> request, CancellationToken token) { token.ThrowIfCancellationRequested(); return inventory.WarehouseCount + request.Offset; }
}
public sealed class CollectHook<TMarker> : ISendRequestTransform<CollectCounts<TMarker>>, ISendResponseTransform<CollectCounts<TMarker>, int>, ISendExceptionObserver<CollectCounts<TMarker>>, ISendFinallyObserver<CollectCounts<TMarker>> where TMarker : class
{
    public CollectCounts<TMarker> Before(CollectCounts<TMarker> request, CancellationToken token) => request with { Offset = request.Offset + 1 };
    public int After(CollectCounts<TMarker> request, int response, CancellationToken token) => response + 10;
    public void OnException(CollectCounts<TMarker> request, Exception error, CancellationToken token) { }
    public void Finally(CollectCounts<TMarker> request, SendExitInfo exit, CancellationToken token) { }
}

public sealed record ReadCounts<TMarker>(int Offset) : IRequest<int> where TMarker : class;
[ZendiatorHandler]
public sealed class PrimaryReader<TMarker>(IInventory inventory) : IRequestHandler<ReadCounts<TMarker>, int> where TMarker : class
{
    public async ValueTask<int> HandleAsync(ReadCounts<TMarker> request, CancellationToken token) => await inventory.ReadPrimaryAsync(token).ConfigureAwait(false) + request.Offset;
}
[ZendiatorHandler]
public sealed class ReplicaReader<TMarker>(IInventory inventory) : IRequestHandler<ReadCounts<TMarker>, int> where TMarker : class
{
    public async ValueTask<int> HandleAsync(ReadCounts<TMarker> request, CancellationToken token) => await inventory.ReadReplicaAsync(token).ConfigureAwait(false) + request.Offset;
}
public sealed class ReadHook<TMarker> : IAsyncSendRequestTransform<ReadCounts<TMarker>>, IAsyncSendResponseTransform<ReadCounts<TMarker>, int>, IAsyncSendExceptionObserver<ReadCounts<TMarker>>, IAsyncSendFinallyObserver<ReadCounts<TMarker>> where TMarker : class
{
    public ValueTask<ReadCounts<TMarker>> BeforeAsync(ReadCounts<TMarker> request, CancellationToken token) => new(request with { Offset = request.Offset + 1 });
    public ValueTask<int> AfterAsync(ReadCounts<TMarker> request, int response, CancellationToken token) => new(response + 20);
    public ValueTask OnExceptionAsync(ReadCounts<TMarker> request, Exception error, CancellationToken token) => ValueTask.CompletedTask;
    public ValueTask FinallyAsync(ReadCounts<TMarker> request, SendExitInfo exit, CancellationToken token) => ValueTask.CompletedTask;
}

public sealed record ReadNumbers(int Count) : IStreamRequest<int>;
[ZendiatorHandler]
public sealed class ReadNumbersHandler(IInventory inventory) : IStreamRequestHandler<ReadNumbers, int>
{
    public async IAsyncEnumerable<int> HandleAsync(ReadNumbers request, [EnumeratorCancellation] CancellationToken token)
    {
        _ = inventory.CachedCount;
        await Task.Yield();
        for (var item = 1; item <= request.Count; item++) { token.ThrowIfCancellationRequested(); yield return item; }
    }
}
public sealed class KeepEven : IStreamItemFilter<ReadNumbers, int>
{
    public bool Accept(ReadNumbers request, int item, CancellationToken token) => item % 2 == 0;
}
public sealed class TimesTen : IStreamItemMap<ReadNumbers, int>
{
    public int Map(ReadNumbers request, int item, CancellationToken token) => item * 10;
}
public sealed class KeepAtMostForty : IStreamItemFilter<ReadNumbers, int>
{
    public bool Accept(ReadNumbers request, int item, CancellationToken token) => item <= 40;
}
