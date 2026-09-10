using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Tests;

public sealed class GenericDispatchTests
{
    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddScoped<Trace>();
        services.AddScoped<AuditLog>();
        services.AddScoped<Gate>();
        services.AddScoped<IRepository<User>, UserRepository>();
        services.AddScoped<IRepository<Product>, ProductRepository>();
        services.AddZendiator();
        return services;
    }

    private static ValueTask<T> Load<T>(IZendiator sender, int id, CancellationToken cancellationToken)
        where T : class =>
        sender.SendAsync(new GetById<T>(id), cancellationToken);

    [Fact]
    public async Task Closed_constructions_resolve_through_the_open_route()
    {
        UserRepository.Calls = 0;
        ProductRepository.Calls = 0;
        await using var provider = Services().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var user = await mediator.SendAsync(new GetById<User>(1));
        var product = await mediator.SendAsync(new GetById<Product>(2));
        Assert.Equal(new User(1, "user-1"), user);
        Assert.Equal(new Product(2, "code-2"), product);
        Assert.Equal(1, UserRepository.Calls);
        Assert.Equal(1, ProductRepository.Calls);
        Assert.Equal(
            ["outer", "middle", "inner", "/inner", "/middle", "/outer", "outer", "middle", "inner", "/inner", "/middle", "/outer"],
            scope.ServiceProvider.GetRequiredService<Trace>().Events);
    }

    [Fact]
    public async Task Unbound_type_arguments_flow_from_the_caller()
    {
        await using var provider = Services().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        Assert.Equal(new User(3, "user-3"), await Load<User>(mediator, 3, CancellationToken.None));
        Assert.Equal(new Product(4, "code-4"), await Load<Product>(mediator, 4, CancellationToken.None));
    }

    [Fact]
    public async Task Closed_types_keep_separate_scoped_services()
    {
        await using var provider = Services().BuildServiceProvider();
        Guid first;
        await using (var scope = provider.CreateAsyncScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
            first = scope.ServiceProvider.GetRequiredService<GetByIdHandler<User>>().Id;
            Assert.Equal(first, scope.ServiceProvider.GetRequiredService<GetByIdHandler<User>>().Id);
            Assert.NotEqual(first, scope.ServiceProvider.GetRequiredService<GetByIdHandler<Product>>().Id);
            await mediator.SendAsync(new GetById<User>(1));
        }
        await using (var scope = provider.CreateAsyncScope())
        {
            Assert.NotEqual(first, scope.ServiceProvider.GetRequiredService<GetByIdHandler<User>>().Id);
        }
    }

    [Fact]
    public async Task Closed_factory_overrides_win_for_their_construction()
    {
        var services = Services();
        var fixedUser = new User(9, "fixed");
        services.AddScoped<GetByIdHandler<User>>(_ => new GetByIdHandler<User>(new FixedUserRepository(fixedUser)));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        Assert.Equal(fixedUser, await mediator.SendAsync(new GetById<User>(9)));
        Assert.Equal(new Product(9, "code-9"), await mediator.SendAsync(new GetById<Product>(9)));
    }

    [Fact]
    public async Task Generic_void_commands_run_without_unit()
    {
        DeleteEntitiesHandler<User>.Deleted.Clear();
        await using var provider = Services().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        await mediator.SendAsync(new DeleteEntities<User>([1, 2]));
        Assert.Equal(["User:1", "User:2"], DeleteEntitiesHandler<User>.Deleted);
        Assert.Equal(["cmd", "/cmd"], scope.ServiceProvider.GetRequiredService<Trace>().Events);
    }

    private sealed class FixedUserRepository(User user) : IRepository<User>
    {
        public ValueTask<User> GetAsync(int id, CancellationToken cancellationToken) => new(user);
    }
}
