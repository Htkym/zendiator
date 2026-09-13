namespace Zendiator.SourceGenerator;

/// <summary>Route collections captured after pipeline analysis has finished.</summary>
internal sealed record GenerationRoutes(
    RequestRoutes Requests,
    RequestRoutes Synchronous,
    EquatableArray<EmissionNotificationRoute> Notifications,
    EquatableArray<EmissionRoute> Streams);