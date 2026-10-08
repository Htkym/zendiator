using Microsoft.CodeAnalysis;

namespace Zendiator.SourceGenerator;

/// <summary>Resolved interface definitions, grouped by dispatch family.</summary>
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
