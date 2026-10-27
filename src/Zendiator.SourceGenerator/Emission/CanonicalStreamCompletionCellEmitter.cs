using System.Linq;

namespace Zendiator.SourceGenerator;

internal static class CanonicalStreamCompletionCellEmitter
{
    internal static string Emit(CanonicalStreamFallbackModel model, int cursor)
    {
        var runner = Name(model, "PendingTaskRunner");
        var start = Name(model, "PendingTaskStart");
        return $$"""
                        private {{runner}}? _pendingTaskRunner;
                        private global::System.Threading.Tasks.ValueTask<bool> AwaitMoveAsync(global::System.Threading.Tasks.ValueTask<bool> pending)
                            => AwaitMoveAsync(pending, out _);
                        private global::System.Threading.Tasks.ValueTask<bool> AwaitMoveAsync(global::System.Threading.Tasks.ValueTask<bool> pending, out global::Zendiator.Streaming.StreamCursorState.MoveCompletion? completion)
                        {
                            var start = new {{start}}(this, pending);
                            var boundary = global::System.Runtime.CompilerServices.AsyncValueTaskMethodBuilder<bool>.Create();
                            boundary.Start(ref start);
                            completion = start.Completion;
                            return start.Result;
                        }
                        private void DetachPendingTaskRunner({{runner}} runner)
                        {
                            if (global::System.Object.ReferenceEquals(_pendingTaskRunner, runner)) _pendingTaskRunner = null;
                        }
                        private struct {{start}} : global::System.Runtime.CompilerServices.IAsyncStateMachine
                        {
                            private readonly Cursor{{cursor}} _owner;
                            private readonly global::System.Threading.Tasks.ValueTask<bool> _pending;
                            internal global::System.Threading.Tasks.ValueTask<bool> Result;
                            internal global::Zendiator.Streaming.StreamCursorState.MoveCompletion? Completion;
                            internal {{start}}(Cursor{{cursor}} owner, global::System.Threading.Tasks.ValueTask<bool> pending)
                            { _owner = owner; _pending = pending; Result = default; Completion = null; }
                            public void MoveNext()
                            {
                                try { Result = Start(); }
                                catch (global::System.Exception error) { Completion = null; Result = Fault(error); }
                            }
                            private global::System.Threading.Tasks.ValueTask<bool> Start()
                            {
                                var awaiter = _pending.ConfigureAwait(false).GetAwaiter();
                                bool completed = false, moved = false;
                                global::System.Exception? failure = null;
                                try
                                {
                                    completed = awaiter.IsCompleted;
                                    if (completed) moved = awaiter.GetResult();
                                }
                                catch (global::System.Exception error) { failure = error; }
                                if (failure is not null) return _owner.MoveFailed(failure);
                                if (completed) return moved ? _owner.MoveSucceeded(true) : _owner.EndAsync();
                                var runner = _owner._pendingTaskRunner ??= new {{runner}}();
                                return runner.Begin(_owner, awaiter, out Completion);
                            }
                            private static global::System.Threading.Tasks.ValueTask<bool> Fault(global::System.Exception error)
                            {
                                var promise = global::System.Runtime.CompilerServices.AsyncTaskMethodBuilder<bool>.Create();
                                promise.SetException(error);
                                return new(promise.Task);
                            }
                            public void SetStateMachine(global::System.Runtime.CompilerServices.IAsyncStateMachine stateMachine) =>
                                throw new global::System.NotSupportedException("The pending entry boundary is not boxed.");
                        }
                        private sealed class {{runner}}
                        {
                            private const int Idle = 0, Raw = 1, Cleanup = 2;
                            private Cursor{{cursor}}? _owner;
                            private readonly global::System.Action _resume;
                            private global::System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable<bool>.ConfiguredValueTaskAwaiter _awaiter;
                            private global::Zendiator.Streaming.StreamCursorState.MoveCompletion? _firstCompletion, _secondCompletion, _completion;
                            private global::System.Threading.ExecutionContext? _context;
                            private int _phase;
                            internal {{runner}}() { _resume = Resume; }
                            internal global::System.Threading.Tasks.ValueTask<bool> Begin(
                                Cursor{{cursor}} owner,
                                global::System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable<bool>.ConfiguredValueTaskAwaiter awaiter,
                                out global::Zendiator.Streaming.StreamCursorState.MoveCompletion completion)
                            {
                                if (_phase != Idle) throw new global::System.InvalidOperationException("The pending Stream runner is unavailable.");
                                var first = _firstCompletion ??= new global::Zendiator.Streaming.StreamCursorState.MoveCompletion();
                                if (first.TryPrepare(out var operation)) completion = first;
                                else
                                {
                                    var second = _secondCompletion ??= new global::Zendiator.Streaming.StreamCursorState.MoveCompletion();
                                    if (second.TryPrepare(out operation)) completion = second;
                                    else
                                    {
                                        completion = new global::Zendiator.Streaming.StreamCursorState.MoveCompletion();
                                        completion.TryPrepare(out operation);
                                    }
                                }
                                _completion = completion;
                                _owner = owner;
                                _awaiter = awaiter;
                                _context = global::System.Threading.ExecutionContext.Capture();
                                _phase = Raw;
                                Register(awaiter);
                                return operation;
                            }
                            private void Register(global::System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable<bool>.ConfiguredValueTaskAwaiter awaiter)
                            {
                                try { awaiter.UnsafeOnCompleted(_resume); }
                                catch (global::System.Exception error) { ReportRegistrationFailure(error); }
                            }
                            private static void ReportRegistrationFailure(global::System.Exception error)
                            {
                                try
                                {
                                    var dispatch = global::System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error);
                                    if (global::System.Threading.ThreadPool.QueueUserWorkItem(
                                        static (global::System.Runtime.ExceptionServices.ExceptionDispatchInfo failure) => failure.Throw(),
                                        dispatch, preferLocal: false)) return;
                                }
                                catch (global::System.Exception reportingError)
                                {
                                    global::System.Environment.FailFast("Cannot report a Stream awaiter registration failure.", reportingError);
                                }
                                global::System.Environment.FailFast("Cannot queue a Stream awaiter registration failure.", error);
                            }
                            private void Resume()
                            {
                                var context = _context;
                                if (context is null) ResumeCore();
                                else global::System.Threading.ExecutionContext.Run(context,
                                    static state => (({{runner}})state!).ResumeCore(), this);
                            }
                            private void ResumeCore()
                            {
                                var phase = _phase;
                                if (phase != Raw && phase != Cleanup) throw new global::System.InvalidOperationException("The Stream runner has no pending await.");
                                var awaiter = _awaiter;
                                _awaiter = default;
                                bool moved = false;
                                global::System.Exception? failure = null;
                                try { moved = awaiter.GetResult(); }
                                catch (global::System.Exception error) { failure = error; }
                                if (phase == Cleanup)
                                {
                                    Finish(moved, failure, closing: true);
                                    return;
                                }
                                if (failure is not null || !moved)
                                {
                                    BeginCleanup(failure);
                                    return;
                                }
                                _owner!.MoveSucceeded(true).GetAwaiter().GetResult();
                                Finish(true, null, closing: false);
                            }
                            private void BeginCleanup(global::System.Exception? rawFailure)
                            {
                                global::System.Threading.Tasks.ValueTask<bool> operation = default;
                                global::System.Exception? failure = null;
                                try { operation = rawFailure is null ? _owner!.EndAsync() : _owner!.MoveFailed(rawFailure); }
                                catch (global::System.Exception error) { failure = error; }
                                if (failure is not null)
                                {
                                    Finish(false, failure, closing: true);
                                    return;
                                }
                                var awaiter = operation.ConfigureAwait(false).GetAwaiter();
                                bool completed = false, moved = false;
                                try
                                {
                                    completed = awaiter.IsCompleted;
                                    if (completed) moved = awaiter.GetResult();
                                }
                                catch (global::System.Exception error) { failure = error; }
                                if (failure is not null || completed)
                                {
                                    Finish(moved, failure, closing: true);
                                    return;
                                }
                                _awaiter = awaiter;
                                _context = global::System.Threading.ExecutionContext.Capture();
                                _phase = Cleanup;
                                Register(awaiter);
                            }
                            private void Finish(bool result, global::System.Exception? error, bool closing)
                            {
                                var completion = _completion!;
                                var owner = _owner!;
                                _awaiter = default;
                                _completion = null;
                                _context = null;
                                _owner = null;
                                _phase = Idle;
                                if (closing) owner.DetachPendingTaskRunner(this);
                                completion.Complete(result, error);
                            }
                        }
            """;
    }

    private static string Name(CanonicalStreamFallbackModel model, string name)
    {
        while (model.Generic?.Parameters.Contains(name) == true) name = "_" + name;
        return name;
    }
}
