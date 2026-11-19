using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Filters;
using Competitive;
using Perfolizer.Horology;
using System.Security.Cryptography;
using System.Text.Json;

if (args.Length > 0 && args[0] == "--artifact-gate")
{
    if (args.Length != 2)
        throw new ArgumentException("--artifact-gate requires one saved request file");
    await ArtifactGate.Run(Path.GetFullPath(args[1]));
    return;
}

if (args.Length > 0 && args[0] == "--validate-struct-upstream-child")
{
    if (args.Length != 3)
        throw new ArgumentException("--validate-struct-upstream-child requires one type name and output directory");
    StructUpstreamGate.RecordChild(args[1], Path.GetFullPath(args[2]));
    return;
}

if (args.Length > 0 && args[0] == "--validate-struct-upstream")
{
    if (args.Length != 2)
        throw new ArgumentException("--validate-struct-upstream requires one output directory");
    await StructUpstreamGate.Run(Path.GetFullPath(args[1]));
    return;
}

if (!File.Exists("Zendiator.UseCaseBenchmarks.csproj"))
    throw new InvalidOperationException("Run from the use-case benchmark directory.");
if (args.Contains("--help"))
{
    BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    return;
}
var output = Path.GetFullPath(Environment.GetEnvironmentVariable("COLD_RUN") ?? Path.Combine("..", "..", ".local", "benchmarks", "explore"));
Directory.CreateDirectory(output);
Environment.SetEnvironmentVariable("COLD_EVIDENCE_DIR", output);
if (args.Contains("--validate-only"))
    Environment.SetEnvironmentVariable("COLD_AUDIT_REG", "1");
var runtimeHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Zendiator.DependencyInjection.ZendiatorServiceResolver).Assembly.Location)));
var expectedChildRuntimeHash = Environment.GetEnvironmentVariable("COLD_EXPECT_CHILD_Z_SHA") ?? "SKIP";
Environment.SetEnvironmentVariable("COLD_EXPECT_Z_SHA", expectedChildRuntimeHash);
File.WriteAllText(Path.Combine(output, "runtime-sha256.txt"), runtimeHash);
var skipGate = Environment.GetEnvironmentVariable("COLD_SKIP_GATE") == "1";
var gateProof = Environment.GetEnvironmentVariable("COLD_GATE_PROOF");
if (skipGate)
{
    if (args.Contains("--validate-only") || string.IsNullOrWhiteSpace(gateProof) || !File.Exists(gateProof))
        throw new InvalidOperationException("A completed correctness gate is required before a single-case run.");
    using var proof = JsonDocument.Parse(File.ReadAllText(gateProof));
    if (proof.RootElement.ValueKind != JsonValueKind.Array || proof.RootElement.GetArrayLength() != 389 ||
        proof.RootElement.EnumerateArray().Any(row => !row.TryGetProperty("status", out var status) || status.GetString() != "Passed"))
        throw new InvalidOperationException("Correctness gate evidence is incomplete.");
    var streamProofRows = proof.RootElement.EnumerateArray().Where(row =>
        row.TryGetProperty("suite", out var suite) && suite.GetString() == "Stream" &&
        row.TryGetProperty("library", out var library) && library.GetString()!.StartsWith("Zendiator", StringComparison.Ordinal)).ToArray();
    if (streamProofRows.Length == 0 || streamProofRows.Any(row =>
        !row.TryGetProperty("streamProtocol", out var protocol) || protocol.GetString() != Correctness.StreamProtocol))
        throw new InvalidOperationException("Old correctness evidence cannot validate the native-pull Stream protocol.");
    Console.WriteLine("Correctness gate verified from parent run.");
}
else
{
    try
    {
        await GeneratedGate.Run();
    }
    finally
    {
        Correctness.Save(output);
    }
    Console.WriteLine($"Correctness passed: {Correctness.Results.Count} checks.");
}
var hashes = Directory.EnumerateFiles(".").Where(p => Path.GetExtension(p) is ".cs" or ".csproj" or ".ps1" || Path.GetFileName(p) == "packages.lock.json")
    .Select(p => new { path = p, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))) });
var generated = Directory.Exists(Path.Combine("obj", "Release", "net10.0", "generated"))
    ? Directory.EnumerateFiles(Path.Combine("obj", "Release", "net10.0", "generated"), "*.cs", SearchOption.AllDirectories)
        .Select(p => new { path = p, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))) })
    : [];
