using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using static Zendiator.SourceGenerator.SymbolUtilities;

namespace Zendiator.SourceGenerator;

internal sealed record CatalogHandlerContract(BoundDispatchFamily Family,ITypeSymbol Request,ITypeSymbol? Response);
internal sealed record CatalogHandlerFact(INamedTypeSymbol Type,CatalogHandlerContract[] Contracts,string[] Errors);

// Exact H01 v1 signature encoding; no method bodies, tokens, MVIDs or syntax positions.
internal static class CatalogSignatureV1
{
    internal const string Algorithm="zendiator.handler-contract/1";
    internal static string DefinitionId(INamedTypeSymbol type)
    {
        var names=new Stack<string>();
        for(var current=type.OriginalDefinition;current!=null;current=current.ContainingType)names.Push(current.MetadataName);
        return type.ContainingAssembly.Identity+"|"+(type.ContainingNamespace.IsGlobalNamespace?"":type.ContainingNamespace.ToDisplayString()+".")+string.Join("+",names);
    }
    internal static string TypeId(ITypeSymbol type,bool annotations=true)=>type switch
    {
        IDynamicTypeSymbol=>Json(new[]{"object",annotations&&type.NullableAnnotation==NullableAnnotation.Annotated?"2":"0"}),
        INamedTypeSymbol{SpecialType:SpecialType.System_Object}=>Json(new[]{"object",annotations&&type.NullableAnnotation==NullableAnnotation.Annotated?"2":"0"}),
        ITypeParameterSymbol p=>Json(new[]{"parameter",DefinitionId((INamedTypeSymbol)p.ContainingSymbol),p.Ordinal.ToString(CultureInfo.InvariantCulture),(annotations?(int)p.NullableAnnotation:0).ToString(CultureInfo.InvariantCulture)}),
        IArrayTypeSymbol a=>Json(new[]{"array",a.Rank.ToString(CultureInfo.InvariantCulture),TypeId(a.ElementType,annotations),(annotations?(int)a.NullableAnnotation:0).ToString(CultureInfo.InvariantCulture)}),
        INamedTypeSymbol n=>Json(new[]{"named",DefinitionId(n),n.ContainingType is {} outer?TypeId(outer,annotations):"",(annotations?(int)n.NullableAnnotation:0).ToString(CultureInfo.InvariantCulture)}.Concat(n.TypeArguments.Select(t=>TypeId(t,annotations)))),
        _=>throw new NotSupportedException("Unsupported catalog signature type: "+type.TypeKind)
    };
    private static IEnumerable<ITypeParameterSymbol> Parameters(INamedTypeSymbol type)
    {
        var owners=new Stack<INamedTypeSymbol>();
        for(var current=type;current!=null;current=current.ContainingType)owners.Push(current);
        return owners.SelectMany(o=>o.TypeParameters.OrderBy(p=>p.Ordinal));
    }
    private static bool Contains(ITypeSymbol type,ITypeParameterSymbol parameter)=>Same(type,parameter)||
        type is INamedTypeSymbol n&&(n.TypeArguments.Any(t=>Contains(t,parameter))||n.ContainingType is {} outer&&Contains(outer,parameter))||
        type is IArrayTypeSymbol a&&Contains(a.ElementType,parameter);
    internal static CatalogHandlerFact Inspect(Compilation compilation,INamedTypeSymbol type)
    {
        var errors=new List<string>();var contracts=new List<CatalogHandlerContract>();
        if(type.IsAbstract||type.IsStatic||type.TypeKind is not (TypeKind.Class or TypeKind.Struct))errors.Add("HandlerNotConcrete");
        if(type.IsRefLikeType)errors.Add("RefLikeHandlerUnsupported");
        if(!compilation.IsSymbolAccessibleWithin(type,compilation.Assembly))errors.Add("HandlerInaccessible");
        if(!IsPublicDefinition(type))errors.Add("CatalogHandlerNotPublic");
        var assembly=compilation.GetTypeByMetadataName(ContractNames.Handler)?.ContainingAssembly;
        var definitions=new[]{(ContractNames.Handler,BoundDispatchFamily.SendAsync),(ContractNames.VoidHandler,BoundDispatchFamily.SendAsync),
            (ContractNames.SyncHandler,BoundDispatchFamily.SendSync),(ContractNames.SyncVoidHandler,BoundDispatchFamily.SendSync),
            (ContractNames.StreamHandler,BoundDispatchFamily.Stream),(ContractNames.NotificationHandler,BoundDispatchFamily.Notification)};
        foreach(var iface in type.AllInterfaces)foreach(var (name,family) in definitions)
            if(Same(iface.OriginalDefinition,assembly?.GetTypeByMetadataName(name)))contracts.Add(new(family,iface.TypeArguments[0],iface.TypeArguments.Length==2?iface.TypeArguments[1]:null));
        if(contracts.Count==0)errors.Add("MissingHandlerContract");
        if(!SupportedSignature(type)||Parameters(type).SelectMany(p=>p.ConstraintTypes).Any(t=>!SupportedSignature(t))||
            contracts.Any(c=>!SupportedSignature(c.Request)||c.Response!=null&&!SupportedSignature(c.Response)))
            errors.Add("UnsupportedCatalogSignature");
        foreach(var contract in contracts)
        {
            if(Parameters(type).Any(p=>!Contains(contract.Request,p)))errors.Add("UninferableHandlerParameter");
            if(contracts.Count(c=>c.Family==contract.Family&&Same(c.Request,contract.Request))>1)errors.Add("AmbiguousHandlerContract");
        }
        return new(type,contracts.ToArray(),errors.Distinct().ToArray());
    }
    // An incomplete/invalid source contract may carry error, pointer or function-pointer symbols.
    // The compiler owns those errors; never send an unencodable shape to the fingerprint writer.
    private static bool SupportedSignature(ITypeSymbol type)=>type switch
    {
        IDynamicTypeSymbol=>true,
        ITypeParameterSymbol p=>p.ContainingSymbol is INamedTypeSymbol,
        IArrayTypeSymbol a=>SupportedSignature(a.ElementType),
        INamedTypeSymbol n=>n.TypeKind!=TypeKind.Error&&n.TypeArguments.All(SupportedSignature)&&
            (n.ContainingType==null||SupportedSignature(n.ContainingType)),
        _=>false
    };
    internal static string Fingerprint(CatalogHandlerFact fact)
    {
        var rows=new List<string>{Algorithm,TypeId(fact.Type),fact.Type.DeclaredAccessibility.ToString(),fact.Type.TypeKind.ToString(),
            fact.Type.IsAbstract.ToString(),fact.Type.IsStatic.ToString(),fact.Type.IsRefLikeType.ToString()};
        foreach(var p in Parameters(fact.Type))
            rows.Add(Json(new object[]{DefinitionId((INamedTypeSymbol)p.ContainingSymbol),p.Ordinal,p.Variance.ToString(),p.HasReferenceTypeConstraint,
                p.ReferenceTypeConstraintNullableAnnotation.ToString(),p.HasValueTypeConstraint,p.HasUnmanagedTypeConstraint,p.HasNotNullConstraint,
                p.HasConstructorConstraint,p.AllowsRefLikeType,p.ConstraintTypes.Select(t=>TypeId(t)).OrderBy(t=>t,StringComparer.Ordinal).ToArray()}));
        rows.AddRange(fact.Contracts.Select(c=>Json(new[]{c.Family.ToString(),TypeId(c.Request),c.Response==null?"void":TypeId(c.Response)})).OrderBy(c=>c,StringComparer.Ordinal));
        using var hash=SHA256.Create();
        return string.Concat(hash.ComputeHash(Encoding.UTF8.GetBytes(Json(rows))).Select(b=>b.ToString("X2",CultureInfo.InvariantCulture)));
    }
    // H01 uses System.Text.Json's default BasicLatin/HTML-safe JSON encoding.
    // Keep the netstandard analyzer dependency-free and validate against that reference in tests.
    internal static string Json(object? value)
    {
        if(value==null)return "null";
        if(value is bool flag)return flag?"true":"false";
        if(value is int number)return number.ToString(CultureInfo.InvariantCulture);
        if(value is string text)
        {
            var b=new StringBuilder("\"");
            for(var i=0;i<text.Length;i++)
            {
                var c=text[i];
                switch(c)
                {
                    case '\\':b.Append("\\\\");continue;
                    case '\b':b.Append("\\b");continue;
                    case '\t':b.Append("\\t");continue;
                    case '\n':b.Append("\\n");continue;
                    case '\f':b.Append("\\f");continue;
                    case '\r':b.Append("\\r");continue;
                }
                if(c>=' '&&c<='\x7e'&&c is not ('"' or '\'' or '&' or '+' or '<' or '>' or '\x60'))b.Append(c);
                else
                {
                    if(char.IsHighSurrogate(c))
                    {
                        if(i+1<text.Length&&char.IsLowSurrogate(text[i+1])){b.Append("\\u").Append(((int)c).ToString("X4",CultureInfo.InvariantCulture));c=text[++i];}
                        else c='\ufffd';
                    }
                    else if(char.IsLowSurrogate(c))c='\ufffd';
                    b.Append("\\u").Append(((int)c).ToString("X4",CultureInfo.InvariantCulture));
                }
            }
            return b.Append('"').ToString();
        }
        if(value is IEnumerable values)return "["+string.Join(",",values.Cast<object?>().Select(Json))+"]";
        throw new NotSupportedException("Unsupported catalog JSON value.");
    }
}
