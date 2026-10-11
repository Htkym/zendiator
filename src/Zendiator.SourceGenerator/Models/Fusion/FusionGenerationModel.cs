namespace Zendiator.SourceGenerator;

/// <summary>
/// The production value-provider boundary. Existing routes retain their typed emitter;
/// missing canonical lowering is represented as an opaque boundary, never fabricated sites.
/// </summary>
internal sealed record FusionGenerationModel(
    GenerationModel Emission, EquatableArray<BehaviorFusionPlan> Routes);
