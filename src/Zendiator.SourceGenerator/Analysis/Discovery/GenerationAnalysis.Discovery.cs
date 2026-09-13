using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using static Zendiator.SourceGenerator.SymbolUtilities;
using static Zendiator.SourceGenerator.ContractNames;

namespace Zendiator.SourceGenerator;

internal sealed partial class GenerationAnalysis
{
    private INamedTypeSymbol requestDefinition = null!;
    private INamedTypeSymbol handlerDefinition = null!;
    private INamedTypeSymbol behaviorDefinition = null!;
    private INamedTypeSymbol voidRequestDefinition = null!;
    private INamedTypeSymbol voidHandlerDefinition = null!;
    private INamedTypeSymbol voidBehaviorDefinition = null!;
    private INamedTypeSymbol notificationDefinition = null!;
    private INamedTypeSymbol notificationHandlerDefinition = null!;
    private INamedTypeSymbol multiResponseDefinition = null!;
    private INamedTypeSymbol multiVoidDefinition = null!;
    private INamedTypeSymbol syncRequestDefinition = null!;
    private INamedTypeSymbol syncVoidRequestDefinition = null!;
    private INamedTypeSymbol syncHandlerDefinition = null!;
    private INamedTypeSymbol syncVoidHandlerDefinition = null!;
    private INamedTypeSymbol syncBehaviorDefinition = null!;
    private INamedTypeSymbol syncVoidBehaviorDefinition = null!;
    private INamedTypeSymbol syncMultiResponseDefinition = null!;
    private INamedTypeSymbol syncMultiVoidDefinition = null!;
    private INamedTypeSymbol streamRequestDefinition = null!;
    private INamedTypeSymbol streamHandlerDefinition = null!;
    private INamedTypeSymbol streamBehaviorDefinition = null!;
    private readonly List<(INamedTypeSymbol Type, int Order, bool IsOpenBehavior)> pipelines = new List<(INamedTypeSymbol Type, int Order, bool IsOpenBehavior)>();
    private ISymbol accessContext = null!;
    private readonly Dictionary<INamedTypeSymbol, ITypeSymbol> requests = new Dictionary<INamedTypeSymbol, ITypeSymbol>(SymbolEqualityComparer.Default);
    private readonly Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> handlers = new Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>>(SymbolEqualityComparer.Default);
    private readonly Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> voidHandlers = new Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>>(SymbolEqualityComparer.Default);
    private readonly Dictionary<INamedTypeSymbol, ITypeSymbol> openRequests = new Dictionary<INamedTypeSymbol, ITypeSymbol>(SymbolEqualityComparer.Default);
    private readonly List<OpenBinding> openHandlers = new List<OpenBinding>();
    private readonly List<OpenBinding> openVoidHandlers = new List<OpenBinding>();
    private readonly Dictionary<INamedTypeSymbol, ITypeSymbol> openSyncRequests = new Dictionary<INamedTypeSymbol, ITypeSymbol>(SymbolEqualityComparer.Default);
    private readonly List<OpenBinding> openSyncHandlers = new List<OpenBinding>();
    private readonly List<OpenBinding> openSyncVoidHandlers = new List<OpenBinding>();
    private readonly Dictionary<INamedTypeSymbol, ITypeSymbol> syncRequests = new Dictionary<INamedTypeSymbol, ITypeSymbol>(SymbolEqualityComparer.Default);
    private readonly Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> syncHandlers = new Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>>(SymbolEqualityComparer.Default);
    private readonly Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> syncVoidHandlers = new Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>>(SymbolEqualityComparer.Default);
    private readonly HashSet<INamedTypeSymbol> syncVoidRequests = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
    private readonly Dictionary<INamedTypeSymbol, ITypeSymbol> streamRequests = new Dictionary<INamedTypeSymbol, ITypeSymbol>(SymbolEqualityComparer.Default);
    private readonly Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> streamHandlers = new Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>>(SymbolEqualityComparer.Default);
    private readonly Dictionary<INamedTypeSymbol, ITypeSymbol> openStreamRequests = new Dictionary<INamedTypeSymbol, ITypeSymbol>(SymbolEqualityComparer.Default);
    private readonly List<OpenBinding> openStreamHandlers = new List<OpenBinding>();
    private readonly HashSet<INamedTypeSymbol> knownNotifications = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
    private readonly HashSet<INamedTypeSymbol> openNotifications = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
    private readonly Dictionary<INamedTypeSymbol, List<Subscriber>> subscribers = new Dictionary<INamedTypeSymbol, List<Subscriber>>(SymbolEqualityComparer.Default);
    private readonly List<OpenBinding> openSubscribers = new List<OpenBinding>();

