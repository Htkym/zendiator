using System.Security.Cryptography;
using System.Text.Json;
using Zendiator.DependencyInjection;

namespace Competitive;

internal static class ChildEvidence
{
    private static int registered;

    public static void Record(string scenario, string lifetime)
    {
        if (Environment.GetEnvironmentVariable("COLD_CAPTURE_CHILD") != "1") return;
        if (Interlocked.CompareExchange(ref registered, 1, 0) != 0)
            throw new InvalidOperationException("Expected one child setup evidence per process");
        var directory = Environment.GetEnvironmentVariable("COLD_EVIDENCE_DIR")
            ?? throw new InvalidOperationException("COLD_EVIDENCE_DIR is required for child evidence");
        Directory.CreateDirectory(directory);
        var runtimeHash = ArtifactEvidence.Hash(typeof(ZendiatorServiceResolver).Assembly.Location);
        var file = Path.Combine(directory, $"child-{Environment.ProcessId}-{scenario}-{lifetime}-{Guid.NewGuid():N}.json");
        var processIdentity = ArtifactEvidence.ProcessIdentity();
        var runtime = ArtifactEvidence.Runtime();
        ArtifactEvidence.Write(file, new
        {
            pid = Environment.ProcessId, startedUtc = DateTimeOffset.UtcNow,
            scenario, lifetime, processIdentity, phase = "global-setup",
            runtime = runtime.Framework, runtimeEvidence = runtime, assemblies = ArtifactEvidence.Assemblies()
        });
        var initialSha = ArtifactEvidence.Hash(file);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            var finalPath = Path.Combine(directory, $"loaded-final-{Environment.ProcessId}.json");
            try
            {
                // The process has completed workload/cleanup; do not claim setup's partial map is final.
                var finalRuntime = ArtifactEvidence.Runtime();
                ArtifactEvidence.Write(finalPath, new
                {
                    schemaVersion = 1, pid = Environment.ProcessId, capturedUtc = DateTimeOffset.UtcNow,
                    scenario, lifetime, processIdentity, phase = "process-exit-after-workload", initialEvidenceSha256 = initialSha,
                    runtime = finalRuntime.Framework, runtimeEvidence = finalRuntime,
                    assemblies = ArtifactEvidence.Assemblies(),
                    scope = "All currently loaded, file-backed managed assemblies; dynamic/native code is outside this map"
                });
            }
            catch (Exception error)
            {
                // The driver rejects a failed/missing final capture; do not silently reuse setup evidence.
                try { ArtifactEvidence.Write(finalPath, new { schemaVersion = 1, pid = Environment.ProcessId,
                    phase = "capture-failed", error = error.ToString() }); }
                catch { Console.Error.WriteLine("Final child assembly capture failed"); }
            }
        };
        var expected = Environment.GetEnvironmentVariable("COLD_EXPECT_Z_SHA");
        if (expected != "SKIP" && !StringComparer.OrdinalIgnoreCase.Equals(runtimeHash, expected))
            throw new InvalidOperationException($"Wrong Zendiator runtime in benchmark child: {runtimeHash} != {expected}. Evidence: {file}");
    }
}
