using System.Text.Json;
using Competitive.FoundatioRelease;

namespace Competitive;

internal static class BenchmarkGate
{
    internal const string Suite = "foundatio";
    internal const int ExpectedCases = 16;

    internal static async Task Run(string output, string? runtimeConfig = null, string? deps = null)
    {
        var previousMode = Environment.GetEnvironmentVariable("ROOT_V8_HOST_MODE");
        Environment.SetEnvironmentVariable("ROOT_V8_HOST_MODE", "semantic-only");
        object details;
        try
        {
            details = await FoundatioSemanticValidation.RunAsync().ConfigureAwait(false);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ROOT_V8_HOST_MODE", previousMode);
        }

        var semantic = JsonSerializer.SerializeToElement(details, ArtifactEvidence.JsonOptions);
        var observations = semantic.GetProperty("records").EnumerateArray()
            .Concat(semantic.GetProperty("features").EnumerateArray()).ToArray();
        var rows = new List<object>();
        foreach (var (type, method) in new[]
        {
            (nameof(FoundatioSend0), "Typed"), (nameof(FoundatioSend5), "Typed"),
            (nameof(FoundatioSend0), "ResolveSend"), (nameof(FoundatioSend5), "ResolveSend"),
            (nameof(FoundatioSend5), "ScopeK1")
        })
            Add(type, method, new Dictionary<string, object?> { ["Lifetime"] = "Scoped" });
        Add(nameof(FoundatioNotification16), "Dispatch", []);
        foreach (var asynchronous in new[] { false, true })
        {
            Add(nameof(FoundatioStream5), "Full", StreamParameters(1024, asynchronous));
            Add(nameof(FoundatioStream5), "EarlyBreak", StreamParameters(16, asynchronous));
            Add(nameof(FoundatioRelay5), "Full", StreamParameters(1024, asynchronous));
            Add(nameof(FoundatioRelay5), "EarlyBreak", StreamParameters(16, asynchronous));
        }
        Add(nameof(FoundatioStream0), "Full", StreamParameters(1024, true));
        Add(nameof(FoundatioStream0), "EarlyBreak", StreamParameters(16, false));
        if (rows.Count != ExpectedCases)
            throw new InvalidOperationException("Foundatio baseline gate case selection changed.");

        Directory.CreateDirectory(output);
        ArtifactEvidence.Write(Path.Combine(output, "semantic-details.json"), details);
        ArtifactEvidence.Write(Path.Combine(output, "correctness.json"), rows);

        void Add(string type, string method, Dictionary<string, object?> parameters)
        {
            var fullType = typeof(FoundatioState).Namespace + "." + type;
            var expectedParameters = JsonSerializer.SerializeToElement(parameters);
            if (!observations.Any(row => row.TryGetProperty("actualType", out var actualType)
                && actualType.GetString() == fullType && row.GetProperty("method").GetString() == method
                && ((row.TryGetProperty("scenario", out var scenario) && scenario.GetString() == "normal")
                    || (row.TryGetProperty("mode", out var mode) && mode.GetString() == "success"))
                && JsonElement.DeepEquals(row.GetProperty("parameters"), expectedParameters)))
                throw new InvalidOperationException($"Original semantic gate did not cover {fullType}.{method} with the baseline parameters.");
            rows.Add(new
            {
                status = "Passed",
                suite = Suite,
                type = fullType,
                method,
                parameters
            });
        }
    }

    private static Dictionary<string, object?> StreamParameters(int count, bool asynchronous) =>
        new()
        {
            ["Count"] = count,
            ["Asynchronous"] = asynchronous
        };
}
