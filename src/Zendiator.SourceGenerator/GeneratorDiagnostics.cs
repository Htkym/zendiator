using Microsoft.CodeAnalysis;

namespace Zendiator.SourceGenerator;

internal static class GeneratorDiagnostics
{
    internal static readonly DiagnosticDescriptor[] Rules =
    {
        Rule("ZEN0001", "Missing handler"), Rule("ZEN0002", "Duplicate handler"),
        Rule("ZEN0003", "Unsupported contract"), Rule("ZEN0004", "Invalid pipeline"),
        Rule("ZEN0005", "Invalid mediator declaration"),
        Rule("ZEN0006", "Conflicting generation modes"), Rule("ZEN0007", "Generated name collision"),
        Rule("ZEN0008", "Conflicting request kinds"), Rule("ZEN0009", "Unsupported generic binding"),
        Rule("ZEN0010", "Ambiguous generic binding"), Rule("ZEN0011", "Incompatible constraints"),
        Rule("ZEN0012", "Invalid ref route"), Rule("ZEN0013", "Invalid handler registration"),
        Rule("ZEN0014", "Unsupported notification erasure"), Rule("ZEN0015", "Conflicting configuration source"),
        Rule("ZEN0016", "Conflicting configuration structure"),         Rule("ZEN0017", "Unsupported configuration expression"),
        Rule("ZEN0018", "Invalid configuration value"), Rule("ZEN0019", "Ambiguous registration binding"),
        Rule("ZEN0020", "Interception connection failure")
    };

    internal static DiagnosticDescriptor Rule(string id, string title) =>
        new(id, title, "{0}", "Zendiator", DiagnosticSeverity.Error, true);
}
