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
    EquatableArray<SelectedHandlerModel> Handlers, EquatableArray<string> Errors, bool CompilerOwnsHandlerError);
internal sealed record CompositionSelectionCatalog(EquatableArray<CompositionSelectionModel> Compositions);
