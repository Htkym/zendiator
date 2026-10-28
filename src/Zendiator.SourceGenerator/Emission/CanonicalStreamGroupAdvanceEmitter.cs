using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Zendiator.SourceGenerator;

internal static class CanonicalStreamGroupAdvanceEmitter
{
    internal static IReadOnlyList<CanonicalStreamFallbackSegment> Chain(CanonicalStreamFallbackModel model)
    {
        if (model.Segments.Count <= 2 || model.Segments.Count > 6) return Array.Empty<CanonicalStreamFallbackSegment>();
        var current = model.Segments.Single(s => s.Id == model.ConsumerSegment);
        var item = current.OutputItemName ?? model.Handler.ItemName;
        var chain = new List<CanonicalStreamFallbackSegment> { current };
        while (current.Wrapper != null && current.Upstream is { } upstream && current.Hooks.Count == 0
            && (current.SourceItemName ?? model.Handler.ItemName) == item
            && (current.OutputItemName ?? model.Handler.ItemName) == item)
        {
            var child = model.Segments.Single(s => s.Id == upstream);
            if (child.Hooks.Count != 0 || (child.OutputItemName ?? model.Handler.ItemName) != item
                || (child.SourceItemName ?? model.Handler.ItemName) != item) break;
            chain.Add(child);
            current = child;
        }
        return chain.Count > 1 ? chain : Array.Empty<CanonicalStreamFallbackSegment>();
    }

    internal static bool IsParent(CanonicalStreamFallbackModel model, int cursor) =>
        cursor == model.ConsumerSegment && Chain(model).Count > 1;

    internal static bool HasIdentitySource(CanonicalStreamFallbackModel model, int cursor)
    {
        var chain = Chain(model);
        return chain.Take(chain.Count - 1).Any(s => s.Id == cursor);
    }

    internal static string Emit(CanonicalStreamFallbackModel model, int cursor)
    {
        var chain = Chain(model);
        var index = -1;
        for (var i = 0; i < chain.Count; i++) if (chain[i].Id == cursor) index = i;
        if (index < 0) return "";
        var b = new StringBuilder();
        if (index + 1 < chain.Count)
        {
            var child = chain[index + 1].Id;
            b.AppendLine("private Cursor" + child + "? _identitySource;");
            if (index != 0) b.AppendLine("internal Cursor" + child + "? GroupIdentitySource => _identitySource;");
        }
        b.AppendLine("internal global::System.Threading.Tasks.ValueTask<bool> CompleteGroupResult(bool moved, global::System.Exception? error) => error is not null ? MoveFailed(error) : moved ? MoveSucceeded(true) : EndAsync();");
        if (index == 0) EmitConsumer(b, chain);
        else b.AppendLine("""
            internal bool TryBeginGroupMove() => TryBeginMove();
            internal void CheckGroupRoot()
            {
                if (_root.IsClosed) throw new global::System.ObjectDisposedException("Stream");
            }
            internal void CheckGroupToken() => Token.ThrowIfCancellationRequested();
            internal global::System.Threading.Tasks.ValueTask<bool> MoveGroupRaw() => MoveCoreAsync();
            internal void ReleaseGroupMove() => ReleasePrivateMoveClaim();
            """);
        return b.ToString();
    }

    private static void EmitConsumer(StringBuilder b, IReadOnlyList<CanonicalStreamFallbackSegment> chain)
    {
        var children = chain.Skip(1).ToArray();
        var parameters = string.Join(", ", children.Select(s => "Cursor" + s.Id + "? p" + s.Id));
        var arguments = string.Join(", ", children.Select(s => "p" + s.Id));
        b.AppendLine("private global::System.Threading.Tasks.ValueTask<bool> AdvanceGroup(Cursor" + children[0].Id + " child)");
        b.AppendLine("{");
        b.AppendLine("Token.ThrowIfCancellationRequested();");
        foreach (var child in children) b.AppendLine("Cursor" + child.Id + "? p" + child.Id + " = null;");
        b.AppendLine("int depth = -1;");
        b.AppendLine("global::System.Threading.Tasks.ValueTask<bool> pending = default;");
        b.AppendLine("bool completed = true, moved = false;");
        b.AppendLine("global::System.Exception? failure = null;");
        b.AppendLine("try {");
        for (var i = 0; i < children.Length; i++)
        {
            var id = children[i].Id;
            var target = i == 0 ? "child" : "next" + id;
            b.AppendLine("if (!" + target + ".TryBeginGroupMove()) goto Settling;");
            b.AppendLine("p" + id + " = " + target + "; depth = " + i + ";");
            b.AppendLine(target + ".CheckGroupRoot();");
            if (i + 1 < children.Length)
            {
                var nextId = children[i + 1].Id;
                b.AppendLine("var next" + nextId + " = " + target + ".GroupIdentitySource;");
                b.AppendLine("if (next" + nextId + " is null) { pending = " + target + ".MoveGroupRaw(); goto Observing; }");
                b.AppendLine(target + ".CheckGroupToken();");
            }
            else b.AppendLine("pending = " + target + ".MoveGroupRaw();");
        }
        if (children.Length > 1) b.AppendLine("Observing:");
        b.AppendLine("completed = pending.IsCompletedSuccessfully;");
        b.AppendLine("if (completed) moved = pending.GetAwaiter().GetResult();");
        b.AppendLine("} catch (global::System.Exception error) { failure = error; }");
        b.AppendLine("Settling:");
        b.AppendLine("if (failure is null && !completed) return AwaitGroupMoveAsync(pending, depth, false, " + arguments + ");");
        b.AppendLine("return CompleteGroupMove(depth, moved, failure, false, " + arguments + ");");
        b.AppendLine("}");
        b.AppendLine("private global::System.Threading.Tasks.ValueTask<bool> CompleteGroupMove(int depth, bool moved, global::System.Exception? failure, bool settled, " + parameters + ")");
        b.AppendLine("{");
        b.AppendLine("while (depth >= 0) {");
        b.AppendLine("if (!settled) {");
        b.AppendLine("global::System.Threading.Tasks.ValueTask<bool> operation = default;");
        b.AppendLine("bool completed = false;");
        b.AppendLine("try {");
        b.AppendLine("switch (depth) {");
        for (var i = 0; i < children.Length; i++)
            b.AppendLine("case " + i + ": operation = p" + children[i].Id + "!.CompleteGroupResult(moved, failure); break;");
        b.AppendLine("default: throw new global::System.InvalidOperationException(\"The Stream group depth is invalid.\"); }");
        b.AppendLine("failure = null;");
        b.AppendLine("completed = operation.IsCompleted;");
        b.AppendLine("if (completed) moved = operation.GetAwaiter().GetResult();");
        b.AppendLine("} catch (global::System.Exception error) { moved = false; failure = error; }");
        b.AppendLine("if (failure is null && !completed) return AwaitGroupMoveAsync(operation, depth, true, " + arguments + ");");
        b.AppendLine("}");
        b.AppendLine("switch (depth) {");
        for (var i = 0; i < children.Length; i++)
        {
            var id = children[i].Id;
            b.AppendLine("case " + i + ": { var released = p" + id + "!; p" + id + " = null; depth = " + (i - 1) + "; settled = false; released.ReleaseGroupMove(); break; }");
        }
        b.AppendLine("default: throw new global::System.InvalidOperationException(\"The Stream group depth is invalid.\"); }");
        b.AppendLine("}");
        b.AppendLine("return PublishMove(CompleteGroupResult(moved, failure));");
        b.AppendLine("}");
    }
}
