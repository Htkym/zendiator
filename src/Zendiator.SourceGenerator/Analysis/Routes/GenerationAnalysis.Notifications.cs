using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using static Zendiator.SourceGenerator.SymbolUtilities;
using static Zendiator.SourceGenerator.GenericConstraints;

namespace Zendiator.SourceGenerator;

internal sealed partial class GenerationAnalysis
{
    private readonly List<NotificationRoute> notifications = new List<NotificationRoute>();

    private static int AttributeOrder(AttributeData attribute)
    {
        foreach (var pair in attribute.NamedArguments)
        {
            if (pair.Key == "Order" && pair.Value.Value is int named) return named;
        }
        if (attribute.ConstructorArguments.FirstOrDefault().Value is int constructed) return constructed;
        return 0;
    }

    private int SubscriberOrder(INamedTypeSymbol handler)
    {
        if (FullNameOf(handler) is string key && diHandlerOrders.TryGetValue(key, out var configured)) return configured.Order;
        if (handler.IsGenericType && FullNameOf(handler.OriginalDefinition) is string definition &&
            diHandlerOrders.TryGetValue(definition, out var configuredDefinition)) return configuredDefinition.Order;
        var attr = handler.GetAttributes().FirstOrDefault(static a => a.AttributeClass?.ToDisplayString() == "Zendiator.HandlerOrderAttribute");
        if (attr == null) return 0;
        foreach (var pair in attr.NamedArguments)
        {
            if (pair.Key == "Order" && pair.Value.Value is int named) return named;
        }
        if (attr.ConstructorArguments.FirstOrDefault().Value is int constructed) return constructed;
        return 0;
    }

    private void AddSubscriber(INamedTypeSymbol notification, INamedTypeSymbol handler)
    {
        if (!subscribers.TryGetValue(notification, out var list)) subscribers[notification] = list = new();
        if (list.Any(s => Same(s.Handler, handler))) return;
        list.Add(new Subscriber(handler, SubscriberOrder(handler), handler.ContainingAssembly.Identity.ToString(), Name(handler)));
    }

    private void BuildNotifications()
    {
        foreach (var attribute in compilation.Assembly.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != "Zendiator.NotificationAttribute") continue;
            if (attribute.ConstructorArguments.FirstOrDefault().Value is not INamedTypeSymbol declared)
            {
                Error(13, "Notification requires a notification type.");
                continue;
            }
            if (declared.IsRefLikeType)
            {
                Error(12, $"Notification {Name(declared)} is a ref struct. Ref struct notifications are not supported.", declared);
                continue;
            }
            if (!declared.AllInterfaces.Any(i => Same(i.OriginalDefinition, notificationDefinition)))
            {
                Error(13, $"Notification {Name(declared)} must implement Zendiator.INotification.", declared);
                continue;
            }
            if (IsOpenDefinition(declared))
            {
                if (!IsPublicDefinition(declared))
                    Error(13, $"Notification {Name(declared)} must be public.", declared);
                else openNotifications.Add(declared);
            }
            else if (declared.IsAbstract || declared.TypeKind == TypeKind.Interface || HasOpenArguments(declared) || !Public(declared))
            {
                Error(13, $"Notification {Name(declared)} must be a public concrete type. Declare the notification or include its assembly.", declared);
            }
            else knownNotifications.Add(declared);
        }
        if (diMode)
        {
            foreach (var notification in diNotifications) knownNotifications.Add(notification);
            foreach (var def in diOpenNotifications) openNotifications.Add(def);
        }
        foreach (var notification in knownNotifications.OrderBy(Name, StringComparer.Ordinal))
        {
            var route = new NotificationRoute(notification, isOpen: false);
            if (subscribers.TryGetValue(notification, out var subs))
            {
                route.Subscribers.AddRange(subs
                    .OrderBy(static s => s.Order)
                    .ThenBy(static s => s.AssemblyId, StringComparer.Ordinal)
                    .ThenBy(static s => s.FullName, StringComparer.Ordinal));
            }
            route.NotificationDisplay = Name(notification);
            notifications.Add(route);
        }
        foreach (var def in openNotifications.OrderBy(Name, StringComparer.Ordinal))
        {
            var route = new NotificationRoute(def, isOpen: true);
            var binds = openSubscribers.Where(b => Same(b.RequestDefinition, def))
                .Select(b => (Binding: b, Order: SubscriberOrder(b.Definition), Assembly: b.Definition.ContainingAssembly.Identity.ToString(), Full: Name(b.Definition)))
                .OrderBy(static x => x.Order)
                .ThenBy(static x => x.Assembly, StringComparer.Ordinal)
                .ThenBy(static x => x.Full, StringComparer.Ordinal)
                .Select(static x => x.Binding).ToList();
            var merged = def.TypeParameters.Select(FromTypeParam).ToArray();
            foreach (var binding in binds)
            {
                var mapArgs = new ITypeSymbol[binding.Definition.TypeParameters.Length];
                for (var j = 0; j < mapArgs.Length; j++) mapArgs[j] = def.TypeParameters[binding.HandlerToDef[j]];
                for (var j = 0; j < binding.Definition.TypeParameters.Length; j++)
                    MergeHandlerParam(merged[binding.HandlerToDef[j]], binding.Definition.TypeParameters[j], binding.Definition, mapArgs, compilation);
                var hArgs = new string[binding.Definition.TypeParameters.Length];
                for (var j = 0; j < hArgs.Length; j++) hArgs[j] = def.TypeParameters[binding.HandlerToDef[j]].Name;
                route.Subscribers.Add(new Subscriber(binding.Definition, SubscriberOrder(binding.Definition), binding.Definition.ContainingAssembly.Identity.ToString(), Name(binding.Definition)));
                route.HandlerDisplays.Add(ClosedGenericName(binding.Definition, hArgs));
                route.HandlerContractDisplays.Add($"global::Zendiator.INotificationHandler<{Name(def)}>");
            }
            foreach (var p in def.TypeParameters) route.OpenTypeParams.Add(p.Name);
            foreach (var m in merged) route.MethodConstraints.Add(RenderConstraints(m));
            route.NotificationDisplay = Name(def);
            notifications.Add(route);
        }
        foreach (var route in notifications)
        {
            if (route.IsOpen || !route.Notification.IsGenericType) continue;
            foreach (var binding in openSubscribers)
            {
                if (Covers(binding, route.Notification))
                {
                    var closedName = route.Subscribers.Count == 0 ? "(none)" : Name(route.Subscribers[0].Handler);
                    Error(10, $"Ambiguous generic binding for {Name(route.Notification)}: closed subscriber {closedName} overlaps open subscriber {Name(binding.Definition)}. Keep one.", route.Notification);
                }
            }
        }
    }
}
