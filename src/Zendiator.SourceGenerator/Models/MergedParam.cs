using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Zendiator.SourceGenerator;

internal sealed class MergedParam(string name)
{
    public string Name { get; } = name;
    public bool Class;
    public bool ClassNullable = true;
    public bool Struct;
    public bool Unmanaged;
    public bool NotNull;
    public bool New;
    public bool HasPrimary;
    public bool AllowsRefLike;
    public List<ITypeSymbol> Types = new();
}
