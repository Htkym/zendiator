using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Zendiator.SourceGenerator;

internal sealed class Route(INamedTypeSymbol request, ITypeSymbol response, INamedTypeSymbol handler, bool isVoid = false, bool isSync = false)
{
    public INamedTypeSymbol Request { get; } = request;
    public ITypeSymbol Response { get; } = response;
    public INamedTypeSymbol Handler { get; } = handler;
    public bool IsVoid { get; } = isVoid;
    public bool IsSync { get; } = isSync;
    public List<INamedTypeSymbol> Behaviors { get; } = new();
    public bool IsOpen { get; set; }
    public List<string> OpenTypeParams { get; } = new();
    public List<string> MethodConstraints { get; } = new();
    public string RequestDisplay { get; set; } = "";
    public string ResponseDisplay { get; set; } = "";
    public string HandlerDisplay { get; set; } = "";
    public string HandlerContractDisplay { get; set; } = "";
    public List<string> BehaviorDisplays { get; } = new();
    public List<string> BehaviorContractDisplays { get; } = new();
}
