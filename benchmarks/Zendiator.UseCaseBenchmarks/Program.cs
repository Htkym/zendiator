using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Filters;
using Competitive;
using Perfolizer.Horology;
using System.Security.Cryptography;
using System.Text.Json;

if (!File.Exists("Zendiator.UseCaseBenchmarks.csproj")) throw new InvalidOperationException("Run from the use-case benchmark directory.");
if (args.Contains("--help")) { BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args); return; }
var output = Path.GetFullPath(Environment.GetEnvironmentVariable("COLD_RUN") ?? Path.Combine("..", "..", ".local", "benchmarks", "explore"));
Directory.CreateDirectory(output);
Environment.SetEnvironmentVariable("COLD_EVIDENCE_DIR", output);
if (args.Contains("--validate-only")) Environment.SetEnvironmentVariable("COLD_AUDIT_REG", "1");
var runtimeHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Zendiator.DependencyInjection.ZendiatorServiceResolver).Assembly.Location)));
var expectedChildRuntimeHash = Environment.GetEnvironmentVariable("COLD_EXPECT_CHILD_Z_SHA") ?? "SKIP";
Environment.SetEnvironmentVariable("COLD_EXPECT_Z_SHA", expectedChildRuntimeHash);
File.WriteAllText(Path.Combine(output, "runtime-sha256.txt"), runtimeHash);
try { await GeneratedGate.Run(); }
finally { Correctness.Save(output); }
Console.WriteLine($"Correctness passed: {Correctness.Results.Count} checks.");
var hashes = Directory.EnumerateFiles(".").Where(p => Path.GetExtension(p) is ".cs" or ".csproj" or ".ps1" || Path.GetFileName(p) == "packages.lock.json")
    .Select(p => new { path = p, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))) });
var generated = Directory.Exists(Path.Combine("obj", "Release", "net10.0", "generated"))
    ? Directory.EnumerateFiles(Path.Combine("obj", "Release", "net10.0", "generated"), "*.cs", SearchOption.AllDirectories)
        .Select(p => new { path = p, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))) })
    : [];
var generatorPath = Path.GetFullPath(Path.Combine("..", "..", "src", "Zendiator.SourceGenerator", "bin", "Release", "netstandard2.0", "Zendiator.SourceGenerator.dll"));
File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(new
{
    startedUtc = DateTimeOffset.UtcNow,
    mediatorLifetime = Registration.MediatorLifetime,
    source = "current checked-out Zendiator workspace; see source and runtime hashes",
    processId = Environment.ProcessId,
    runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    runtimeSha256 = runtimeHash,
    expectedChildRuntimeSha256 = expectedChildRuntimeHash,
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
if (args.Contains("--validate-only")) return;
if (args.Contains("--breakdown")) { await AllocationBreakdown.Run(output); return; }
if (args.Contains("--inspect"))
{
    var zero = new ZendiatorSend0 { Lifetime = "Scoped" };
    var five = new ZendiatorSend5 { Lifetime = "Scoped" };
    zero.Setup(); five.Setup();
    try
    {
        for (var round = 0; round < 3; round++)
        {
            for (var i = 0; i < 200000; i++)
                if (await zero.ScopeK1() != 42 || await five.ScopeK1() != 42)
                    throw new InvalidOperationException("Diagnostic result mismatch.");
            Thread.Sleep(300);
        }
    }
    finally { zero.Cleanup(); five.Cleanup(); }
    Console.WriteLine("JIT diagnostic passed.");
    return;
}
var formal = Environment.GetEnvironmentVariable("COLD_FORMAL") == "1";
var coldOnly = Environment.GetEnvironmentVariable("COLD_ONLY") == "1";
var competitors = Environment.GetEnvironmentVariable("COLD_COMPETITORS") == "1";
var scopedPair = Environment.GetEnvironmentVariable("COLD_SCOPED_PAIR") == "1";
var selectedCase = Environment.GetEnvironmentVariable("COLD_CASE");
var matrixFile = Environment.GetEnvironmentVariable("COLD_MATRIX_FILE");
var matrix = matrixFile is null ? null : JsonSerializer.Deserialize<MatrixCase[]>(File.ReadAllText(matrixFile))
    ?? throw new InvalidOperationException("Matrix file was empty.");
var selectedLifetime = Environment.GetEnvironmentVariable("COLD_LIFETIME") ?? "Scoped";
var pinned = Environment.GetEnvironmentVariable("COLD_PINNED") == "1";
var job = Job.Default.WithId("Comparison").WithWarmupCount(matrix is not null || formal ? 20 : 10)
    .WithIterationCount(matrix is not null || formal ? 12 : 8)
    .WithLaunchCount(matrix is not null ? 1 : formal ? 3 : selectedCase is not null ? 1 : 2)
    .WithIterationTime(TimeInterval.FromMilliseconds(matrix is not null || formal ? 500 : 300));
if (pinned) job = job.WithAffinity(new IntPtr(1));
Environment.SetEnvironmentVariable("COLD_CAPTURE_CHILD", "1");
var config = DefaultConfig.Instance.AddJob(job).AddExporter(JsonExporter.Full).WithArtifactsPath(output)
    .AddFilter(new SimpleFilter(b =>
    {
        var name = b.Descriptor.Type.Name;
        var method = b.Descriptor.WorkloadMethod.Name;
        var lifetime = b.Parameters.Items.FirstOrDefault(p => p.Name == "Lifetime")?.Value as string;
        if (matrix is not null) return matrix.Any(c => c.Type == name && c.Method == method
            && c.Lifetime == lifetime
            && (c.Count is null || Equals(b.Parameters.Items.FirstOrDefault(p => p.Name == "Count")?.Value, c.Count.Value))
            && (c.Asynchronous is null || Equals(b.Parameters.Items.FirstOrDefault(p => p.Name == "Asynchronous")?.Value, c.Asynchronous.Value)));
        if (selectedCase is not null) return name + "." + method == selectedCase && lifetime == selectedLifetime;
        if (name.StartsWith("Direct") || lifetime != "Scoped") return false;
        if (scopedPair) return method == "ScopeK1" && name is "ZendiatorSend0" or "ImmediateSend0";
        if (competitors) return method == "ScopeK1" && name is "ZendiatorSend0" or "MediatRHistoricalSend0" or "ImmediateSend0" or "DispatchRSend0" or "MediatorSend0";
        if (coldOnly) return method == "ScopeK1" && name is "ZendiatorSend0" or "MediatRHistoricalSend0";
        return name == "ZendiatorSend0" && method is "Typed" or "ScopeK1"
            || name == "ZendiatorSend5" && method is "Typed" or "ScopeK1"
            || name == "MediatRHistoricalSend0" && method == "ScopeK1";
    }));
var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
var cases = summaries.Sum(s => s.Reports.Length);
var failures = summaries.Sum(s => s.Reports.Count(r => !r.Success));
File.WriteAllText(Path.Combine(output, "outcome.json"), JsonSerializer.Serialize(new
{
    finished = DateTimeOffset.Now,
    cases,
    failures,
    validationErrors = summaries.Count(s => s.HasCriticalValidationErrors)
}, new JsonSerializerOptions { WriteIndented = true }));
if (cases != (matrix is not null ? matrix.Length : selectedCase is not null ? 1 : scopedPair ? 2 : competitors ? 5 : coldOnly ? 2 : 5) || failures != 0 || summaries.Any(s => s.HasCriticalValidationErrors))
    throw new InvalidOperationException("Incomplete comparison; inspect retained logs.");

internal sealed record MatrixCase(string Type, string Method, string? Lifetime = null, int? Count = null, bool? Asynchronous = null);
