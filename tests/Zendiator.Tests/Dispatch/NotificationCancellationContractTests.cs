using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Tests;

public sealed record FaultedCancellationNotification : INotification;

public sealed class FaultedCancellationNotificationHandler : INotificationHandler<FaultedCancellationNotification>
{
    public ValueTask HandleAsync(FaultedCancellationNotification notification, CancellationToken cancellationToken)
        => new(Task.FromException(new OperationCanceledException(cancellationToken)));
}

public sealed class NotificationCancellationContractTests
{
    [Fact]
    public async Task Single_subscriber_faulted_task_with_cancellation_exception_reports_canceled_publish()
    {
        var services = new ServiceCollection();
        services.AddZendiator();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        using var cancellation = new CancellationTokenSource();

        var pending = scope.ServiceProvider.GetRequiredService<IZendiator>()
            .PublishAsync(new FaultedCancellationNotification(), cancellation.Token);

        Assert.True(pending.IsCanceled);
        Assert.False(pending.IsFaulted);
        var observed = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
        Assert.Equal(cancellation.Token, observed.CancellationToken);
    }
}
