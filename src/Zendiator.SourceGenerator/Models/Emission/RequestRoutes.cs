namespace Zendiator.SourceGenerator;

internal sealed record RequestRoutes(EquatableArray<EmissionRoute> Single, EquatableArray<EmissionMultiRoute> Multiple);