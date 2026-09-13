using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Zendiator.SourceGenerator;

internal sealed class MultiRoute(INamedTypeSymbol request, ITypeSymbol response, bool isVoid, bool isOpen, bool isSync = false)
{
    public INamedTypeSymbol Request { get; } = request;
    public ITypeSymbol Response { get; } = response;
    public bool IsVoid { get; } = isVoid;
    public bool IsOpen { get; } = isOpen;
    public bool IsSync { get; } = isSync;
    public List<MultiBranch> Branches { get; } = new();
    public List<string> OpenTypeParams { get; } = new();
    public List<string> MethodConstraints { get; } = new();
    public string RequestDisplay { get; set; } = "";
    public string ResponseDisplay { get; set; } = "";
}
