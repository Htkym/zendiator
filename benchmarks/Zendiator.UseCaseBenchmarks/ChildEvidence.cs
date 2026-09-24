using System.Security.Cryptography;
using System.Text.Json;
using Zendiator.DependencyInjection;

namespace Competitive;

internal static class ChildEvidence
{
    public static void Record(string scenario, string lifetime)
    {
        if (Environment.GetEnvironmentVariable("COLD_CAPTURE_CHILD") != "1") return;
        var directory = Environment.GetEnvironmentVariable("COLD_EVIDENCE_DIR")
            ?? throw new InvalidOperationException("COLD_EVIDENCE_DIR is required for child evidence.");
        Directory.CreateDirectory(directory);
        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && IsRelevant(assembly.GetName().Name))
            .Select(static assembly => new
            {
                name = assembly.GetName().Name,
                path = assembly.Location,
                mvid = assembly.ManifestModule.ModuleVersionId,
                sha256 = File.Exists(assembly.Location)
                    ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location)))
                    : null
            })
            .OrderBy(static item => item.name, StringComparer.Ordinal)
            .ToArray();
        var runtimeHash = Convert.ToHexString(SHA256.HashData(
            File.ReadAllBytes(typeof(ZendiatorServiceResolver).Assembly.Location)));
        var file = Path.Combine(directory, $"child-{Environment.ProcessId}-{scenario}-{lifetime}.json");
        File.WriteAllText(file, JsonSerializer.Serialize(new
        {
            pid = Environment.ProcessId,
            startedUtc = DateTimeOffset.UtcNow,
            scenario,
            lifetime,
            runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            assemblies
        }, new JsonSerializerOptions { WriteIndented = true }));
        var expected = Environment.GetEnvironmentVariable("COLD_EXPECT_Z_SHA");
        if (expected != "SKIP" && !StringComparer.OrdinalIgnoreCase.Equals(runtimeHash, expected))
            throw new InvalidOperationException($"Wrong Zendiator runtime in benchmark child: {runtimeHash} != {expected}. Evidence: {file}");
    }

    private static bool IsRelevant(string? name) => name is not null &&
        (name.StartsWith("Zendiator", StringComparison.Ordinal)
         || name.StartsWith("Competitive", StringComparison.Ordinal)
         || name.StartsWith("MediatR", StringComparison.Ordinal)
         || name.StartsWith("Mediator", StringComparison.Ordinal)
         || name.StartsWith("Immediate", StringComparison.Ordinal)
         || name.StartsWith("DispatchR", StringComparison.Ordinal)
         || name.StartsWith("Microsoft.Extensions.DependencyInjection", StringComparison.Ordinal));
}
