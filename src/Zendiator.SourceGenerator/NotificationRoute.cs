using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Zendiator.SourceGenerator;

internal sealed class NotificationRoute(INamedTypeSymbol notification, bool isOpen)
{
    public INamedTypeSymbol Notification { get; } = notification;
    public bool IsOpen { get; } = isOpen;
    public List<Subscriber> Subscribers { get; } = new();
    public List<string> OpenTypeParams { get; } = new();
    public List<string> MethodConstraints { get; } = new();
    public string NotificationDisplay { get; set; } = "";
    public List<string> HandlerDisplays { get; } = new();
    public List<string> HandlerContractDisplays { get; } = new();
}
