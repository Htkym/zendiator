namespace Zendiator.SourceGenerator;

internal sealed record GenerationRoutes(
    RequestRoutes Requests,
    RequestRoutes Synchronous,
    EquatableArray<EmissionNotificationRoute> Notifications);
