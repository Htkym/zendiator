using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using static Zendiator.SourceGenerator.SymbolUtilities;
using static Zendiator.SourceGenerator.MarkedHandlerDiscovery;

namespace Zendiator.SourceGenerator;

/// <summary>Reads only dedicated interfaces of a supplied stage. Method bodies never grant capabilities.</summary>
internal static class StageContractBinding
{
    private sealed record Role(string Name, BoundCallbackPhase Phase, BoundCompletionKind Completion,
        bool ResponseOnly = false, bool VoidOnly = false, bool Wrapper = false, bool Stream = false, bool ScopedObserver = false);
    private static readonly Role[] Roles =
    {
        new("IScopedSendBeforeObserver`1", BoundCallbackPhase.ObserveBefore, BoundCompletionKind.Synchronous, ScopedObserver: true),
        new("IScopedSendResponseObserver`2", BoundCallbackPhase.ObserveSuccess, BoundCompletionKind.Synchronous, ResponseOnly: true, ScopedObserver: true),
        new("IScopedSendSuccessObserver`1", BoundCallbackPhase.ObserveSuccess, BoundCompletionKind.Synchronous, VoidOnly: true, ScopedObserver: true),
        new("IScopedSendExceptionObserver`1", BoundCallbackPhase.ObserveException, BoundCompletionKind.Synchronous, ScopedObserver: true),
        new("IScopedSendFinallyObserver`1", BoundCallbackPhase.ObserveFinally, BoundCompletionKind.Synchronous, ScopedObserver: true),
        new("ISendRequestTransform`1", BoundCallbackPhase.Before, BoundCompletionKind.Synchronous),
        new("ISendResponseTransform`2", BoundCallbackPhase.Success, BoundCompletionKind.Synchronous, ResponseOnly: true),
        new("ISendSuccessObserver`1", BoundCallbackPhase.Success, BoundCompletionKind.Synchronous, VoidOnly: true),
        new("ISendExceptionObserver`1", BoundCallbackPhase.Exception, BoundCompletionKind.Synchronous),
        new("ISendFinallyObserver`1", BoundCallbackPhase.Finally, BoundCompletionKind.Synchronous),
        new("IAsyncSendRequestTransform`1", BoundCallbackPhase.Before, BoundCompletionKind.ValueTask),
        new("IAsyncSendResponseTransform`2", BoundCallbackPhase.Success, BoundCompletionKind.ValueTask, ResponseOnly: true),
        new("IAsyncSendSuccessObserver`1", BoundCallbackPhase.Success, BoundCompletionKind.ValueTask, VoidOnly: true),
        new("IAsyncSendExceptionObserver`1", BoundCallbackPhase.Exception, BoundCompletionKind.ValueTask),
        new("IAsyncSendFinallyObserver`1", BoundCallbackPhase.Finally, BoundCompletionKind.ValueTask),
        new("ISendWrapper`2", BoundCallbackPhase.Wrapper, BoundCompletionKind.Synchronous, ResponseOnly: true, Wrapper: true),
        new("ISendWrapper`1", BoundCallbackPhase.Wrapper, BoundCompletionKind.Synchronous, VoidOnly: true, Wrapper: true),
        new("IAsyncSendWrapper`2", BoundCallbackPhase.Wrapper, BoundCompletionKind.ValueTask, ResponseOnly: true, Wrapper: true),
        new("IAsyncSendWrapper`1", BoundCallbackPhase.Wrapper, BoundCompletionKind.ValueTask, VoidOnly: true, Wrapper: true),
        new("IStreamItemFilter`2", BoundCallbackPhase.Filter, BoundCompletionKind.Synchronous, Stream: true),
        new("IStreamItemMap`2", BoundCallbackPhase.Map, BoundCompletionKind.Synchronous, Stream: true),
        new("IAsyncStreamItemFilter`2", BoundCallbackPhase.Filter, BoundCompletionKind.ValueTask, Stream: true),
        new("IAsyncStreamItemMap`2", BoundCallbackPhase.Map, BoundCompletionKind.ValueTask, Stream: true),
        new("IStreamItemMap`3", BoundCallbackPhase.Map, BoundCompletionKind.Synchronous, Stream: true),
        new("IAsyncStreamItemMap`3", BoundCallbackPhase.Map, BoundCompletionKind.ValueTask, Stream: true),
        new("IStreamMiddleware`2", BoundCallbackPhase.Wrapper, BoundCompletionKind.ValueTask, Wrapper: true, Stream: true)
    };

