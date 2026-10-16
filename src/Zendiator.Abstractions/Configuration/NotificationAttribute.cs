namespace Zendiator;

/// <summary>Declares a known notification type that has no subscriber in the scanned assemblies.</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class NotificationAttribute(Type notificationType) : Attribute
{
    public Type NotificationType { get; } = notificationType;
}
