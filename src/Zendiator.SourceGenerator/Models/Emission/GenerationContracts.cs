using Microsoft.CodeAnalysis;

namespace Zendiator.SourceGenerator;

internal sealed class GenerationContracts(
    RequestContracts requests,
    RequestContracts synchronous,
    INamedTypeSymbol notificationHandler)
{
    public RequestContracts Requests { get; } = requests;
    public RequestContracts Synchronous { get; } = synchronous;
    public INamedTypeSymbol NotificationHandler { get; } = notificationHandler;
}