var generatorPath = Path.GetFullPath(Path.Combine("..", "..", "src", "Zendiator.SourceGenerator", "bin", "Release", "netstandard2.0", "Zendiator.SourceGenerator.dll"));
using var packageLock = JsonDocument.Parse(File.ReadAllText("packages.lock.json"));
var packageVersions = packageLock.RootElement.GetProperty("dependencies").GetProperty("net10.0")
    .EnumerateObject().Where(package => package.Value.GetProperty("type").GetString() == "Direct")
    .ToDictionary(package => package.Name, package => package.Value.GetProperty("resolved").GetString());
File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(new
{
    startedUtc = DateTimeOffset.UtcNow,
    mediatorLifetime = Registration.MediatorLifetime,
    streamProtocol = Correctness.StreamProtocol,
    streamConditions = "Zendiator uses a dedicated canonical composition; peers retain historical enumerable handlers and registrations; standard enumeration and typed terminals are separate methods",
    source = "current checked-out Zendiator workspace; see source and runtime hashes",
    processId = Environment.ProcessId,
    runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    packageVersions,
    runtimeSha256 = runtimeHash,
    expectedChildRuntimeSha256 = expectedChildRuntimeHash,
    gateProofSha256 = skipGate ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(gateProof!))) : null,
    generatorPath,
    generatorSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(generatorPath))),
    assemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Program).Assembly.Location))),
    diAssembly = typeof(Microsoft.Extensions.DependencyInjection.ServiceProvider).Assembly.FullName,
    diFileVersion = System.Diagnostics.FileVersionInfo.GetVersionInfo(typeof(Microsoft.Extensions.DependencyInjection.ServiceProvider).Assembly.Location).FileVersion,
    diSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Microsoft.Extensions.DependencyInjection.ServiceProvider).Assembly.Location))),
    hashes,
    generated,
    args
}, new JsonSerializerOptions { WriteIndented = true }));
if (args.Contains("--validate-only"))
    return;
var matrixFile = Environment.GetEnvironmentVariable("COLD_MATRIX_FILE")
    ?? throw new InvalidOperationException("A fixed matrix is required for measurement; use run_interleaved.py.");
var matrix = JsonSerializer.Deserialize<MatrixCase[]>(File.ReadAllText(matrixFile))
    ?? throw new InvalidOperationException("The matrix was empty.");
if (Environment.GetEnvironmentVariable("COLD_SINGLE_CASE") != "1" || matrix.Length != 1 || !skipGate)
    throw new InvalidOperationException("The shared driver must select one case after its suite gate.");
var selected = matrix[0];
Environment.SetEnvironmentVariable("COLD_CAPTURE_CHILD", "1");
var job = Job.Default.WithId("Comparison").WithWarmupCount(20).WithIterationCount(12)
    .WithLaunchCount(1).WithIterationTime(TimeInterval.FromMilliseconds(500));
if (Environment.GetEnvironmentVariable("COLD_PINNED") == "1")
    job = job.WithAffinity(new IntPtr(1));
var config = DefaultConfig.Instance.AddJob(job).AddExporter(JsonExporter.Full)
    .WithArtifactsPath(output)
    .WithOrderer(new InterleavedOrderer()).WithOptions(ConfigOptions.JoinSummary | ConfigOptions.KeepBenchmarkFiles)
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
    reports = summaries.SelectMany(s => s.Reports).Select(report => new
    {
        type = report.BenchmarkCase.Descriptor.Type.Name,
        method = report.BenchmarkCase.Descriptor.WorkloadMethod.Name,
        buildSuccess = report.BuildResult.IsBuildSuccess,
        paths = report.BuildResult.ArtifactsPaths
    }).ToArray()
});
var cases = summaries.Sum(s => s.Reports.Length);
var failures = summaries.Sum(s => s.Reports.Count(r => !r.Success));
File.WriteAllText(Path.Combine(output, "outcome.json"), JsonSerializer.Serialize(new
{
    finished = DateTimeOffset.Now,
    cases,
    failures,
    validationErrors = summaries.Count(s => s.HasCriticalValidationErrors)
}, new JsonSerializerOptions { WriteIndented = true }));
if (cases != 1 || failures != 0 || summaries.Any(s => s.HasCriticalValidationErrors))
    throw new InvalidOperationException("Incomplete comparison; inspect retained logs.");

internal sealed record MatrixCase(string Type, string Method, string? Lifetime = null, int? Count = null, bool? Asynchronous = null);
