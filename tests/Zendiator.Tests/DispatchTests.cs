using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Tests;

public sealed class DispatchTests
{
    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddScoped<Trace>();
        services.AddScoped<AuditLog>();
        services.AddScoped<Gate>();
        services.AddZendiator();
        return services;
    }

    [Fact]
    public async Task Typed_requests_and_three_behaviors_preserve_order()
    {
        await using var provider = Services().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        Assert.Same(mediator, scope.ServiceProvider.GetRequiredService<Zendiator>());
        Assert.Equal(7, await mediator.SendAsync(new Sum(3, 4)));
        Assert.Equal(["outer", "middle", "inner", "handler", "/inner", "/middle", "/outer"], scope.ServiceProvider.GetRequiredService<Trace>().Events);
        Assert.Null(await mediator.SendAsync(new Echo(null)));
        Assert.Equal("hello", await mediator.SendAsync(new Echo("hello")));
        Assert.Equal(global::Zendiator.Unit.Value, await mediator.SendAsync(new Complete()));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await mediator.SendAsync((Echo)null!));
    }

    [Fact]
    public async Task Suspended_operations_cancellation_and_exceptions_are_preserved()
    {
        await using var provider = Services().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var source = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = mediator.SendAsync(new Delayed(source.Task));
        Assert.False(operation.IsCompleted);
        source.SetResult(42);
        Assert.Equal(42, await operation);
        using var cancellation = new CancellationTokenSource();
        var pending = mediator.SendAsync(new Delayed(new TaskCompletionSource<int>().Task), cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await mediator.SendAsync(new Sum(), cancellation.Token));
        var error = new InvalidOperationException("original");
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(async () => await mediator.SendAsync(new Fail(error))));
    }

    [Fact]
    public async Task Short_circuit_does_not_construct_handler_and_retry_is_sequential()
    {
        var services = Services();
        services.AddScoped<ValidateHandler>(_ => throw new InvalidOperationException("must not construct"));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        Assert.False((await mediator.SendAsync(new Validate(false))).Success);
        Assert.Equal(2, await mediator.SendAsync(new Retry()));
        using var replacement = new CancellationTokenSource();
        Assert.Equal(replacement.Token, await mediator.SendAsync(new ChangeToken(replacement.Token)));
        replacement.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await mediator.SendAsync(new ChangeToken(replacement.Token)));
    }

    [Fact]
    public async Task Scope_lifetimes_disposal_and_transient_overrides_are_respected()
    {
        var services = Services();
        var count = services.Count;
        services.AddZendiator();
        Assert.Equal(count, services.Count);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        IdentityHandler handler;
        Guid first;
        await using (var scope = provider.CreateAsyncScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
            first = await mediator.SendAsync(new Identity());
            Assert.Equal(first, await mediator.SendAsync(new Identity()));
            handler = scope.ServiceProvider.GetRequiredService<IdentityHandler>();
        }
        Assert.True(handler.Disposed);
        await using (var scope = provider.CreateAsyncScope())
            Assert.NotEqual(first, await scope.ServiceProvider.GetRequiredService<IZendiator>().SendAsync(new Identity()));

        var transient = new ServiceCollection();
        transient.AddTransient<IdentityHandler>();
        transient.AddScoped<Trace>();
        transient.AddScoped<AuditLog>();
        transient.AddScoped<Gate>();
        transient.AddZendiator();
        await using var other = transient.BuildServiceProvider();
        await using var otherScope = other.CreateAsyncScope();
        var otherMediator = otherScope.ServiceProvider.GetRequiredService<IZendiator>();
        Assert.NotEqual(await otherMediator.SendAsync(new Identity()), await otherMediator.SendAsync(new Identity()));
    }
}
