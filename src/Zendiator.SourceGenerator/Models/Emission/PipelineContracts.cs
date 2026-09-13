using Microsoft.CodeAnalysis;

namespace Zendiator.SourceGenerator;

/// <summary>A handler/behavior contract pair for a pipeline.</summary>
internal sealed class PipelineContracts(INamedTypeSymbol handler, INamedTypeSymbol behavior)
{
    public INamedTypeSymbol Handler { get; } = handler;
    public INamedTypeSymbol Behavior { get; } = behavior;
}
