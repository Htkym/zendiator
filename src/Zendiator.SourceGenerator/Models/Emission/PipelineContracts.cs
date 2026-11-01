using Microsoft.CodeAnalysis;

namespace Zendiator.SourceGenerator;

internal sealed class PipelineContracts(INamedTypeSymbol handler, INamedTypeSymbol behavior)
{
    public INamedTypeSymbol Handler { get; } = handler;
    public INamedTypeSymbol Behavior { get; } = behavior;
}
