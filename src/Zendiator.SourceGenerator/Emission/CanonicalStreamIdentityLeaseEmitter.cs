using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Zendiator.SourceGenerator;

internal static class CanonicalStreamIdentityLeaseEmitter
{
    internal static IReadOnlyList<CanonicalStreamFallbackSegment> Chain(CanonicalStreamFallbackModel model)
    {
        var current = model.Segments.Single(s => s.Id == model.ConsumerSegment);
        var item = current.OutputItemName ?? model.Handler.ItemName;
        var chain = new List<CanonicalStreamFallbackSegment> { current };
        var visited = new HashSet<int> { current.Id };
        while (current.Wrapper != null && current.Upstream is { } upstream && current.Hooks.Count == 0
            && (current.SourceItemName ?? model.Handler.ItemName) == item
            && (current.OutputItemName ?? model.Handler.ItemName) == item)
        {
            var child = model.Segments.Single(s => s.Id == upstream);
            if (child.Hooks.Count != 0 || (child.OutputItemName ?? model.Handler.ItemName) != item
                || (child.SourceItemName ?? model.Handler.ItemName) != item) break;
            if (!visited.Add(child.Id)) throw new InvalidOperationException("A Stream identity prefix is cyclic.");
            chain.Add(child);
            current = child;
        }
        return chain.Count > 1 ? chain : Array.Empty<CanonicalStreamFallbackSegment>();
    }

    internal static bool IsParent(CanonicalStreamFallbackModel model, int cursor) =>
        cursor == model.ConsumerSegment && Chain(model).Count > 1;

