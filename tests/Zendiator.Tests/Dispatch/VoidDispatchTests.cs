using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Tests;

public sealed class VoidDispatchTests
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
    public async Task Void_command_runs_without_unit()
    {
        DeleteUserHandler.Deleted.Clear();
        await using var provider = Services().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        await mediator.SendAsync(new DeleteUser(7));
        Assert.Equal([7], DeleteUserHandler.Deleted);
        Assert.Equal(["cmd", "deleted:7", "/cmd"], scope.ServiceProvider.GetRequiredService<Trace>().Events);
    }

    [Fact]
    public async Task Void_short_circuit_does_not_construct_handler()
    {
        GuardedDeleteHandler.Calls = 0;
        var services = Services();
        services.AddScoped<GuardedDeleteHandler>(_ => throw new InvalidOperationException("must not construct"));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IZendiator>().SendAsync(new GuardedDelete(0));
        Assert.Equal(0, GuardedDeleteHandler.Calls);
    }

    [Fact]
    public async Task Void_handler_runs_when_not_short_circuited()
    {
        GuardedDeleteHandler.Calls = 0;
        await using var provider = Services().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IZendiator>().SendAsync(new GuardedDelete(3));
        Assert.Equal(1, GuardedDeleteHandler.Calls);
    }

    [Fact]
    public async Task Void_retry_invokes_a_fresh_continuation_sequentially()
    {
        FlakyHandler.Calls = 0;
        await using var provider = Services().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IZendiator>().SendAsync(new FlakyCommand());
        Assert.Equal(2, FlakyHandler.Calls);
    }

    [Fact]
    public async Task Void_exceptions_and_cancellation_flow()
    {
        await using var provider = Services().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        Assert.Same(BoomHandler.Failure, await Assert.ThrowsAsync<InvalidOperationException>(async () => await mediator.SendAsync(new Boom())));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await mediator.SendAsync(new DeleteUser(1), cancellation.Token));
    }

    [Fact]
    public async Task Void_behavior_can_replace_the_token_downstream()
    {
        SeenTokenHandler.Seen = default;
        await using var provider = Services().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IZendiator>().SendAsync(new SeenToken());
        Assert.NotEqual(CancellationToken.None, SeenTokenHandler.Seen);
    }

    [Fact]
    public async Task Void_behavior_can_replace_the_request_downstream()
    {
        TopUpHandler.Seen = -1;
        await using var provider = Services().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IZendiator>().SendAsync(new TopUp(1));
        Assert.Equal(11, TopUpHandler.Seen);
    }

    [Fact]
    public async Task In_flight_void_operation_honors_cancellation()
    {
        await using var provider = Services().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var pending = mediator.SendAsync(new LatchCommand(gate), cancellation.Token);
        Assert.False(pending.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
        gate.TrySetResult();
    }
}
