using System.Linq;

namespace Zendiator.SourceGenerator;

internal static class CanonicalStreamPendingTaskEmitter
{
    internal static string Emit(CanonicalStreamFallbackModel model, int cursor)
    {
        if (model.Segments.Count == 1 && model.Segments[0].Wrapper is null && model.Segments[0].Hooks.Count == 0)
            return CanonicalStreamCompletionCellEmitter.Emit(model, cursor);
        if (CanonicalStreamGroupAdvanceEmitter.IsParent(model, cursor))
            return CanonicalStreamGroupPendingEmitter.Emit(model, CanonicalStreamGroupAdvanceEmitter.Chain(model));
        if (CanonicalStreamPrivateAdvanceEmitter.IsParent(model, cursor))
            return EmitIdentity(model, cursor, CanonicalStreamPrivateAdvanceEmitter.Inner(model)!.Id);
        var runner = Name(model, "PendingTaskRunner");
        var start = Name(model, "PendingTaskStart");
        return $$"""
                        private {{runner}}? _pendingTaskRunner;
                        private global::System.Threading.Tasks.ValueTask<bool> AwaitMoveAsync(global::System.Threading.Tasks.ValueTask<bool> pending)
                        {
                            var start = new {{start}}(this, pending);
                            var boundary = global::System.Runtime.CompilerServices.AsyncValueTaskMethodBuilder<bool>.Create();
                            boundary.Start(ref start);
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
                            internal {{start}}(Cursor{{cursor}} owner, global::System.Threading.Tasks.ValueTask<bool> pending)
                            { _owner = owner; _pending = pending; Result = default; }
                            public void MoveNext()
                            {
                                try { Result = Start(); }
                                catch (global::System.Exception error) { Result = Fault(error); }
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
                                return new(runner.Begin(_owner, awaiter));
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
                            private global::System.Runtime.CompilerServices.AsyncTaskMethodBuilder<bool> _promise;
                            private global::System.Threading.ExecutionContext? _context;
                            private int _phase;
                            internal {{runner}}() { _resume = Resume; }
                            internal global::System.Threading.Tasks.Task<bool> Begin(
                                Cursor{{cursor}} owner,
                                global::System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable<bool>.ConfiguredValueTaskAwaiter awaiter)
                            {
                                if (_phase != Idle) throw new global::System.InvalidOperationException("The pending Stream runner is unavailable.");
                                _promise = global::System.Runtime.CompilerServices.AsyncTaskMethodBuilder<bool>.Create();
                                var task = _promise.Task;
                                _owner = owner;
                                _awaiter = awaiter;
                                _context = global::System.Threading.ExecutionContext.Capture();
                                _phase = Raw;
                                Register(awaiter);
                                return task;
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
                                var promise = _promise;
                                var owner = _owner!;
                                _awaiter = default;
                                _promise = default;
                                _context = null;
                                _owner = null;
                                _phase = Idle;
                                if (closing) owner.DetachPendingTaskRunner(this);
                                if (error is null) promise.SetResult(result);
                                else promise.SetException(error);
                            }
                        }
            """;
    }

