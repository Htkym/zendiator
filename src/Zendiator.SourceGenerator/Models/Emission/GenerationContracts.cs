using Microsoft.CodeAnalysis;

namespace Zendiator.SourceGenerator;

internal sealed class GenerationContracts(
    RequestContracts requests,
    RequestContracts synchronous,
    PipelineContracts streams,
    INamedTypeSymbol notificationHandler,
    INamedTypeSymbol streamValidator)
{
    public RequestContracts Requests { get; } = requests;
    public RequestContracts Synchronous { get; } = synchronous;
    public PipelineContracts Streams { get; } = streams;
    public INamedTypeSymbol NotificationHandler { get; } = notificationHandler;
    public INamedTypeSymbol StreamValidator { get; } = streamValidator;
}
