using Di.Generated;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.DiConfiguration.Tests;

[Collection("DI runtime")]
public sealed class RegressionTests
{
    [Fact]
    public async Task Mixed_handler_contracts_dispatch_through_the_correct_implementation()
    {
        using var provider = DiRuntimeTests.CreateServices().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        Assert.Equal(1, await mediator.SendAsync(new MixedFirst()));
        Assert.Equal(7, await mediator.SendAsync(new MixedSecond()));
        Assert.Equal(7, mediator.SendSync(new MixedSyncSecond()));
        await mediator.PublishAsync(new MixedNoteSecond());
        Assert.Equal(7, scope.ServiceProvider.GetRequiredService<MixedHandler>().NotificationResult);
        var items = new List<int>();
        await foreach (var item in mediator.StreamAsync(new MixedStreamSecond())) items.Add(item);
        Assert.Equal([7], items);
    }

    [Fact]
    public async Task Constructor_constrained_response_keeps_its_behavior()
    {
        NewResponseBehavior<Construct<ConstructedItem>, ConstructedItem>.Calls = 0;
        using var provider = DiRuntimeTests.CreateServices().BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.NotNull(await scope.ServiceProvider.GetRequiredService<IZendiator>().SendAsync(new Construct<ConstructedItem>()));
        Assert.Equal(1, NewResponseBehavior<Construct<ConstructedItem>, ConstructedItem>.Calls);
    }

    [Fact]
    public async Task Closed_generic_configuration_matches_reference_assembly_compilation()
    {
        ClosedBehavior<Add, int>.Calls = 0;
        using var provider = DiRuntimeTests.CreateServices(staticForm: true).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        Assert.Equal(3, await mediator.SendAsync(new Add(1, 2)));
        Assert.Equal(1, ClosedBehavior<Add, int>.Calls);
        await mediator.PublishAsync(new ClosedNote<int>());
    }

    [Fact]
    public void Generic_sync_void_and_combined_ref_constraints_dispatch()
    {
        using var provider = DiRuntimeTests.CreateServices().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        mediator.SendSync(new GenericReset<int>());
        Assert.Equal(1, scope.ServiceProvider.GetRequiredService<GenericResetHandler<int>>().Calls);
        mediator.SendAllSync(new GenericResetAll<int>());
        Assert.Equal(1, scope.ServiceProvider.GetRequiredService<GenericResetAllHandler<int>>().Calls);
        Assert.Equal(17, mediator.SendSync(new ConstrainedBox<Span<byte>>()));
    }
}

public sealed record MixedFirst : IRequest<int>;
public sealed record MixedSecond : IRequest<int>;
public sealed record MixedSyncFirst : ISyncRequest<int>;
public sealed record MixedSyncSecond : ISyncRequest<int>;
public sealed record MixedNoteFirst : INotification;
public sealed record MixedNoteSecond : INotification;
public sealed record MixedStreamFirst : IStreamRequest<int>;
public sealed record MixedStreamSecond : IStreamRequest<int>;

public sealed class MixedHandler : IRequestHandler<MixedFirst, int>, IRequestHandler<MixedSecond, int>,
    ISyncRequestHandler<MixedSyncFirst, int>, ISyncRequestHandler<MixedSyncSecond, int>,
    INotificationHandler<MixedNoteFirst>, INotificationHandler<MixedNoteSecond>,
    IStreamRequestHandler<MixedStreamFirst, int>, IStreamRequestHandler<MixedStreamSecond, int>
{
    public int NotificationResult { get; private set; }
    public ValueTask<int> HandleAsync(MixedFirst request, CancellationToken cancellationToken) => new(1);
    ValueTask<int> IRequestHandler<MixedSecond, int>.HandleAsync(MixedSecond request, CancellationToken cancellationToken) => new(7);
    public ValueTask<int> HandleAsync(MixedSecond request, CancellationToken cancellationToken) => new(99);
    public int Handle(MixedSyncFirst request, CancellationToken cancellationToken) => 1;
    int ISyncRequestHandler<MixedSyncSecond, int>.Handle(MixedSyncSecond request, CancellationToken cancellationToken) => 7;
    public int Handle(MixedSyncSecond request, CancellationToken cancellationToken) => 99;
    public ValueTask HandleAsync(MixedNoteFirst notification, CancellationToken cancellationToken) => default;
    ValueTask INotificationHandler<MixedNoteSecond>.HandleAsync(MixedNoteSecond notification, CancellationToken cancellationToken)
    {
        NotificationResult = 7;
        return default;
    }
    public ValueTask HandleAsync(MixedNoteSecond notification, CancellationToken cancellationToken)
    {
        NotificationResult = 99;
        return default;
    }
    public IAsyncEnumerable<int> HandleAsync(MixedStreamFirst request, CancellationToken cancellationToken) => Item(1);
    IAsyncEnumerable<int> IStreamRequestHandler<MixedStreamSecond, int>.HandleAsync(MixedStreamSecond request, CancellationToken cancellationToken) => Item(7);
    public IAsyncEnumerable<int> HandleAsync(MixedStreamSecond request, CancellationToken cancellationToken) => Item(99);
    private static async IAsyncEnumerable<int> Item(int value)
    {
        await Task.CompletedTask;
        yield return value;
    }
}

public sealed class ConstructedItem;
public sealed record Construct<T> : IRequest<T> where T : new();
public sealed class ConstructHandler<T> : IRequestHandler<Construct<T>, T> where T : new()
{
    public ValueTask<T> HandleAsync(Construct<T> request, CancellationToken cancellationToken) => new(new T());
}
public sealed class NewResponseBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse> where TResponse : new()
{
    public static int Calls;
    public ValueTask<TResponse> HandleAsync<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<TRequest, TResponse>
    {
        Calls++;
        return next.InvokeAsync(request, cancellationToken);
    }
}
public sealed class ClosedBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public static int Calls;
    public ValueTask<TResponse> HandleAsync<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<TRequest, TResponse>
    {
        Calls++;
        return next.InvokeAsync(request, cancellationToken);
    }
}
public sealed record ClosedNote<T> : INotification;
public sealed record GenericReset<T> : ISyncRequest;
public sealed class GenericResetHandler<T> : ISyncRequestHandler<GenericReset<T>>
{
    public int Calls;
    public void Handle(GenericReset<T> request, CancellationToken cancellationToken) => Calls++;
}
public sealed record GenericResetAll<T> : ISyncMultiRequest;
public sealed class GenericResetAllHandler<T> : ISyncRequestHandler<GenericResetAll<T>>
{
    public int Calls;
    public void Handle(GenericResetAll<T> request, CancellationToken cancellationToken) => Calls++;
}
public readonly ref struct ConstrainedBox<T> : ISyncRequest<int> where T : struct, allows ref struct;
public sealed class ConstrainedBoxHandler<T> : ISyncRequestHandler<ConstrainedBox<T>, int> where T : struct, allows ref struct
{
    public int Handle(scoped ConstrainedBox<T> request, CancellationToken cancellationToken) => 17;
}
