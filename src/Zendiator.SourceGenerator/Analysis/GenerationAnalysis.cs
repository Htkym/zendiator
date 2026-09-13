using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Zendiator.SourceGenerator.GeneratorDiagnostics;

namespace Zendiator.SourceGenerator;

/// <summary>Owns the semantic state of one generation run; each stage adds routes or diagnostics.</summary>
internal sealed partial class GenerationAnalysis(ImmutableArray<INamedTypeSymbol> declarations, ImmutableArray<InvocationExpressionSyntax> diInvocations, Compilation compilation, CancellationToken ct)
{
    private readonly List<Diagnostic> errors = new List<Diagnostic>();

    private void Error(int rule, string message, ISymbol? symbol = null) => errors.Add(Diagnostic.Create(
        Rules[rule - 1], symbol?.Locations.FirstOrDefault(static l => l.IsInSource) ??
        declarations.FirstOrDefault()?.Locations.FirstOrDefault(static l => l.IsInSource) ?? Location.None, message));

    private void ErrorAt(int rule, string message, Location location) =>
        errors.Add(Diagnostic.Create(Rules[rule - 1], location, message));

    internal GenerationResult Generate()
    {
        var configurationError = Configure();
        if (configurationError != null) return configurationError;
        var discoveryError = DiscoverTypes();
        if (discoveryError != null) return discoveryError;
        BuildRequests();
        BuildStreams();
        BuildMultiRequests();
        BuildSyncRequests();
        BuildNotifications();
        ApplyPipelines();
        return EmitResult();
    }
}
