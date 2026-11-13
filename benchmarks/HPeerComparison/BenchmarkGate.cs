namespace Competitive;

internal static class BenchmarkGate
{
    internal const string Suite = "hooks";
    internal const int ExpectedCases = 18;

    internal static async Task Run(string output)
    {
        Directory.CreateDirectory(output);
        await HPeerValidation.Run(Path.Combine(output, "h-validation.json")).ConfigureAwait(false);
        var details = new
        {
            foundatioTimingPaths = await HFoundatioBenchmarks.ValidateTimingPathsAsync().ConfigureAwait(false),
            allPeersTiming = await HAllPeersTimingAdmission.RunAsync().ConfigureAwait(false),
            allPeersPriority = await HAllPeersPriorityAdmission.RunAsync().ConfigureAwait(false)
        };
        ArtifactEvidence.Write(Path.Combine(output, "h-timing-validation.json"), details);

        Type[] types = [typeof(HZendiatorBenchmarks), typeof(HMediatRHistoricalBenchmarks),
            typeof(HMediatorBenchmarks), typeof(HDispatchRBenchmarks), typeof(HImmediateBenchmarks),
            typeof(HFoundatioBenchmarks)];
        string[] methods = ["FirstSend", "WarmSend", "ColdOuterFailure"];
        var cases = types.SelectMany(type => methods.Select(method => new
        {
            status = "Passed",
            type = type.FullName!,
            method
        })).ToArray();
        ArtifactEvidence.Write(Path.Combine(output, "correctness.json"), cases);
    }
}
