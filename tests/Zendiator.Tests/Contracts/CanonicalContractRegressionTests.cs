using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Tests.Contracts;

public sealed class CanonicalContractRegressionTests
{
    private sealed record Request(int Value);
    private readonly record struct LargeRequest(long A, long B, long C, long D, long E, long F, long G, long H);
    private struct PlainValueRequest { public int Value; }

    private sealed class Source(ContractEvents events, int count) : IAsyncEnumerable<int>, IAsyncEnumerator<int>
    {
        public int Moves, Reads, Disposals;
        public Exception? GetError, MoveError, CurrentError, DisposeError;
        public ContractOnce<bool>? PendingMove, PendingDispose;
        public CancellationToken Token;
        public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken token = default)
        { Token = token; if (GetError is { } error) throw error; return this; }
        public int Current
        {
            get { Reads++; events.Add("source", "current", count, Token); if (CurrentError is { } error) throw error; return Moves - 1; }
        }
        public ValueTask<bool> MoveNextAsync()
        {
            events.Add("source", "move", count, Token, detail: Moves.ToString()); Moves++; Token.ThrowIfCancellationRequested();
            if (MoveError is { } error) throw error;
            return PendingMove?.Task ?? new(Moves <= count);
        }
        public ValueTask DisposeAsync()
        {
            Disposals++; events.Add("source", "dispose", count, Token);
            if (DisposeError is { } error) throw error;
            return PendingDispose?.Void ?? default;
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(16)] [InlineData(1024)]
    public async Task Stream_lengths_cache_Current_and_cleanup_once(int count)
    {
        var events = new ContractEvents("Stream", 1); var source = new Source(events, count); var starts = 0;
        var stream = CanonicalControl.Stream(new Request(3), (_, _) => { starts++; return source; });
        var cursor = stream.GetAsyncEnumerator(); Assert.Equal(0, starts); Assert.Throws<InvalidOperationException>(() => cursor.Current);
        var values = new List<int>();
        while (await cursor.MoveNextAsync()) { values.Add(cursor.Current); Assert.Equal(cursor.Current, values[^1]); }
        Assert.Equal(Enumerable.Range(0, count), values); Assert.Equal(count, source.Reads); Assert.Equal(count + 1, source.Moves);
        Assert.Throws<InvalidOperationException>(() => cursor.Current);
        await cursor.DisposeAsync(); await cursor.DisposeAsync(); Assert.Equal(1, source.Disposals); Assert.Equal(1, starts);
        Assert.False(await cursor.MoveNextAsync());
        Assert.Equal(Enumerable.Range(0, count).SelectMany(_ => new[] { "source.move", "source.current" }).Concat(["source.move", "source.dispose"]), events.Phases());
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task Token_identity_and_link_ownership(int mode)
    {
        using var apiSource = new CancellationTokenSource(); using var enumSource = new CancellationTokenSource();
        var api = mode is 1 or 3 or 4 ? apiSource.Token : default;
        var enumeration = mode == 3 ? api : mode is 2 or 4 ? enumSource.Token : default;
        var events = new ContractEvents("Stream", 1); var source = new Source(events, 1); var effective = default(CancellationToken);
        var cursor = CanonicalControl.Stream(new Request(1), (_, t) => { effective = t; return source; }, api).GetAsyncEnumerator(enumeration);
        Assert.True(await cursor.MoveNextAsync()); Assert.Equal(effective, source.Token);
        if (mode != 4) Assert.Equal(api.CanBeCanceled ? api : enumeration, effective);
        else { Assert.NotEqual(api, effective); Assert.NotEqual(enumeration, effective); }
        var cancellations = 0; using var registration = effective.Register(() => cancellations++);
        await cursor.DisposeAsync(); apiSource.Cancel(); enumSource.Cancel();
        Assert.Equal(mode is 0 or 4 ? 0 : 1, cancellations);
    }

    [Theory]
    [InlineData("factory")] [InlineData("get")] [InlineData("move")] [InlineData("current")]
    public async Task Startup_failure_has_no_retry_and_disposes_only_acquired_inner(string phase)
    {
        var events = new ContractEvents("Stream", 1); var source = new Source(events, 1); var error = new FormatException(phase); var starts = 0;
        if (phase == "get") source.GetError = error; if (phase == "move") source.MoveError = error; if (phase == "current") source.CurrentError = error;
        var cursor = CanonicalControl.Stream(new Request(1), (_, _) => { starts++; if (phase == "factory") throw error; return source; }).GetAsyncEnumerator();
        var observed = await ContractAwait.Observe(cursor.MoveNextAsync); Assert.False(observed.CallsiteThrow); Assert.True(observed.Faulted); Assert.Same(error, observed.Error);
        Assert.False(await cursor.MoveNextAsync()); await cursor.DisposeAsync(); Assert.Equal(1, starts);
        Assert.Equal(phase is "move" or "current" ? 1 : 0, source.Disposals);
    }

    [Fact]
    public async Task Entry_null_is_synchronous_and_pre_cancel_skips_factory()
    {
        Assert.Throws<ArgumentNullException>(() => CanonicalControl.Stream<Request, int>(null!, (_, _) => null!));
        using var cancel = new CancellationTokenSource(); cancel.Cancel(); var starts = 0;
        var cursor = CanonicalControl.Stream<Request, int>(new Request(1), (_, _) => { starts++; throw new Exception("unreachable"); }, cancel.Token).GetAsyncEnumerator();
        var observed = await ContractAwait.Observe(cursor.MoveNextAsync); Assert.False(observed.CallsiteThrow); Assert.True(observed.Canceled);
        Assert.Equal(cancel.Token, Assert.IsAssignableFrom<OperationCanceledException>(observed.Error).CancellationToken);
        await cursor.DisposeAsync(); Assert.Equal(0, starts);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Pending_move_and_cleanup_failure_release_linked_owner(bool cancellation)
    {
        using var api = new CancellationTokenSource(); using var enumeration = new CancellationTokenSource();
        var once = new ContractOnce<bool>(); var events = new ContractEvents("Stream", 1);
        var cleanup = new InvalidOperationException("cleanup"); var source = new Source(events, 1) { PendingMove = once, DisposeError = cleanup };
        var cursor = CanonicalControl.Stream(new Request(1), (_, _) => source, api.Token).GetAsyncEnumerator(enumeration.Token);
        var error = cancellation ? new OperationCanceledException(source.Token) : (Exception)new FormatException("move");
        // Effective token is established by the call; construct its cancellation exception after entry.
        var observed = await ContractAwait.Observe(cursor.MoveNextAsync, () => once.Fail(cancellation ? new OperationCanceledException(source.Token) : error));
        Assert.False(observed.CallsiteThrow); Assert.False(observed.Completed); Assert.False(observed.Canceled); Assert.False(observed.Faulted);
        Assert.Equal(1, once.Consumptions); if (cancellation) Assert.Equal(source.Token, Assert.IsAssignableFrom<OperationCanceledException>(observed.Error).CancellationToken); else Assert.Same(error, observed.Error);
        var callbacks = 0; using var registration = source.Token.Register(() => callbacks++);
        Assert.Same(cleanup, await Assert.ThrowsAsync<InvalidOperationException>(async () => await cursor.DisposeAsync()));
        api.Cancel(); enumeration.Cancel(); Assert.Equal(0, callbacks); await cursor.DisposeAsync(); Assert.Equal(1, source.Disposals);
    }

    [Fact]
    public async Task Pending_operations_are_single_consumption_and_overlap_is_rejected()
    {
        var once = new ContractOnce<bool>(); var cleanup = new ContractOnce<bool>(); var source = new Source(new("Stream", 1), 1) { PendingMove = once, PendingDispose = cleanup };
        var cursor = CanonicalControl.Stream(new Request(1), (_, _) => source).GetAsyncEnumerator(); var move = cursor.MoveNextAsync();
        Assert.Throws<InvalidOperationException>(() => cursor.MoveNextAsync()); Assert.Throws<InvalidOperationException>(() => cursor.DisposeAsync());
        once.Complete(true); Assert.True(await move); Assert.Equal(1, once.Consumptions);
        var disposing = cursor.DisposeAsync(); Assert.Throws<InvalidOperationException>(() => cursor.DisposeAsync()); cleanup.Complete(true); await disposing;
        Assert.Equal(1, cleanup.Consumptions); await cursor.DisposeAsync(); Assert.Equal(1, source.Disposals);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Unstarted_and_early_break_do_not_prefetch(bool earlyBreak)
    {
        var starts = 0; var source = new Source(new("Stream", 1), 16);
        var stream = CanonicalControl.Stream(new Request(1), (_, _) => { starts++; return source; });
        if (earlyBreak) { await foreach (var _ in stream) break; }
        else await stream.GetAsyncEnumerator().DisposeAsync();
        Assert.Equal(earlyBreak ? 1 : 0, starts); Assert.Equal(earlyBreak ? 1 : 0, source.Moves); Assert.Equal(earlyBreak ? 1 : 0, source.Disposals);
    }

    [Fact]
    public async Task Replay_parallel_cursors_and_other_thread_keep_independent_state()
    {
        var request = new Request(9); var sources = new List<Source>();
        var stream = CanonicalControl.Stream(request, (r, _) => { Assert.Same(request, r); var source = new Source(new("Stream", sources.Count + 1), 2); sources.Add(source); return source; });
        using var cancel = new CancellationTokenSource(); var first = stream.GetAsyncEnumerator(cancel.Token); var second = stream.GetAsyncEnumerator();
        Assert.True(await first.MoveNextAsync()); Assert.True(await second.MoveNextAsync()); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await first.MoveNextAsync()); await first.DisposeAsync();
        Assert.True(await second.MoveNextAsync()); Assert.Equal(1, second.Current); await second.DisposeAsync();
        await Task.Run(async () => { await using var third = stream.GetAsyncEnumerator(); Assert.True(await third.MoveNextAsync()); Assert.Equal(0, third.Current); });
        Assert.Equal(3, sources.Count); Assert.All(sources, s => Assert.Equal(1, s.Disposals));
    }

    [Fact]
    public async Task Typed_value_and_large_closed_generic_requests_keep_values()
    {
        var large = new LargeRequest(1, 2, 3, 4, 5, 6, 7, 8); var seen = default(LargeRequest);
        await foreach (var _ in CanonicalControl.Stream(large, (r, _) => { seen = r; return new Source(new("Stream", 1), 1); })) { }
        Assert.Equal(large, seen);
        var value = await CanonicalControl.Send(41, [], (r, _) => new ValueTask<int>(r + 1), new("SendAsync", 1)); Assert.Equal(42, value);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(3)] [InlineData(5)]
    public void Sync_send_has_nested_unwind_and_saved_stage_requests(int depth)
    {
        var events = new ContractEvents("SendSync", 13, 2); var saved = new List<Request>();
        var stages = Enumerable.Range(0, depth).Select(i => new ContractSyncStage<Request, int>("s" + i, () => new()
        {
            Before = r => { events.Add("s" + i, "before", r); return new(r.Value + 1); },
            After = (r, value) => { saved.Add(r); events.Add("s" + i, "after", r); return value + 10; }
        })).ToArray();
        var original = new Request(10); Request? handlerRequest = null;
        var result = CanonicalControl.SendSync(original, stages, r => { handlerRequest = r; return r.Value; }, events);
        Assert.Equal(10 + 11 * depth, result); Assert.Equal(Enumerable.Range(1, depth).Reverse().Select(i => 10 + i), saved.Select(r => r.Value));
        Assert.Equal(10 + depth, handlerRequest!.Value); Assert.All(events.Snapshot(), e => { Assert.Equal(13, e.Invocation); Assert.Equal(2, e.Branch); Assert.Equal("SendSync", e.Family); });
        var forward = Enumerable.Range(0, depth).SelectMany(i => new[] { $"s{i}.guard", $"s{i}.resolve", $"s{i}.enter", $"s{i}.before" });
        var unwind = Enumerable.Range(0, depth).Reverse().SelectMany(i => new[] { $"s{i}.after", $"s{i}.finally" });
        Assert.Equal(new[] { "send.enter" }.Concat(forward).Concat(["handler.guard", "handler.enter", "handler.return"]).Concat(unwind).Concat(["send.return"]), events.Phases());
        if (depth > 0) Assert.NotSame(original, handlerRequest);
    }

    [Theory]
    [InlineData("guard")] [InlineData("factory")] [InlineData("before")] [InlineData("handler")] [InlineData("after")] [InlineData("observer")] [InlineData("finally")]
    public async Task Async_failure_respects_entered_and_exception_replacement(string phase)
    {
        using var cancellation = new CancellationTokenSource(); var events = new ContractEvents("SendAsync", 1);
        var initial = new FormatException("initial"); var replacement = new InvalidOperationException("replacement");
        var observers = new List<Exception>(); var endings = new List<(string, ContractExit)>();
        ContractStage<Request, int> Stage(string id) => new(id, () =>
        {
            if (id == "B" && phase == "factory") throw initial;
            return new()
            {
                Before = (r, _) => { events.Add(id, "before", r); if (id == "A" && phase == "guard") cancellation.Cancel(); if (id == "B" && phase == "before") throw initial; return new(new Request(r.Value + 1)); },
                After = (_, value, _) => id == "B" && phase == "after" ? throw initial : new(value + 1),
                Observe = (_, error, _) => { observers.Add(error); if (id == "B" && phase == "observer") throw replacement; return default; },
                Finally = (_, exit, _) => { endings.Add((id, exit)); if (id == "B" && phase == "finally") throw replacement; return default; }
            };
        });
        var observed = await ContractAwait.Observe(() => CanonicalControl.Send(new Request(1), [Stage("A"), Stage("B")],
            (r, _) => phase is "handler" or "observer" or "finally" ? throw initial : new ValueTask<int>(r.Value), events, cancellation.Token));
        Assert.False(observed.CallsiteThrow); Assert.NotNull(observed.Error);
        Assert.Equal(phase == "guard", observed.Canceled); Assert.Equal(phase != "guard", observed.Faulted);
        Assert.Equal(phase is "guard" or "factory" ? new[] { "A" } : new[] { "B", "A" }, endings.Select(e => e.Item1));
        if (phase is "observer" or "finally") { Assert.Same(replacement, observed.Error); Assert.Same(replacement, observers[^1]); }
        else if (phase != "guard") Assert.Same(initial, observed.Error);
        if (phase is "guard" or "factory") Assert.DoesNotContain(events.Snapshot(), e => e.Stage == "B" && e.Kind == "enter");
        Assert.DoesNotContain(events.Snapshot(), e => e.Kind == "return" && e.Stage == "send");
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public async Task Async_hook_and_handler_suspension_consume_sources_once(bool hookPending, bool handlerPending)
    {
        var before = new ContractOnce<Request>(); var handler = new ContractOnce<int>(); var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stage = new ContractStage<Request, int>("A", () => new() { Before = (r, _) => hookPending ? before.Task : new(r) });
        var pending = CanonicalControl.Send(new Request(1), [stage], (r, _) => { reached.SetResult(); return handlerPending ? handler.Task : new(r.Value); }, new("SendAsync", 1));
        Assert.Equal(!hookPending && !handlerPending, pending.IsCompletedSuccessfully);
        if (hookPending) before.Complete(new Request(2));
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(5)); if (handlerPending) handler.Complete(3);
        Assert.Equal(handlerPending ? 3 : hookPending ? 2 : 1, await pending);
        Assert.Equal(hookPending ? 1 : 0, before.Consumptions); Assert.Equal(handlerPending ? 1 : 0, handler.Consumptions);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Callback_context_preserves_native_async_boundary_and_caller(bool suspend)
    {
        var prior = ContractEvents.Ambient.Value; ContractEvents.Ambient.Value = "caller";
        try
        {
            var once = new ContractOnce<bool>(); var events = new ContractEvents("SendAsync", 1); string? handlerAmbient = null;
            async ValueTask<Request> Native(Request r, CancellationToken _)
            { ContractEvents.Ambient.Value = "native"; if (suspend) await once.Task.ConfigureAwait(false); events.Add("native", "resumed", r); return r; }
            var a = new ContractStage<Request, int>("A", () => new() { Before = (r, _) => { ContractEvents.Ambient.Value = "direct"; return new(r); } });
            var b = new ContractStage<Request, int>("B", () => { ContractEvents.Ambient.Value = "factory"; return new() { Before = Native }; });
            var pending = CanonicalControl.Send(new Request(1), [a, b], (r, _) => { handlerAmbient = ContractEvents.Ambient.Value; return new(r.Value); }, events);
            Assert.Equal("caller", ContractEvents.Ambient.Value); if (suspend) once.Complete(true); Assert.Equal(1, await pending);
            Assert.Equal("factory", handlerAmbient); Assert.Equal("native", events.Snapshot().Single(e => e.Stage == "native").Ambient);
            Assert.Equal("caller", ContractEvents.Ambient.Value); Assert.Equal(suspend ? 1 : 0, once.Consumptions);
        }
        finally { ContractEvents.Ambient.Value = prior; }
    }

    [Fact]
    public async Task Sync_context_flows_and_suppressed_Task_Run_does_not_inherit_caller()
    {
        var prior = ContractEvents.Ambient.Value; ContractEvents.Ambient.Value = "caller";
        try
        {
            var stage = new ContractSyncStage<Request, int>("A", () => new() { Before = r => { ContractEvents.Ambient.Value = "sync"; return r; } });
            CanonicalControl.SendSync(new Request(1), [stage], r => r.Value, new("SendSync", 1)); Assert.Equal("sync", ContractEvents.Ambient.Value);
            Task<string?> child; using (ExecutionContext.SuppressFlow()) child = Task.Run<string?>(() => ContractEvents.Ambient.Value);
            Assert.Null(await child); Assert.Equal("sync", ContractEvents.Ambient.Value);
        }
        finally { ContractEvents.Ambient.Value = prior; }
    }

    [Fact]
    public async Task Void_and_notifications_have_single_consumption_and_stop_after_failure()
    {
        var request = new Request(1); var calls = new List<int>(); CanonicalControl.SendVoid(request, _ => calls.Add(0));
        var once = new ContractOnce<bool>(); var pending = CanonicalControl.SendVoidAsync(request, _ => once.Void); once.Complete(true); await pending; Assert.Equal(1, once.Consumptions);
        var failure = new FormatException("notification"); var source = new ContractOnce<bool>();
        var notification = CanonicalControl.NotifyAsync(request, [r => { calls.Add(r.Value); return default; }, _ => { calls.Add(2); return source.Void; }, _ => throw new Exception("unreachable")]);
        source.Fail(failure); Assert.Same(failure, await Assert.ThrowsAsync<FormatException>(async () => await notification));
        Assert.Equal(new[] { 0, 1, 2 }, calls); Assert.Equal(1, source.Consumptions);
        Assert.Same(failure, Assert.Throws<FormatException>(() => CanonicalControl.Notify(request, [_ => throw failure, _ => throw new Exception("unreachable")])));
    }

    [Fact]
    public void SendAll_keeps_original_request_and_commits_Span_only_after_all_success()
    {
        var request = new Request(2); var entered = 0; var destination = new[] { -1, -1, -1 };
        Func<Request, int>[] branches = [r => { entered++; Assert.Same(request, r); return r.Value; }, r => { entered++; Assert.Same(request, r); return r.Value + 1; }];
        Assert.Throws<ArgumentException>(() => CanonicalControl.SendAllSync(request, branches, destination.AsSpan(0, 1))); Assert.Equal(0, entered);
        CanonicalControl.SendAllSync(request, branches, destination); Assert.Equal(new[] { 2, 3, -1 }, destination);
        var failure = new InvalidOperationException("branch"); destination = [-1, -1, -1]; entered = 0;
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => CanonicalControl.SendAllSync(request,
            new Func<Request, int>[] { _ => { entered++; return 2; }, _ => throw failure, _ => { entered++; return 4; } }, destination)));
        Assert.Equal(new[] { -1, -1, -1 }, destination); Assert.Equal(1, entered);
        var overlap = new[] { 3, 4 }; CanonicalControl.SendAllSync(overlap, new Func<int[], int>[] { r => r[1], r => r[0] }, overlap); Assert.Equal(new[] { 4, 3 }, overlap);
    }

    private sealed class Borrowed : IDisposable { public int Disposals; public void Dispose() => Disposals++; }
    [Fact]
    public async Task DI_scoped_reuse_is_borrowed_and_disposed_scope_failure_is_lazy()
    {
        var services = new ServiceCollection(); var calls = 0; services.AddScoped(_ => { calls++; return new Borrowed(); });
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }); var scope = provider.CreateScope();
        Borrowed? borrowed = null; var state = new List<Source>();
        var stream = CanonicalControl.Stream(new Request(1), (_, _) => { borrowed = scope.ServiceProvider.GetRequiredService<Borrowed>(); var s = new Source(new("Stream", state.Count + 1), 1); state.Add(s); return s; });
        Assert.Equal(0, calls); await foreach (var _ in stream) { } await foreach (var _ in stream) { }
        Assert.Equal(1, calls); Assert.Equal(0, borrowed!.Disposals); Assert.All(state, s => Assert.Equal(1, s.Disposals));
        scope.Dispose(); Assert.Equal(1, borrowed.Disposals); var cursor = stream.GetAsyncEnumerator();
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await cursor.MoveNextAsync()); await cursor.DisposeAsync(); Assert.Equal(2, state.Count);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Async_dispose_is_single_consumption_even_after_cancellation_or_failure(bool fail)
    {
        using var api = new CancellationTokenSource(); using var enumeration = new CancellationTokenSource();
        var cleanup = new ContractOnce<bool>(); var source = new Source(new("Stream", 1), 1) { PendingDispose = cleanup };
        var cursor = CanonicalControl.Stream(new Request(1), (_, _) => source, api.Token).GetAsyncEnumerator(enumeration.Token);
        Assert.True(await cursor.MoveNextAsync()); var calls = 0; using var registration = source.Token.Register(() => calls++);
        var pending = cursor.DisposeAsync(); api.Cancel(); Assert.Equal(1, calls);
        var error = new FormatException("dispose");
        var observed = await ContractAwait.Observe(() => pending, () => { if (fail) cleanup.Fail(error); else cleanup.Complete(true); });
        Assert.False(observed.CallsiteThrow); Assert.False(observed.Completed); Assert.False(observed.Faulted); Assert.False(observed.Canceled);
        if (fail) Assert.Same(error, observed.Error); else Assert.Null(observed.Error);
        enumeration.Cancel(); Assert.Equal(1, calls); Assert.Equal(1, cleanup.Consumptions); await cursor.DisposeAsync(); Assert.Equal(1, source.Disposals);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Span_does_not_commit_on_After_or_Finally_failure(bool inFinally)
    {
        var error = new FormatException("unwind"); var destination = new[] { -1, -1 }; var entered = 0;
        var stage = new ContractSyncStage<Request, int>("A", () => new()
        { After = (_, value) => inFinally ? value : throw error, Finally = (_, _) => { if (inFinally) throw error; } });
        Func<Request, int>[] branches = [_ => 7, r => { entered++; return CanonicalControl.SendSync(r, [stage], q => q.Value, new("SendAllSync", 1, 1)); }];
        Assert.Same(error, Assert.Throws<FormatException>(() => CanonicalControl.SendAllSync(new Request(1), branches, destination)));
        Assert.Equal(new[] { -1, -1 }, destination); Assert.Equal(1, entered);
    }

    [Fact]
    public void Recorder_distinguishes_reference_token_and_exception_identity()
    {
        var events = new ContractEvents("SendAsync", 4, 3); var first = new Request(1); var equalValue = new Request(1);
        using var a = new CancellationTokenSource(); using var b = new CancellationTokenSource();
        var error = new FormatException("same text"); var otherError = new FormatException("same text");
        var service = new Borrowed(); var otherService = new Borrowed();
        events.Add("A", "before", first, a.Token, error, service: service, resolution: "first");
        events.Add("B", "before", first, a.Token, error, service: service, resolution: "reuse");
        events.Add("C", "before", equalValue, b.Token, otherError, service: otherService, resolution: "first");
        var rows = events.Snapshot(); Assert.Equal(rows[0].Request, rows[1].Request); Assert.NotEqual(rows[0].Request, rows[2].Request);
        Assert.Equal(rows[0].Token, rows[1].Token); Assert.NotEqual(rows[0].Token, rows[2].Token);
        Assert.Equal(rows[0].Error, rows[1].Error); Assert.NotEqual(rows[0].Error, rows[2].Error);
        Assert.Equal(rows[0].Service, rows[1].Service); Assert.NotEqual(rows[0].Service, rows[2].Service);
        Assert.Equal(new[] { "first", "reuse", "first" }, rows.Select(e => e.Resolution));
    }

    [Fact]
    public void Recorder_value_snapshots_detect_equal_text_with_different_values_and_types()
    {
        var original = new PlainValueRequest { Value = 1 }; var changed = new PlainValueRequest { Value = 2 };
        Assert.Equal(original.ToString(), changed.ToString());
        var expected = new ContractEvents("SendAsync", 1); var actual = new ContractEvents("SendAsync", 1); var same = new ContractEvents("SendAsync", 1);
        expected.Add("handler", "enter", original); actual.Add("handler", "enter", changed); same.Add("handler", "enter", new PlainValueRequest { Value = 1 });
        var snapshot = expected.Snapshot().Single();
        Assert.NotEqual(snapshot, actual.Snapshot().Single()); Assert.Equal(snapshot, same.Snapshot().Single());
        original.Value = 9; Assert.Equal(1, Assert.IsType<PlainValueRequest>(snapshot.Request.Value).Value);
        Assert.Equal(typeof(PlainValueRequest), snapshot.Request.Type);
        var narrow = new ContractEvents("SendAsync", 1); var wide = new ContractEvents("SendAsync", 1);
        Assert.Equal(1.ToString(), 1L.ToString()); narrow.Add("handler", "enter", 1); wide.Add("handler", "enter", 1L);
        Assert.NotEqual(narrow.Snapshot().Single(), wide.Snapshot().Single());
        Assert.Equal(typeof(int), narrow.Snapshot().Single().Request.Type); Assert.Equal(typeof(long), wide.Snapshot().Single().Request.Type);
    }

    [Fact]
    public async Task Cancellation_during_Before_still_awaits_owned_cleanup_and_skips_later_factory()
    {
        using var cancellation = new CancellationTokenSource(); var before = new ContractOnce<Request>(); var cleanup = new ContractOnce<bool>();
        var cleanupEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var later = 0;
        var stage = new ContractStage<Request, int>("A", () => new()
        {
            Before = (_, _) => before.Task,
            Finally = (_, exit, token) => { Assert.False(exit.Success); Assert.True(token.IsCancellationRequested); cleanupEntered.SetResult(); return cleanup.Void; }
        });
        var next = new ContractStage<Request, int>("B", () => { later++; throw new Exception("unreachable"); });
        var pending = CanonicalControl.Send(new Request(1), [stage, next], (_, _) => throw new Exception("unreachable"), new("SendAsync", 1), cancellation.Token);
        using var registration = cancellation.Token.Register(() => before.Fail(new OperationCanceledException(cancellation.Token)));
        cancellation.Cancel(); await cleanupEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.False(pending.IsCompleted); cleanup.Complete(true);
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending); Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(1, before.Consumptions); Assert.Equal(1, cleanup.Consumptions); Assert.Equal(0, later);
    }

    [Fact]
    public async Task Transient_factory_override_resolves_per_operation_and_container_owns_services()
    {
        var values = new List<Borrowed>(); var services = new ServiceCollection(); services.AddTransient(_ => { var service = new Borrowed(); values.Add(service); return service; });
        using var provider = services.BuildServiceProvider(); var scope = provider.CreateScope(); var sources = new List<Source>();
        var stream = CanonicalControl.Stream(new Request(1), (_, _) => { _ = scope.ServiceProvider.GetRequiredService<Borrowed>(); var source = new Source(new("Stream", sources.Count + 1), 1); sources.Add(source); return source; });
        await foreach (var _ in stream) { } await foreach (var _ in stream) { }
        Assert.Equal(2, values.Count); Assert.NotSame(values[0], values[1]); Assert.All(values, v => Assert.Equal(0, v.Disposals));
        Assert.All(sources, s => Assert.Equal(1, s.Disposals)); scope.Dispose(); Assert.All(values, v => Assert.Equal(1, v.Disposals));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task Opaque_wrapper_next_count_request_token_and_child_state_are_preserved(int repetitions)
    {
        using var originalToken = new CancellationTokenSource(); using var replacementToken = new CancellationTokenSource();
        var original = new Request(1); var replacement = new Request(9); var entered = 0; var traces = new List<ContractEvents>();
        ValueTask<int> Next(Request request, CancellationToken token)
        {
            Assert.Same(replacement, request); Assert.Equal(replacementToken.Token, token);
            var trace = new ContractEvents("SendAsync", ++entered); traces.Add(trace);
            return CanonicalControl.Send(request, [], (r, t) => { Assert.Equal(replacementToken.Token, t); return new ValueTask<int>(r.Value); }, trace, token);
        }
        async ValueTask<int> Wrapper(Request request, CancellationToken token)
        {
            Assert.Same(original, request); Assert.Equal(originalToken.Token, token);
            var result = 7; for (var i = 0; i < repetitions; i++) result = await Next(replacement, replacementToken.Token); return result;
        }
        Assert.Equal(repetitions == 0 ? 7 : 9, await Wrapper(original, originalToken.Token)); Assert.Equal(repetitions, entered);
        Assert.Equal(Enumerable.Range(1, repetitions), traces.Select(t => t.Snapshot()[0].Invocation));
    }

    [Fact]
    public async Task Sync_and_completed_async_controls_have_equal_full_normal_trace()
    {
        var sync = new ContractEvents("SendSync", 1); var async = new ContractEvents("SendAsync", 1); var request = new Request(1);
        ContractSyncStage<Request, int> Sync(string id) => new(id, () => new()
        { Before = r => { sync.Add(id, "before", r); return new(r.Value + 1); }, After = (r, value) => { sync.Add(id, "after", r); return value + 1; } });
        ContractStage<Request, int> Async(string id) => new(id, () => new()
        { Before = (r, _) => { async.Add(id, "before", r); return new(new Request(r.Value + 1)); }, After = (r, value, _) => { async.Add(id, "after", r); return new(value + 1); } });
        var a = CanonicalControl.SendSync(request, [Sync("A"), Sync("B")], r => r.Value, sync);
        var b = await CanonicalControl.Send(request, [Async("A"), Async("B")], (r, _) => new ValueTask<int>(r.Value), async);
        Assert.Equal(a, b); Assert.Equal(sync.Snapshot().Select(e => e with { Family = "normal" }), async.Snapshot().Select(e => e with { Family = "normal" }));
    }

    [Fact]
    public async Task Stream_Current_is_cached_inside_each_Move_context_boundary()
    {
        var prior = ContractEvents.Ambient.Value; ContractEvents.Ambient.Value = "caller";
        try
        {
            var events = new ContractEvents("Stream", 1); var source = new Source(events, 2);
            var cursor = CanonicalControl.Stream(new Request(1), (_, _) => { ContractEvents.Ambient.Value = "factory"; return source; }).GetAsyncEnumerator();
            Assert.True(await cursor.MoveNextAsync()); Assert.Equal("caller", ContractEvents.Ambient.Value); Assert.Equal(0, cursor.Current); Assert.Equal(0, cursor.Current);
            Assert.True(await cursor.MoveNextAsync()); Assert.Equal(1, cursor.Current); await cursor.DisposeAsync();
            Assert.Equal(new[] { "factory", "caller" }, events.Snapshot().Where(e => e.Kind == "current").Select(e => e.Ambient)); Assert.Equal(2, source.Reads);
        }
        finally { ContractEvents.Ambient.Value = prior; }
    }

    [Theory]
    [InlineData("throw")] [InlineData("fault")] [InlineData("cancel")] [InlineData("pending")] [InlineData("success")]
    public async Task Non_generic_ValueTask_observation_preserves_status_and_single_consumption(string mode)
    {
        using var cancellation = new CancellationTokenSource(); if (mode == "cancel") cancellation.Cancel();
        var once = new ContractOnce<bool>(); var error = new FormatException(mode);
        var observed = await ContractAwait.Observe(() => mode switch
        { "throw" => throw error, "fault" => ValueTask.FromException(error), "cancel" => ValueTask.FromCanceled(cancellation.Token), "pending" => once.Void, _ => default },
            () => { if (mode == "pending") once.Complete(true); });
        Assert.Equal(mode == "throw", observed.CallsiteThrow); Assert.Equal(mode == "fault", observed.Faulted);
        Assert.Equal(mode == "cancel", observed.Canceled); Assert.Equal(mode == "success", observed.Completed);
        if (mode is "throw" or "fault") Assert.Same(error, observed.Error);
        else if (mode == "cancel") Assert.Equal(cancellation.Token, Assert.IsAssignableFrom<OperationCanceledException>(observed.Error).CancellationToken);
        else Assert.Null(observed.Error);
        Assert.Equal(mode == "pending" ? 1 : 0, once.Consumptions);
    }
}
