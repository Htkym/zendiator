using Microsoft.CodeAnalysis;

namespace Zendiator.SourceGenerator;

/// <summary>Handler and behavior definitions for requests with and without a response.</summary>
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
