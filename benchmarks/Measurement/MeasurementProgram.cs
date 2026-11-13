using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Filters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using Competitive;
using Competitive.FoundatioRelease;
using Perfolizer.Horology;
using System.Text.Json;
using System.Runtime.InteropServices;

if (args is ["--artifact-gate", var requestPath])
{
    await ArtifactGate.Run(Path.GetFullPath(requestPath));
    return;
}
if (args is ["--list", "flat"] || args.Contains("--help"))
{
    BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    return;
}
FoundatioRuntimeEvidence.VerifyRuntime();
var assemblyName = typeof(Program).Assembly.GetName().Name!;
if (!File.Exists(assemblyName + ".csproj"))
    throw new InvalidOperationException("Run from the selected benchmark project directory.");
var output = Path.GetFullPath(Environment.GetEnvironmentVariable("COLD_RUN")
    ?? throw new InvalidOperationException("COLD_RUN is required for the shared measurement entry."));
Directory.CreateDirectory(output);
Environment.SetEnvironmentVariable("COLD_EVIDENCE_DIR", output);
var runtimeHash = ArtifactEvidence.Hash(typeof(Zendiator.DependencyInjection.ZendiatorServiceResolver).Assembly.Location);
_ = typeof(Zendiator.IRequest<>).Assembly;
Environment.SetEnvironmentVariable("COLD_EXPECT_Z_SHA", Environment.GetEnvironmentVariable("COLD_EXPECT_CHILD_Z_SHA") ?? "SKIP");
var skipGate = Environment.GetEnvironmentVariable("COLD_SKIP_GATE") == "1";
var gateProof = Environment.GetEnvironmentVariable("COLD_GATE_PROOF");
if (skipGate)
{
    if (args.Contains("--validate-only") || string.IsNullOrEmpty(gateProof))
        throw new InvalidOperationException("A completed suite gate is required before timing.");
    using var proof = JsonDocument.Parse(File.ReadAllText(gateProof));
    if (proof.RootElement.ValueKind != JsonValueKind.Array || proof.RootElement.GetArrayLength() != BenchmarkGate.ExpectedCases ||
        proof.RootElement.EnumerateArray().Any(row => row.GetProperty("status").GetString() != "Passed"))
        throw new InvalidOperationException("The selected suite gate is incomplete.");
}
else
{
    Environment.SetEnvironmentVariable("COLD_AUDIT_REG", "1");
    await BenchmarkGate.Run(output);
}
using var packageLock = JsonDocument.Parse(File.ReadAllText("packages.lock.json"));
var packageVersions = packageLock.RootElement.GetProperty("dependencies").GetProperty("net10.0")
    .EnumerateObject().Where(package => package.Value.GetProperty("type").GetString() == "Direct")
    .ToDictionary(package => package.Name, package => package.Value.GetProperty("resolved").GetString());
var generatorPath = Path.GetFullPath("../../src/Zendiator.SourceGenerator/bin/Release/netstandard2.0/Zendiator.SourceGenerator.dll");
ArtifactEvidence.Write(Path.Combine(output, "manifest.json"), new
{
    entryProtocol = "zendiator-shared-measurement-v2",
    consumerName = assemblyName,
    benchmarkSuite = BenchmarkGate.Suite,
    benchmarkGateCases = BenchmarkGate.ExpectedCases,
    runtime = RuntimeInformation.FrameworkDescription,
    runtimeSha256 = runtimeHash,
    assemblySha256 = ArtifactEvidence.Hash(typeof(Program).Assembly.Location),
    generatorSha256 = File.Exists(generatorPath) ? ArtifactEvidence.Hash(generatorPath) : null,
    gateProofSha256 = skipGate ? ArtifactEvidence.Hash(gateProof!) : null,
    diSha256 = ArtifactEvidence.Hash(typeof(Microsoft.Extensions.DependencyInjection.ServiceProvider).Assembly.Location),
    packageVersions
});
if (args.Contains("--validate-only"))
    return;
var matrixFile = Environment.GetEnvironmentVariable("COLD_MATRIX_FILE")
    ?? throw new InvalidOperationException("A fixed matrix is required for supplemental measurements.");