    private static string EmitIdentity(CanonicalStreamFallbackModel model, int cursor, int child)
    {
        var runner = Name(model, "PendingTaskRunner");
        var start = Name(model, "PendingTaskStart");
        return $$"""
                        private {{runner}}? _pendingTaskRunner;
                        private global::System.Threading.Tasks.ValueTask<bool> AwaitMoveAsync(global::System.Threading.Tasks.ValueTask<bool> pending)
                            => AwaitMoveAsync(pending, null);
                        private global::System.Threading.Tasks.ValueTask<bool> AwaitMoveAsync(
                            global::System.Threading.Tasks.ValueTask<bool> pending, Cursor{{child}}? child)
                        {
                            var start = new {{start}}(this, pending, child);
                            var boundary = global::System.Runtime.CompilerServices.AsyncValueTaskMethodBuilder<bool>.Create();
                            boundary.Start(ref start);
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
                            private readonly Cursor{{child}}? _child;
                            internal global::System.Threading.Tasks.ValueTask<bool> Result;
                            internal {{start}}(Cursor{{cursor}} owner, global::System.Threading.Tasks.ValueTask<bool> pending, Cursor{{child}}? child)
                            { _owner = owner; _pending = pending; _child = child; Result = default; }
                            public void MoveNext()
                            {
                                try { Result = Start(); }
                                catch (global::System.Exception error) { Result = Fault(error); }
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
                                if (failure is not null || completed)
                                {
                                    if (_child is { } child) return _owner.CompleteIdentityMove(child, moved, failure);
                                    if (failure is not null) return _owner.MoveFailed(failure);
                                    return moved ? _owner.MoveSucceeded(true) : _owner.EndAsync();
                                }
                                var runner = _owner._pendingTaskRunner ??= new {{runner}}();
                                return new(runner.Begin(_owner, awaiter, _child));
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
                            private const int Idle = 0, Raw = 1, Cleanup = 2, ChildCleanup = 3, Parent = 4;
                            private Cursor{{cursor}}? _owner;
                            private Cursor{{child}}? _child;
                            private readonly global::System.Action _resume;
                            private global::System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable<bool>.ConfiguredValueTaskAwaiter _awaiter;
                            private global::System.Runtime.CompilerServices.AsyncTaskMethodBuilder<bool> _promise;
                            private global::System.Threading.ExecutionContext? _context;
                            private int _phase;
                            internal {{runner}}() { _resume = Resume; }
                            internal global::System.Threading.Tasks.Task<bool> Begin(
                                Cursor{{cursor}} owner,
                                global::System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable<bool>.ConfiguredValueTaskAwaiter awaiter,
                                Cursor{{child}}? child = null) => BeginPhase(owner, awaiter, child, Raw);
                            internal global::System.Threading.Tasks.Task<bool> BeginChildCleanup(
                                Cursor{{cursor}} owner,
                                global::System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable<bool>.ConfiguredValueTaskAwaiter awaiter,
                                Cursor{{child}} child) => BeginPhase(owner, awaiter, child, ChildCleanup);
                            private global::System.Threading.Tasks.Task<bool> BeginPhase(
                                Cursor{{cursor}} owner,
                                global::System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable<bool>.ConfiguredValueTaskAwaiter awaiter,
                                Cursor{{child}}? child, int phase)
                            {
                                if (_phase != Idle) throw new global::System.InvalidOperationException("The pending Stream runner is unavailable.");
                                _promise = global::System.Runtime.CompilerServices.AsyncTaskMethodBuilder<bool>.Create();
                                var task = _promise.Task;
                                _owner = owner;
                                _child = child;
                                _awaiter = awaiter;
                                _context = global::System.Threading.ExecutionContext.Capture();
                                _phase = phase;
                                Register(awaiter);
                                return task;
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
                                if (phase != Raw && phase != Cleanup && phase != ChildCleanup)
                                    throw new global::System.InvalidOperationException("The Stream runner has no pending await.");
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
                                if (phase == ChildCleanup)
                                {
                                    CompleteChild(moved, failure);
                                    return;
                                }
                                if (_child is not null)
                                {
                                    BeginChildSettlement(moved, failure);
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
                            private void BeginChildSettlement(bool moved, global::System.Exception? rawFailure)
                            {
                                global::System.Threading.Tasks.ValueTask<bool> operation = default;
                                global::System.Exception? failure = null;
                                try { operation = _child!.CompletePrivateResult(moved, rawFailure); }
                                catch (global::System.Exception error) { failure = error; }
                                if (failure is not null)
                                {
                                    CompleteChild(false, failure);
                                    return;
                                }
                                var awaiter = operation.ConfigureAwait(false).GetAwaiter();
                                bool completed = false, result = false;
                                try
                                {
                                    completed = awaiter.IsCompleted;
                                    if (completed) result = awaiter.GetResult();
                                }
                                catch (global::System.Exception error) { failure = error; }
                                if (failure is not null || completed)
                                {
                                    CompleteChild(result, failure);
                                    return;
                                }
                                _awaiter = awaiter;
                                _context = global::System.Threading.ExecutionContext.Capture();
                                _phase = ChildCleanup;
                                Register(awaiter);
                            }
                            private void CompleteChild(bool moved, global::System.Exception? failure)
                            {
                                var child = _child!;
                                _child = null;
                                _awaiter = default;
                                _context = null;
                                _phase = Parent;
                                child.ReleasePrivateMove();
                                CompleteParent(moved, failure);
                            }
                            private void CompleteParent(bool moved, global::System.Exception? rawFailure)
                            {
                                global::System.Threading.Tasks.ValueTask<bool> operation = default;
                                global::System.Exception? failure = null;
                                var closing = rawFailure is not null || !moved;
                                try { operation = _owner!.CompleteIdentityParent(moved, rawFailure); }
                                catch (global::System.Exception error) { failure = error; }
                                if (failure is not null)
                                {
                                    Finish(false, failure, closing: true);
                                    return;
                                }
                                var awaiter = operation.ConfigureAwait(false).GetAwaiter();
                                bool completed = false, result = false;
                                try
                                {
                                    completed = awaiter.IsCompleted;
                                    if (completed) result = awaiter.GetResult();
                                }
                                catch (global::System.Exception error) { failure = error; }
                                if (failure is not null || completed)
                                {
                                    Finish(result, failure, closing || failure is not null);
                                    return;
                                }
                                _awaiter = awaiter;
                                _context = global::System.Threading.ExecutionContext.Capture();
                                _phase = Cleanup;
                                Register(awaiter);
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
                                var promise = _promise;
                                var owner = _owner!;
                                _awaiter = default;
                                _promise = default;
                                _context = null;
                                _child = null;
                                _owner = null;
                                _phase = Idle;
                                if (closing) owner.DetachPendingTaskRunner(this);
                                if (error is null) promise.SetResult(result);
                                else promise.SetException(error);
                            }
                        }
            """;
    }

    private static string Name(CanonicalStreamFallbackModel model, string name)
    {
        while (model.Generic?.Parameters.Contains(name) == true)
            name = "_" + name;
        return name;
    }
}
