using Microsoft.Extensions.DependencyInjection;
using Xunit;
using MiddleStreamValidator = global::Zendiator.DiConfiguration.Tests.EntryValidators<int>.Middle;
using NativeMediator = global::Zendiator.DiConfiguration.Tests.Generated.NativeDiStreams.IZendiator;
using NativeConcrete = global::Zendiator.DiConfiguration.Tests.Generated.NativeDiStreams.Zendiator;

namespace Zendiator.DiConfiguration.Tests;

[Collection("DI runtime")]
public sealed class StreamValidationTests
{
    private static ServiceCollection Services() => DiRuntimeTests.CreateServices(configureDependencies: services =>
    {
        services.AddTransient<MiddleStreamValidator>(provider =>
        {
            var trace = provider.GetRequiredService<StreamValidationTrace>();
            trace.MiddleFactories++;
            trace.Events.Add("middle:factory");
            if (trace.FailMiddleActivation)
            {
                trace.FailMiddleActivation = false;
                throw trace.ActivationFailure;
            }
            return new MiddleStreamValidator(trace);
        });
    });

    private static async Task<int[]> Read(IAsyncEnumerable<int> stream)
    {
        var values = new List<int>();
        await foreach (var value in stream)
            values.Add(value);
        return values.ToArray();
    }

    [Fact]
    public async Task Validation_middleware_is_lazy_skips_later_services_on_failure_and_activation_can_retry()
    {
        await using var provider = Services().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var trace = scope.ServiceProvider.GetRequiredService<StreamValidationTrace>();
        var mediator = (NativeConcrete)scope.ServiceProvider.GetRequiredService<NativeMediator>();
        trace.FailFirst = true;
        var firstStream = mediator.StreamAsync(new ValidatedStream(2));
        Assert.Empty(trace.Events);
        Assert.Same(trace.Failure, await Assert.ThrowsAsync<InvalidOperationException>(async () => await Read(firstStream)));
        Assert.Equal(0, trace.MiddleFactories);
        Assert.Equal(0, trace.LastConstructions);
        Assert.Equal(0, trace.BehaviorConstructions);
        var first = mediator.GetRequiredService<SharedStreamValidator>();
        trace.FailFirst = false;
        trace.FailMiddleActivation = true;
        Assert.Same(trace.ActivationFailure, await Assert.ThrowsAsync<InvalidOperationException>(async () => await Read(mediator.StreamAsync(new ValidatedStream(2)))));
        trace.FailMiddle = true;
        Assert.Same(trace.Failure, await Assert.ThrowsAsync<InvalidOperationException>(async () => await Read(mediator.StreamAsync(new ValidatedStream(2)))));
        Assert.Equal(0, trace.LastConstructions);
        trace.FailMiddle = false;
        var recovered = await Read(mediator.StreamAsync(new ValidatedStream(2)));
        Assert.Equal([0, 1], recovered);
        Assert.Equal(["first:new", "first:validate", "first:validate", "middle:factory", "first:validate", "middle:factory",
            "middle:new", "middle:validate", "first:validate", "middle:validate", "last:new", "last:validate"], trace.Events);
        Assert.Equal(1, trace.FirstConstructions);
        Assert.Equal(1, trace.MiddleConstructions);
        Assert.Equal(2, trace.MiddleFactories);
        Assert.Same(first, mediator.GetRequiredService<SharedStreamValidator>());
        Assert.Equal(1, trace.HandlerCalls);
        Assert.Equal(1, trace.BehaviorCalls);
        Assert.Equal(1, trace.IteratorDisposals);
    }

