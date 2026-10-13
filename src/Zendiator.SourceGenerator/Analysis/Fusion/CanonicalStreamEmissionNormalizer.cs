using System;
using System.Collections.Generic;
using System.Linq;

namespace Zendiator.SourceGenerator;

internal enum CanonicalStreamConnectionKind { Candidate, CanonicalPending, InvalidContract, ConflictingComposition }
internal sealed record CanonicalStreamConnection(string RouteKey, string CompositionIdentity, string Anchor,
    CanonicalStreamConnectionKind Kind, CanonicalStreamPrototypeModel? Candidate, string Reason);
internal sealed record CanonicalStreamConnectionCatalog(EquatableArray<CanonicalStreamConnection> Routes);

// C05 connects the reviewed candidate to route lowering/rendering. Deferred canonical
// generation is explicit; neither diagnostics nor their suppression select a legacy adapter.
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
                reason = ValidGraph + "The selected closed Stream candidate is connected to route lowering and rendering. Public runtime generation and equivalence are pending; registration remains blocked.";
            }
            else
            {
                kind = CanonicalStreamConnectionKind.CanonicalPending;
                reason = ValidGraph + "This shape is outside the selected closed class/int/Filter->Map->Filter candidate or its supported schedule. Same-contract canonical runtime generation and equivalence are pending; emission remains blocked. No legacy/reference fallback is selected.";
            }
            connections.Add(new(input.Key, c.Identity, route.Anchor, kind, candidate, reason));
        }
        var collisions = new HashSet<string>(connections.Where(c => c.Candidate != null).GroupBy(c => c.Candidate!.HintName)
            .Where(g => g.Count() != 1).Select(g => g.Key), StringComparer.Ordinal);
        return new(new(connections.Select(c => c.Candidate != null && collisions.Contains(c.Candidate.HintName)
            ? c with { Kind = CanonicalStreamConnectionKind.ConflictingComposition, Candidate = null,
                Reason = "Stream candidate output identity is ambiguous; public emission remains blocked." } : c)));
    }

    internal static IEnumerable<CanonicalSourceFile> Files(IEnumerable<CanonicalStreamPrototypeModel> models, IEnumerable<CanonicalRouteText> rows)
    {
        var rendered = rows.ToArray();
        foreach (var model in models)
        {
            var file = rendered.Single(r => r.StreamCandidateFile?.HintName == model.HintName).StreamCandidateFile;
            if (file == null || file.Source.Length == 0)
                throw new InvalidOperationException("Selected Stream candidate has no matching rendered route file.");
            yield return file;
        }
    }
}
