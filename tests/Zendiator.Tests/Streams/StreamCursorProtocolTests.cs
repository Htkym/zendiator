using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks.Sources;
using global::Zendiator;
using global::Zendiator.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using StreamMediator = global::Zendiator.Tests.NativeStreams.Generated.StreamComposition.IZendiator;

namespace Zendiator.Tests.NativeStreams;

public sealed class StreamCursorProtocolTests
{
    [Fact]
    public async Task Standard_json_serialization_preserves_options_order_and_pending_source_io()
    {
        var probe = new StreamProbe { WaitForOpen = true, WaitForSecondMove = true };
        probe.Ambient.Value = "http-request";
        await using var provider = Services(probe).BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var stream = scope.ServiceProvider.GetRequiredService<StreamMediator>().StreamAsync(new StreamRequest(3));
        Assert.IsAssignableFrom<IAsyncEnumerable<int>>(stream);
        Assert.Equal(0, probe.Opened);
        using var output = new MemoryStream();
        var options = new JsonSerializerOptions { NumberHandling = JsonNumberHandling.WriteAsString };
        var serialization = JsonSerializer.SerializeAsync<IAsyncEnumerable<int>>(output, stream, options);
        await probe.OpenStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(serialization.IsCompleted);
        Assert.Equal(0, probe.Moves);
        await CompleteOutsideRequestContext(() => probe.Open.Complete(new NativeCursor(probe, 3, probe.Token)));
        await probe.MiddleStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(serialization.IsCompleted);
        Assert.Equal(2, probe.Moves);
        await CompleteOutsideRequestContext(() => probe.Read.Complete(true));
        await serialization;
        using var document = JsonDocument.Parse(output.ToArray());
        Assert.Equal(["0", "1", "2"], document.RootElement.EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(1, probe.Open.Consumptions);
        Assert.Equal(1, probe.Read.Consumptions);
        Assert.Equal(1, probe.SourceDisposals);
        Assert.Equal(0, probe.HandlerDisposals);
        Assert.Equal(probe.Moves + 1, probe.Contexts.Count);
        Assert.All(probe.Contexts, context => Assert.Equal("http-request", context));
    }

    [Fact]
    public async Task Each_enumerator_is_independent_and_disposal_before_first_move_does_not_acquire()
    {
        var probe = new StreamProbe();
        await using var provider = Services(probe).BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        var scope = provider.CreateAsyncScope();
        try
        {
            var stream = scope.ServiceProvider.GetRequiredService<StreamMediator>().StreamAsync(new StreamRequest(3));
            var abandoned = stream.GetAsyncEnumerator();
            await abandoned.DisposeAsync();
            Assert.Equal(0, probe.HandlerConstructions);
            Assert.Equal(0, probe.Opened);
            using var cancellation = new CancellationTokenSource();
            await using var first = stream.GetAsyncEnumerator(cancellation.Token);
            await using var second = stream.GetAsyncEnumerator();
            Assert.NotSame(first, second);
            Assert.True(await first.MoveNextAsync());
            Assert.True(await second.MoveNextAsync());
            Assert.Equal(0, first.Current);
            Assert.Equal(0, second.Current);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await first.MoveNextAsync());
            Assert.True(await second.MoveNextAsync());
            Assert.Equal(1, second.Current);
            Assert.Equal(2, probe.Opened);
            Assert.Equal(1, probe.HandlerConstructions);
            Assert.Equal(0, probe.HandlerDisposals);
        }
        finally { await scope.DisposeAsync(); }
        Assert.Equal(1, probe.HandlerDisposals);
    }

