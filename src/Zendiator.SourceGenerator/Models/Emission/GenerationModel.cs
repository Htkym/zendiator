namespace Zendiator.SourceGenerator;

/// <summary>Immutable, symbol-free boundary between semantic analysis and cached emission.</summary>
internal sealed record GenerationModel(GenerationTarget Target, GenerationRoutes Routes);