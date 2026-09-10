using System.Linq;
using Microsoft.CodeAnalysis;
using static Zendiator.SourceGenerator.SymbolUtilities;
using static Zendiator.SourceGenerator.GenericConstraints;

namespace Zendiator.SourceGenerator;

internal sealed partial class GenerationAnalysis
{
    private void ApplyPipelines()
    {
        foreach (var pipeline in pipelines.OrderBy(p => p.Order))
        {
            ct.ThrowIfCancellationRequested();
            var type = pipeline.Type;
            var isOpenBehavior = pipeline.IsOpenBehavior;
            var definition = isOpenBehavior ? type.OriginalDefinition : type;
            var contracts = definition.AllInterfaces.Where(i => Same(i.OriginalDefinition, behaviorDefinition)).ToArray();
            var voidContracts = definition.AllInterfaces.Where(i => Same(i.OriginalDefinition, voidBehaviorDefinition)).ToArray();
            var syncContracts = definition.AllInterfaces.Where(i => Same(i.OriginalDefinition, syncBehaviorDefinition)).ToArray();
            var syncVoidContracts = definition.AllInterfaces.Where(i => Same(i.OriginalDefinition, syncVoidBehaviorDefinition)).ToArray();
            var streamContracts = definition.AllInterfaces.Where(i => Same(i.OriginalDefinition, streamBehaviorDefinition)).ToArray();
            var accessible = compilation.IsSymbolAccessibleWithin(definition, accessContext);
            var validAsync = definition.TypeKind == TypeKind.Class && !definition.IsAbstract && accessible && contracts.Length == 1 &&
                (!isOpenBehavior ||
                 (definition.Arity == 2 && !HasGenericContainer(definition) &&
                     Same(contracts[0].TypeArguments[0], definition.TypeParameters[0]) &&
                     Same(contracts[0].TypeArguments[1], definition.TypeParameters[1])));
            var validVoid = definition.TypeKind == TypeKind.Class && !definition.IsAbstract && accessible && voidContracts.Length == 1 &&
                (!isOpenBehavior ||
                 (definition.Arity == 1 && !HasGenericContainer(definition) &&
                     Same(voidContracts[0].TypeArguments[0], definition.TypeParameters[0])));
            var validSyncResponse = definition.TypeKind == TypeKind.Class && !definition.IsAbstract && accessible && syncContracts.Length == 1 &&
                (!isOpenBehavior ||
                 (definition.Arity == 2 && !HasGenericContainer(definition) &&
                     Same(syncContracts[0].TypeArguments[0], definition.TypeParameters[0]) &&
                     Same(syncContracts[0].TypeArguments[1], definition.TypeParameters[1])));
            var validSyncVoid = definition.TypeKind == TypeKind.Class && !definition.IsAbstract && accessible && syncVoidContracts.Length == 1 &&
                (!isOpenBehavior ||
                 (definition.Arity == 1 && !HasGenericContainer(definition) &&
                     Same(syncVoidContracts[0].TypeArguments[0], definition.TypeParameters[0])));
            var validStream = definition.TypeKind == TypeKind.Class && !definition.IsAbstract && accessible && streamContracts.Length == 1 &&
                (!isOpenBehavior ||
                 (definition.Arity == 2 && !HasGenericContainer(definition) &&
                     Same(streamContracts[0].TypeArguments[0], definition.TypeParameters[0]) &&
                     Same(streamContracts[0].TypeArguments[1], definition.TypeParameters[1])));
            if (!validAsync && !validVoid && !validSyncResponse && !validSyncVoid && !validStream)
            {
                Error(4, $"Invalid behavior {Name(type)}. Use an accessible class implementing one pipeline contract; open behaviors must map <TRequest,TResponse> directly and open void behaviors <TRequest> directly.", mediator);
                continue;
            }
            var matched = false;
            var matchedVoid = false;
            foreach (var route in routes)
            {
                if (route.IsVoid) continue;
                INamedTypeSymbol closed;
                if (isOpenBehavior)
                {
                    if (!validAsync) continue;
                    if (route.IsOpen)
                    {
                        if (!SatisfiesConstraints(definition, new[] { route.Request, route.Response }, compilation)) continue;
                        route.Behaviors.Add(definition);
                        route.BehaviorDisplays.Add(ClosedGenericName(definition, new[] { route.RequestDisplay, route.ResponseDisplay }));
                        route.BehaviorContractDisplays.Add($"global::Zendiator.IPipelineBehavior<{route.RequestDisplay}, {route.ResponseDisplay}>");
                        matched = true;
                        continue;
                    }
                    if (!SatisfiesConstraints(definition, new[] { route.Request, route.Response }, compilation)) continue;
                    closed = definition.Construct(route.Request, route.Response);
                }
                else
                {
                    if (!validAsync || route.IsOpen) continue;
                    if (!Same(contracts[0].TypeArguments[0], route.Request) || !Same(contracts[0].TypeArguments[1], route.Response)) continue;
                    closed = type;
                }
                route.Behaviors.Add(closed);
                matched = true;
            }
            foreach (var route in routes)
            {
                if (!route.IsVoid) continue;
                INamedTypeSymbol closed;
                if (isOpenBehavior)
                {
                    if (!validVoid) continue;
                    if (route.IsOpen)
                    {
                        if (!SatisfiesConstraints(definition, new[] { route.Request }, compilation)) continue;
                        route.Behaviors.Add(definition);
                        route.BehaviorDisplays.Add(ClosedGenericName(definition, new[] { route.RequestDisplay }));
                        route.BehaviorContractDisplays.Add($"global::Zendiator.IPipelineBehavior<{route.RequestDisplay}>");
                        matchedVoid = true;
                        continue;
                    }
                    if (!SatisfiesConstraints(definition, new[] { route.Request }, compilation)) continue;
                    closed = definition.Construct(route.Request);
                }
                else
                {
                    if (!validVoid || route.IsOpen) continue;
                    if (!Same(voidContracts[0].TypeArguments[0], route.Request)) continue;
                    closed = type;
                }
                route.Behaviors.Add(closed);
                matchedVoid = true;
            }
            var matchedMulti = false;
            foreach (var route in multiRoutes)
            {
                if (route.IsVoid)
                {
                    if (!validVoid) continue;
                    foreach (var branch in route.Branches)
                    {
                        if (route.IsOpen)
                        {
                            if (!isOpenBehavior) continue;
                            if (!SatisfiesConstraints(definition, new[] { route.Request }, compilation)) continue;
                            branch.Behaviors.Add(definition);
                            branch.BehaviorDisplays.Add(ClosedGenericName(definition, new[] { route.RequestDisplay }));
                            branch.BehaviorContractDisplays.Add($"global::Zendiator.IPipelineBehavior<{route.RequestDisplay}>");
                            matchedMulti = true;
                        }
                        else if (isOpenBehavior)
                        {
                            if (!SatisfiesConstraints(definition, new[] { route.Request }, compilation)) continue;
                            branch.Behaviors.Add(definition.Construct(route.Request));
                            matchedMulti = true;
                        }
                        else
                        {
                            if (!Same(voidContracts[0].TypeArguments[0], route.Request)) continue;
                            branch.Behaviors.Add(type);
                            matchedMulti = true;
                        }
                    }
                }
                else
                {
                    if (!validAsync) continue;
                    foreach (var branch in route.Branches)
                    {
                        if (route.IsOpen)
                        {
                            if (!isOpenBehavior) continue;
                            if (!SatisfiesConstraints(definition, new[] { route.Request, route.Response }, compilation)) continue;
                            branch.Behaviors.Add(definition);
                            branch.BehaviorDisplays.Add(ClosedGenericName(definition, new[] { route.RequestDisplay, route.ResponseDisplay }));
                            branch.BehaviorContractDisplays.Add($"global::Zendiator.IPipelineBehavior<{route.RequestDisplay}, {route.ResponseDisplay}>");
                            matchedMulti = true;
                        }
                        else if (isOpenBehavior)
                        {
                            if (!SatisfiesConstraints(definition, new[] { route.Request, route.Response }, compilation)) continue;
                            branch.Behaviors.Add(definition.Construct(route.Request, route.Response));
                            matchedMulti = true;
                        }
                        else
                        {
                            if (!Same(contracts[0].TypeArguments[0], route.Request) || !Same(contracts[0].TypeArguments[1], route.Response)) continue;
                            branch.Behaviors.Add(type);
                            matchedMulti = true;
                        }
                    }
                }
            }
            var matchedSync = false;
            foreach (var route in syncRoutes)
            {
                if (route.IsVoid)
                {
                    if (!validSyncVoid) continue;
                    if (route.IsOpen)
                    {
                        if (!isOpenBehavior) continue;
                        if (!SatisfiesConstraints(definition, new[] { route.Request }, compilation)) continue;
                        if (route.Request.IsRefLikeType && !definition.TypeParameters[0].AllowsRefLikeType) continue;
                        route.Behaviors.Add(definition);
                        route.BehaviorDisplays.Add(ClosedGenericName(definition, new[] { route.RequestDisplay }));
                        route.BehaviorContractDisplays.Add($"global::Zendiator.ISyncPipelineBehavior<{route.RequestDisplay}>");
                        matchedSync = true;
                    }
                    else if (isOpenBehavior)
                    {
                        if (!SatisfiesConstraints(definition, new[] { route.Request }, compilation)) continue;
                        if (route.Request.IsRefLikeType && !definition.TypeParameters[0].AllowsRefLikeType) continue;
                        route.Behaviors.Add(definition.Construct(route.Request));
                        matchedSync = true;
                    }
                    else
                    {
                        if (!Same(syncVoidContracts[0].TypeArguments[0], route.Request)) continue;
                        route.Behaviors.Add(type);
                        matchedSync = true;
                    }
                }
                else
                {
                    if (!validSyncResponse) continue;
                    if (route.IsOpen)
                    {
                        if (!isOpenBehavior) continue;
                        if (!SatisfiesConstraints(definition, new[] { route.Request, route.Response }, compilation)) continue;
                        if (route.Request.IsRefLikeType && !definition.TypeParameters[0].AllowsRefLikeType) continue;
                        route.Behaviors.Add(definition);
                        route.BehaviorDisplays.Add(ClosedGenericName(definition, new[] { route.RequestDisplay, route.ResponseDisplay }));
                        route.BehaviorContractDisplays.Add($"global::Zendiator.ISyncPipelineBehavior<{route.RequestDisplay}, {route.ResponseDisplay}>");
                        matchedSync = true;
                    }
                    else if (isOpenBehavior)
                    {
                        if (!SatisfiesConstraints(definition, new[] { route.Request, route.Response }, compilation)) continue;
                        if (route.Request.IsRefLikeType && !definition.TypeParameters[0].AllowsRefLikeType) continue;
                        route.Behaviors.Add(definition.Construct(route.Request, route.Response));
                        matchedSync = true;
                    }
                    else
                    {
                        if (!Same(syncContracts[0].TypeArguments[0], route.Request) || !Same(syncContracts[0].TypeArguments[1], route.Response)) continue;
                        route.Behaviors.Add(type);
                        matchedSync = true;
                    }
                }
            }
            foreach (var route in syncMultiRoutes)
            {
                if (route.IsVoid)
                {
                    if (!validSyncVoid) continue;
                    foreach (var branch in route.Branches)
                    {
                        if (route.IsOpen)
                        {
                            if (!isOpenBehavior) continue;
                            if (!SatisfiesConstraints(definition, new[] { route.Request }, compilation)) continue;
                            if (route.Request.IsRefLikeType && !definition.TypeParameters[0].AllowsRefLikeType) continue;
                            branch.Behaviors.Add(definition);
                            branch.BehaviorDisplays.Add(ClosedGenericName(definition, new[] { route.RequestDisplay }));
                            branch.BehaviorContractDisplays.Add($"global::Zendiator.ISyncPipelineBehavior<{route.RequestDisplay}>");
                            matchedSync = true;
                        }
                        else if (isOpenBehavior)
                        {
                            if (!SatisfiesConstraints(definition, new[] { route.Request }, compilation)) continue;
                            if (route.Request.IsRefLikeType && !definition.TypeParameters[0].AllowsRefLikeType) continue;
                            branch.Behaviors.Add(definition.Construct(route.Request));
                            matchedSync = true;
                        }
                        else
                        {
                            if (!Same(syncVoidContracts[0].TypeArguments[0], route.Request)) continue;
                            branch.Behaviors.Add(type);
                            matchedSync = true;
                        }
                    }
                }
                else
                {
                    if (!validSyncResponse) continue;
                    foreach (var branch in route.Branches)
                    {
                        if (route.IsOpen)
                        {
                            if (!isOpenBehavior) continue;
                            if (!SatisfiesConstraints(definition, new[] { route.Request, route.Response }, compilation)) continue;
                            if (route.Request.IsRefLikeType && !definition.TypeParameters[0].AllowsRefLikeType) continue;
                            branch.Behaviors.Add(definition);
                            branch.BehaviorDisplays.Add(ClosedGenericName(definition, new[] { route.RequestDisplay, route.ResponseDisplay }));
                            branch.BehaviorContractDisplays.Add($"global::Zendiator.ISyncPipelineBehavior<{route.RequestDisplay}, {route.ResponseDisplay}>");
                            matchedSync = true;
                        }
                        else if (isOpenBehavior)
                        {
                            if (!SatisfiesConstraints(definition, new[] { route.Request, route.Response }, compilation)) continue;
                            if (route.Request.IsRefLikeType && !definition.TypeParameters[0].AllowsRefLikeType) continue;
                            branch.Behaviors.Add(definition.Construct(route.Request, route.Response));
                            matchedSync = true;
                        }
                        else
                        {
                            if (!Same(syncContracts[0].TypeArguments[0], route.Request) || !Same(syncContracts[0].TypeArguments[1], route.Response)) continue;
                            branch.Behaviors.Add(type);
                            matchedSync = true;
                        }
                    }
                }
            }
            if (!matched && !matchedVoid && !matchedMulti && !matchedSync && !isOpenBehavior)
            {
                // Stream-only behaviors are valid even when no regular route matches; check streams first.
                var streamMatched = false;
                foreach (var route in streamRoutes)
                {
                    if (isOpenBehavior)
                    {
                        if (!validStream) continue;
                        if (route.IsOpen)
                        {
                            if (!SatisfiesConstraints(definition, new[] { route.Request, route.Response }, compilation)) continue;
                        }
                        else
                        {
                            if (!SatisfiesConstraints(definition, new[] { route.Request, route.Response }, compilation)) continue;
                        }
                        streamMatched = true;
                        break;
                    }
                    else
                    {
                        if (!validStream || route.IsOpen) continue;
                        if (!Same(streamContracts[0].TypeArguments[0], route.Request) || !Same(streamContracts[0].TypeArguments[1], route.Response)) continue;
                        streamMatched = true;
                        break;
                    }
                }
                if (!streamMatched)
                    Error(4, $"Behavior {Name(type)} does not match any registered request.", mediator);
            }
            // Attach stream behaviors to stream routes (typed, per-enumeration resolution).
            foreach (var route in streamRoutes)
            {
                INamedTypeSymbol closedStream;
                if (isOpenBehavior)
                {
                    if (!validStream) continue;
                    if (route.IsOpen)
                    {
                        if (!SatisfiesConstraints(definition, new[] { route.Request, route.Response }, compilation)) continue;
                        route.Behaviors.Add(definition);
                        route.BehaviorDisplays.Add(ClosedGenericName(definition, new[] { route.RequestDisplay, route.ResponseDisplay }));
                        route.BehaviorContractDisplays.Add($"global::Zendiator.IStreamPipelineBehavior<{route.RequestDisplay}, {route.ResponseDisplay}>");
                        continue;
                    }
                    if (!SatisfiesConstraints(definition, new[] { route.Request, route.Response }, compilation)) continue;
                    closedStream = definition.Construct(route.Request, route.Response);
                }
                else
                {
                    if (!validStream || route.IsOpen) continue;
                    if (!Same(streamContracts[0].TypeArguments[0], route.Request) || !Same(streamContracts[0].TypeArguments[1], route.Response)) continue;
                    closedStream = type;
                }
                route.Behaviors.Add(closedStream);
            }
        }
    }
}
