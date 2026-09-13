using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Zendiator.SourceGenerator;

internal sealed partial class GenerationAnalysis
{
    private readonly Dictionary<ITypeSymbol, string> _names = new(SymbolEqualityComparer.IncludeNullability);

    private string Name(ITypeSymbol symbol)
    {
        if (!_names.TryGetValue(symbol, out var name))
        {
            name = SymbolUtilities.Name(symbol);
            _names.Add(symbol, name);
        }
        return name;
    }
}