    internal static BoundStageModel Bind(INamedTypeSymbol type, FusionStageKind kind, string anchor,
        INamedTypeSymbol request, BoundDispatchFamily family, FusionResultKind resultKind, ITypeSymbol? result,
        Compilation compilation, List<CompositionDiagnosticFact> facts, CanonicalStreamOpenBinding? open = null, bool typedStream = false)
    {
        var before = facts.Count;
        string Name(ITypeSymbol symbol) => open?.Display(symbol) ?? CanonicalSymbolName.Display(symbol, compilation);
        var hierarchy = DescribeHierarchy(type);
        var callbacks = new List<BoundCallbackModel>();
        var syncCallbacks = new Dictionary<BoundCallbackPhase, BoundSyncHookCallbackEmission>();
        var scopedCallbacks = new Dictionary<BoundCallbackPhase, BoundSyncHookCallbackEmission>();
        var asyncCallbacks = new Dictionary<BoundCallbackPhase, BoundAsyncHookCallbackEmission>();
        BoundWrapperEmission? wrapperEmission = null;
        BoundStreamHookEmission? streamEmission = null;
        BoundStreamStageItems? streamItems = null;
        var capabilityCalls = new Dictionary<BoundCallbackPhase, CapabilityCallbackEmission>();
        CapabilityCallbackEmission? Capability(BoundCallbackPhase phase) => capabilityCalls.TryGetValue(phase, out var value) ? value : null;
        var compilerOwned = false;
        void Reject(string key, string message) => facts.Add(new(key, anchor, message));
        if (type.TypeKind == TypeKind.Error)
        {
            compilerOwned = true;
            return Value();
        }
        if (!compilation.IsSymbolAccessibleWithin(type, compilation.Assembly))
            Reject("InaccessibleStage", $"Stage {Name(type)} is inaccessible to generated code.");
        if (type.IsRefLikeType)
            Reject("RefLikeCapture", $"Stage {Name(type)} cannot be held as a borrowed DI service.");
        if (type.IsAbstract || type.IsStatic || type.IsUnboundGenericType || HasOpenShape(type) && (open == null || !open.Contains(type)) ||
            type.TypeKind is not (TypeKind.Class or TypeKind.Struct))
            Reject("UnsupportedGenerationShape", $"Stage {Name(type)} requires a closed, concrete class or struct; open binding is not connected yet.");
        if (facts.Count != before)
            return Value();
        var assembly = compilation.GetTypeByMetadataName(ContractNames.Handler)?.ContainingAssembly;
        var stream = family == BoundDispatchFamily.Stream;
        var sync = family is BoundDispatchFamily.SendSync or BoundDispatchFamily.SendAllSync;
        var matches = new List<(INamedTypeSymbol Contract, Role Role)>();
        foreach (var role in Roles.Where(r => r.Stream == stream && r.Wrapper == (kind == FusionStageKind.Wrapper)))
        {
            var definition = assembly?.GetTypeByMetadataName("Zendiator." + role.Name);
            foreach (var contract in type.AllInterfaces.Where(i => Same(i.OriginalDefinition, definition)))
                matches.Add((contract, role));
        }
        if (matches.Count == 0)
            Reject("StageContractMismatch", $"Stage {Name(type)} has no dedicated {kind} contract for {family}.");
        var relevant = matches.Where(m => RegistrationIdentity(m.Contract.TypeArguments[0], compilation) == RegistrationIdentity(request, compilation)).ToArray();
        if (matches.Count != 0 && relevant.Length == 0)
            Reject("StageTypeMismatch", $"Stage {Name(type)} has no contract for request {Name(request)}.");
        if (kind == FusionStageKind.Wrapper && relevant.Length != 0)
        {
            // Hook completion and wrapper continuation are independent contracts.
            if (relevant.Select(m => m.Role.Completion).Distinct().Count() > 1)
            {
                Reject("AmbiguousStageContract", $"Wrapper {Name(type)} has synchronous and asynchronous contracts for the same phase. Declare separate stage types or an explicit adaptor.");
                return Value();
            }
            var compatible = relevant.Where(m => ContinuationMatches(m.Contract, stream, sync, assembly, compilation)).ToArray();
            if (compatible.Length == 0)
            {
                Reject("StageContractMismatch", $"Wrapper {Name(type)} requires " +
                    (stream ? "IStreamUpstream" : sync ? "ISendContinuation" : "IAsyncSendContinuation") +
                    $" for {family}; the declared wrapper continuation is incompatible.");
                return Value();
            }
            relevant = compatible;
        }
        foreach (var (contract, role) in relevant)
        {
            if ((role.ResponseOnly && resultKind == FusionResultKind.Void) || (role.VoidOnly && resultKind != FusionResultKind.Void))
                Reject("CapabilityResultShapeMismatch", $"Stage {Name(type)} has a capability for a different response/void shape.");
            else if (contract.TypeArguments.Length >= 2 && (result == null || (typedStream && role.Stream
                ? !SymbolEqualityComparer.IncludeNullability.Equals(contract.TypeArguments[1], result)
                : RegistrationIdentity(contract.TypeArguments[1], compilation) != RegistrationIdentity(result, compilation))))
                Reject("StageTypeMismatch", $"Stage {Name(type)} has a response/item type different from this route.");
            if (role.Stream && contract.TypeArguments.Length == 3 && !typedStream)
                Reject("UnsupportedGenerationShape", "A type-changing Stream map requires StreamPipeline<TRequest,TFinal>.");
            var output = role.Stream && contract.TypeArguments.Length == 3 ? contract.TypeArguments[2] : result;
            if (typedStream && role.Stream && result != null && output != null)
            {
                if (output.IsRefLikeType || HasOpenShape(output) && (open == null || !open.Contains(output)) || !(open?.Public(output) ?? Public(output)))
                    Reject("UnsupportedGenerationShape", "Stream stage output requires a public, ownable item type with proved arguments.");
                streamItems = new(new(Identity(result), Name(result), result.IsValueType), new(Identity(output), Name(output), output.IsValueType));
            }
            if (sync && !role.Wrapper && role.Completion == BoundCompletionKind.ValueTask)
                Reject("AsyncHookOnSyncRoute", $"Stage {Name(type)} supplies an asynchronous callback on {family}; blocking conversion is unsupported.");
            var members = contract.GetMembers().OfType<IMethodSymbol>().ToArray();
            var implementation = members.Length == 1 ? type.FindImplementationForInterfaceMember(members[0]) as IMethodSymbol : null;
            if (implementation == null)
            {
                compilerOwned = true;
                continue;
            }
            if (role.Stream && role.Wrapper && !CanonicalStreamContractBinding.Matches(contract, implementation, compilation))
            {
                Reject("StageContractMismatch", $"Middleware {Name(type)} requires OpenAsync(request, IStreamUpstream<TRequest,TItem>, token) returning ValueTask<IAsyncEnumerator<TItem>>.");
                continue;
            }
            if (role.ScopedObserver)
            {
                if (!implementation.ReturnsVoid || implementation.Parameters.Length == 0 || implementation.Parameters[0].ScopedKind != ScopedKind.ScopedValue)
                    compilerOwned = true;
                scopedCallbacks[role.Phase] = new(Name(contract), members[0].Name);
            }
            if (role.Wrapper)
                wrapperEmission = new(Name(type), Name(contract), members[0].Name, role.Completion);
            if (role.Stream && !role.Wrapper && (role.Completion is BoundCompletionKind.Synchronous or BoundCompletionKind.ValueTask))
                streamEmission = new(Name(type), Name(contract), members[0].Name, role.Phase, type.IsValueType, role.Completion)
                {
                    InputItemName = typedStream ? streamItems?.Input.Name : null,
                    OutputItemName = typedStream ? streamItems?.Output.Name : null
                };
            var callTarget = family == BoundDispatchFamily.SendAsync && kind == FusionStageKind.Hook &&
                (role.Completion == BoundCompletionKind.ValueTask ||
                    role.Completion == BoundCompletionKind.Synchronous && role.Phase == BoundCallbackPhase.Before)
                ? CanonicalAsyncCallBinding.Bind(type, members[0], implementation, compilation, Name) : null;
            if (!role.Wrapper && !role.Stream && !role.ScopedObserver)
                capabilityCalls[role.Phase] = new(Name(contract), members[0].Name, role.Completion)
                {
                    CallTarget = callTarget
                };
            if (role.Completion == BoundCompletionKind.Synchronous && !role.ScopedObserver)
                syncCallbacks[role.Phase] = new(Name(contract), members[0].Name);
            else if (role.Completion == BoundCompletionKind.ValueTask)
                asyncCallbacks[role.Phase] = new(Name(contract), members[0].Name)
                {
                    CallTarget = callTarget
                };
            callbacks.Add(new(role.Phase, role.Completion, Identity(contract),
                implementation.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) + "/" + implementation.DeclaredAccessibility +
                "/explicit=" + !implementation.ExplicitInterfaceImplementations.IsEmpty + "/virtual=" + implementation.IsVirtual +
                "/override=" + implementation.IsOverride + "/abstract=" + implementation.IsAbstract,
                new(implementation.Parameters.Select(p => new BoundParameterModel(Identity(p.Type), p.RefKind.ToString(), p.ScopedKind.ToString()))),
                Identity(implementation.ReturnType), Constraints(implementation.TypeParameters), role.Completion == BoundCompletionKind.ValueTask));
        }
        if (callbacks.GroupBy(c => c.Phase).Any(g => g.Count() > 1) ||
            (stream && callbacks.Any(c => c.Phase == BoundCallbackPhase.Filter) && callbacks.Any(c => c.Phase == BoundCallbackPhase.Map)))
            Reject("AmbiguousStageContract", $"Stage {Name(type)} has multiple contracts for one phase, or both Stream Filter and Map. Declare separate stage types.");
        return Value();