var matrix = JsonSerializer.Deserialize<MeasurementCase[]>(File.ReadAllText(matrixFile))
    ?? throw new InvalidOperationException("The matrix was empty.");
if (Environment.GetEnvironmentVariable("COLD_SINGLE_CASE") != "1" || matrix.Length != 1 || !skipGate)
    throw new InvalidOperationException("The shared driver must select one case after its suite gate.");
var selected = matrix[0];
using (var proof = JsonDocument.Parse(File.ReadAllText(gateProof!)))
{
    var parameters = new Dictionary<string, object?>();
    if (selected.Lifetime is not null)
        parameters["Lifetime"] = selected.Lifetime;
    if (selected.Count is not null)
        parameters["Count"] = selected.Count;
    if (selected.Asynchronous is not null)
        parameters["Asynchronous"] = selected.Asynchronous;
    var expectedParameters = JsonSerializer.SerializeToElement(parameters);
    var emptyParameters = JsonSerializer.SerializeToElement(new Dictionary<string, object?>());
    if (!proof.RootElement.EnumerateArray().Any(row =>
        row.GetProperty("type").GetString()!.EndsWith("." + selected.Type, StringComparison.Ordinal) &&
        row.GetProperty("method").GetString() == selected.Method &&
        JsonElement.DeepEquals(row.TryGetProperty("parameters", out var actual) ? actual : emptyParameters, expectedParameters)))
        throw new InvalidOperationException("The suite gate does not cover the selected method.");
}
Environment.SetEnvironmentVariable("COLD_CAPTURE_CHILD", "1");
var job = Job.Default.WithId("Comparison").WithWarmupCount(20).WithIterationCount(12)
    .WithLaunchCount(1).WithIterationTime(TimeInterval.FromMilliseconds(500));
if (Environment.GetEnvironmentVariable("COLD_PINNED") == "1")
    job = job.WithAffinity(new IntPtr(1));
var config = DefaultConfig.Instance.AddJob(job).AddExporter(JsonExporter.Full)
    .WithArtifactsPath(output).WithOptions(ConfigOptions.KeepBenchmarkFiles)
    .AddFilter(new SimpleFilter(benchmark => benchmark.Descriptor.Type.Name == selected.Type &&
        benchmark.Descriptor.WorkloadMethod.Name == selected.Method &&
        benchmark.Parameters.Items.Count() == (selected.Lifetime is null ? 0 : 1) +
            (selected.Count is null ? 0 : 1) + (selected.Asynchronous is null ? 0 : 1) &&
        benchmark.Parameters.Items.All(parameter => parameter.Name switch
        {
            "Lifetime" => Equals(parameter.Value, selected.Lifetime),
            "Count" => Equals(parameter.Value, selected.Count),
            "Asynchronous" => Equals(parameter.Value, selected.Asynchronous),
            _ => false
        })));
var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
ArtifactEvidence.Write(Path.Combine(output, "build-artifacts.json"), new
{
    schemaVersion = 1,
    reports = summaries.SelectMany(summary => summary.Reports).Select(report => new
    {
        type = report.BenchmarkCase.Descriptor.Type.Name,
        method = report.BenchmarkCase.Descriptor.WorkloadMethod.Name,
        buildSuccess = report.BuildResult.IsBuildSuccess,
        paths = report.BuildResult.ArtifactsPaths
    }).ToArray()
});
var cases = summaries.Sum(summary => summary.Reports.Length);
var failures = summaries.Sum(summary => summary.Reports.Count(report => !report.Success));
var validationErrors = summaries.Count(summary => summary.HasCriticalValidationErrors);
ArtifactEvidence.Write(Path.Combine(output, "outcome.json"), new { cases, failures, validationErrors });
if (cases != 1 || failures != 0 || validationErrors != 0)
    throw new InvalidOperationException("Expected exactly one successful supplemental case.");

internal sealed record MeasurementCase(string Type, string Method, string? Lifetime = null,
    int? Count = null, bool? Asynchronous = null);
