using System.Linq;
using Microsoft.CodeAnalysis;
using static Zendiator.SourceGenerator.SymbolUtilities;

namespace Zendiator.SourceGenerator;

// Interfaces confirm a supplied receiver; they never discover unmarked handlers.
internal static class CanonicalStreamContractBinding
{
    internal static bool IsSourceHandler(INamedTypeSymbol contract, Compilation compilation) =>
        Same(contract.OriginalDefinition, Definition(compilation, ContractNames.StreamHandler));

    internal static bool IsMiddleware(INamedTypeSymbol contract, Compilation compilation) =>
        Same(contract.OriginalDefinition, Definition(compilation, ContractNames.StreamMiddleware));

    internal static bool Matches(INamedTypeSymbol contract, IMethodSymbol? implementation, Compilation compilation)
    {
        var middleware = IsMiddleware(contract, compilation);
        if ((!middleware && !IsSourceHandler(contract, compilation)) ||
            contract.TypeArguments.Length != (middleware ? 2 : 3) || implementation == null || implementation.IsAbstract)
            return false;
        var cursor = middleware
            ? compilation.GetTypeByMetadataName("System.Collections.Generic.IAsyncEnumerator`1")?.Construct(contract.TypeArguments[1])
            : contract.TypeArguments[2];
        var valueTask = compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask`1");
        if (cursor == null || valueTask == null) return false;
        var result = valueTask.Construct(cursor);
        var members = contract.GetMembers().OfType<IMethodSymbol>().ToArray();
        return members.Length == 1 && Method(members[0]) && Method(implementation);

        bool Method(IMethodSymbol method)
        {
            if ((method.Name != "OpenAsync" && method.ExplicitInterfaceImplementations.IsEmpty) || method.IsStatic ||
                method.Arity != 0 || method.Parameters.Length != (middleware ? 3 : 2) || method.ReturnsByRef || method.ReturnsByRefReadonly ||
                !Same(method.ReturnType, result) || method.Parameters.Any(p => p.RefKind != RefKind.None || p.ScopedKind != ScopedKind.None) ||
                !Same(method.Parameters[0].Type, contract.TypeArguments[0]) ||
                !Same(method.Parameters[method.Parameters.Length - 1].Type, compilation.GetTypeByMetadataName("System.Threading.CancellationToken"))) return false;
            var upstream = middleware ? Definition(compilation, "Zendiator.IStreamUpstream`2")?.Construct(contract.TypeArguments.ToArray()) : null;
            return !middleware || upstream != null && Same(method.Parameters[1].Type, upstream);
        }
    }

    internal static bool TryCursor(INamedTypeSymbol contract, Compilation compilation, out INamedTypeSymbol? cursor)
    {
        cursor = contract.TypeArguments.Length == 3 ? contract.TypeArguments[2] as INamedTypeSymbol : null;
        var enumerator = compilation.GetTypeByMetadataName("System.Collections.Generic.IAsyncEnumerator`1");
        return cursor is { TypeKind: TypeKind.Class or TypeKind.Struct, IsRefLikeType: false, IsUnboundGenericType: false } &&
            enumerator != null && compilation.IsSymbolAccessibleWithin(cursor, compilation.Assembly) &&
            compilation.ClassifyCommonConversion(cursor, enumerator.Construct(contract.TypeArguments[1])).IsImplicit;
    }

    private static INamedTypeSymbol? Definition(Compilation compilation, string name) =>
        compilation.GetTypeByMetadataName(ContractNames.Handler)?.ContainingAssembly.GetTypeByMetadataName(name);
}
