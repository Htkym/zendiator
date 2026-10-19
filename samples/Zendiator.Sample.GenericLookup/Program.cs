using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenericLookup.Generated.App;
using Microsoft.Extensions.DependencyInjection;
using Zendiator;
using Zendiator.DependencyInjection;

namespace GenericLookup;

public static class Program
{
    public static async Task Main()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IEntityStore<Product>>(new InMemoryEntityStore<Product>([new("p-7", "Keyboard")]));
        services.AddSingleton<IEntityStore<Customer>>(new InMemoryEntityStore<Customer>([new("c-3", "Ada")]));
        services.AddSingleton<IStatusSource>(new InventoryStatus(2));
        AppServices.Add(services);

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        Product product = await mediator.SendAsync(new Lookup<Product>("p-7"));
        Customer customer = await mediator.SendAsync(new Lookup<Customer>("c-3"));
        int status = await mediator.SendAsync(new StatusQuery());

        Console.WriteLine($"Product: {product.Name}");
        Console.WriteLine($"Customer: {customer.Name}");
        Console.WriteLine($"Status: {status}");
    }
}

public static class AppServices
{
    public static IServiceCollection Add(IServiceCollection services) => services.AddZendiator<App>(configuration =>
    {
        configuration.Handlers(typeof(Lookup<>), DispatchFamily.SendAsync, typeof(LookupHandler<>));
        configuration.Handlers<StatusQuery>(DispatchFamily.SendAsync, typeof(StatusHandler));
    });
}

[ZendiatorComposition("genericLookup")]
public sealed class App;

public interface IEntity
{
    string Id { get; }
}

public sealed record Product(string Id, string Name) : IEntity;
public sealed record Customer(string Id, string Name) : IEntity;
public sealed record Lookup<T>(string Id) : IRequest<T> where T : class;
public sealed record StatusQuery : IRequest<int>;

public interface IEntityStore<T> where T : class, IEntity
{
    ValueTask<T> FindAsync(string id, CancellationToken cancellationToken);
}

public sealed class InMemoryEntityStore<T>(IEnumerable<T> entities) : IEntityStore<T> where T : class, IEntity
{
    private readonly Dictionary<string, T> _entities = entities.ToDictionary(entity => entity.Id);

    public ValueTask<T> FindAsync(string id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new(_entities.TryGetValue(id, out var entity) ? entity : throw new KeyNotFoundException(id));
    }
}

[ZendiatorHandler]
public sealed class LookupHandler<T>(IEntityStore<T> store) : IRequestHandler<Lookup<T>, T> where T : class, IEntity
{
    public ValueTask<T> HandleAsync(Lookup<T> request, CancellationToken cancellationToken) => store.FindAsync(request.Id, cancellationToken);
}

public interface IStatusSource
{
    int ActiveCount { get; }
}

public sealed record InventoryStatus(int ActiveCount) : IStatusSource;

[ZendiatorHandler]
public sealed class StatusHandler(IStatusSource status) : IRequestHandler<StatusQuery, int>
{
    public ValueTask<int> HandleAsync(StatusQuery request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new(status.ActiveCount);
    }
}
