using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using static Zendiator.SourceGenerator.SymbolUtilities;
using static Zendiator.SourceGenerator.MarkedHandlerDiscovery;

namespace Zendiator.SourceGenerator;

/// <summary>Binds explicit factory ownership and completion; bodies never grant an ownership transfer.</summary>
internal static class SessionFactoryContractBinding
{
    private static readonly (string Name, FusionOwnership Ownership, BoundCompletionKind Completion)[] Roles =
    {
        ("IOwnedSessionFactory`2", FusionOwnership.OwnedSession, BoundCompletionKind.Synchronous),
        ("IBorrowedSessionFactory`2", FusionOwnership.BorrowedResource, BoundCompletionKind.Synchronous),
        ("IAsyncOwnedSessionFactory`2", FusionOwnership.OwnedSession, BoundCompletionKind.ValueTask),
        ("IAsyncBorrowedSessionFactory`2", FusionOwnership.BorrowedResource, BoundCompletionKind.ValueTask)
    };

    internal static (BoundSessionModel? Session, bool CompilerOwnsError) Bind(INamedTypeSymbol factory,
        INamedTypeSymbol session, INamedTypeSymbol request, BoundDispatchFamily family, string anchor,
        Compilation compilation, List<CompositionDiagnosticFact> facts)
    {
        void Reject(string key, string message) => facts.Add(new(key, anchor, message));
        if (factory.TypeKind == TypeKind.Error) return (null, true);
        if (factory.IsRefLikeType) { Reject("RefLikeCapture", "A session factory cannot be captured as a DI service."); return (null, false); }
        if (factory.IsAbstract || factory.IsStatic || factory.IsUnboundGenericType || HasOpenShape(factory) ||
            factory.TypeKind is not (TypeKind.Class or TypeKind.Struct))
        { Reject("UnsupportedGenerationShape", "A session factory requires a closed, concrete class or struct."); return (null, false); }
        if (!compilation.IsSymbolAccessibleWithin(factory, compilation.Assembly))
        { Reject("InaccessibleStage", "The session factory is inaccessible to generated code."); return (null, false); }
        var assembly = compilation.GetTypeByMetadataName(ContractNames.Handler)?.ContainingAssembly;
        var matches = Roles.SelectMany(role => factory.AllInterfaces
            .Where(i => Same(i.OriginalDefinition, assembly?.GetTypeByMetadataName("Zendiator." + role.Name)))
            .Select(i => (Contract: i, role.Ownership, role.Completion))).ToArray();
        var relevant = matches.Where(m => RegistrationIdentity(m.Contract.TypeArguments[0], compilation) == RegistrationIdentity(request, compilation) &&
            RegistrationIdentity(m.Contract.TypeArguments[1], compilation) == RegistrationIdentity(session, compilation)).ToArray();
        if (relevant.Length == 0)
        {
            Reject(matches.Length == 0 ? "StageContractMismatch" : "StageTypeMismatch",
                $"Factory {Name(factory)} requires one explicit session factory contract for {Name(request)} and {Name(session)}.");
            return (null, false);
        }
        if (relevant.Length != 1)
        { Reject("AmbiguousStageContract", "Declare one factory ownership/completion contract for this request and session; no contract is preferred implicitly."); return (null, false); }
        var selected = relevant[0];
        var sync = family is BoundDispatchFamily.SendSync or BoundDispatchFamily.SendAllSync;
        if (sync && selected.Completion == BoundCompletionKind.ValueTask)
        { Reject("AsyncHookOnSyncRoute", "An asynchronous session factory cannot run on a synchronous route; blocking conversion is unsupported."); return (null, false); }
        var acquire = Callback(factory, selected.Contract, BoundCallbackPhase.SessionAcquire, selected.Completion);
        if (acquire == null) return (null, true);
        BoundCallbackModel? cleanup = null;
        if (selected.Ownership == FusionOwnership.OwnedSession)
        {
            var disposable = compilation.GetTypeByMetadataName(sync ? "System.IDisposable" : "System.IAsyncDisposable");
            var contract = session.AllInterfaces.FirstOrDefault(i => Same(i, disposable));
            if (contract == null)
            { Reject("StageContractMismatch", "An Owned session requires " + (sync ? "IDisposable" : "IAsyncDisposable") + " on this route."); return (null, false); }
            cleanup = Callback(session, contract, BoundCallbackPhase.SessionCleanup,
                sync ? BoundCompletionKind.Synchronous : BoundCompletionKind.ValueTask);
            if (cleanup == null) return (null, true);
        }
        return (new(Identity(factory), Identity(session), acquire, selected.Ownership, cleanup)
            { SuppliesCallbackReceiver = true, FactoryHierarchy = StageContractBinding.DescribeHierarchy(factory) }, false);
    }

    private static BoundCallbackModel? Callback(INamedTypeSymbol type, INamedTypeSymbol contract,
        BoundCallbackPhase phase, BoundCompletionKind completion)
    {
        var member = contract.GetMembers().OfType<IMethodSymbol>().SingleOrDefault();
        var implementation = member == null ? null : type.FindImplementationForInterfaceMember(member) as IMethodSymbol;
        if (implementation == null) return null;
        return new(phase, completion, Identity(contract),
            implementation.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) + "/" + implementation.DeclaredAccessibility +
            "/explicit=" + !implementation.ExplicitInterfaceImplementations.IsEmpty + "/virtual=" + implementation.IsVirtual +
            "/override=" + implementation.IsOverride + "/abstract=" + implementation.IsAbstract,
            new(implementation.Parameters.Select(p => new BoundParameterModel(Identity(p.Type), p.RefKind.ToString(), p.ScopedKind.ToString()))),
            Identity(implementation.ReturnType), Constraints(implementation.TypeParameters), completion == BoundCompletionKind.ValueTask);
    }
}