    [Fact]
    public async Task Pending_move_rejects_overlap_without_cleanup_and_pending_disposal_is_idempotent()
    {
        var probe = new StreamProbe { WaitForSecondMove = true, WaitForDispose = true };
        await using var provider = Services(probe).BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var cursor = scope.ServiceProvider.GetRequiredService<StreamMediator>()
            .StreamAsync(new StreamRequest(3)).GetAsyncEnumerator();
        Assert.Throws<InvalidOperationException>(() => cursor.Current);
        Assert.True(await cursor.MoveNextAsync());
        Assert.Equal(0, cursor.Current);
        var pending = cursor.MoveNextAsync();
        Assert.False(pending.IsCompleted);
        Assert.Throws<InvalidOperationException>(() => cursor.Current);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await cursor.MoveNextAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await cursor.DisposeAsync());
        Assert.Equal(0, probe.SourceDisposals);
        Assert.Equal(2, probe.Moves);
        probe.Read.Complete(true);
        Assert.True(await pending);
        Assert.Equal(1, probe.Read.Consumptions);
        Assert.Equal(1, cursor.Current);
        var disposal = cursor.DisposeAsync().AsTask();
        Assert.False(disposal.IsCompleted);
        Assert.Throws<InvalidOperationException>(() => cursor.Current);
        var repeated = cursor.DisposeAsync().AsTask();
        Assert.Same(disposal, repeated);
        probe.Disposal.SetResult();
        await disposal;
        await repeated;
        await cursor.DisposeAsync();
        Assert.Equal(1, probe.SourceDisposals);
        Assert.False(await cursor.MoveNextAsync());
        Assert.Throws<InvalidOperationException>(() => cursor.Current);
    }

    [Fact]
    public async Task Completed_move_state_stays_guarded_until_the_whole_returned_operation_completes()
    {
        await CheckPublication(true);
        await CheckPublication(false);

        static async Task CheckPublication(bool hasValue)
        {
            var cursor = new DelayedPublicationCursor(hasValue);
            var pending = cursor.MoveNextAsync();
            Assert.True(cursor.CoreCompleted);
            Assert.False(pending.IsCompleted);
            Assert.False(cursor.CanReopen);
            Assert.Throws<InvalidOperationException>(() => cursor.Current);
            Assert.Throws<InvalidOperationException>(() => { _ = cursor.MoveNextAsync(); });
            Assert.Throws<InvalidOperationException>(() => { _ = cursor.DisposeAsync(); });
            Assert.Equal(hasValue ? 0 : 1, cursor.Disposals);

            cursor.Publication.Complete(hasValue);
            Assert.Equal(hasValue, await pending);
            Assert.Equal(1, cursor.Publication.Consumptions);
            if (hasValue) Assert.Equal(7, cursor.Current);
            else Assert.True(cursor.CanReopen);
            await cursor.DisposeAsync();
            Assert.Equal(1, cursor.Disposals);
            Assert.False(await cursor.MoveNextAsync());
        }
    }

    [Fact]
    public async Task Reserved_owner_binds_one_enumeration_and_cold_disposal_never_acquires()
    {
        using var api = new CancellationTokenSource();
        using var enumeration = new CancellationTokenSource();
        using var other = new CancellationTokenSource();
        var cursor = new ProtocolCursor(api.Token, reserveFirstEnumeration: true);
        Assert.Equal(api.Token, cursor.ApiToken);
        Assert.Equal(default, cursor.CancellationToken);
        Assert.Throws<InvalidOperationException>(() => { _ = cursor.MoveNextAsync(); });
        Assert.Throws<InvalidOperationException>(() => cursor.Current);
        Assert.True(cursor.Claim(enumeration.Token));
        Assert.False(cursor.Claim(other.Token));
        Assert.True(await cursor.MoveNextAsync());
        other.Cancel();
        Assert.False(cursor.CancellationToken.IsCancellationRequested);
        enumeration.Cancel();
        Assert.True(cursor.CancellationToken.IsCancellationRequested);
        await cursor.DisposeAsync();
        Assert.Equal(1, cursor.Disposals);

        var abandoned = new ProtocolCursor(api.Token, reserveFirstEnumeration: true);
        await abandoned.DisposeAsync();
        await abandoned.DisposeAsync();
        Assert.False(abandoned.Claim(enumeration.Token));
        Assert.False(await abandoned.MoveNextAsync());
        Assert.Equal(0, abandoned.Disposals);
        Assert.Equal(default, abandoned.CancellationToken);
    }

    [Theory]
    [InlineData("invoke")]
    [InlineData("status")]
    [InlineData("result")]
    public async Task Synchronous_cleanup_failure_releases_linked_cancellation_and_finishes_disposal(string failurePoint)
    {
        using var api = new CancellationTokenSource();
        using var enumeration = new CancellationTokenSource();
        var failure = new ArithmeticException("cleanup " + failurePoint);
        var cleanup = new CleanupSource
        {
            StatusError = failurePoint == "status" ? failure : null,
            ResultError = failurePoint == "result" ? failure : null,
        };
        cleanup.Complete();
        var cursor = new ProtocolCursor(api.Token, enumeration.Token)
        {
            Cleanup = () => failurePoint == "invoke" ? throw failure : cleanup.Operation,
        };
        Assert.True(await cursor.MoveNextAsync());
        var linkedToken = cursor.CancellationToken;
        Assert.Same(failure, await Assert.ThrowsAsync<ArithmeticException>(async () => await cursor.DisposeAsync()));
        Assert.Equal(failurePoint == "result" ? 1 : 0, cleanup.Consumptions);
        api.Cancel();
        Assert.False(linkedToken.IsCancellationRequested);
        await cursor.DisposeAsync();
        Assert.False(await cursor.MoveNextAsync());
        Assert.Equal(1, cursor.Disposals);
    }

    [Fact]
    public async Task Disposal_waiter_joining_during_cleanup_observes_the_same_synchronous_failure()
    {
        var failure = new ArithmeticException("cleanup failed after waiter joined");
        Task? joined = null;
        ProtocolCursor cursor = null!;
        cursor = new ProtocolCursor(default)
        {
            Cleanup = () =>
            {
                joined = cursor.DisposeAsync().AsTask();
                Assert.False(joined.IsCompleted);
                throw failure;
            },
        };
        Assert.True(await cursor.MoveNextAsync());
        var disposal = cursor.DisposeAsync().AsTask();
        Assert.Same(joined, disposal);
        Assert.Same(failure, await Assert.ThrowsAsync<ArithmeticException>(() => disposal));
        var repeated = cursor.DisposeAsync().AsTask();
        Assert.Same(disposal, repeated);
        Assert.Same(failure, await Assert.ThrowsAsync<ArithmeticException>(() => repeated));
        Assert.Equal(1, cursor.Disposals);
        Assert.False(await cursor.MoveNextAsync());
        Assert.Throws<InvalidOperationException>(() => cursor.Current);
    }

    [Fact]
    public async Task Concurrent_disposal_finish_and_join_complete_and_consume_cleanup_once()
    {
        using var race = new Barrier(2);
        var timeout = TimeSpan.FromSeconds(10);
        var cleanup = new CleanupSource();
        cleanup.Complete();
        var cursor = new ProtocolCursor(default)
        {
            Cleanup = () =>
            {
                Assert.True(race.SignalAndWait(timeout));
                return cleanup.Operation;
            },
        };
        Assert.True(await cursor.MoveNextAsync());
        var closer = Task.Factory.StartNew(() => cursor.DisposeAsync().AsTask(),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        var joiner = Task.Factory.StartNew(async () =>
        {
            Assert.True(race.SignalAndWait(timeout));
            await cursor.DisposeAsync();
            Assert.False(cursor.HasPending);
            Assert.False(await cursor.MoveNextAsync());
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        var disposals = await Task.WhenAll(closer, joiner).WaitAsync(timeout);
        await Task.WhenAll(disposals).WaitAsync(timeout);
        await cursor.DisposeAsync();
        Assert.Equal(1, cleanup.Consumptions);
        Assert.Equal(1, cursor.Disposals);
        Assert.False(await cursor.MoveNextAsync());
        Assert.Throws<InvalidOperationException>(() => cursor.Current);
    }

    [Fact]
    public async Task Pending_cleanup_owns_linked_cancellation_until_consumed_and_shares_disposal_completion()
    {
        using var api = new CancellationTokenSource();
        using var enumeration = new CancellationTokenSource();
        var cleanup = new CleanupSource();
        var cursor = new ProtocolCursor(api.Token, enumeration.Token) { Cleanup = () => cleanup.Operation };
        Assert.True(await cursor.MoveNextAsync());
        var linkedToken = cursor.CancellationToken;
        var disposal = cursor.DisposeAsync().AsTask();
        Assert.False(disposal.IsCompleted);
        Assert.Same(disposal, cursor.DisposeAsync().AsTask());
        Assert.True(cursor.HasPending);
        api.Cancel();
        Assert.True(linkedToken.IsCancellationRequested);
        cleanup.Complete();
        await disposal;
        Assert.False(cursor.HasPending);
        await cursor.DisposeAsync();
        Assert.Equal(1, cleanup.Consumptions);
        Assert.Equal(1, cursor.Disposals);
        Assert.False(await cursor.MoveNextAsync());
    }

    [Fact]
    public async Task Pending_move_cleanup_failure_takes_priority_and_keeps_ownership_until_completion()
    {
        using var api = new CancellationTokenSource();
        using var enumeration = new CancellationTokenSource();
        var cleanup = new CleanupSource();
        var cursor = new ProtocolCursor(api.Token, enumeration.Token) { Cleanup = () => cleanup.Operation };
        Assert.True(await cursor.MoveNextAsync());
        cursor.MoveError = new FormatException("move failed");
        var pending = cursor.MoveNextAsync();
        Assert.False(pending.IsCompleted);
        Assert.Throws<InvalidOperationException>(() => { _ = cursor.DisposeAsync(); });
        api.Cancel();
        Assert.True(cursor.CancellationToken.IsCancellationRequested);
        var failure = new ArithmeticException("pending cleanup failed");
        cleanup.Complete(failure);
        Assert.Same(failure, await Assert.ThrowsAsync<ArithmeticException>(async () => await pending));
        await cursor.DisposeAsync();
        Assert.False(await cursor.MoveNextAsync());
        Assert.Equal(1, cleanup.Consumptions);
        Assert.Equal(1, cursor.Disposals);
    }

    [Fact]
    public async Task Json_request_cancellation_and_serializer_failure_dispose_the_acquired_cursor()
    {
        var cancellationProbe = new StreamProbe { WaitForSecondMove = true };
        await using (var provider = Services(cancellationProbe).BuildServiceProvider())
        await using (var scope = provider.CreateAsyncScope())
        using (var cancellation = new CancellationTokenSource())
        using (var output = new MemoryStream())
        {
            var stream = scope.ServiceProvider.GetRequiredService<StreamMediator>().StreamAsync(new StreamRequest(5), cancellation.Token);
            var serialization = JsonSerializer.SerializeAsync<IAsyncEnumerable<int>>(output, stream, cancellationToken: cancellation.Token);
            await cancellationProbe.MiddleStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancellation.Cancel();
            cancellationProbe.Read.Complete(true);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await serialization);
            Assert.Equal(2, cancellationProbe.Moves);
            Assert.Equal(1, cancellationProbe.Read.Consumptions);
            Assert.Equal(1, cancellationProbe.SourceDisposals);
        }

        var failure = new FormatException("serializer failed");
        var failureProbe = new StreamProbe();
        await using var failureProvider = Services(failureProbe).BuildServiceProvider();
        await using var failureScope = failureProvider.CreateAsyncScope();
        using var failedOutput = new MemoryStream();
        var options = new JsonSerializerOptions();
        options.Converters.Add(new FailingConverter(failure));
        var failingStream = failureScope.ServiceProvider.GetRequiredService<StreamMediator>().StreamAsync(new StreamRequest(5));
        Assert.Same(failure, await Assert.ThrowsAsync<FormatException>(async () =>
            await JsonSerializer.SerializeAsync<IAsyncEnumerable<int>>(failedOutput, failingStream, options)));
        Assert.Equal(2, failureProbe.Moves);
        Assert.Equal(1, failureProbe.SourceDisposals);
        Assert.Equal(0, failureProbe.MiddlewareFailures);
    }

    [Fact]
    public async Task Instance_consumer_uses_the_same_cursor_and_false_finishes_normally_before_cancellation()
    {
        var probe = new StreamProbe();
        await using var provider = Services(probe).BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        using var cancellation = new CancellationTokenSource();
        var sink = new StoppingSink(cancellation);
        var stream = scope.ServiceProvider.GetRequiredService<StreamMediator>().StreamSource(new StreamRequest(8));
        await stream.ConsumeAsync(sink, cancellation.Token);
        Assert.Equal([0], sink.Items);
        Assert.Equal(1, probe.Opened);
        Assert.Equal(1, probe.Moves);
        Assert.Equal(1, probe.SourceDisposals);
        Assert.Equal(0, probe.HandlerDisposals);
        Assert.Equal(0, sink.Disposals);
        Assert.True(cancellation.IsCancellationRequested);
    }

    [Fact]
    public async Task Pending_consumer_observes_the_effective_API_and_terminal_token_once()
    {
        var probe = new StreamProbe();
        await using var provider = Services(probe).BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        using var api = new CancellationTokenSource();
        using var terminal = new CancellationTokenSource();
        var sink = new PendingSink();
        var stream = scope.ServiceProvider.GetRequiredService<StreamMediator>().StreamSource(new StreamRequest(4), api.Token);
        var operation = stream.ConsumeAsync(sink, terminal.Token);
        await sink.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(operation.IsCompleted);
        Assert.NotEqual(api.Token, sink.Callback.Token);
        Assert.NotEqual(terminal.Token, sink.Callback.Token);
        api.Cancel();
        Assert.True(sink.Callback.Token.IsCancellationRequested);
        sink.Callback.Complete(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await operation);
        Assert.Equal(1, sink.Callback.Consumptions);
        Assert.Equal(1, probe.Moves);
        Assert.Equal(1, probe.SourceDisposals);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Consumer_error_is_not_injected_into_middleware_and_cleanup_failure_takes_priority(bool cleanupFails)
    {
        var sinkError = new FormatException("sink failed");
        var cleanupError = new ArithmeticException("cleanup failed");
        var probe = new StreamProbe { DisposalError = cleanupFails ? cleanupError : null };
        await using var provider = Services(probe).BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var stream = scope.ServiceProvider.GetRequiredService<StreamMediator>().StreamSource(new WrappedStreamRequest(3));
        Exception? observed = null;
        try { await stream.ConsumeAsync(new FailingSink(sinkError)); }
        catch (Exception error) { observed = error; }
        Assert.Same(cleanupFails ? cleanupError : sinkError, observed);
        Assert.Equal(1, probe.Moves);
        Assert.Equal(1, probe.SourceDisposals);
        Assert.Equal(0, probe.MiddlewareFailures);
        Assert.Equal(0, probe.HandlerDisposals);
    }

    private static ServiceCollection Services(StreamProbe probe)
    {
        var services = new ServiceCollection();
        services.AddSingleton(probe);
        services.AddZendiator<StreamComposition>(configuration =>
        {
            configuration.Handlers<StreamRequest>(DispatchFamily.Stream, typeof(StreamHandler));
            configuration.Pipeline<StreamRequest>(DispatchFamily.Stream);
            configuration.Handlers<WrappedStreamRequest>(DispatchFamily.Stream, typeof(StreamHandler));
            configuration.Pipeline<WrappedStreamRequest>(DispatchFamily.Stream, Stage.Wrapper<ProbeMiddleware>());
        });
        return services;
    }

    private static Task CompleteOutsideRequestContext(Action complete)
    {
        using (ExecutionContext.SuppressFlow()) return Task.Run(complete);
    }

    private sealed class ProtocolCursor(CancellationToken apiToken, CancellationToken enumerationToken = default,
        bool reserveFirstEnumeration = false)
        : global::Zendiator.Streaming.StreamCursorState(apiToken, enumerationToken, reserveFirstEnumeration), IAsyncEnumerator<int>
    {
        public Func<ValueTask>? Cleanup { get; init; }
        public Exception? MoveError { get; set; }
        public int Disposals { get; private set; }
        public CancellationToken ApiToken => OriginalApiToken;
        public bool HasPending => HasPendingOperation;
        public bool Claim(CancellationToken token) => TryClaimFirstEnumeration(token);
        public int Current { get { ValidateCurrent(); return 7; } }

        public ValueTask<bool> MoveNextAsync()
        {
            if (!TryBeginMove()) return new(false);
            ValueTask<bool> operation;
            try
            {
                _ = TryStart();
                if (MoveError is { } error) throw error;
                operation = MoveSucceeded(true);
            }
            catch (Exception error) { operation = MoveFailed(error); }
            return PublishMove(operation);
        }

        protected override ValueTask DisposeCoreAsync()
        {
            Disposals++;
            return Cleanup?.Invoke() ?? default;
        }
    }

    private sealed class CleanupSource : IValueTaskSource
    {
        private ManualResetValueTaskSourceCore<bool> _source = new() { RunContinuationsAsynchronously = true };
        public Exception? StatusError { get; init; }
        public Exception? ResultError { get; init; }
        public int Consumptions;
        public ValueTask Operation => new(this, _source.Version);

        public void Complete(Exception? error = null)
        {
            if (error is null) _source.SetResult(true);
            else _source.SetException(error);
        }

        public void GetResult(short token)
        {
            if (Interlocked.Increment(ref Consumptions) != 1) throw new InvalidOperationException("Cleanup was consumed twice.");
            _ = _source.GetResult(token);
            if (ResultError is { } error) throw error;
        }

        public ValueTaskSourceStatus GetStatus(short token) =>
            StatusError is { } error ? throw error : _source.GetStatus(token);
        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) =>
            _source.OnCompleted(continuation, state, token, flags);
    }

    private sealed class DelayedPublicationCursor(bool hasValue)
        : global::Zendiator.Streaming.StreamCursorState, IAsyncEnumerator<int>
    {
        private bool _normalEnd;
        public OnceSource<bool> Publication { get; } = new();
        public bool CoreCompleted { get; private set; }
        public bool CanReopen => _normalEnd && !HasPendingOperation;
        public int Disposals { get; private set; }
        public int Current { get { ValidateCurrent(); return 7; } }

        public ValueTask<bool> MoveNextAsync()
        {
            if (!TryBeginMove()) return new(false);
            _ = TryStart();
            _normalEnd = !MoveSucceeded(hasValue).GetAwaiter().GetResult();
            CoreCompleted = true;
            // Keep the public completion pending after success state or EOF cleanup is complete.
            return PublishMove(Publication.Operation);
        }

        protected override ValueTask DisposeCoreAsync()
        {
            Disposals++;
            return default;
        }
    }

    private sealed class StoppingSink(CancellationTokenSource cancellation) : IStreamSink<int>, IDisposable
    {
        public List<int> Items { get; } = [];
        public int Disposals;
        public ValueTask<bool> OnNextAsync(int item, CancellationToken cancellationToken)
        {
            Items.Add(item);
            cancellation.Cancel();
            return new(false);
        }
        public void Dispose() => Disposals++;
    }
    private sealed class FailingSink(Exception error) : IStreamSink<int>
    {
        public ValueTask<bool> OnNextAsync(int item, CancellationToken cancellationToken) => ValueTask.FromException<bool>(error);
    }
    private sealed class PendingSink : IStreamSink<int>
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public OnceSource<bool> Callback { get; } = new();
        public ValueTask<bool> OnNextAsync(int item, CancellationToken cancellationToken)
        {
            Callback.Token = cancellationToken;
            Entered.SetResult();
            return Callback.Operation;
        }
    }
    private sealed class FailingConverter(Exception error) : JsonConverter<int>
    {
        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetInt32();
        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
        {
            if (value == 1) throw error;
            writer.WriteNumberValue(value);
        }
    }
}

