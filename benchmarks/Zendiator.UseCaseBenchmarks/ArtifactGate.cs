using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

namespace Competitive;

internal sealed record ArtifactGateRequest(string Protocol, string Session, string Case, string ChildProofSha256,
    int ChildPid, string BundleRoot, string Output, string ConsumerPath, string RuntimeConfigPath, string DepsPath,
    RuntimeEvidence ExpectedRuntime, LoadedAssemblyEvidence[] ExpectedAssemblies,
    Dictionary<string, string> BundleFiles, string GateImplementationSha256, string GateDriverSha256);

internal static class ArtifactGate
{
    public const string Protocol = "zendiator-child-artifact-gate-v1";

    public static async Task Run(string requestPath)
    {
        // This mode must run before Program reads any workspace/csproj/generator/obj files.
        Environment.SetEnvironmentVariable("COLD_CAPTURE_CHILD", "0");
        Environment.SetEnvironmentVariable("COLD_AUDIT_REG", "1");
        var request = JsonSerializer.Deserialize<ArtifactGateRequest>(File.ReadAllText(requestPath), ArtifactEvidence.JsonOptions)
            ?? throw new InvalidOperationException("Artifact gate request is empty");
        if (request.Protocol != Protocol || request.ExpectedAssemblies.Length == 0 ||
            request.ExpectedAssemblies.Select(a => a.Name).Distinct(StringComparer.Ordinal).Count() != request.ExpectedAssemblies.Length)
            throw new InvalidOperationException("Invalid artifact gate identity");
        var attempt = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(requestPath))!;
        if (!ArtifactEvidence.SamePath(request.BundleRoot, System.IO.Path.Combine(attempt, "bundle")) ||
            !ArtifactEvidence.SamePath(request.Output, System.IO.Path.Combine(attempt, "artifact-gate")))
            throw new InvalidOperationException("Artifact gate output must be beside its saved request/bundle");
        if (ArtifactEvidence.Within(request.Output, request.BundleRoot) ||
            !ArtifactEvidence.Within(request.ConsumerPath, request.BundleRoot) ||
            !ArtifactEvidence.SamePath(typeof(ArtifactGate).Assembly.Location, request.ConsumerPath) ||
            !ArtifactEvidence.SamePath(Assembly.GetEntryAssembly()?.Location
                ?? throw new InvalidOperationException("No consumer entry assembly"), request.ConsumerPath))
            throw new InvalidOperationException("The preserved consumer must be the explicitly launched entry assembly");
        if (!ArtifactEvidence.Within(request.RuntimeConfigPath, request.BundleRoot) ||
            !ArtifactEvidence.Within(request.DepsPath, request.BundleRoot))
            throw new InvalidOperationException("Runtime configuration must come from the saved child bundle");
        var requestHash = ArtifactEvidence.Hash(requestPath);
        Directory.CreateDirectory(request.Output);
        CheckBundle(request);
        CheckRuntime(request);
        var expected = request.ExpectedAssemblies.ToDictionary(a => a.Name, StringComparer.Ordinal);
        foreach (var name in new[] { "Zendiator.UseCaseBenchmarks", "Zendiator", "Zendiator.Abstractions",
                                    "Microsoft.Extensions.DependencyInjection", "Microsoft.Extensions.DependencyInjection.Abstractions",
                                    "System.Private.CoreLib" })
            if (!expected.ContainsKey(name)) throw new InvalidOperationException($"Missing mandatory child binding: {name}");
        foreach (var item in request.ExpectedAssemblies)
        {
            if (!ArtifactEvidence.Within(item.Path, request.BundleRoot) &&
                !ArtifactEvidence.Within(item.Path, request.ExpectedRuntime.FrameworkDirectory))
                throw new InvalidOperationException($"Binding outside saved bundle/original framework: {item.Name}");
            if (ArtifactEvidence.Hash(item.Path) != item.Sha256)
                throw new InvalidOperationException($"Saved binding changed: {item.Name}");
        }
        AssemblyLoadContext.Default.Resolving += Resolve;
        Assembly? Resolve(AssemblyLoadContext context, AssemblyName name)
        {
            if (name.Name is null || !expected.TryGetValue(name.Name, out var item))
                throw new FileNotFoundException($"No recorded child binding for {name}");
            return context.LoadFromAssemblyPath(item.Path);
        }
        try
        {
            // Include the generated host as an audited binding, without invoking its StartupObject.
            foreach (var item in request.ExpectedAssemblies)
            {
                var current = AppDomain.CurrentDomain.GetAssemblies()
                    .SingleOrDefault(assembly => assembly.GetName().Name == item.Name);
                if (current is null) AssemblyLoadContext.Default.LoadFromAssemblyPath(item.Path);
                else if (!ArtifactEvidence.SamePath(current.Location, item.Path) ||
                         current.FullName != item.FullName || ArtifactEvidence.Hash(current.Location) != item.Sha256)
                    throw new InvalidOperationException($"Entry startup substituted a binding: {item.Name}");
            }
            await GeneratedGate.Run();
            if (Correctness.Results.Count != 394)
                throw new InvalidOperationException($"Expected 394 benchmark-gate results, got {Correctness.Results.Count}");
            Correctness.Save(request.Output);
            var loaded = ArtifactEvidence.Assemblies();
            CheckBindings(request, loaded);
            CheckRuntime(request);
            CheckBundle(request);
            if (ArtifactEvidence.Hash(requestPath) != requestHash)
                throw new InvalidOperationException("Artifact gate request changed while validating");
            ArtifactEvidence.Write(System.IO.Path.Combine(request.Output, "artifact-proof.json"), new
            {
                schemaVersion = 1,
                protocol = Protocol,
                request.Session,
                request.Case,
                request.ChildPid,
                request.ChildProofSha256,
                requestHash,
                request.GateImplementationSha256,
                request.GateDriverSha256,
                processId = Environment.ProcessId,
                startedFrom = "preserved consumer entry, separate process",
                benchmarkGateCases = Correctness.Results.Count,
                correctnessSha256 = ArtifactEvidence.Hash(System.IO.Path.Combine(request.Output, "correctness.json")),
                bundleBeforeAndAfterVerified = true,
                request.BundleFiles,
                runtimeEvidence = ArtifactEvidence.Runtime(request.RuntimeConfigPath, request.DepsPath),
                assemblies = loaded,
                scope = "394 benchmark-gate checks; focused notification contracts and full-suite tests remain separate"
            });
        }
        finally
        {
            AssemblyLoadContext.Default.Resolving -= Resolve;
        }
    }

    private static void CheckBundle(ArtifactGateRequest request)
    {
        var directories = new Stack<string>();
        directories.Push(request.BundleRoot);
        var files = new List<string>();
        while (directories.TryPop(out var directory))
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Reparse directories are not valid saved artifacts");
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Reparse artifacts are not valid saved artifacts");
                if ((attributes & FileAttributes.Directory) != 0) directories.Push(entry);
                else files.Add(entry);
            }
        }
        if (files.Count != request.BundleFiles.Count)
            throw new InvalidOperationException("Saved bundle file set changed");
        foreach (var file in files)
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Reparse points are not valid saved artifacts");
            var name = System.IO.Path.GetRelativePath(request.BundleRoot, file).Replace('\\', '/');
            if (!request.BundleFiles.TryGetValue(name, out var digest) || ArtifactEvidence.Hash(file) != digest)
                throw new InvalidOperationException($"Saved bundle changed: {name}");
        }
    }

    private static void CheckRuntime(ArtifactGateRequest request)
    {
        var actual = ArtifactEvidence.Runtime(request.RuntimeConfigPath, request.DepsPath);
        var expected = request.ExpectedRuntime;
        if (actual.Framework != expected.Framework || actual.FrameworkVersion != expected.FrameworkVersion ||
            actual.Architecture != expected.Architecture || actual.OsArchitecture != expected.OsArchitecture ||
            !ArtifactEvidence.SamePath(actual.FrameworkDirectory, expected.FrameworkDirectory) ||
            !ArtifactEvidence.SamePath(actual.HostPath, expected.HostPath) || actual.HostSha256 != expected.HostSha256 ||
            actual.ServerGC != expected.ServerGC || actual.LatencyMode != expected.LatencyMode ||
            actual.RuntimeConfig.Sha256 != expected.RuntimeConfig.Sha256 || actual.Deps.Sha256 != expected.Deps.Sha256 ||
            actual.AppDeps.Length != expected.AppDeps.Length ||
            actual.AppDeps.Any(item => !expected.AppDeps.Any(wanted =>
                ArtifactEvidence.SamePath(item.Path, wanted.Path) && item.Sha256 == wanted.Sha256)) ||
            actual.NativeRuntime.Length != expected.NativeRuntime.Length ||
            actual.NativeRuntime.Any(item => !expected.NativeRuntime.Any(wanted =>
                ArtifactEvidence.SamePath(item.Path, wanted.Path) && item.Sha256 == wanted.Sha256)) ||
            actual.Controls.Count != expected.Controls.Count ||
            actual.Controls.Any(pair => !expected.Controls.TryGetValue(pair.Key, out var value) || pair.Value != value))
            throw new InvalidOperationException("Runtime/architecture/configuration differs from measured child; no fallback allowed");
    }

    private static void CheckBindings(ArtifactGateRequest request, LoadedAssemblyEvidence[] loaded)
    {
        if (loaded.Select(a => a.Name).Distinct(StringComparer.Ordinal).Count() != loaded.Length)
            throw new InvalidOperationException("Ambiguous loaded assembly names");
        var map = loaded.ToDictionary(a => a.Name, StringComparer.Ordinal);
        foreach (var expected in request.ExpectedAssemblies)
            if (!map.TryGetValue(expected.Name, out var actual) || actual.Sha256 != expected.Sha256 ||
                actual.Mvid != expected.Mvid || actual.FullName != expected.FullName ||
                !ArtifactEvidence.SamePath(actual.Path, expected.Path))
                throw new InvalidOperationException($"Loaded binding differs from measured child: {expected.Name}");
        foreach (var actual in loaded)
        {
            if (ArtifactEvidence.Within(actual.Path, request.BundleRoot))
            {
                var name = System.IO.Path.GetRelativePath(request.BundleRoot, actual.Path).Replace('\\', '/');
                if (!request.BundleFiles.TryGetValue(name, out var digest) || actual.Sha256 != digest)
                    throw new InvalidOperationException($"Loaded bundle substitution: {actual.Name}");
            }
            else if (!ArtifactEvidence.Within(actual.Path, request.ExpectedRuntime.FrameworkDirectory))
                throw new InvalidOperationException($"Unexpected external binding: {actual.Name}");
        }
    }
}
