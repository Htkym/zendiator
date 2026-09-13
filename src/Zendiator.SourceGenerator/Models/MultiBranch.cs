using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Zendiator.SourceGenerator;

internal sealed class MultiBranch
{
    public INamedTypeSymbol Handler { get; set; } = null!;
    public int Order;
    public string AssemblyId = "";
    public string FullName = "";
    public bool HandlerIsOpen;
    public string HandlerDisplay = "";
    public string HandlerContractDisplay = "";
    public List<INamedTypeSymbol> Behaviors { get; } = new();
    public List<string> BehaviorDisplays { get; } = new();
    public List<string> BehaviorContractDisplays { get; } = new();
}