        BoundStageModel Value() => new(anchor, Identity(type), Name(type), kind, hierarchy,
            new(callbacks.OrderBy(c => c.Phase).ThenBy(c => c.Interface, StringComparer.Ordinal)), FusionOwnership.BorrowedService, compilerOwned)
        {
            ReceiverIsGeneric = type.IsGenericType,
            ReceiverIsPublic = open?.Public(type) ?? Public(type),
            ScopedObserver = facts.Count == before && !compilerOwned && callbacks.Count == 4 && scopedCallbacks.Count == 4
                ? new(Name(type), scopedCallbacks[BoundCallbackPhase.ObserveBefore], scopedCallbacks[BoundCallbackPhase.ObserveSuccess],
                    scopedCallbacks[BoundCallbackPhase.ObserveException], scopedCallbacks[BoundCallbackPhase.ObserveFinally])
                {
                    ObserverIsValueType = type.IsValueType
                } : null,
            StreamItems = facts.Count == before && !compilerOwned && typedStream && callbacks.Count == 1 ? streamItems : null,
            StreamEmission = facts.Count == before && !compilerOwned && family == BoundDispatchFamily.Stream &&
                kind == FusionStageKind.Hook && callbacks.Count == 1 ? streamEmission : null,
            StreamWrapperEmission = facts.Count == before && !compilerOwned && family == BoundDispatchFamily.Stream &&
                kind == FusionStageKind.Wrapper && callbacks.Count == 1 && wrapperEmission != null
                    ? wrapperEmission with
                    {
                        WrapperIsValueType = type.IsValueType
                    } : null,
            SyncEmission = facts.Count == before && !compilerOwned && (family is BoundDispatchFamily.SendSync or BoundDispatchFamily.SendAllSync) &&
                kind == FusionStageKind.Hook && type.TypeKind is (TypeKind.Class or TypeKind.Struct) && compilation.IsSymbolAccessibleWithin(type, compilation.Assembly) &&
                callbacks.Count == 4 && syncCallbacks.Count == 4 &&
                syncCallbacks.ContainsKey(BoundCallbackPhase.Before) && syncCallbacks.ContainsKey(BoundCallbackPhase.Success) &&
                syncCallbacks.ContainsKey(BoundCallbackPhase.Exception) && syncCallbacks.ContainsKey(BoundCallbackPhase.Finally)
                    ? new(Name(type), syncCallbacks[BoundCallbackPhase.Before], syncCallbacks[BoundCallbackPhase.Success],
                        syncCallbacks[BoundCallbackPhase.Exception], syncCallbacks[BoundCallbackPhase.Finally])
                    {
                        HookIsValueType = type.IsValueType
                    } : null,
            AsyncEmission = facts.Count == before && !compilerOwned && (family is BoundDispatchFamily.SendAsync or BoundDispatchFamily.SendAllAsync) &&
                kind == FusionStageKind.Hook && type.TypeKind is (TypeKind.Class or TypeKind.Struct) && compilation.IsSymbolAccessibleWithin(type, compilation.Assembly) &&
                callbacks.Count == 4 && asyncCallbacks.Count == 4 &&
                asyncCallbacks.ContainsKey(BoundCallbackPhase.Before) && asyncCallbacks.ContainsKey(BoundCallbackPhase.Success) &&
                asyncCallbacks.ContainsKey(BoundCallbackPhase.Exception) && asyncCallbacks.ContainsKey(BoundCallbackPhase.Finally)
                    ? new(Name(type), asyncCallbacks[BoundCallbackPhase.Before], asyncCallbacks[BoundCallbackPhase.Success],
                        asyncCallbacks[BoundCallbackPhase.Exception], asyncCallbacks[BoundCallbackPhase.Finally])
                    {
                        HookIsValueType = type.IsValueType
                    } : null,
            WrapperEmission = facts.Count == before && !compilerOwned && kind == FusionStageKind.Wrapper &&
                (family is BoundDispatchFamily.SendSync or BoundDispatchFamily.SendAsync or BoundDispatchFamily.SendAllSync or BoundDispatchFamily.SendAllAsync) && type.TypeKind is (TypeKind.Class or TypeKind.Struct) && compilation.IsSymbolAccessibleWithin(type, compilation.Assembly) &&
                callbacks.Count == 1
                    ? wrapperEmission! with
                    {
                        WrapperIsValueType = type.IsValueType
                    } : null,
            Capabilities = facts.Count == before && !compilerOwned && kind == FusionStageKind.Hook &&
                (family is BoundDispatchFamily.SendSync or BoundDispatchFamily.SendAsync or BoundDispatchFamily.SendAllSync or BoundDispatchFamily.SendAllAsync) && type.TypeKind is (TypeKind.Class or TypeKind.Struct) && compilation.IsSymbolAccessibleWithin(type, compilation.Assembly) &&
                capabilityCalls.Count > 0 && capabilityCalls.Count <= 4
                    ? new(Name(type), Capability(BoundCallbackPhase.Before), Capability(BoundCallbackPhase.Success), Capability(BoundCallbackPhase.Exception), Capability(BoundCallbackPhase.Finally))
                    {
                        HookIsValueType = type.IsValueType
                    } : null
        };
    }

    internal static ITypeSymbol? StreamOutput(INamedTypeSymbol type, BoundStageModel stage)
    {
        if (stage.Callbacks.Count != 1 || stage.StreamItems == null)
            return null;
        var contract = type.AllInterfaces.SingleOrDefault(i => Identity(i) == stage.Callbacks[0].Interface);
        return contract == null ? null : contract.TypeArguments.Length == 3 ? contract.TypeArguments[2] : contract.TypeArguments[1];
    }

    internal static EquatableArray<string> DescribeHierarchy(INamedTypeSymbol type)
    {
        var hierarchy = new List<string>();
        for (var current = type; current != null; current = current.BaseType)
        {
            hierarchy.Add(Identity(current) + "/" + current.TypeKind + "/" + current.DeclaredAccessibility +
                "/abstract=" + current.IsAbstract + "/ref=" + current.IsRefLikeType +
                "/static=" + current.IsStatic);
            for (var owner = current; owner != null; owner = owner.ContainingType)
                hierarchy.Add(Identity(owner) + "/constraints=" + Constraints(owner.OriginalDefinition.TypeParameters));
        }
        return new(hierarchy);
    }

    private static bool ContinuationMatches(INamedTypeSymbol contract, bool stream, bool sync,
        IAssemblySymbol? assembly, Compilation compilation)
    {
        if (stream)
            return CanonicalStreamContractBinding.IsMiddleware(contract, compilation);
        var members = contract.GetMembers().OfType<IMethodSymbol>().ToArray();
        if (members.Length != 1 || members[0].TypeParameters.Length != 1)
            return false;
        var name = sync ? "ISendContinuation" : "IAsyncSendContinuation";
        var definition = assembly?.GetTypeByMetadataName("Zendiator." + name + "`" + contract.TypeArguments.Length);
        var constraints = members[0].TypeParameters[0].ConstraintTypes.OfType<INamedTypeSymbol>()
            .Where(c => Same(c.OriginalDefinition, definition)).ToArray();
        return constraints.Length == 1 && constraints[0].TypeArguments.Length == contract.TypeArguments.Length &&
            constraints[0].TypeArguments.Select(t => RegistrationIdentity(t, compilation))
                .SequenceEqual(contract.TypeArguments.Select(t => RegistrationIdentity(t, compilation)));
    }
}