    [Fact]
    public async Task Null_precancel_and_reenumeration_follow_the_lazy_native_cursor_boundary()
    {
        await using var provider = Services().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var trace = scope.ServiceProvider.GetRequiredService<StreamValidationTrace>();
        var mediator = scope.ServiceProvider.GetRequiredService<NativeMediator>();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Equal("request", Assert.Throws<ArgumentNullException>(() => mediator.StreamAsync((ValidatedStream)null!, canceled.Token)).ParamName);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await Read(mediator.StreamAsync(new ValidatedStream(2), canceled.Token)));
        Assert.Equal(0, trace.FirstConstructions);
        Assert.Equal(0, trace.HandlerCalls);
        Assert.Same(trace.Failure, await Assert.ThrowsAsync<InvalidOperationException>(async () => await Read(mediator.StreamAsync(new ValidatedStream(-1)))));
        var stream = mediator.StreamAsync(new ValidatedStream(2));
        var firstRead = await Read(stream);
        Assert.Equal([0, 1], firstRead);
        var secondRead = await Task.Run(() => Read(stream));
        Assert.Equal([0, 1], secondRead);
        Assert.Equal(3, trace.FirstValidations);
        Assert.Equal(2, trace.LastValidations);
        using var api = new CancellationTokenSource();
        using var enumeration = new CancellationTokenSource();
        var linked = mediator.StreamAsync(new ValidatedStream(2), api.Token).GetAsyncEnumerator(enumeration.Token);
        Assert.True(await linked.MoveNextAsync());
        var observed = trace.ObservedToken;
        Assert.NotEqual(api.Token, observed);
        Assert.NotEqual(enumeration.Token, observed);
        await linked.DisposeAsync();
        await linked.DisposeAsync();
        api.Cancel();
        enumeration.Cancel();
        Assert.False(observed.IsCancellationRequested);
        Assert.Equal(3, trace.IteratorDisposals);
        Assert.Equal(0, trace.LastDisposals);
    }

    [Fact]
    public async Task Middleware_handler_public_lookup_and_Send_share_captures_but_DI_owns_their_lifetime()
    {
        await using var provider = Services().BuildServiceProvider();
        var scope = provider.CreateAsyncScope();
        var trace = scope.ServiceProvider.GetRequiredService<StreamValidationTrace>();
        try
        {
            var mediator = (NativeConcrete)scope.ServiceProvider.GetRequiredService<NativeMediator>();
            var first = mediator.GetRequiredService<SharedStreamValidator>();
            var handler = mediator.GetRequiredService<ValidatedStreamHandler>();
            var stream = mediator.StreamAsync(new ValidatedStream(1));
            var other = mediator.StreamAsync(new OtherValidatedStream(1));
            Assert.Equal(0, trace.OtherHandlerConstructions);
            Assert.Equal(0, trace.HandlerCalls);
            Assert.Same(first, mediator.GetRequiredService<SharedStreamValidator>());
            Assert.Equal(first.Id, await mediator.SendAsync(new ValidationLookup()));
            var otherItems = await Read(other);
            Assert.Equal([0], otherItems);
            var items = await Read(stream);
            Assert.Equal([0], items);
            Assert.Same(handler, mediator.GetRequiredService<ValidatedStreamHandler>());
            Assert.Equal(1, trace.FirstConstructions);
            Assert.Equal(1, trace.LastConstructions);
            Assert.Equal(1, trace.OtherValidations);
            var secondMediator = new NativeConcrete(scope.ServiceProvider);
            var secondHandler = secondMediator.GetRequiredService<ValidatedStreamHandler>();
            Assert.NotSame(handler, secondHandler);
            Assert.Equal(1, trace.MiddleConstructions);
            var secondFirst = secondMediator.GetRequiredService<SharedStreamValidator>();
            Assert.NotSame(first, secondFirst);
            var secondItems = await Read(secondMediator.StreamAsync(new ValidatedStream(1)));
            Assert.Equal([0], secondItems);
            Assert.Same(secondHandler, secondMediator.GetRequiredService<ValidatedStreamHandler>());
            Assert.Equal(0, trace.FirstDisposals);
            Assert.Equal(0, trace.LastDisposals);
        }
        finally
        {
            await scope.DisposeAsync();
        }
        Assert.Equal(2, trace.FirstDisposals);
        Assert.Equal(2, trace.MiddleDisposals);
        Assert.Equal(2, trace.LastDisposals);
    }
}