    private GenerationResult? DiscoverTypes()
    {
        requestDefinition = compilation.GetTypeByMetadataName(Request)!;
        handlerDefinition = compilation.GetTypeByMetadataName(Handler)!;
        behaviorDefinition = compilation.GetTypeByMetadataName(Behavior)!;
        voidRequestDefinition = compilation.GetTypeByMetadataName(VoidRequest)!;
        voidHandlerDefinition = compilation.GetTypeByMetadataName(VoidHandler)!;
        voidBehaviorDefinition = compilation.GetTypeByMetadataName(VoidBehavior)!;
        notificationDefinition = compilation.GetTypeByMetadataName(Notification)!;
        notificationHandlerDefinition = compilation.GetTypeByMetadataName(NotificationHandler)!;
        multiResponseDefinition = compilation.GetTypeByMetadataName(MultiResponse)!;
        multiVoidDefinition = compilation.GetTypeByMetadataName(MultiVoid)!;
        syncRequestDefinition = compilation.GetTypeByMetadataName(SyncRequest)!;
        syncVoidRequestDefinition = compilation.GetTypeByMetadataName(SyncVoidRequest)!;
        syncHandlerDefinition = compilation.GetTypeByMetadataName(SyncHandler)!;
        syncVoidHandlerDefinition = compilation.GetTypeByMetadataName(SyncVoidHandler)!;
        syncBehaviorDefinition = compilation.GetTypeByMetadataName(SyncBehavior)!;
        syncVoidBehaviorDefinition = compilation.GetTypeByMetadataName(SyncVoidBehavior)!;
        syncMultiResponseDefinition = compilation.GetTypeByMetadataName(SyncMultiResponse)!;
        syncMultiVoidDefinition = compilation.GetTypeByMetadataName(SyncMultiVoid)!;
        streamRequestDefinition = compilation.GetTypeByMetadataName(StreamRequest)!;
        streamHandlerDefinition = compilation.GetTypeByMetadataName(StreamHandler)!;
        streamBehaviorDefinition = compilation.GetTypeByMetadataName(StreamBehavior)!;
        if (requestDefinition == null || handlerDefinition == null || behaviorDefinition == null ||
            voidRequestDefinition == null || voidHandlerDefinition == null || voidBehaviorDefinition == null ||
            notificationDefinition == null || notificationHandlerDefinition == null ||
            multiResponseDefinition == null || multiVoidDefinition == null ||
            syncRequestDefinition == null || syncVoidRequestDefinition == null ||
            syncHandlerDefinition == null || syncVoidHandlerDefinition == null ||
            syncBehaviorDefinition == null || syncVoidBehaviorDefinition == null ||
            syncMultiResponseDefinition == null || syncMultiVoidDefinition == null ||
            streamRequestDefinition == null || streamHandlerDefinition == null || streamBehaviorDefinition == null)
        {
            Error(3, "Reference Zendiator.Abstractions.");
            return new GenerationResult("", "", errors);
        }
        var assemblies = new HashSet<IAssemblySymbol>(SymbolEqualityComparer.Default) { compilation.Assembly };
        if (diMode)
        {
            foreach (var assembly in diAssemblies) assemblies.Add(assembly);
            foreach (var pipeline in diPipelines) pipelines.Add(pipeline);
        }
        accessContext = assemblyMode || diMode ? compilation.Assembly : mediator!;

        var configAttributes = assemblyMode
            ? compilation.Assembly.GetAttributes().AsEnumerable()
            : mediator != null ? mediator.GetAttributes().AsEnumerable() : Enumerable.Empty<AttributeData>();
        foreach (var attribute in configAttributes)
        {
            ct.ThrowIfCancellationRequested();
            var name = attribute.AttributeClass?.ToDisplayString();
            if (name == "Zendiator.IncludeAssemblyAttribute")
            {
                if (attribute.ConstructorArguments.FirstOrDefault().Value is INamedTypeSymbol marker)
                    assemblies.Add(marker.ContainingAssembly);
                else Error(5, "IncludeAssembly requires a marker type.", mediator);
            }
            if (name != "Zendiator.PipelineBehaviorAttribute") continue;
            if (attribute.ConstructorArguments.FirstOrDefault().Value is not INamedTypeSymbol type)
            {
                Error(4, "PipelineBehavior requires an implementation type.", mediator);
                continue;
            }
            var order = attribute.NamedArguments.FirstOrDefault(p => p.Key == "Order").Value.Value as int? ?? 0;
            if (pipelines.Any(p => p.Order == order || Same(p.Type, type)))
            {
                Error(4, "Pipeline types and Order values must be unique.", mediator);
                continue;
            }
            pipelines.Add((type, order, type.IsUnboundGenericType));
        }
        var allTypes = assemblies.SelectMany(a => Types(a.GlobalNamespace, ct)).OrderBy(Name, StringComparer.Ordinal).ToArray();
        foreach (var type in allTypes)
        {
            ct.ThrowIfCancellationRequested();
            if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct) || type.IsAbstract) continue;
            var contracts = type.AllInterfaces.Where(i => Same(i.OriginalDefinition, requestDefinition)).ToArray();
            if (contracts.Length > 0)
            {
                if (IsOpenDefinition(type))
                {
                    if (type.IsRefLikeType)
                        Error(12, $"Request {Name(type)} is a ref struct. Ref struct requests require synchronous dispatch (SendSync).", type);
                    else if (contracts.Length != 1 || !IsPublicDefinition(type) || !LeavesPublicOrParam(contracts[0].TypeArguments[0], type))
                        Error(3, $"Request {Name(type)} must be a public generic definition with exactly one response contract over public or type-parameter types.", type);
                    else if (type.TypeParameters.Any(static p => p.AllowsRefLikeType))
                        Error(12, $"Request {Name(type)} has ref-like type parameters. Async dispatch cannot accept ref-like arguments; use synchronous dispatch (SendSync).", type);
                    else openRequests[type] = contracts[0].TypeArguments[0];
                }
                else if (type.IsRefLikeType)
                    Error(12, $"Request {Name(type)} is a ref struct. Ref struct requests require synchronous dispatch (SendSync).", type);
                else if (contracts.Length != 1 || HasParameters(type) || !Public(type) || !Public(contracts[0].TypeArguments[0]))
                    Error(3, $"Request {Name(type)} must be public, non-generic and have exactly one public response contract.", type);
                else requests[type] = contracts[0].TypeArguments[0];
            }
            foreach (var contract in type.AllInterfaces.Where(i => Same(i.OriginalDefinition, handlerDefinition)))
            {
                if (IsOpenDefinition(type))
                {
                    CollectOpenHandler(type, contract, openHandlers, isVoid: false);
                    continue;
                }
                if (type.TypeKind != TypeKind.Class || HasParameters(type) ||
                    !compilation.IsSymbolAccessibleWithin(type, accessContext) ||
                    contract.TypeArguments[0] is not INamedTypeSymbol request ||
                    request.IsAbstract || request.TypeKind == TypeKind.Interface || IsOpenDefinition(request) || HasOpenArguments(request))
                {
                    Error(3, $"Handler {Name(type)} must be an accessible non-generic class for a concrete request.", type);
                    continue;
                }
                if (request.IsRefLikeType)
                {
                    Error(12, $"Handler {Name(type)} targets ref struct request {Name(request)}. Ref struct requests require synchronous dispatch (SendSync).", type);
                    continue;
                }
                if (request.IsRefLikeType)
                {
                    Error(12, $"Handler {Name(type)} targets ref struct request {Name(request)}. Ref struct requests require synchronous dispatch (SendSync).", type);
                    continue;
                }
                if (!handlers.TryGetValue(request, out var list)) handlers[request] = list = new();
                if (!list.Any(h => Same(h, type))) list.Add(type);
                // A handler may refer to contracts from a separate assembly; include its exact request.
                var responseContracts = request.AllInterfaces.Where(i => Same(i.OriginalDefinition, requestDefinition)).ToArray();
                if (responseContracts.Length != 1 || !Public(request) || !Public(contract.TypeArguments[1]))
                {
                    Error(3, $"Invalid request contract on {Name(type)}.", type);
                }
                else if (!Same(responseContracts[0].TypeArguments[0], contract.TypeArguments[1]))
                {
                    Error(3, $"Handler {Name(type)} response {Name(contract.TypeArguments[1])} does not match request {Name(request)} response {Name(responseContracts[0].TypeArguments[0])}.", type);
                }
                else if (requests.TryGetValue(request, out var existing) && !Same(existing, contract.TypeArguments[1]))
                {
                    Error(3, $"Conflicting response contracts for {Name(request)}.", type);
                }
                else requests[request] = contract.TypeArguments[1];
            }
            foreach (var contract in type.AllInterfaces.Where(i => Same(i.OriginalDefinition, voidHandlerDefinition)))
            {
                if (IsOpenDefinition(type))
                {
                    CollectOpenHandler(type, contract, openVoidHandlers, isVoid: true);
                    continue;
                }
                if (type.TypeKind != TypeKind.Class || HasParameters(type) ||
                    !compilation.IsSymbolAccessibleWithin(type, accessContext) ||
                    contract.TypeArguments[0] is not INamedTypeSymbol request ||
                    request.IsAbstract || request.TypeKind == TypeKind.Interface || IsOpenDefinition(request) || HasOpenArguments(request))
                {
                    Error(3, $"Handler {Name(type)} must be an accessible non-generic class for a concrete void request.", type);
                    continue;
                }
                if (request.IsRefLikeType)
                {
                    Error(12, $"Handler {Name(type)} targets ref struct request {Name(request)}. Ref struct requests require synchronous dispatch (SendSync).", type);
                    continue;
                }
                if (!request.AllInterfaces.Any(i => Same(i.OriginalDefinition, voidRequestDefinition)))
                {
                    Error(3, $"Handler {Name(type)} request {Name(request)} must implement Zendiator.IRequest.", type);
                    continue;
                }
                if (!voidHandlers.TryGetValue(request, out var vlist)) voidHandlers[request] = vlist = new();
                if (!vlist.Any(h => Same(h, type))) vlist.Add(type);
                // Mirror the response-contract tracking so a void request reached only
                // through its handler still forms a route.
                var voidContracts = request.AllInterfaces.Where(i => Same(i.OriginalDefinition, requestDefinition)).ToArray();
                if (voidContracts.Length != 1 || !Public(request) || !Public(voidContracts[0].TypeArguments[0]))
                {
                    Error(3, $"Invalid request contract on {Name(type)}.", type);
                }
                else if (requests.TryGetValue(request, out var existing) && !Same(existing, voidContracts[0].TypeArguments[0]))
                {
                    Error(3, $"Conflicting response contracts for {Name(request)}.", type);
                }
                else requests[request] = voidContracts[0].TypeArguments[0];
            }
            var syncContracts = type.AllInterfaces.Where(i => Same(i.OriginalDefinition, syncRequestDefinition)).ToArray();
            if (syncContracts.Length > 0)
            {
                if (IsOpenDefinition(type))
                {
                    if (syncContracts.Length != 1 || !IsPublicDefinition(type) || !LeavesPublicOrParam(syncContracts[0].TypeArguments[0], type))
                        Error(3, $"Request {Name(type)} must be a public generic definition with exactly one response contract over public or type-parameter types.", type);
                    else if (MayBeRefLike(syncContracts[0].TypeArguments[0], type))
                        Error(12, $"Request {Name(type)} has a ref-like response. Ref struct responses are not supported; use an ownable response type.", type);
                    else openSyncRequests[type] = syncContracts[0].TypeArguments[0];
                }
                else if (syncContracts.Length != 1 || HasParameters(type) || !Public(type) || !Public(syncContracts[0].TypeArguments[0]))
                {
                    Error(3, $"Request {Name(type)} must be public, non-generic and have exactly one public response contract.", type);
                }
                else if (MayBeRefLike(syncContracts[0].TypeArguments[0], type))
                {
                    Error(12, $"Request {Name(type)} has a ref-like response. Ref struct responses are not supported; use an ownable response type.", type);
                }
                else syncRequests[type] = syncContracts[0].TypeArguments[0];
            }
            if (type.AllInterfaces.Any(i => Same(i.OriginalDefinition, syncVoidRequestDefinition)))
            {
                if (IsOpenDefinition(type))
                {
                    if (!IsPublicDefinition(type))
                        Error(3, $"Request {Name(type)} must be a public generic definition.", type);
                    else syncVoidRequests.Add(type);
                }
                else if (!Public(type))
                {
                    Error(3, $"Request {Name(type)} must be public.", type);
                }
                else syncVoidRequests.Add(type);
            }
            foreach (var contract in type.AllInterfaces.Where(i => Same(i.OriginalDefinition, syncHandlerDefinition)))
            {
                if (IsOpenDefinition(type))
                {
                    CollectOpenSyncHandler(type, contract, openSyncHandlers, isVoid: false);
                    continue;
                }
                if (type.TypeKind != TypeKind.Class || HasParameters(type) ||
                    !compilation.IsSymbolAccessibleWithin(type, accessContext) ||
                    contract.TypeArguments[0] is not INamedTypeSymbol request ||
                    request.IsAbstract || request.TypeKind == TypeKind.Interface || IsOpenDefinition(request) || HasOpenArguments(request))
                {
                    Error(3, $"Handler {Name(type)} must be an accessible non-generic class for a concrete sync request.", type);
                    continue;
                }
                var reqSync = request.AllInterfaces.Where(i => Same(i.OriginalDefinition, syncRequestDefinition)).ToArray();
                if (reqSync.Length != 1 || !Public(request) || !Public(contract.TypeArguments[1]))
                {
                    if (reqSync.Length == 0 && request.AllInterfaces.Any(i => Same(i.OriginalDefinition, syncVoidRequestDefinition)))
                        Error(8, $"Handler {Name(type)} targets void sync request {Name(request)}. Use one-argument ISyncRequestHandler<{Name(request)}>.", type);
                    else if (request.AllInterfaces.Any(i => Same(i.OriginalDefinition, requestDefinition) || Same(i.OriginalDefinition, voidRequestDefinition)))
                        Error(8, $"Handler {Name(type)} targets async request {Name(request)}. Async requests require SendAsync; synchronous dispatch only accepts ISyncRequest.", type);
                    else
                        Error(3, $"Invalid sync request contract on {Name(type)}.", type);
                    continue;
                }
                if (MayBeRefLike(contract.TypeArguments[1], type))
                {
                    Error(12, $"Handler {Name(type)} has a ref-like response. Ref struct responses are not supported; use an ownable response type.", type);
                    continue;
                }
                if (!Same(reqSync[0].TypeArguments[0], contract.TypeArguments[1]))
                {
                    Error(3, $"Handler {Name(type)} response {Name(contract.TypeArguments[1])} does not match request {Name(request)} response {Name(reqSync[0].TypeArguments[0])}.", type);
                    continue;
                }
                if (!syncHandlers.TryGetValue(request, out var list)) syncHandlers[request] = list = new();
                if (!list.Any(h => Same(h, type))) list.Add(type);
                if (syncRequests.TryGetValue(request, out var existing) && !Same(existing, contract.TypeArguments[1]))
                {
                    Error(3, $"Conflicting response contracts for {Name(request)}.", type);
                }
                else syncRequests[request] = contract.TypeArguments[1];
            }
            foreach (var contract in type.AllInterfaces.Where(i => Same(i.OriginalDefinition, syncVoidHandlerDefinition)))
            {
                if (IsOpenDefinition(type))
                {
                    CollectOpenSyncHandler(type, contract, openSyncVoidHandlers, isVoid: true);
                    continue;
                }
                if (type.TypeKind != TypeKind.Class || HasParameters(type) ||
                    !compilation.IsSymbolAccessibleWithin(type, accessContext) ||
                    contract.TypeArguments[0] is not INamedTypeSymbol request ||
                    request.IsAbstract || request.TypeKind == TypeKind.Interface || IsOpenDefinition(request) || HasOpenArguments(request))
                {
                    Error(3, $"Handler {Name(type)} must be an accessible non-generic class for a concrete void sync request.", type);
                    continue;
                }
                if (!request.AllInterfaces.Any(i => Same(i.OriginalDefinition, syncVoidRequestDefinition)))
                {
                    if (request.AllInterfaces.Any(i => Same(i.OriginalDefinition, requestDefinition) || Same(i.OriginalDefinition, voidRequestDefinition)))
                        Error(8, $"Handler {Name(type)} targets async request {Name(request)}. Async requests require SendAsync; synchronous dispatch only accepts ISyncRequest.", type);
                    else
                        Error(8, $"Handler {Name(type)} targets response sync request {Name(request)}. Use two-argument ISyncRequestHandler<{Name(request)}, TResponse>.", type);
                    continue;
                }
                if (!Public(request))
                {
                    Error(3, $"Invalid sync request contract on {Name(type)}.", type);
                    continue;
                }
                if (!syncVoidHandlers.TryGetValue(request, out var vlist)) syncVoidHandlers[request] = vlist = new();
                if (!vlist.Any(h => Same(h, type))) vlist.Add(type);
                syncVoidRequests.Add(request);
            }
            var streamContracts = type.AllInterfaces.Where(i => Same(i.OriginalDefinition, streamRequestDefinition)).ToArray();
            if (streamContracts.Length > 0)
            {
                if (IsOpenDefinition(type))
                {
                    if (type.IsRefLikeType)
                        Error(12, $"Stream request {Name(type)} is a ref struct. Ref struct stream requests are not supported.", type);
                    else if (streamContracts.Length != 1 || !IsPublicDefinition(type) || !LeavesPublicOrParam(streamContracts[0].TypeArguments[0], type))
                        Error(3, $"Stream request {Name(type)} must be a public generic definition with exactly one item contract over public or type-parameter types.", type);
                    else if (type.TypeParameters.Any(static p => p.AllowsRefLikeType))
                        Error(12, $"Stream request {Name(type)} has ref-like type parameters. Async streams cannot accept ref-like arguments.", type);
                    else if (MayBeRefLike(streamContracts[0].TypeArguments[0], type))
                        Error(12, $"Stream request {Name(type)} has a ref-like item. Ref struct stream items are not supported; use an ownable item type.", type);
                    else openStreamRequests[type] = streamContracts[0].TypeArguments[0];
                }
                else if (type.IsRefLikeType)
                    Error(12, $"Stream request {Name(type)} is a ref struct. Ref struct stream requests are not supported.", type);
                else if (streamContracts.Length != 1 || HasParameters(type) || !Public(type) || !Public(streamContracts[0].TypeArguments[0]))
                    Error(3, $"Stream request {Name(type)} must be public, non-generic and have exactly one public item contract.", type);
                else if (streamContracts[0].TypeArguments[0] is INamedTypeSymbol streamItem && streamItem.IsRefLikeType)
                    Error(12, $"Stream request {Name(type)} has a ref-like item. Ref struct stream items are not supported; use an ownable item type.", type);
                else streamRequests[type] = streamContracts[0].TypeArguments[0];
            }
            foreach (var contract in type.AllInterfaces.Where(i => Same(i.OriginalDefinition, streamHandlerDefinition)))
            {
                if (IsOpenDefinition(type))
                {
                    CollectOpenStreamHandler(type, contract);
                    continue;
                }
                if (type.TypeKind != TypeKind.Class || HasParameters(type) ||
                    !compilation.IsSymbolAccessibleWithin(type, accessContext) ||
                    contract.TypeArguments[0] is not INamedTypeSymbol streamReq ||
                    streamReq.IsAbstract || streamReq.TypeKind == TypeKind.Interface || IsOpenDefinition(streamReq) || HasOpenArguments(streamReq))
                {
                    Error(3, $"Stream handler {Name(type)} must be an accessible non-generic class for a concrete stream request.", type);
                    continue;
                }
                if (streamReq.IsRefLikeType)
                {
                    Error(12, $"Stream handler {Name(type)} targets ref struct request {Name(streamReq)}. Ref struct stream requests are not supported.", type);
                    continue;
                }
                if (contract.TypeArguments[1] is INamedTypeSymbol streamItemType && streamItemType.IsRefLikeType)
                {
                    Error(12, $"Stream handler {Name(type)} has a ref-like item. Ref struct stream items are not supported; use an ownable item type.", type);
                    continue;
                }
                if (!streamHandlers.TryGetValue(streamReq, out var slist)) streamHandlers[streamReq] = slist = new();
                if (!slist.Any(h => Same(h, type))) slist.Add(type);
                var streamResponseContracts = streamReq.AllInterfaces.Where(i => Same(i.OriginalDefinition, streamRequestDefinition)).ToArray();
                if (streamResponseContracts.Length != 1 || !Public(streamReq) || !Public(contract.TypeArguments[1]))
                {
                    Error(3, $"Invalid stream request contract on {Name(type)}.", type);
                }
                else if (!Same(streamResponseContracts[0].TypeArguments[0], contract.TypeArguments[1]))
                {
                    Error(3, $"Stream handler {Name(type)} item {Name(contract.TypeArguments[1])} does not match request {Name(streamReq)} item {Name(streamResponseContracts[0].TypeArguments[0])}.", type);
                }
                else if (streamRequests.TryGetValue(streamReq, out var existingStream) && !Same(existingStream, contract.TypeArguments[1]))
                {
                    Error(3, $"Conflicting item contracts for {Name(streamReq)}.", type);
                }
                else streamRequests[streamReq] = contract.TypeArguments[1];
            }
            var notificationContracts = type.AllInterfaces.Where(i => Same(i.OriginalDefinition, notificationDefinition)).ToArray();
            if (notificationContracts.Length > 0 && !type.IsRefLikeType)
            {
                if (IsOpenDefinition(type))
                {
                    if (!IsPublicDefinition(type))
                        Error(3, $"Notification {Name(type)} must be a public generic definition.", type);
                    else openNotifications.Add(type);
                }
                else if (!Public(type))
                {
                    Error(3, $"Notification {Name(type)} must be public.", type);
                }
                else knownNotifications.Add(type);
            }
            foreach (var contract in type.AllInterfaces.Where(i => Same(i.OriginalDefinition, notificationHandlerDefinition)))
            {
                if (IsOpenDefinition(type))
                {
                    CollectOpenSubscriber(type, contract);
                    continue;
                }
                if (type.TypeKind != TypeKind.Class || HasParameters(type) ||
                    !compilation.IsSymbolAccessibleWithin(type, accessContext) ||
                    contract.TypeArguments[0] is not INamedTypeSymbol target ||
                    target.IsAbstract || target.TypeKind == TypeKind.Interface || IsOpenDefinition(target) || HasOpenArguments(target))
                {
                    Error(3, $"Subscriber {Name(type)} must be an accessible non-generic class for a concrete notification.", type);
                    continue;
                }
                if (target.IsRefLikeType)
                {
                    Error(12, $"Subscriber {Name(type)} targets ref struct notification {Name(target)}. Ref struct notifications are not supported.", type);
                    continue;
                }
                if (!target.AllInterfaces.Any(i => Same(i.OriginalDefinition, notificationDefinition)))
                {
                    Error(13, $"Subscriber {Name(type)} target {Name(target)} must implement Zendiator.INotification.", type);
                    continue;
                }
                if (!Public(target))
                {
                    Error(13, $"Subscriber {Name(type)} target {Name(target)} must be public. Declare the notification or include its assembly.", type);
                    continue;
                }
                knownNotifications.Add(target);
                AddSubscriber(target, type);
            }
        }
        return null;
    }
}