    internal static bool HasEdge(CanonicalStreamFallbackModel model, int cursor)
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
        if (index + 1 < chain.Count) EmitEdge(b, model, chain[index + 1].Id, cursor);
        if (index > 0)
        {
            var item = chain[index].OutputItemName ?? model.Handler.ItemName;
            b.AppendLine($$"""
                internal bool TryClaimIdentityLease() => TryBeginMove();
                internal global::System.Threading.Tasks.ValueTask<bool> MoveIdentityLeaseEndpointRaw() => MoveCoreAsync();
                internal {{item}} ReadIdentityLeaseEndpointCurrent() => ReadSource(ref _source);
                internal async global::System.Threading.Tasks.ValueTask<global::System.Exception?> CloseIdentityLeaseChildAsync(
                    global::System.Exception? error, bool disposing)
                {
                    var failure = await CloseClaimedAsync(error, disposing).ConfigureAwait(false);
                    if (!disposing && error is null && failure is null) _normalEnd = true;
                    return failure;
                }
                internal void RetireIdentityLeaseChildClose() => RetireClaimedClose();
                """);
        }
        else EmitRoot(b, model, chain);
        return b.ToString();
    }

    private static void EmitEdge(StringBuilder b, CanonicalStreamFallbackModel model, int child, int cursor)
    {
        b.AppendLine($$"""
            internal Cursor{{child}}? LeaseIdentitySource
            {
                get
                {
                    var child = _upstream?.MatchIdentity(_source);
                    return child is not null && child.CancellationToken == Token ? child : null;
                }
            }
            internal void DetachIdentityLeaseSource(Cursor{{child}} child)
            {
                if (global::System.Object.ReferenceEquals(_source, child))
                {
                    _source = default!;
                    _acquired = false;
                }
                _upstream?.DetachIdentity(child);
            """);
        if (CanonicalStreamPrivateAdvanceEmitter.IsParent(model, cursor)
            || CanonicalStreamGroupAdvanceEmitter.HasIdentitySource(model, cursor))
            b.AppendLine("if (global::System.Object.ReferenceEquals(_identitySource, child)) _identitySource = null;");
        b.AppendLine("}");
    }

    private static void EmitRoot(StringBuilder b, CanonicalStreamFallbackModel model,
        IReadOnlyList<CanonicalStreamFallbackSegment> chain)
    {
        var children = chain.Skip(1).ToArray();
        var descriptor = Name(model, "IdentityLease");
        var item = chain[0].OutputItemName ?? model.Handler.ItemName;
        string Field(CanonicalStreamFallbackSegment child) => "_child" + child.Id;
        string Parameter(CanonicalStreamFallbackSegment child) => "child" + child.Id;
        var endpoint = children[children.Length - 1];
        var parameters = string.Join(", ", children.Select(s => "Cursor" + s.Id + " " + Parameter(s)));
        var arguments = string.Join(", ", children.Select(Parameter));
        var scan = new StringBuilder();
        for (var i = 0; i < children.Length; i++)
        {
            var parent = i == 0 ? "" : Parameter(children[i - 1]) + ".";
            var child = Parameter(children[i]);
            scan.AppendLine("var " + child + " = " + parent + "LeaseIdentitySource;");
            scan.AppendLine("if (" + child + " is null) return;");
        }
        var group = CanonicalStreamGroupAdvanceEmitter.IsParent(model, model.ConsumerSegment);
        var nulls = group ? string.Join(", ", CanonicalStreamGroupAdvanceEmitter.Chain(model).Skip(1).Select(_ => "null")) : "";
        var pending = group
            ? "if (!pending.IsCompletedSuccessfully) return AwaitGroupMoveAsync(pending, -1, false, " + nulls + ");\nreturn PublishMove(CompleteGroupResult(pending.GetAwaiter().GetResult(), null));"
            : "return PublishMove(ObserveMove(pending));";
        b.AppendLine($$"""
            private {{descriptor}}? _identityLease;
            private void AttachIdentityLease()
            {
                {{scan}}
                var lease = new {{descriptor}}();
                _identityLease = lease;
                lease.Attach(this, {{arguments}});
            }
            private global::System.Threading.Tasks.ValueTask<bool> MoveIdentityLeaseRaw() => _identityLease!.Move();
            private {{item}} ReadIdentityLeaseCurrent() => _identityLease!.ReadCurrent();
            private global::System.Threading.Tasks.ValueTask<bool> AdvanceIdentityLease()
            {
                Token.ThrowIfCancellationRequested();
                var pending = MoveIdentityLeaseRaw();
                {{pending}}
            }
            private void PrepareIdentityLeaseTermination(bool moved, global::System.Exception? error)
            {
                if ((!moved || error is not null) && _identityLease is { } lease) lease.Prepare(error);
            }
            private new global::System.Threading.Tasks.ValueTask<bool> MoveFailed(global::System.Exception error)
            {
                PrepareIdentityLeaseTermination(false, error);
                return base.MoveFailed(error);
            }
            private global::System.Threading.Tasks.ValueTask<global::System.Exception?> DisposeIdentityLeaseAsync()
            {
                var lease = _identityLease;
                _identityLease = null;
                if (lease is null) return new((global::System.Exception?)null);
                lease.Detach(this);
                return lease.DisposeAsync();
            }
            private sealed class {{descriptor}}
            {
                private bool _exhausted;
                private bool _disposing = true;
                private global::System.Exception? _error;
            """);
        foreach (var child in children) b.AppendLine("private Cursor" + child.Id + "? " + Field(child) + ";");
        b.AppendLine("internal void Attach(Cursor" + chain[0].Id + " owner, " + parameters + ")");
        b.AppendLine("{");
        for (var i = 0; i < children.Length; i++)
        {
            var child = children[i];
            var value = Parameter(child);
            var parent = i == 0 ? "owner" : Parameter(children[i - 1]);
            b.AppendLine("if (!" + value + ".TryClaimIdentityLease()) { _exhausted = true; return; }");
            b.AppendLine(Field(child) + " = " + value + ";");
            b.AppendLine("if (!global::System.Object.ReferenceEquals(" + parent + ".LeaseIdentitySource, " + value + "))");
            b.AppendLine("throw new global::System.InvalidOperationException(\"The Stream identity edge changed during attachment.\");");
        }
        b.AppendLine("}");
        b.AppendLine("internal global::System.Threading.Tasks.ValueTask<bool> Move()");
        b.AppendLine("{");
        b.AppendLine("if (_exhausted) return new(false);");
        b.AppendLine("return " + Field(endpoint) + "!.MoveIdentityLeaseEndpointRaw();");
        b.AppendLine("}");
        b.AppendLine("internal " + item + " ReadCurrent()");
        b.AppendLine("{");
        b.AppendLine("return " + Field(endpoint) + "!.ReadIdentityLeaseEndpointCurrent();");
        b.AppendLine("}");
        b.AppendLine($$"""
                internal void Prepare(global::System.Exception? error) { _disposing = false; _error = error; }
                internal void Detach(Cursor{{chain[0].Id}} owner)
                {
            """);
        for (var i = 0; i < children.Length; i++)
        {
            var child = children[i];
            var parent = i == 0 ? "owner" : Field(children[i - 1]) + "!";
            b.AppendLine("if (" + Field(child) + " is { } child" + child.Id + ") " + parent + ".DetachIdentityLeaseSource(child" + child.Id + ");");
        }
        b.AppendLine("}");
        b.AppendLine("internal async global::System.Threading.Tasks.ValueTask<global::System.Exception?> DisposeAsync()");
        b.AppendLine("{");
        b.AppendLine("var error = _error; _error = null; var disposing = _disposing;");
        foreach (var child in Enumerable.Reverse(children))
        {
            var local = "child" + child.Id;
            b.AppendLine("var " + local + " = " + Field(child) + ";");
            b.AppendLine(Field(child) + " = null;");
            b.AppendLine("if (" + local + " is not null) {");
            b.AppendLine("try { error = await " + local + ".CloseIdentityLeaseChildAsync(error, disposing).ConfigureAwait(false); }");
            b.AppendLine("catch (global::System.Exception failure) { error = failure; }");
            b.AppendLine("var retiring" + child.Id + " = " + local + "; " + local + " = null;");
            b.AppendLine("retiring" + child.Id + ".RetireIdentityLeaseChildClose();");
            b.AppendLine("}");
        }
        b.AppendLine("return error;");
        b.AppendLine("}");
        b.AppendLine("}");
    }

    private static string Name(CanonicalStreamFallbackModel model, string name)
    {
        while (model.Generic?.Parameters.Contains(name) == true) name = "_" + name;
        return name;
    }
}
