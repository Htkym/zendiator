using System.Runtime;
using System.Runtime.InteropServices;

namespace Competitive.FoundatioRelease;

// The workload bodies retain their original untimed evidence calls.
internal static class FoundatioRuntimeEvidence
{
    public static void VerifyRuntime()
    {
        if (Environment.Version != new Version(10, 0, 14) || GCSettings.IsServerGC ||
            RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new InvalidOperationException("Expected workstation x64 runtime 10.0.14.");
    }

    public static void Begin(object benchmark) => ChildEvidence.Record(benchmark);

    public static void End(object benchmark)
    {
    }

    public static object Capture(string phase, string? runtimeConfig = null, string? deps = null) => new
    {
        phase,
        entryProtocol = "zendiator-shared-measurement-v2",
        runtimeEvidence = ArtifactEvidence.Runtime(runtimeConfig, deps),
        assemblies = ArtifactEvidence.Assemblies()
    };
}
