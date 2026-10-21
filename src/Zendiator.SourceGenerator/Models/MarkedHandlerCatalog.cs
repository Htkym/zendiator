namespace Zendiator.SourceGenerator;

/// <summary>Early, symbol-free metadata. This is discovery input, not a validated execution plan.</summary>
internal sealed record MarkedHandlerContract(
    string Contract, string Request, string? Result, string Implementation, string DefinitionConstraints);
internal sealed record MarkedHandlerModel(
    string Identity, string Name, EquatableArray<string> Hierarchy,
    EquatableArray<MarkedHandlerContract> Contracts, bool CompilerOwnsMarkerError);
internal sealed record MarkedHandlerCatalog(
    EquatableArray<MarkedHandlerModel> Handlers);

internal sealed record SelectedHandlerModel(string Identity, MarkedHandlerModel Definition);
internal sealed record CompositionSelectionModel(string Identity, string Key,
    EquatableArray<SelectedHandlerModel> Handlers, EquatableArray<string> Errors, bool CompilerOwnsHandlerError,
    EquatableArray<BoundPipelineModel> Pipelines = default, EquatableArray<CompositionDiagnosticFact> Facts = default)
{
    public EquatableArray<string> ExternAliases { get; init; }
    public string? GeneratedNamespace { get; init; }
    public string? MarkerName { get; init; }
    public bool UseLocalHandlers { get; init; }
    public bool HasExplicitSelection { get; init; }
    public EquatableArray<string> ExplicitHandlerTypes { get; init; }
    public EquatableArray<string> CatalogMarkerTypes { get; init; }
    public string? MicrosoftDiProviderName { get; init; }
}
internal sealed record CompositionSelectionCatalog(EquatableArray<CompositionSelectionModel> Compositions);
