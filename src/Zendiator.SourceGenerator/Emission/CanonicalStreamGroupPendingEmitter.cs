using System;
using System.Collections.Generic;
using System.Linq;

namespace Zendiator.SourceGenerator;

internal static class CanonicalStreamGroupPendingEmitter
{
    internal static string Emit(CanonicalStreamFallbackModel model, IReadOnlyList<CanonicalStreamFallbackSegment> chain)
    {
        if (chain.Count < 2 || chain.Count > 6) throw new ArgumentException("A private Stream prefix requires one to five hidden owners.", nameof(chain));
        var cursor = chain[0].Id;
        var children = chain.Skip(1).ToArray();
        var runner = Name(model, "PendingTaskRunner");
        var start = Name(model, "PendingTaskStart");
        var groupStart = Name(model, "GroupPendingTaskStart");
        var depthField = Name(model, "_groupDepth");
        var settledField = Name(model, "_groupSettled");
        var hiddenPhase = Name(model, "HiddenSettlement");
        var rootPhase = Name(model, "RootCleanup");
        var settlingPhase = Name(model, "Settling");
        string Parameter(CanonicalStreamFallbackSegment s) => Name(model, "p" + s.Id);
        string Field(CanonicalStreamFallbackSegment s) => Name(model, "_groupP" + s.Id);
        var parameters = string.Join(", ", children.Select(s => "Cursor" + s.Id + "? " + Parameter(s)));
        var arguments = string.Join(", ", children.Select(Parameter));
        var storedArguments = string.Join(", ", children.Select(Field));
        var nullArguments = string.Join(", ", children.Select(_ => "null"));
        var fields = string.Join("\n", children.Select(s => "private Cursor" + s.Id + "? " + Field(s) + ";"));
        var startFields = string.Join("\n", children.Select(s => "private readonly Cursor" + s.Id + "? " + Field(s) + ";"));
        var assignments = string.Join("\n", children.Select(s => Field(s) + " = " + Parameter(s) + ";"));
        var clear = string.Join("\n", children.Select(s => Field(s) + " = null;"));
        var settleCases = string.Join("\n", children.Select((s, depth) => "case " + depth + ": return " + Field(s) + "!.CompleteGroupResult(moved, error);"));
        var releaseCases = string.Join("\n", children.Select((s, depth) => "case " + depth + ": { var child = " + Field(s) + "; " + Field(s) + " = null; child!.ReleaseGroupMove(); return; }"));
        return $$"""
                        private {{runner}}? _pendingTaskRunner;
                        private global::System.Threading.Tasks.ValueTask<bool> AwaitMoveAsync(global::System.Threading.Tasks.ValueTask<bool> pending)
                        {
                            var start = new {{start}}(this, pending);
                            var boundary = global::System.Runtime.CompilerServices.AsyncValueTaskMethodBuilder<bool>.Create();
                            boundary.Start(ref start);
                            return start.Result;
                        }
                        private global::System.Threading.Tasks.ValueTask<bool> AwaitGroupMoveAsync(
                            global::System.Threading.Tasks.ValueTask<bool> pending, int depth, bool settled, {{parameters}})
                        {
                            var start = new {{groupStart}}(this, pending, depth, settled, {{arguments}});
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
                        private struct {{groupStart}} : global::System.Runtime.CompilerServices.IAsyncStateMachine
                        {
                            private readonly Cursor{{cursor}} _owner;
                            private readonly global::System.Threading.Tasks.ValueTask<bool> _pending;
                            private readonly int {{depthField}};
                            private readonly bool {{settledField}};
                            {{startFields}}
                            internal global::System.Threading.Tasks.ValueTask<bool> Result;
                            internal {{groupStart}}(Cursor{{cursor}} owner, global::System.Threading.Tasks.ValueTask<bool> pending,
                                int depth, bool settled, {{parameters}})
                            {
                                _owner = owner; _pending = pending; {{depthField}} = depth; {{settledField}} = settled; Result = default;
                                {{assignments}}
                            }
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
                                    if ({{depthField}} < 0 && {{settledField}}) return failure is null ? new(moved) : Fault(failure);
                                    return _owner.CompleteGroupMove({{depthField}}, moved, failure, {{settledField}}, {{storedArguments}});
                                }
                                var runner = _owner._pendingTaskRunner ??= new {{runner}}();
                                return new(runner.BeginGroup(_owner, awaiter, {{depthField}}, {{settledField}}, {{storedArguments}}));
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
                            private const int Idle = 0, Raw = 1, {{hiddenPhase}} = 2, {{rootPhase}} = 3, {{settlingPhase}} = 4;
                            private Cursor{{cursor}}? _owner;
                            {{fields}}
                            private readonly global::System.Action _resume;
                            private global::System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable<bool>.ConfiguredValueTaskAwaiter _awaiter;
                            private global::System.Runtime.CompilerServices.AsyncTaskMethodBuilder<bool> _promise;
                            private global::System.Threading.ExecutionContext? _context;
                            private int _phase;
                            private int {{depthField}};
                            internal {{runner}}() { _resume = Resume; }
                            internal global::System.Threading.Tasks.Task<bool> Begin(Cursor{{cursor}} owner,
                                global::System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable<bool>.ConfiguredValueTaskAwaiter awaiter) =>
                                BeginGroup(owner, awaiter, -1, false, {{nullArguments}});
                            internal global::System.Threading.Tasks.Task<bool> BeginGroup(Cursor{{cursor}} owner,
                                global::System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable<bool>.ConfiguredValueTaskAwaiter awaiter,
                                int depth, bool settled, {{parameters}})
                            {
                                if (_phase != Idle) throw new global::System.InvalidOperationException("The pending Stream runner is unavailable.");
                                _promise = global::System.Runtime.CompilerServices.AsyncTaskMethodBuilder<bool>.Create();
                                var task = _promise.Task;
                                _owner = owner;
                                {{assignments}}
                                {{depthField}} = depth;
                                _awaiter = awaiter;
                                _context = global::System.Threading.ExecutionContext.Capture();
                                _phase = settled ? depth < 0 ? {{rootPhase}} : {{hiddenPhase}} : Raw;
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
                                if (phase != Raw && phase != {{hiddenPhase}} && phase != {{rootPhase}})
                                    throw new global::System.InvalidOperationException("The Stream runner has no pending await.");
                                var awaiter = _awaiter;
                                _awaiter = default;
                                bool moved = false;
                                global::System.Exception? failure = null;
                                try { moved = awaiter.GetResult(); }
                                catch (global::System.Exception error) { failure = error; }
                                if (phase == {{rootPhase}})
                                {
                                    Finish(moved, failure, closing: true);
                                    return;
                                }
                                if (phase == {{hiddenPhase}}) ReleaseDepth();
                                SettlePrefix(moved, failure);
                            }
                            private void SettlePrefix(bool moved, global::System.Exception? error)
                            {
                                while ({{depthField}} >= 0)
                                {
                                    global::System.Threading.Tasks.ValueTask<bool> operation = default;
                                    global::System.Exception? failure = null;
                                    try { operation = CompleteDepth(moved, error); }
                                    catch (global::System.Exception caught) { failure = caught; }
                                    if (failure is null)
                                    {
                                        var awaiter = operation.ConfigureAwait(false).GetAwaiter();
                                        bool completed = false;
                                        try
                                        {
                                            completed = awaiter.IsCompleted;
                                            if (completed) moved = awaiter.GetResult();
                                        }
                                        catch (global::System.Exception caught) { failure = caught; }
                                        if (failure is null && !completed)
                                        {
                                            _awaiter = awaiter;
                                            _context = global::System.Threading.ExecutionContext.Capture();
                                            _phase = {{hiddenPhase}};
                                            Register(awaiter);
                                            return;
                                        }
                                    }
                                    error = failure;
                                    ReleaseDepth();
                                }
                                CompleteRoot(moved, error);
                            }
                            private global::System.Threading.Tasks.ValueTask<bool> CompleteDepth(bool moved, global::System.Exception? error)
                            {
                                switch ({{depthField}})
                                {
                                    {{settleCases}}
                                    default: throw new global::System.InvalidOperationException("The Stream runner has no acquired owner at this depth.");
                                }
                            }
                            private void ReleaseDepth()
                            {
                                var depth = {{depthField}};
                                {{depthField}} = depth - 1;
                                _awaiter = default;
                                _context = null;
                                _phase = {{settlingPhase}};
                                switch (depth)
                                {
                                    {{releaseCases}}
                                    default: throw new global::System.InvalidOperationException("The Stream runner has no acquired owner to release.");
                                }
                            }
                            private void CompleteRoot(bool moved, global::System.Exception? rawFailure)
                            {
                                global::System.Threading.Tasks.ValueTask<bool> operation = default;
                                global::System.Exception? failure = null;
                                var closing = rawFailure is not null || !moved;
                                try { operation = _owner!.CompleteGroupResult(moved, rawFailure); }
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
                                _phase = {{rootPhase}};
                                Register(awaiter);
                            }
                            private void Finish(bool result, global::System.Exception? error, bool closing)
                            {
                                var promise = _promise;
                                var owner = _owner!;
                                _awaiter = default;
                                _promise = default;
                                _context = null;
                                {{clear}}
                                _owner = null;
                                {{depthField}} = -1;
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
        while (model.Generic?.Parameters.Contains(name) == true) name = "_" + name;
        return name;
    }
}