[ZendiatorComposition("NativeStreamProtocol")]
public sealed class StreamComposition;
public record StreamRequest(int Count) : IStreamRequest<int>;
public sealed record WrappedStreamRequest(int Count) : StreamRequest(Count);
public sealed class StreamProbe
{
    public int HandlerConstructions, HandlerDisposals, Opened, Moves, SourceDisposals, MiddlewareFailures;
    public bool WaitForOpen, WaitForSecondMove, WaitForDispose;
    public Exception? DisposalError;
    public CancellationToken Token;
    public AsyncLocal<string?> Ambient { get; } = new();
    public List<string?> Contexts { get; } = [];
    public TaskCompletionSource OpenStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource MiddleStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Disposal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public OnceSource<NativeCursor> Open { get; } = new();
    public OnceSource<bool> Read { get; } = new();
}

public sealed class OnceSource<T> : IValueTaskSource<T>
{
    private ManualResetValueTaskSourceCore<T> _source = new() { RunContinuationsAsynchronously = true };
    public int Consumptions;
    public CancellationToken Token;
    public ValueTask<T> Operation => new(this, _source.Version);
    public void Complete(T value) => _source.SetResult(value);
    public T GetResult(short token)
    {
        if (Interlocked.Increment(ref Consumptions) != 1) throw new InvalidOperationException("The source was consumed twice.");
        var value = _source.GetResult(token);
        Token.ThrowIfCancellationRequested();
        return value;
    }
    public ValueTaskSourceStatus GetStatus(short token) => _source.GetStatus(token);
    public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) =>
        _source.OnCompleted(continuation, state, token, flags);
}

