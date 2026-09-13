namespace Zendiator.SourceGenerator;

internal sealed record EmissionNotificationRoute
{
    public EmissionType Notification { get; init; } = null!;
    public bool IsOpen { get; init; }
    public EquatableArray<EmissionSubscriber> Subscribers { get; init; }
    public EquatableArray<string> OpenTypeParams { get; init; }
    public EquatableArray<string> MethodConstraints { get; init; }
    public string NotificationDisplay { get; init; } = "";
    public EquatableArray<string> HandlerDisplays { get; init; }
    public EquatableArray<string> HandlerContractDisplays { get; init; }
}
