using System.Linq;
using Microsoft.CodeAnalysis;
using static Zendiator.SourceGenerator.SymbolUtilities;

namespace Zendiator.SourceGenerator;

// Interfaces confirm a supplied receiver; they never discover unmarked handlers.
internal static class CanonicalStreamContractBinding
{
    internal static bool IsSourceHandler(INamedTypeSymbol contract, Compilation compilation) =>
        Same(contract.OriginalDefinition, Definition(compilation, ContractNames.StreamHandler)) || IsEnumerableSource(contract, compilation);

    internal static bool IsEnumerableSource(INamedTypeSymbol contract, Compilation compilation) =>
        Same(contract.OriginalDefinition, Definition(compilation, ContractNames.EnumerableStreamHandler));

    internal static bool IsMiddleware(INamedTypeSymbol contract, Compilation compilation) =>
        Same(contract.OriginalDefinition, Definition(compilation, ContractNames.StreamMiddleware));

    internal static bool Matches(INamedTypeSymbol contract, IMethodSymbol? implementation, Compilation compilation)
    {
        var middleware = IsMiddleware(contract, compilation);
        var enumerable = IsEnumerableSource(contract, compilation);
        if ((!middleware && !IsSourceHandler(contract, compilation)) ||
            contract.TypeArguments.Length != (middleware || enumerable ? 2 : 3) || implementation == null || implementation.IsAbstract)
            return false;
        var cursor = middleware
            ? compilation.GetTypeByMetadataName("System.Collections.Generic.IAsyncEnumerator`1")?.Construct(contract.TypeArguments[1])
            : enumerable ? null : contract.TypeArguments[2];
        var valueTask = compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask`1");
        var result = enumerable
            ? compilation.GetTypeByMetadataName("System.Collections.Generic.IAsyncEnumerable`1")?.Construct(contract.TypeArguments[1])
            : cursor == null || valueTask == null ? null : valueTask.Construct(cursor);
        if (result == null) return false;
        var members = contract.GetMembers().OfType<IMethodSymbol>().ToArray();
        return members.Length == 1 && Method(members[0]) && Method(implementation);

        bool Method(IMethodSymbol method)
        {
            if ((method.Name != (enumerable ? "Handle" : "OpenAsync") && method.ExplicitInterfaceImplementations.IsEmpty) || method.IsStatic ||
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
        var enumerator = compilation.GetTypeByMetadataName("System.Collections.Generic.IAsyncEnumerator`1");
        if (IsEnumerableSource(contract, compilation))
        {
            cursor = enumerator?.Construct(contract.TypeArguments[1]);
            return cursor != null;
        }
        cursor = contract.TypeArguments.Length == 3 ? contract.TypeArguments[2] as INamedTypeSymbol : null;
        return cursor is { TypeKind: TypeKind.Class or TypeKind.Struct, IsRefLikeType: false, IsUnboundGenericType: false } &&
            enumerator != null && compilation.IsSymbolAccessibleWithin(cursor, compilation.Assembly) &&
            compilation.ClassifyCommonConversion(cursor, enumerator.Construct(contract.TypeArguments[1])).IsImplicit;
    }

    private static INamedTypeSymbol? Definition(Compilation compilation, string name) =>
        compilation.GetTypeByMetadataName(ContractNames.Handler)?.ContainingAssembly.GetTypeByMetadataName(name);
}
