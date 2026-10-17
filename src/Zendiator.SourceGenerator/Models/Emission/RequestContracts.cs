using Microsoft.CodeAnalysis;

namespace Zendiator.SourceGenerator;

internal sealed class RequestContracts(
    INamedTypeSymbol handler,
    INamedTypeSymbol behavior,
    INamedTypeSymbol voidHandler,
    INamedTypeSymbol voidBehavior)
{
    public INamedTypeSymbol Handler { get; } = handler;
    public INamedTypeSymbol Behavior { get; } = behavior;
    public INamedTypeSymbol VoidHandler { get; } = voidHandler;
    public INamedTypeSymbol VoidBehavior { get; } = voidBehavior;
}
