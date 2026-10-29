using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Competitive;

internal static class NativePoolingGate
{
    internal const string Family = "native-producer-pooling-v1";
    private static readonly (string Name, bool Pooled, int Depth)[] Paths =
    [
        ("RawOrdinary", false, -1), ("RawPooled", true, -1),
        ("ZenOrdinary0", false, 0), ("ZenPooled0", true, 0),
        ("ZenOrdinary3", false, 3), ("ZenPooled3", true, 3),
        ("ZenOrdinary5", false, 5), ("ZenPooled5", true, 5)
    ];

    internal static async Task Run(string output)
    {
        Directory.CreateDirectory(output);
        Environment.SetEnvironmentVariable("COLD_CAPTURE_CHILD", "0");
        Environment.SetEnvironmentVariable("COLD_AUDIT_REG", "1");
        Environment.SetEnvironmentVariable("COLD_EVIDENCE_DIR", output);
        var results = new List<object>();
        var methods = new[] { nameof(NativePoolingBenchmarks.RawOrdinary), nameof(NativePoolingBenchmarks.RawPooled),
            nameof(NativePoolingBenchmarks.ZenOrdinary0), nameof(NativePoolingBenchmarks.ZenPooled0),
            nameof(NativePoolingBenchmarks.ZenOrdinary5), nameof(NativePoolingBenchmarks.ZenPooled5) };
        var ordinary = typeof(WorkStreamCursor).GetMethod("MovePendingAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var pooled = typeof(PooledWorkStreamCursor).GetMethod("MovePendingAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var benchmark = new NativePoolingBenchmarks { Count = 16 };
        benchmark.Setup();
        try
        {
            foreach (var path in Paths)
            {
                foreach (var asynchronous in new[] { false, true })
                    foreach (var early in new[] { false, true })
                    {
                        var probe = new Probe();
                        var cursor = Create(path, asynchronous, probe, default);
                        Check(probe.Handlers == 0 && probe.Items == 0 && probe.Disposals == 0 && probe.Events.Count == 0,
                            path.Name, "lazy-before-first-Move");
                        var values = new List<int>();
                        var pending = 0;
                        try
                        {
                            while (true)
                            {
                                var move = cursor.MoveNextAsync();
                                if (!move.IsCompleted) pending++;
                                if (!await move.ConfigureAwait(false)) break;
                                values.Add(cursor.Current);
                                if (early) break;
                            }
                        }
                        finally { await cursor.DisposeAsync().ConfigureAwait(false); }
                        var count = early ? 1 : 16;
                        var expectedEvents = Enumerable.Range(1, Math.Max(path.Depth, 0)).Append(100);
                        Check(values.SequenceEqual(Enumerable.Range(0, count)) && probe.Handlers == 1 &&
                            probe.Items == count && probe.Disposals == 1 && probe.Events.SequenceEqual(expectedEvents),
                            path.Name, "values-order-count-and-once-cleanup");
                        if (early) await RepeatDispose(cursor, probe, path.Name, "settled-Early-second-Dispose");
                        results.Add(new
                        {
                            path = path.Name,
                            scenario = early ? "Early16" : "Full16",
                            asynchronous,
                            status = "Passed",
                            pendingMovesObserved = pending,
                            values,
                            probe.Handlers,
                            probe.Items,
                            probe.Disposals
                        });
                    }

                var cold = new Probe();
                var coldCursor = Create(path, true, cold, default);
                await coldCursor.DisposeAsync().ConfigureAwait(false);
                Check(cold.Handlers == 0 && cold.Items == 0 && cold.Disposals == 0 && cold.Events.Count == 0,
                    path.Name, "cold-Dispose-does-not-activate");
                await RepeatDispose(coldCursor, cold, path.Name, "cold-second-Dispose");

                var independent = new Probe();
                var sequence = path.Depth < 0 ? null : benchmark.Stream(path.Pooled, path.Depth, 16, true, independent, default);
                var first = sequence?.GetAsyncEnumerator() ?? Create(path, true, independent, default);
                var second = sequence?.GetAsyncEnumerator() ?? Create(path, true, independent, default);
                try
                {
                    Check(!ReferenceEquals(first, second), path.Name, "independent-cursors");
                    Check(await first.MoveNextAsync().ConfigureAwait(false) && first.Current == 0, path.Name, "first-enumeration-zero");
                    Check(await second.MoveNextAsync().ConfigureAwait(false) && second.Current == 0, path.Name, "second-enumeration-zero");
                    Check(await first.MoveNextAsync().ConfigureAwait(false) && first.Current == 1, path.Name, "first-enumeration-independent-position");
                }
                finally
                {
                    await first.DisposeAsync().ConfigureAwait(false);
                    await second.DisposeAsync().ConfigureAwait(false);
                }
                Check(independent.Handlers == 2 && independent.Items == 3 && independent.Disposals == 2,
                    path.Name, "independent-cleanup");

                await RegisteredNext(path);
                await SavedTask(path);
                await PendingFailure(path, cancel: true);
                await PendingFailure(path, cancel: false);
                results.Add(new
                {
                    path = path.Name,
                    scenario = "Lifecycle",
                    status = "Passed",
                    checks = new[] { "cold-Dispose-and-explicit-second-Dispose", "settled-Early-explicit-second-Dispose",
                        "independent-enumerations", "forced-pending-consume-then-immediate-next",
                        "saved-AsTask-durable-result", "pending-cancel-consume-once-then-Dispose",
                        "pending-fault-consume-once-then-Dispose-and-explicit-second-Dispose" }
                });
            }

            foreach (var method in methods)
            {
                foreach (var early in new[] { false, true })
                {
                    benchmark.Early = early;
                    var result = (ValueTask<int>)typeof(NativePoolingBenchmarks).GetMethod(method)!.Invoke(benchmark, null)!;
                    Check(await result.ConfigureAwait(false) == (early ? 0 : 120), method, "exact-timed-consumer-sync16");
                }
            }
            var asynchronousBenchmark = new NativePoolingBenchmarks { Count = 1024, Asynchronous = true };
            asynchronousBenchmark.Setup();
            try
            {
                foreach (var method in methods)
                {
                    var result = (ValueTask<int>)typeof(NativePoolingBenchmarks).GetMethod(method)!.Invoke(asynchronousBenchmark, null)!;
                    Check(await result.ConfigureAwait(false) == 523776, method, "exact-timed-consumer-TaskYield-Full1024");
                    results.Add(new { path = method, scenario = "ExactTimedConsumerTaskYieldFull1024", status = "Passed", sum = 523776 });
                }
            }
            finally { asynchronousBenchmark.Cleanup(); }
            Check(ordinary.GetCustomAttribute<AsyncMethodBuilderAttribute>() is null &&
                pooled.GetCustomAttribute<AsyncMethodBuilderAttribute>()?.BuilderType == typeof(PoolingAsyncValueTaskMethodBuilder<>),
                Family, "builder-attribute-selection");
            Check(BuilderFields(ordinary).Contains(typeof(AsyncValueTaskMethodBuilder<bool>)) &&
                BuilderFields(pooled).Contains(typeof(PoolingAsyncValueTaskMethodBuilder<bool>)),
                Family, "compiler-state-machine-builder-selection");
            Save("Passed", null);
        }
        catch (Exception error)
        {
            Save("Failed", error.ToString());
            throw;
        }
        finally { benchmark.Cleanup(); }

        IAsyncEnumerator<int> Create((string Name, bool Pooled, int Depth) path, bool asynchronous, Probe probe, CancellationToken token) =>
            path.Depth < 0
                ? path.Pooled ? new PooledWorkStreamCursor(16, asynchronous, probe, token) : new WorkStreamCursor(16, asynchronous, probe, token)
                : benchmark.Stream(path.Pooled, path.Depth, 16, asynchronous, probe, token).GetAsyncEnumerator();

        async Task RegisteredNext((string Name, bool Pooled, int Depth) path)
        {
            var probe = new Probe();
            var cursor = Create(path, true, probe, default);
            try
            {
                var context = new QueuedContext();
                var nextContext = new QueuedContext();
                var first = StartQueued(cursor, context, path.Name);
                var awaiter = first.ConfigureAwait(false).GetAwaiter();
                var nextIssued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                ValueTask<bool> next = default;
                awaiter.UnsafeOnCompleted(() =>
                {
                    try
                    {
                        Check(awaiter.GetResult() && cursor.Current == 0, path.Name, "registered-first-result");
                        // The old ValueTask is consumed before reentry; it is never read after the next Move.
                        next = StartQueued(cursor, nextContext, path.Name);
                        nextIssued.SetResult();
                    }
                    catch (Exception error) { nextIssued.SetException(error); }
                });
                context.Drain();
                await nextIssued.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                nextContext.Drain();
                Check(await next.ConfigureAwait(false) && cursor.Current == 1, path.Name, "next-Move-immediately-after-GetResult");
            }
            finally { await cursor.DisposeAsync().ConfigureAwait(false); }
            Check(probe.Items == 2 && probe.Disposals == 1, path.Name, "registered-next-cleanup");
        }

        async Task SavedTask((string Name, bool Pooled, int Depth) path)
        {
            var probe = new Probe();
            var cursor = Create(path, true, probe, default);
            try
            {
                var context = new QueuedContext();
                var first = StartQueued(cursor, context, path.Name);
                var saved = first.AsTask();
                context.Drain();
                Check(await saved.ConfigureAwait(false) && cursor.Current == 0, path.Name, "saved-Task-first-result");
                for (var expected = 1; expected < 16; expected++)
                    Check(await cursor.MoveNextAsync().ConfigureAwait(false) && cursor.Current == expected, path.Name, "later-sequential-Moves");
                Check(!await cursor.MoveNextAsync().ConfigureAwait(false), path.Name, "saved-Task-EOF");
                Check(saved.Status == TaskStatus.RanToCompletion && saved.Result, path.Name, "saved-Task-remains-true-after-reuse");
            }
            finally { await cursor.DisposeAsync().ConfigureAwait(false); }
            Check(probe.Items == 16 && probe.Disposals == 1, path.Name, "saved-Task-cleanup");
        }

        async Task PendingFailure((string Name, bool Pooled, int Depth) path, bool cancel)
        {
            using var cancellation = new CancellationTokenSource();
            var probe = new Probe();
            var cursor = Create(path, true, probe, cancellation.Token);
            try
            {
                var context = new QueuedContext();
                var pending = StartQueued(cursor, context, path.Name);
                var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                pending.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(() => completed.SetResult());
                if (cancel) cancellation.Cancel();
                else probe.Fail = true;
                context.Drain();
                await completed.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Check(cancel ? pending.IsCanceled && !pending.IsFaulted : pending.IsFaulted && !pending.IsCanceled,
                    path.Name, cancel ? "pending-Canceled-classification-before-consumption" : "pending-Faulted-classification-before-consumption");
                var caught = false;
                try { await pending.ConfigureAwait(false); }
                catch (OperationCanceledException error) when (cancel)
                {
                    Check(error.CancellationToken == cancellation.Token, path.Name, "pending-cancel-token");
                    caught = true;
                }
                catch (ArithmeticException error) when (!cancel)
                {
                    Check(error.Message == "Expected correctness sentinel", path.Name, "pending-fault-sentinel");
                    caught = true;
                }
                Check(caught, path.Name, cancel ? "pending-cancel-observed-once" : "pending-fault-observed-once");
            }
            finally { await cursor.DisposeAsync().ConfigureAwait(false); }
            Check(probe.Handlers == 1 && probe.Items == 0 && probe.Disposals == 1, path.Name, "failed-Move-settled-before-Dispose");
            if (!cancel) await RepeatDispose(cursor, probe, path.Name, "settled-fault-second-Dispose");
        }

        async Task RepeatDispose(IAsyncEnumerator<int> cursor, Probe probe, string path, string scenario)
        {
            var handlers = probe.Handlers;
            var items = probe.Items;
            var logicalDisposals = probe.Disposals;
            var events = probe.Events.ToArray();
            await cursor.DisposeAsync().ConfigureAwait(false);
            Check(probe.Handlers == handlers && probe.Items == items && probe.Disposals == logicalDisposals &&
                probe.Events.SequenceEqual(events), path, scenario + "-unchanged-logical-events");
        }

        void Save(string status, string? error) => ArtifactEvidence.Write(Path.Combine(output, "native-pooling-validation.json"), new
        {
            schemaVersion = 1,
            family = Family,
            status,
            error,
            results,
            entryType = benchmark.EntryType.FullName,
            composition = typeof(BenchmarkPooledNativeStreams).FullName,
            ordinaryProducer = typeof(WorkStreamCursor).FullName,
            pooledProducer = typeof(PooledWorkStreamCursor).FullName,
            producerStateMachineBuilders = new[]
            {
                new { producer = typeof(WorkStreamCursor).FullName, fields = BuilderFields(ordinary).Select(type => type.FullName).ToArray() },
                new { producer = typeof(PooledWorkStreamCursor).FullName, fields = BuilderFields(pooled).Select(type => type.FullName).ToArray() }
            },
            consumer = "NativePoolingBenchmarks.Consume(IAsyncEnumerator<int>,bool)",
            timingExecuted = false,
            originalFamiliesReplaced = false,
            controlledDepth3 = "Semantic generalization only; not an additional timed condition",
            limits = new[] { "Task.Yield does not guarantee every public Move is observed pending",
                "The BCL builder pools pending method state, not cursor objects",
                "No assertion of private pool hits, exact box counts, zero allocation, or illegal-consumption detection" },
            assemblies = ArtifactEvidence.Assemblies(),
            runtime = ArtifactEvidence.Runtime()
        });
    }

    private static Type[] BuilderFields(MethodInfo method)
    {
        var stateMachine = method.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType
            ?? throw new InvalidOperationException("Expected a compiler-generated async state machine");
        return stateMachine.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(field => field.FieldType)
            .Where(type => type.IsGenericType &&
                (type.GetGenericTypeDefinition() == typeof(AsyncValueTaskMethodBuilder<>) ||
                    type.GetGenericTypeDefinition() == typeof(PoolingAsyncValueTaskMethodBuilder<>)))
            .ToArray();
    }

    private static ValueTask<bool> StartQueued(IAsyncEnumerator<int> cursor, QueuedContext context, string path)
    {
        var prior = SynchronizationContext.Current;
        ValueTask<bool> move;
        try { SynchronizationContext.SetSynchronizationContext(context); move = cursor.MoveNextAsync(); }
        finally { SynchronizationContext.SetSynchronizationContext(prior); }
        Check(!move.IsCompleted && context.Count > 0, path, "controlled-source-pending");
        return move;
    }

    private static void Check(bool condition, string path, string scenario)
    {
        if (!condition) throw new InvalidOperationException($"{path}: {scenario}");
    }

    private sealed class QueuedContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> callbacks = new();
        internal int Count => callbacks.Count;
        public override void Post(SendOrPostCallback callback, object? state) => callbacks.Enqueue((callback, state));
        internal void Drain()
        {
            while (callbacks.TryDequeue(out var item)) item.Callback(item.State);
        }
    }
}
