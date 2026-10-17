using Microsoft.CodeAnalysis;

namespace Zendiator.SourceGenerator;

internal static class GeneratorDiagnostics
{
    internal static readonly DiagnosticDescriptor StreamValidator = Rule("ZEN0024", "Invalid stream validator");
    internal static readonly DiagnosticDescriptor UnsupportedGenerationShape = Rule("ZEN0035", "Unsupported generation shape",
        helpLinkUri: "https://github.com/Htkym/zendiator/blob/feature/0.5.0/docs/diagnostics/ZEN0035.md");
    internal static readonly DiagnosticDescriptor StageContractMismatch = Rule("ZEN0025", "Stage contract mismatch",
        helpLinkUri: "https://github.com/Htkym/zendiator/blob/feature/0.5.0/docs/diagnostics/ZEN0025.md");
    internal static readonly DiagnosticDescriptor StageTypeMismatch = Rule("ZEN0026", "Stage type mismatch",
        helpLinkUri: "https://github.com/Htkym/zendiator/blob/feature/0.5.0/docs/diagnostics/configuration.md#zen0026-stage-type-mismatch");
    internal static readonly DiagnosticDescriptor AmbiguousStageContract = Rule("ZEN0027", "Ambiguous stage contract");
    internal static readonly DiagnosticDescriptor AmbiguousPipelineOrder = Rule("ZEN0028", "Ambiguous pipeline order");
    internal static readonly DiagnosticDescriptor UnsupportedDispatchFamily = Rule("ZEN0029", "Unsupported dispatch family");
    internal static readonly DiagnosticDescriptor AsyncHookOnSyncRoute = Rule("ZEN0030", "Async hook on synchronous route");
    internal static readonly DiagnosticDescriptor CapabilityResultShapeMismatch = Rule("ZEN0031", "Capability result shape mismatch");
    internal static readonly DiagnosticDescriptor InaccessibleStage = Rule("ZEN0033", "Inaccessible stage");
    internal static readonly DiagnosticDescriptor RefLikeCapture = Rule("ZEN0034", "Ref-like capture");
    internal static readonly DiagnosticDescriptor IncompleteExecutionContract = Rule("ZEN0037", "Incomplete execution contract",
        helpLinkUri: "https://github.com/Htkym/zendiator/blob/feature/0.5.0/docs/diagnostics/configuration.md#zen0037-incomplete-execution-contract");

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
        Rule("ZEN0020", "Interception connection failure"), StreamValidator
    };

    internal static DiagnosticDescriptor Rule(string id, string title, string? helpLinkUri = null) =>
        new(id, title, "{0}", "Zendiator", DiagnosticSeverity.Error, true, helpLinkUri: helpLinkUri);
}
