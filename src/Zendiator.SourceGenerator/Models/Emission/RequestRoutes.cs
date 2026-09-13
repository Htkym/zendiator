namespace Zendiator.SourceGenerator;

/// <summary>Single- and multi-handler routes for one dispatch family.</summary>
internal sealed record RequestRoutes(EquatableArray<EmissionRoute> Single, EquatableArray<EmissionMultiRoute> Multiple);