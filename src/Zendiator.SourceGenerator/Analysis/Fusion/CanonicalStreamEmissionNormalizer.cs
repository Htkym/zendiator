using System;
using System.Collections.Generic;
using System.Linq;

namespace Zendiator.SourceGenerator;

internal enum CanonicalStreamConnectionKind
{
    CanonicalCandidate, CanonicalPending, InvalidContract, ConflictingComposition
}
internal sealed record CanonicalStreamConnection(string RouteKey, string CompositionIdentity, string Anchor,
    CanonicalStreamConnectionKind Kind, string Reason)
{
    public CanonicalStreamFallbackModel? Canonical
    {
        get; init;
    }
}
internal sealed record CanonicalStreamConnectionCatalog(EquatableArray<CanonicalStreamConnection> Routes);

internal static class CanonicalStreamEmissionNormalizer
{
    internal static CanonicalStreamConnectionCatalog Connect(CompositionSelectionCatalog catalog, IEnumerable<CanonicalRouteBackend> rows)
    {
        var connections = new List<CanonicalStreamConnection>();
        foreach (var row in rows.Where(r => r.Route.Input.Route.Family == BoundDispatchFamily.Stream))
        {
            var input = row.Route.Input;
            var c = input.Composition;
            var route = input.Route;
            CanonicalStreamConnectionKind kind;
            string reason;
            CanonicalStreamFallbackModel? canonical = null;
            if (catalog.Compositions.Count(other => other.Identity == c.Identity || other.Key == c.Key ||
                c.GeneratedNamespace != null && other.GeneratedNamespace == c.GeneratedNamespace) != 1)
            {
                kind = CanonicalStreamConnectionKind.ConflictingComposition;
                reason = "Stream generation requires one authoritative composition call site and a unique marker/key/namespace.";
            }
            else if (row.Route.Graph?.IsValid != true || c.Errors.Count != 0 || c.CompilerOwnsHandlerError ||
                c.Facts.Any(f => f.Key != "IncompleteExecutionContract") ||
                route.Handlers.Any(h => h.CompilerOwnsError) || route.Stages.Any(s => s.CompilerOwnsError))
            {
                kind = CanonicalStreamConnectionKind.InvalidContract;
                reason = "Stream generation is blocked by an invalid or compiler-owned contract.";
            }
            else if (row.StreamCanonical is { } selected && selected.Handler.CursorName.Length != 0)
            {
                canonical = selected;
                kind = CanonicalStreamConnectionKind.CanonicalCandidate;
                reason = "The bound typed Stream source, item regions and opaque middleware boundaries use the native pull backend.";
            }
            else
            {
                kind = CanonicalStreamConnectionKind.CanonicalPending;
                reason = "The Stream contract has no complete native source projection; registration remains blocked.";
            }
            connections.Add(new(input.Key, c.Identity, route.Anchor, kind, reason)
            {
                Canonical = canonical
            });
        }
        var collisions = new HashSet<string>(connections.Where(c => c.Canonical != null).GroupBy(c => c.Canonical!.HintName)
            .Where(g => g.Count() != 1).Select(g => g.Key), StringComparer.Ordinal);
        return new(new(connections.Select(c => c.Canonical is { } model && collisions.Contains(model.HintName)
            ? c with
            {
                Kind = CanonicalStreamConnectionKind.ConflictingComposition,
                Canonical = null,
                Reason = "Stream output identity is ambiguous; public emission remains blocked."
            } : c)));
    }

    internal static IEnumerable<CanonicalSourceFile> Files(IEnumerable<CanonicalStreamFallbackModel> canonical,
        IEnumerable<CanonicalRouteText> rows)
    {
        var rendered = rows.Select(r => r.StreamCanonicalFile).Where(f => f != null).Select(f => f!).ToArray();
        foreach (var model in canonical)
        {
            var file = rendered.Single(f => f.HintName == model.HintName);
            if (file.Source.Length == 0)
                throw new InvalidOperationException("Selected Stream source has no matching rendered route file.");
            yield return file;
        }
    }
}
