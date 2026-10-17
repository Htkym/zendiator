using System;
using System.Collections.Generic;
using System.Linq;

namespace Zendiator.SourceGenerator;

internal enum CanonicalStreamConnectionKind { Candidate, CanonicalCandidate, CanonicalPending, InvalidContract, ConflictingComposition }
internal sealed record CanonicalStreamConnection(string RouteKey, string CompositionIdentity, string Anchor,
    CanonicalStreamConnectionKind Kind, CanonicalStreamPrototypeModel? Candidate, string Reason)
{ public CanonicalStreamFallbackModel? Canonical { get; init; } }
internal sealed record CanonicalStreamConnectionCatalog(EquatableArray<CanonicalStreamConnection> Routes);

// Deferred canonical routes never fall back to the legacy adapter, even when diagnostics are suppressed.
internal static class CanonicalStreamEmissionNormalizer
{
    private const string ValidGraph = "The Stream execution graph satisfies its structural contract. ";
    internal static CanonicalStreamConnectionCatalog Connect(CompositionSelectionCatalog catalog, IEnumerable<CanonicalRouteBackend> rows)
    {
        var connections = new List<CanonicalStreamConnection>();
        foreach (var row in rows.Where(r => r.Route.Input.Route.Family == BoundDispatchFamily.Stream))
        {
            var input = row.Route.Input; var c = input.Composition; var route = input.Route;
            CanonicalStreamConnectionKind kind; string reason; CanonicalStreamPrototypeModel? candidate = null;
            CanonicalStreamFallbackModel? canonical = null;
            if (catalog.Compositions.Count(other => other.Identity == c.Identity || other.Key == c.Key ||
                c.GeneratedNamespace != null && other.GeneratedNamespace == c.GeneratedNamespace) != 1)
            {
                kind = CanonicalStreamConnectionKind.ConflictingComposition;
                reason = "Stream candidate generation requires one authoritative composition call site and a unique marker/key/namespace. Public emission remains blocked.";
            }
            else if (row.Route.Graph?.IsValid != true || c.Errors.Count != 0 || c.CompilerOwnsHandlerError ||
                c.Facts.Any(f => f.Key != "IncompleteExecutionContract") ||
                route.Handlers.Any(h => h.CompilerOwnsError) || route.Stages.Any(s => s.CompilerOwnsError))
            {
                kind = CanonicalStreamConnectionKind.InvalidContract;
                reason = "Stream candidate generation is blocked by an invalid or compiler-owned contract; existing source diagnostics remain authoritative.";
            }
            else if (row.StreamCandidate is { } selected)
            {
                kind = CanonicalStreamConnectionKind.Candidate; candidate = selected;
                reason = ValidGraph + "The selected " + (route.StreamGeneric == null ? "closed" : "open generic") + " Stream candidate is connected to route lowering and rendering. Public runtime generation and equivalence are pending; registration remains blocked.";
            }
            else if (row.StreamCanonical is { } fallback)
            {
                kind = CanonicalStreamConnectionKind.CanonicalCandidate; canonical = fallback;
                reason = ValidGraph + "A same-contract canonical Stream candidate is generated for this " + (route.StreamGeneric == null ? "closed" : "open generic") + " shape. Public runtime generation and equivalence are pending; registration remains blocked. No legacy/reference adapter is selected.";
            }
            else
            {
                kind = CanonicalStreamConnectionKind.CanonicalPending;
                reason = ValidGraph + "This shape is outside the closed class-receiver canonical backend or its supported schedule. Open binding, value-type receivers, sessions and alias support remain pending; emission remains blocked. No legacy/reference fallback is selected.";
            }
            connections.Add(new(input.Key, c.Identity, route.Anchor, kind, candidate, reason) { Canonical = canonical });
        }
        string? Hint(CanonicalStreamConnection c) => c.Candidate?.HintName ?? c.Canonical?.HintName;
        var collisions = new HashSet<string>(connections.Where(c => Hint(c) != null).GroupBy(c => Hint(c)!)
            .Where(g => g.Count() != 1).Select(g => g.Key), StringComparer.Ordinal);
        return new(new(connections.Select(c => Hint(c) is { } hint && collisions.Contains(hint)
            ? c with
            {
                Kind = CanonicalStreamConnectionKind.ConflictingComposition,
                Candidate = null,
                Canonical = null,
                Reason = "Stream candidate output identity is ambiguous; public emission remains blocked."
            } : c)));
    }

    internal static IEnumerable<CanonicalSourceFile> Files(IEnumerable<CanonicalStreamPrototypeModel> fused,
        IEnumerable<CanonicalStreamFallbackModel> canonical, IEnumerable<CanonicalRouteText> rows)
    {
        var rendered = rows.SelectMany(r => new[] { r.StreamCandidateFile, r.StreamCanonicalFile }).Where(f => f != null).Select(f => f!).ToArray();
        foreach (var hint in fused.Select(m => m.HintName).Concat(canonical.Select(m => m.HintName)))
        {
            var file = rendered.Single(f => f.HintName == hint);
            if (file.Source.Length == 0)
                throw new InvalidOperationException("Selected Stream candidate has no matching rendered route file.");
            yield return file;
        }
    }
}
