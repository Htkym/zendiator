using Microsoft.CodeAnalysis;

namespace Zendiator.SourceGenerator;

internal sealed class OpenBinding(
    INamedTypeSymbol definition,
    INamedTypeSymbol requestDefinition,
    ITypeSymbol responsePattern,
    int[] patternMap,
    ITypeSymbol[] patternFixed,
    int[] handlerToDef,
    int[] handlerPosition,
    bool isVoid)
{
    public INamedTypeSymbol Definition { get; } = definition;
    public INamedTypeSymbol RequestDefinition { get; } = requestDefinition;
    public ITypeSymbol ResponsePattern { get; } = responsePattern;
    public int[] PatternMap { get; } = patternMap;
    public ITypeSymbol[] PatternFixed { get; } = patternFixed;
    public int[] HandlerToDef { get; } = handlerToDef;
    public int[] HandlerPosition { get; } = handlerPosition;
    public bool IsVoid { get; } = isVoid;
    public MergedParam[] Merged { get; set; } = [];
}
