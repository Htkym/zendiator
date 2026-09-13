using Microsoft.CodeAnalysis;

namespace Zendiator.SourceGenerator;

internal sealed class Subscriber(INamedTypeSymbol handler, int order, string assemblyId, string fullName)
{
    public INamedTypeSymbol Handler { get; } = handler;
    public int Order { get; } = order;
    public string AssemblyId { get; } = assemblyId;
    public string FullName { get; } = fullName;
}
