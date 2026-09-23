using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using static Zendiator.SourceGenerator.SymbolUtilities;

namespace Zendiator.SourceGenerator;

/// <summary>Projects one completed analysis into symbol-free, value-equatable template inputs.</summary>
internal sealed class EmissionModelFactory(GenerationContracts contracts, Func<ITypeSymbol, string> nameOf)
{
    private readonly Dictionary<ITypeSymbol, EmissionType> _types = new(SymbolEqualityComparer.IncludeNullability);
    private readonly Dictionary<INamedTypeSymbol, string> _openNames = new(SymbolEqualityComparer.Default);

    private EmissionType Type(ITypeSymbol symbol)
    {
        if (_types.TryGetValue(symbol, out var type)) return type;
        var name = nameOf(symbol);
        var openName = name;
        if (symbol is INamedTypeSymbol { Arity: > 0 } named)
        {
            var definition = named.OriginalDefinition;
            if (!_openNames.TryGetValue(definition, out openName))
            {
                openName = OpenTypeofName(definition);
                _openNames.Add(definition, openName);
            }
        }
        type = new EmissionType(name, openName,
            symbol.IsReferenceType, symbol.IsRefLikeType, IsPublic: Public(symbol));
        _types.Add(symbol, type);
        return type;
    }

    private EmissionType Service(INamedTypeSymbol symbol, INamedTypeSymbol contract, string method)
    {
        var type = Type(symbol);
        return UseDirectCall(symbol, contract, method) ? type with { DirectCall = true } : type;
    }

    internal EmissionRoute Request(Route route) => Route(route, route.IsSync ? contracts.Synchronous : contracts.Requests);

    private EmissionRoute Route(Route route, RequestContracts family) => Route(route,
        route.IsVoid ? family.VoidHandler : family.Handler,
        route.IsVoid ? family.VoidBehavior : family.Behavior);

    internal EmissionRoute Stream(Route route) => Route(route, contracts.Streams.Handler, contracts.Streams.Behavior);

    private EmissionRoute Route(Route route, INamedTypeSymbol handler, INamedTypeSymbol behavior)
    {
        var method = route.IsSync ? "Handle" : "HandleAsync";
        return new EmissionRoute
        {
            Request = Type(route.Request),
            Response = Type(route.Response),
            Handler = Service(route.Handler, handler, method),
            IsVoid = route.IsVoid,
            IsSync = route.IsSync,
            IsOpen = route.IsOpen,
            OpenTypeParams = new(route.OpenTypeParams),
            MethodConstraints = new(route.MethodConstraints),
            RequestDisplay = route.RequestDisplay,
            ResponseDisplay = route.ResponseDisplay,
            HandlerDisplay = route.HandlerDisplay,
            HandlerContractDisplay = route.HandlerContractDisplay,
            Behaviors = new(route.Behaviors.Select(b => Service(b, behavior, method))),
            BehaviorDisplays = new(route.BehaviorDisplays),
            BehaviorContractDisplays = new(route.BehaviorContractDisplays)
        };
    }

    internal EmissionMultiRoute Multiple(MultiRoute route) => new()
    {
        Request = Type(route.Request),
        Response = Type(route.Response),
        IsVoid = route.IsVoid,
        IsSync = route.IsSync,
        IsOpen = route.IsOpen,
        OpenTypeParams = new(route.OpenTypeParams),
        MethodConstraints = new(route.MethodConstraints),
        RequestDisplay = route.RequestDisplay,
        ResponseDisplay = route.ResponseDisplay,
        Branches = new(route.Branches.Select(b => Branch(b, route)))
    };

    private EmissionBranch Branch(MultiBranch branch, MultiRoute route)
    {
        var family = route.IsSync ? contracts.Synchronous : contracts.Requests;
        var method = route.IsSync ? "Handle" : "HandleAsync";
        return new EmissionBranch
        {
            Handler = Service(branch.Handler, route.IsVoid ? family.VoidHandler : family.Handler, method),
            HandlerIsOpen = branch.HandlerIsOpen,
            HandlerDisplay = branch.HandlerDisplay,
            HandlerContractDisplay = branch.HandlerContractDisplay,
            Behaviors = new(branch.Behaviors.Select(b => Service(b, route.IsVoid ? family.VoidBehavior : family.Behavior, method))),
            BehaviorDisplays = new(branch.BehaviorDisplays),
            BehaviorContractDisplays = new(branch.BehaviorContractDisplays)
        };
    }

    internal EmissionNotificationRoute Notification(NotificationRoute route) => new()
    {
        Notification = Type(route.Notification),
        IsOpen = route.IsOpen,
        Subscribers = new(route.Subscribers.Select(s => new EmissionSubscriber(Service(s.Handler, contracts.NotificationHandler, "HandleAsync")))),
        OpenTypeParams = new(route.OpenTypeParams),
        MethodConstraints = new(route.MethodConstraints),
        NotificationDisplay = route.NotificationDisplay,
        HandlerDisplays = new(route.HandlerDisplays),
        HandlerContractDisplays = new(route.HandlerContractDisplays)
    };
}
