using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Zendiator.SourceGenerator;

internal sealed partial class GenerationAnalysis
{
    // This index belongs to one analysis. Symbols must not survive into incremental models.
    private readonly Dictionary<ITypeSymbol, Dictionary<INamedTypeSymbol, INamedTypeSymbol[]>> _contracts = new(SymbolEqualityComparer.IncludeNullability);

    private INamedTypeSymbol[] GetContracts(ITypeSymbol type, INamedTypeSymbol definition)
    {
        if (!_contracts.TryGetValue(type, out var byDefinition))
        {
            byDefinition = new Dictionary<INamedTypeSymbol, INamedTypeSymbol[]>(SymbolEqualityComparer.Default);
            foreach (var group in type.AllInterfaces.GroupBy(i => i.OriginalDefinition, SymbolEqualityComparer.Default))
                byDefinition.Add((INamedTypeSymbol)group.Key!, group.ToArray());
            _contracts.Add(type, byDefinition);
        }
        return byDefinition.TryGetValue(definition, out var contracts) ? contracts : Array.Empty<INamedTypeSymbol>();
    }
}
