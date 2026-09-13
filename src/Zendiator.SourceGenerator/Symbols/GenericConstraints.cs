using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using static Zendiator.SourceGenerator.SymbolUtilities;

namespace Zendiator.SourceGenerator;

internal static class GenericConstraints
{
    internal static bool SatisfiesConstraints(INamedTypeSymbol type, ITypeSymbol[] args, Compilation compilation)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var parameter = type.TypeParameters[i];
            var arg = args[i];
            var nullableValue = arg is INamedTypeSymbol n && n.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
            if ((parameter.HasReferenceTypeConstraint && !arg.IsReferenceType) ||
                (parameter.HasValueTypeConstraint && (!arg.IsValueType || nullableValue)) ||
                (parameter.HasUnmanagedTypeConstraint && !arg.IsUnmanagedType) ||
                (parameter.HasNotNullConstraint && (arg.NullableAnnotation == NullableAnnotation.Annotated || nullableValue)) ||
                (parameter.HasReferenceTypeConstraint && parameter.ReferenceTypeConstraintNullableAnnotation != NullableAnnotation.Annotated && arg.NullableAnnotation == NullableAnnotation.Annotated))
                return false;
            if (parameter.HasConstructorConstraint && !arg.IsValueType &&
                arg is not ITypeParameterSymbol { HasConstructorConstraint: true } &&
                (arg is not INamedTypeSymbol named || named.IsAbstract ||
                 !named.InstanceConstructors.Any(c => c.DeclaredAccessibility == Accessibility.Public && c.Parameters.Length == 0)))
                return false;
            foreach (var constraint in parameter.ConstraintTypes)
            {
                var resolved = Substitute(constraint, type, args, compilation);
                if (arg is INamedTypeSymbol refArg && refArg.IsRefLikeType)
                {
                    // No boxing conversion exists for ref structs; verify the
                    // interface implementation directly instead.
                    if (resolved is INamedTypeSymbol required &&
                        refArg.AllInterfaces.Any(i => Same(i, required))) continue;
                    return false;
                }
                var conversion = ((CSharpCompilation)compilation).ClassifyConversion(arg, resolved);
                if (!(conversion.IsIdentity || conversion.IsImplicit && (conversion.IsReference || conversion.IsBoxing))) return false;
            }
        }
        return true;
    }

    internal static ITypeSymbol Substitute(ITypeSymbol symbol, INamedTypeSymbol owner, ITypeSymbol[] args, Compilation compilation)
    {
        if (symbol is ITypeParameterSymbol p && Same(p.ContainingSymbol, owner)) return args[p.Ordinal];
        if (symbol is IArrayTypeSymbol array) return compilation.CreateArrayTypeSymbol(Substitute(array.ElementType, owner, args, compilation), array.Rank);
        if (symbol is INamedTypeSymbol named && named.IsGenericType)
            return named.OriginalDefinition.Construct(named.TypeArguments.Select(t => Substitute(t, owner, args, compilation)).ToArray());
        return symbol;
    }

    internal static MergedParam FromTypeParam(ITypeParameterSymbol p)
    {
        var m = new MergedParam(p.Name)
        {
            Class = p.HasReferenceTypeConstraint,
            ClassNullable = p.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated,
            Struct = p.HasValueTypeConstraint,
            Unmanaged = p.HasUnmanagedTypeConstraint,
            NotNull = p.HasNotNullConstraint,
            New = p.HasConstructorConstraint,
            HasPrimary = p.HasReferenceTypeConstraint || p.HasValueTypeConstraint || p.HasUnmanagedTypeConstraint,
            AllowsRefLike = p.AllowsRefLikeType,
        };
        m.Types.AddRange(p.ConstraintTypes);
        return m;
    }

    internal static string PrimaryOf(MergedParam m) => m.Class ? "class" : m.Unmanaged ? "unmanaged" : m.Struct ? "struct" : "";

    internal static bool MergeHandlerParam(MergedParam target, ITypeParameterSymbol source, INamedTypeSymbol handlerDef, ITypeSymbol[] mapArgs, Compilation compilation)
    {
        var sourcePrimary = source.HasReferenceTypeConstraint ? "class" : source.HasUnmanagedTypeConstraint ? "unmanaged" : source.HasValueTypeConstraint ? "struct" : "";
        var targetPrimary = PrimaryOf(target);
        if (sourcePrimary.Length != 0 && targetPrimary.Length != 0)
        {
            var compatible = sourcePrimary == targetPrimary ||
                (sourcePrimary == "unmanaged" && targetPrimary == "struct") ||
                (sourcePrimary == "struct" && targetPrimary == "unmanaged");
            if (!compatible) return false;
            if (targetPrimary == "struct" && sourcePrimary == "unmanaged") target.Unmanaged = true;
        }
        else if (sourcePrimary.Length != 0)
        {
            target.Class = source.HasReferenceTypeConstraint;
            target.Struct = source.HasValueTypeConstraint;
            target.Unmanaged = source.HasUnmanagedTypeConstraint;
            target.HasPrimary = true;
        }
        if (source.HasReferenceTypeConstraint)
            target.ClassNullable = target.ClassNullable && source.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated;
        target.NotNull = target.NotNull || source.HasNotNullConstraint;
        target.New = target.New || source.HasConstructorConstraint;
        foreach (var constraint in source.ConstraintTypes)
        {
            var mapped = Substitute(constraint, handlerDef, mapArgs, compilation);
            if (!target.Types.Any(t => Same(t, mapped))) target.Types.Add(mapped);
        }
        return true;
    }

    internal static string RenderConstraints(MergedParam m)
    {
        var parts = new List<string>();
        if (m.Class) parts.Add(m.ClassNullable ? "class?" : "class");
        else if (m.Unmanaged) parts.Add("unmanaged");
        else if (m.Struct) parts.Add("struct");
        if (m.NotNull && !m.Class && !m.Struct && !m.Unmanaged) parts.Add("notnull");
        foreach (var t in m.Types) parts.Add(Name(t));
        if (m.New && !m.Struct && !m.Unmanaged) parts.Add("new()");
        if (m.AllowsRefLike) parts.Add("allows ref struct");
        if (parts.Count == 0) return "";
        return $" where {m.Name} : {string.Join(", ", parts)}";
    }
}
