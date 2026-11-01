using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    [Fact]
    public async Task Failure_data_preserves_original_throw_origin_and_returns_the_fault()
    {
        var f = CapabilityThree();
        var p = new AsyncHookProbe();
        var body = new InvalidOperationException("original Handler failure");
        p.Failures.Add("handler", body);
        var prior = AsyncHookProbe.Ambient.Value;
        try
        {
            IServiceCollection services = new ServiceCollection();
            services.AddSingleton(p);
            f.Register.Invoke(null, [services]);
            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            await using (var scope = provider.CreateAsyncScope())
            {
                var request = Activator.CreateInstance(f.Request, [10])!;
                AsyncHookProbe.Ambient.Value = "caller";
                ValueTask<int> operation = default;
                Assert.Null(Record.Exception(() => { operation = (ValueTask<int>)InvokeCapability(f, true, scope.ServiceProvider, request, p.Cancellation.Token); }));
                Assert.Equal("caller", AsyncHookProbe.Ambient.Value);
                Assert.True(operation.IsCompleted);
                Assert.True(operation.IsFaulted);
                Assert.False(operation.IsCanceled);
                var completion = operation.AsTask();
                var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => completion);
                Assert.Same(body, actual);
                Assert.Contains("AsyncHookProbe.Hit", actual.StackTrace ?? "");
                Assert.Equal("caller", AsyncHookProbe.Ambient.Value);
                AssertFailureDataTraceAndRequests(p, request);
                Assert.All(p.Calls.Where(call => call.Phase.EndsWith(".exception", StringComparison.Ordinal)), call => Assert.Same(body, call.Error));
                Assert.All(p.Calls.Where(call => call.Exit != null), call =>
                {
                    Assert.False(call.Exit!.Value.Success);
                    Assert.Same(body, call.Exit.Value.Error);
                });
                Assert.Empty(p.Gates);
                Assert.Empty(p.ReleasedHooks);
                Assert.Equal(0, p.HandlerDisposals);
            }
            AssertFailureDataBorrowedDisposal(p);
        }
        finally
        {
            AsyncHookProbe.Ambient.Value = prior;
            p.Cancellation.Dispose();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failure_data_awaits_observer_and_finally_once_and_preserves_outer_priority(bool canceledFinally)
    {
        var f = CapabilityThree();
        var p = new AsyncHookProbe { Pending = "h2.exception" };
        var body = new InvalidOperationException("Handler failure");
        var observer = new NotSupportedException("pending observer replacement");
        using var foreign = new CancellationTokenSource();
        Exception final = canceledFinally ? new OperationCanceledException("pending Finally replacement", foreign.Token) : new FormatException("pending Finally replacement");
        p.Failures.Add("handler", body);
        var observerReady = new TaskCompletionSource<IAsyncOnceGate>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finallyReady = new TaskCompletionSource<IAsyncOnceGate>(TaskCreationOptions.RunContinuationsAsynchronously);
        var published = 0;
        p.VoidGateCreated = gate =>
        {
            switch (Interlocked.Increment(ref published))
            {
                case 1:
                    observerReady.SetResult(gate);
                    break;
                case 2:
                    finallyReady.SetResult(gate);
                    break;
                default:
                    throw new InvalidOperationException("Unexpected pending void callback.");
            }
        };
        var prior = AsyncHookProbe.Ambient.Value;
        try
        {
            IServiceCollection services = new ServiceCollection();
            services.AddSingleton(p);
            f.Register.Invoke(null, [services]);
            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            await using (var scope = provider.CreateAsyncScope())
            {
                var request = Activator.CreateInstance(f.Request, [10])!;
                AsyncHookProbe.Ambient.Value = "caller";
                var operation = (ValueTask<int>)InvokeCapability(f, true, scope.ServiceProvider, request, p.Cancellation.Token);
                Assert.Equal("caller", AsyncHookProbe.Ambient.Value);
                Assert.False(operation.IsCompleted);
                var completion = operation.AsTask();
                var observerGate = await observerReady.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.False(completion.IsCompleted);
                Assert.Equal(0, observerGate.Consumptions);
                p.Pending = "h2.finally";
                observerGate.Fail(observer);
                var finallyGate = await finallyReady.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(1, observerGate.Consumptions);
                Assert.Equal(0, finallyGate.Consumptions);
                Assert.False(completion.IsCompleted);
                p.Pending = null;
                finallyGate.Fail(final);
                Exception? actual = null;
                try
                {
                    await completion.WaitAsync(TimeSpan.FromSeconds(10));
                }
                catch (Exception error)
                {
                    actual = error;
                }
                Assert.Same(final, actual);
                Assert.Equal(canceledFinally, completion.IsCanceled);
                Assert.Equal(!canceledFinally, completion.IsFaulted);
                if (canceledFinally)
                    Assert.Equal(foreign.Token, Assert.IsType<OperationCanceledException>(actual).CancellationToken);
                Assert.Equal("caller", AsyncHookProbe.Ambient.Value);
                Assert.Equal(2, published);
                Assert.Equal(2, p.Gates.Count);
                Assert.All(p.Gates, gate => Assert.Equal(1, gate.Consumptions));
                AssertFailureDataTraceAndRequests(p, request);
                Assert.Same(body, p.Calls.Single(call => call.Phase == "h2.exception").Error);
                var innerExit = p.Calls.Single(call => call.Phase == "h2.finally").Exit!.Value;
                Assert.False(innerExit.Success);
                Assert.Same(observer, innerExit.Error);
                foreach (var id in new[] { 1, 0 })
                {
                    Assert.Same(final, p.Calls.Single(call => call.Phase == $"h{id}.exception").Error);
                    var exit = p.Calls.Single(call => call.Phase == $"h{id}.finally").Exit!.Value;
                    Assert.False(exit.Success);
                    Assert.Same(final, exit.Error);
                }
                Assert.Empty(p.ReleasedHooks);
                Assert.Equal(0, p.HandlerDisposals);
            }
            AssertFailureDataBorrowedDisposal(p);
        }
        finally
        {
            AsyncHookProbe.Ambient.Value = prior;
            p.Cancellation.Dispose();
        }
    }

    private static void AssertFailureDataTraceAndRequests(AsyncHookProbe p, object original)
    {
        Assert.Equal(new[] { "h0.construct", "h1.construct", "h2.construct", "handler.construct", "h0.before", "h1.before", "h2.before", "handler", "h2.exception", "h2.finally", "h1.exception", "h1.finally", "h0.exception", "h0.finally" }, p.Trace);
        var expectedContext = new (string Phase, string? Ambient)[]
        {
            ("h0.before", "caller"),
            ("h1.before", "h0.before"),
            ("h2.before", "h1.before"),
            ("handler", "h2.before"),
            ("h2.exception", "handler"),
            ("h2.finally", "h2.exception"),
            ("h1.exception", "h2.finally"),
            ("h1.finally", "h1.exception"),
            ("h0.exception", "h1.finally"),
            ("h0.finally", "h0.exception")
        };
        Assert.Equal(expectedContext, p.Calls.Select(call => (call.Phase, call.Ambient)));
        Assert.Same(original, p.Calls.Single(call => call.Phase == "h0.before").Request);
        foreach (var id in new[] { 0, 1, 2 })
        {
            var before = p.Calls.Single(call => call.Phase == $"h{id}.before").Request!;
            var saved = p.Calls.Single(call => call.Phase == (id == 2 ? "handler" : $"h{id + 1}.before")).Request!;
            Assert.NotSame(before, saved);
            Assert.Equal((int)before.GetType().GetProperty("Value")!.GetValue(before)! + 1, (int)saved.GetType().GetProperty("Value")!.GetValue(saved)!);
            Assert.Same(saved, p.Calls.Single(call => call.Phase == $"h{id}.exception").Request);
            Assert.Same(saved, p.Calls.Single(call => call.Phase == $"h{id}.finally").Request);
        }
    }

    private static void AssertFailureDataBorrowedDisposal(AsyncHookProbe p)
    {
        Assert.Equal(3, p.Hooks.Count);
        Assert.Equal(3, p.ReleasedHooks.Count);
        Assert.All(p.Hooks, hook => Assert.Single(p.ReleasedHooks, released => ReferenceEquals(hook, released)));
        Assert.Equal(1, p.HandlerDisposals);
        Assert.Equal(0, p.FactoryDisposals);
        Assert.Equal(new[] { "handler.dispose", "h2.cleanup", "h1.cleanup", "h0.cleanup" }, p.Trace.TakeLast(4));
    }
}
