namespace Competitive.Perf03.Diagnostics;

internal static class Perf03CauseVariant
{
    internal const string Id = "V0";

    internal static object Metadata => Id switch
    {
        "V0" => Describe("V0", "faithful-control", "consumer-only-probed", false),
        "VEC" => Describe("V0", "remove-explicit-context-capture-run", "explicit-flow-unsupported", false),
        "VEntry" => Describe("V0", "remove-builder-start", "entry-isolation-unsupported", false),
        "VRoot" => Describe("V0", "root-owned-result-source", "consumer-only-probed", true),
        "VWord" => Describe("VRoot", "reduce-root-state-atomics", "consumer-only-probed", true),
        _ => throw new InvalidOperationException("Unknown PERF03 diagnostic variant")
    };

    private static object Describe(string referenceVariant, string factor, string ambientContext,
        bool oldUnconsumedValueTaskMayInvalidate) => new
        {
            id = Id,
            referenceVariant,
            factor,
            ambientContext,
            oldUnconsumedValueTaskMayInvalidate,
            normalFidelityClaim = Id == "V0",
            countedEvidenceVariant = Id,
            candidateCountedEvidenceAvailable = Id == "VEC" || Id == "VEntry" || Id == "VRoot",
            timedPendingDenominatorKnown = false
        };

    internal static void RequireBoundCountedEvidence()
    {
        _ = Metadata;
        if (Id == "VWord")
            throw new InvalidOperationException("PERF03 diagnostic variant " + Id +
                " is unranked: candidate-specific counted source/map/catalog and operation trace binding are missing; V0 counts cannot describe a changed candidate.");
    }
}
