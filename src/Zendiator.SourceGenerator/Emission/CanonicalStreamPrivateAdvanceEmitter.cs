using System.Linq;

namespace Zendiator.SourceGenerator;

internal static class CanonicalStreamPrivateAdvanceEmitter
{
    internal static CanonicalStreamFallbackSegment? Inner(CanonicalStreamFallbackModel model)
    {
        if (model.Segments.Count != 2) return null;
        var inner = model.Segments.SingleOrDefault(s => s.Wrapper == null && s.Upstream == null && s.Hooks.Count == 0);
        var outer = model.Segments.Single(s => s.Id == model.ConsumerSegment);
        return inner != null && outer.Wrapper != null && outer.Upstream == inner.Id && outer.Hooks.Count == 0
            && (outer.SourceItemName ?? model.Handler.ItemName) == model.Handler.ItemName
            && (outer.OutputItemName ?? model.Handler.ItemName) == model.Handler.ItemName ? inner : null;
    }

    internal static bool IsParent(CanonicalStreamFallbackModel model, int cursor) =>
        cursor == model.ConsumerSegment && Inner(model) != null;

    internal static string Emit(CanonicalStreamFallbackModel model, int cursor)
    {
        var inner = Inner(model);
        if (inner == null) return "";
        if (cursor == inner.Id)
            return """
                        internal bool TryBeginPrivateMove() => TryBeginMove();
                        internal global::System.Threading.Tasks.ValueTask<bool> MovePrivateRaw()
                        {
                            if (_root.IsClosed) throw new global::System.ObjectDisposedException("Stream");
                            return MoveCoreAsync();
                        }
                        internal global::System.Threading.Tasks.ValueTask<bool> CompletePrivateResult(bool moved, global::System.Exception? error) =>
                            error is not null ? MoveFailed(error) : moved ? MoveSucceeded(true) : EndAsync();
                        internal void ReleasePrivateMove() => ReleasePrivateMoveClaim();
                """;
        if (!IsParent(model, cursor)) return "";
        var runner = "PendingTaskRunner";
        while (model.Generic?.Parameters.Contains(runner) == true) runner = "_" + runner;
        return $$"""
                        private Cursor{{inner.Id}}? _identitySource;
                        private global::System.Threading.Tasks.ValueTask<bool> AdvanceIdentity(Cursor{{inner.Id}} child)
                        {
                            Token.ThrowIfCancellationRequested();
                            if (!child.TryBeginPrivateMove()) return EndAsync();
                            global::System.Threading.Tasks.ValueTask<bool> pending = default;
                            bool completed = false, moved = false;
                            global::System.Exception? failure = null;
                            try
                            {
                                pending = child.MovePrivateRaw();
                                completed = pending.IsCompletedSuccessfully;
                                if (completed) moved = pending.GetAwaiter().GetResult();
                            }
                            catch (global::System.Exception error) { failure = error; }
                            if (failure is null && !completed) return AwaitMoveAsync(pending, child);
                            return CompleteIdentityMove(child, moved, failure);
                        }
                        private global::System.Threading.Tasks.ValueTask<bool> CompleteIdentityMove(
                            Cursor{{inner.Id}} child, bool moved, global::System.Exception? rawFailure)
                        {
                            global::System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable<bool>.ConfiguredValueTaskAwaiter awaiter = default;
                            bool completed = false;
                            global::System.Exception? failure = null;
                            try
                            {
                                var operation = child.CompletePrivateResult(moved, rawFailure);
                                awaiter = operation.ConfigureAwait(false).GetAwaiter();
                                completed = awaiter.IsCompleted;
                                if (completed) moved = awaiter.GetResult();
                            }
                            catch (global::System.Exception error) { failure = error; }
                            if (failure is null && !completed)
                            {
                                var runner = _pendingTaskRunner ??= new {{runner}}();
                                return new(runner.BeginChildCleanup(this, awaiter, child));
                            }
                            child.ReleasePrivateMove();
                            return CompleteIdentityParent(moved, failure);
                        }
                        private global::System.Threading.Tasks.ValueTask<bool> CompleteIdentityParent(bool moved, global::System.Exception? error) =>
                            error is not null ? MoveFailed(error) : moved ? MoveSucceeded(true) : EndAsync();
                """;
    }
}
