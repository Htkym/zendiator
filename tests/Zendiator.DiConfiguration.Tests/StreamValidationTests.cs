using Di.Generated;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using MiddleStreamValidator = Zendiator.DiConfiguration.Tests.EntryValidators<int>.Middle;

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
        services.AddTransient<DiStreamBehavior<ValidatedStream, int>>(provider =>
        {
            provider.GetRequiredService<StreamValidationTrace>().PipelineFirstFactories++;
            return new DiStreamBehavior<ValidatedStream, int>();
        });
        services.AddTransient<DiStreamBehavior<OtherValidatedStream, int>>(provider =>
        {
            provider.GetRequiredService<StreamValidationTrace>().OtherPipelineFactories++;
            return new DiStreamBehavior<OtherValidatedStream, int>();
        });
    });

    private static async Task<int[]> Read(IAsyncEnumerable<int> stream)
    {
        var values = new List<int>();
        await foreach (var value in stream) values.Add(value);
        return values.ToArray();
    }

    [Fact]
    public async Task Entry_failure_does_not_acquire_later_services_and_activation_can_retry()
    {
        await using var provider = Services().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var trace = scope.ServiceProvider.GetRequiredService<StreamValidationTrace>();
        var mediator = (global::Di.Generated.Zendiator)scope.ServiceProvider.GetRequiredService<IZendiator>();
        trace.FailFirst = true;
        Assert.Same(trace.Failure, Assert.Throws<InvalidOperationException>(() => mediator.StreamAsync(new ValidatedStream(2))));
        Assert.Equal(0, trace.MiddleFactories);
        Assert.Equal(0, trace.PipelineFirstFactories);
        Assert.Equal(0, trace.LastConstructions);
        var first = mediator.GetRequiredService<SharedStreamValidator>();
        trace.FailFirst = false;
        trace.FailMiddleActivation = true;
        Assert.Same(trace.ActivationFailure, Assert.Throws<InvalidOperationException>(() => mediator.StreamAsync(new ValidatedStream(2))));
        trace.FailMiddle = true;
        Assert.Same(trace.Failure, Assert.Throws<InvalidOperationException>(() => mediator.StreamAsync(new ValidatedStream(2))));
        Assert.Equal(0, trace.LastConstructions);
        trace.FailMiddle = false;
        var stream = mediator.StreamAsync(new ValidatedStream(2));
        Assert.Equal(["first:new", "first:validate", "first:validate", "middle:factory", "first:validate", "middle:factory",
            "middle:new", "middle:validate", "first:validate", "middle:validate", "last:new", "last:validate"], trace.Events);
        Assert.Equal(1, trace.FirstConstructions);
        Assert.Equal(1, trace.MiddleConstructions);
        Assert.Equal(2, trace.MiddleFactories);
        Assert.Same(first, mediator.GetRequiredService<SharedStreamValidator>());
        Assert.Equal(0, trace.HandlerCalls);
        Assert.Equal(0, trace.BehaviorConstructions);
        Assert.Equal(0, trace.PipelineFirstFactories);
        var items = await Read(stream);
        Assert.Equal([0, 1], items);
        Assert.Equal(1, trace.HandlerCalls);
        Assert.Equal(1, trace.BehaviorCalls);
        Assert.Equal(1, trace.PipelineFirstFactories);
    }

    [Fact]
    public async Task Null_cancel_and_reenumeration_preserve_the_validation_boundary()
    {
        await using var provider = Services().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var trace = scope.ServiceProvider.GetRequiredService<StreamValidationTrace>();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Equal("request", Assert.Throws<ArgumentNullException>(() => mediator.StreamAsync((ValidatedStream)null!, canceled.Token)).ParamName);
        Assert.Equal(0, trace.FirstConstructions);
        await using (var enumerator = mediator.StreamAsync((DiNumbers)null!, canceled.Token).GetAsyncEnumerator())
            Assert.Equal("_request", (await Assert.ThrowsAsync<ArgumentNullException>(async () => await enumerator.MoveNextAsync())).ParamName);
        Assert.Same(trace.Failure, Assert.Throws<InvalidOperationException>(() => mediator.StreamAsync(new ValidatedStream(-1), canceled.Token)));
        var canceledStream = mediator.StreamAsync(new ValidatedStream(2), canceled.Token);
        Assert.Equal(2, trace.FirstValidations);
        Assert.Equal(1, trace.LastValidations);
        await using (var enumerator = canceledStream.GetAsyncEnumerator())
        {
            var pending = enumerator.MoveNextAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
            Assert.True(pending.IsCanceled);
            Assert.False(await enumerator.MoveNextAsync());
        }
        Assert.Equal(0, trace.HandlerCalls);
        Assert.Equal(0, trace.BehaviorConstructions);
        var stream = mediator.StreamAsync(new ValidatedStream(2));
        Assert.Equal(3, trace.FirstValidations);
        var firstItems = await Read(stream);
        var otherThreadItems = await Task.Run(() => Read(stream));
        Assert.Equal([0, 1], firstItems);
        Assert.Equal([0, 1], otherThreadItems);
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
    public async Task Validator_handler_public_lookup_and_other_routes_share_transient_captures()
    {
        await using var provider = Services().BuildServiceProvider();
        var scope = provider.CreateAsyncScope();
        var trace = scope.ServiceProvider.GetRequiredService<StreamValidationTrace>();
        try
        {
            var mediator = (global::Di.Generated.Zendiator)scope.ServiceProvider.GetRequiredService<IZendiator>();
            var first = mediator.GetRequiredService<SharedStreamValidator>();
            var stream = mediator.StreamAsync(new ValidatedStream(1));
            var handler = mediator.GetRequiredService<ValidatedStreamHandler>();
            var other = mediator.StreamAsync(new OtherValidatedStream(1));
            Assert.Equal(0, trace.OtherHandlerConstructions);
            Assert.Equal(0, trace.OtherPipelineFactories);
            Assert.Equal(0, trace.HandlerCalls);
            Assert.Equal(0, trace.BehaviorConstructions);
            Assert.Same(first, mediator.GetRequiredService<SharedStreamValidator>());
            Assert.Equal(first.Id, await mediator.SendAsync(new ValidationLookup()));
            var otherItems = await Read(other);
            var items = await Read(stream);
            Assert.Equal([0], otherItems);
            Assert.Equal(1, trace.OtherPipelineFactories);
            Assert.Equal([0], items);
            Assert.Same(handler, mediator.GetRequiredService<ValidatedStreamHandler>());
            Assert.Equal(1, trace.FirstConstructions);
            Assert.Equal(1, trace.LastConstructions);
            Assert.Equal(1, trace.OtherValidations);
            var secondMediator = new global::Di.Generated.Zendiator(scope.ServiceProvider);
            var secondHandler = secondMediator.GetRequiredService<ValidatedStreamHandler>();
            Assert.NotSame(handler, secondHandler);
            Assert.Equal(1, trace.MiddleConstructions); // Public Find only acquires the requested dependency.
            var secondFirst = secondMediator.GetRequiredService<SharedStreamValidator>();
            Assert.NotSame(first, secondFirst);
            _ = secondMediator.StreamAsync(new ValidatedStream(1));
            Assert.Same(secondHandler, secondMediator.GetRequiredService<ValidatedStreamHandler>());
        }
        finally { await scope.DisposeAsync(); }
        Assert.Equal(2, trace.FirstDisposals);
        Assert.Equal(2, trace.MiddleDisposals);
        Assert.Equal(2, trace.LastDisposals);
    }
}