[ZendiatorHandler]
public sealed class StreamHandler : IStreamSourceHandler<StreamRequest, int, NativeCursor>,
    IStreamSourceHandler<WrappedStreamRequest, int, NativeCursor>, IDisposable
{
    private readonly StreamProbe _probe;
    public StreamHandler(StreamProbe probe) { _probe = probe; probe.HandlerConstructions++; }
    public ValueTask<NativeCursor> OpenAsync(StreamRequest request, CancellationToken cancellationToken)
    {
        _probe.Opened++;
        _probe.Contexts.Add(_probe.Ambient.Value);
        _probe.Token = cancellationToken;
        _probe.Read.Token = cancellationToken;
        _probe.OpenStarted.TrySetResult();
        return _probe.WaitForOpen ? _probe.Open.Operation : new(new NativeCursor(_probe, request.Count, cancellationToken));
    }
    public ValueTask<NativeCursor> OpenAsync(WrappedStreamRequest request, CancellationToken cancellationToken) =>
        OpenAsync((StreamRequest)request, cancellationToken);
    public void Dispose() => _probe.HandlerDisposals++;
}

public sealed class NativeCursor(StreamProbe probe, int count, CancellationToken token) : IAsyncEnumerator<int>
{
    private int _next, _current;
    private bool _valid;
    public int Current => _valid ? _current : throw new InvalidOperationException("No current item.");
    public ValueTask<bool> MoveNextAsync()
    {
        probe.Moves++;
        probe.Contexts.Add(probe.Ambient.Value);
        _valid = false;
        token.ThrowIfCancellationRequested();
        if (_next == count) return new(false);
        _current = _next++;
        _valid = true;
        if (_current != 1 || !probe.WaitForSecondMove) return new(true);
        probe.MiddleStarted.TrySetResult();
        return probe.Read.Operation;
    }
    public ValueTask DisposeAsync()
    {
        _valid = false;
        probe.SourceDisposals++;
        if (probe.DisposalError is { } error) return ValueTask.FromException(error);
        return probe.WaitForDispose ? new(probe.Disposal.Task) : default;
    }
}

public sealed class ProbeMiddleware(StreamProbe probe) : IStreamMiddleware<WrappedStreamRequest, int>
{
    public async ValueTask<IAsyncEnumerator<int>> OpenAsync(WrappedStreamRequest request, IStreamUpstream<WrappedStreamRequest, int> upstream, CancellationToken cancellationToken)
    {
        try { return await upstream.OpenAsync(request, cancellationToken); }
        catch { probe.MiddlewareFailures++; throw; }
    }
}
