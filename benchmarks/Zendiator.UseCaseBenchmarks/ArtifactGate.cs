using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;

namespace Competitive;

internal sealed record ArtifactGateRequest(string Protocol, string Session, string Case, string ChildProofSha256,
    int ChildPid, string BundleRoot, string Output, string ConsumerPath, string RuntimeConfigPath, string DepsPath,
    RuntimeEvidence ExpectedRuntime, LoadedAssemblyEvidence[] ExpectedAssemblies,
    Dictionary<string, string> BundleFiles, string GateImplementationSha256, string GateDriverSha256,
    string? StructUpstreamGateImplementationSha256 = null, string ConsumerName = "Zendiator.UseCaseBenchmarks",
    string BenchmarkSuite = "usecase", int BenchmarkGateCases = 389,
    string? Perf03CauseGateImplementationSha256 = null, string? Perf03CauseVariant = null,
    JsonElement? Perf03CauseCaseSet = null, Dictionary<string, string>? Perf03CauseSourceFiles = null,
    JsonElement? Perf03CauseVariantMetadata = null);

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
            request.ConsumerName != typeof(ArtifactGate).Assembly.GetName().Name ||
            request.ExpectedAssemblies.Select(a => a.Name).Distinct(StringComparer.Ordinal).Count() != request.ExpectedAssemblies.Length)
            throw new InvalidOperationException("Invalid artifact gate identity");
#if SUPPLEMENTAL_BENCHMARK
        if (request.Perf03CauseGateImplementationSha256 is not null || request.Perf03CauseVariant is not null ||
            request.Perf03CauseCaseSet is not null || request.Perf03CauseSourceFiles is not null || request.Perf03CauseVariantMetadata is not null)
            throw new InvalidOperationException("PERF-03 diagnostics require the original use-case consumer");
        if (request.BenchmarkSuite != BenchmarkGate.Suite || request.BenchmarkGateCases != BenchmarkGate.ExpectedCases)
            throw new InvalidOperationException("Artifact gate suite differs from the preserved consumer");
#else
        if (request.BenchmarkSuite != "usecase" || request.BenchmarkGateCases != 389 ||
            request.StructUpstreamGateImplementationSha256 is not { Length: 64 } ||
            request.StructUpstreamGateImplementationSha256.Any(c => !(c is >= '0' and <= '9' or >= 'A' and <= 'F')))
            throw new InvalidOperationException("Invalid use-case artifact gate identity");
        var perf03Requested = CheckPerf03CauseIdentity(request);
#endif
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
#if !SUPPLEMENTAL_BENCHMARK
        if (perf03Requested)
            CheckPerf03CauseSources(request);
#endif
        CheckRuntime(request);
        var expected = request.ExpectedAssemblies.ToDictionary(a => a.Name, StringComparer.Ordinal);
        foreach (var name in new[] { request.ConsumerName, "Zendiator", "Zendiator.Abstractions",
                                    "Microsoft.Extensions.DependencyInjection", "Microsoft.Extensions.DependencyInjection.Abstractions",
                                    "System.Private.CoreLib" })
            if (!expected.ContainsKey(name))
                throw new InvalidOperationException($"Missing mandatory child binding: {name}");
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
                if (current is null)
                    AssemblyLoadContext.Default.LoadFromAssemblyPath(item.Path);
                else if (!ArtifactEvidence.SamePath(current.Location, item.Path) ||
                         current.FullName != item.FullName || ArtifactEvidence.Hash(current.Location) != item.Sha256)
                    throw new InvalidOperationException($"Entry startup substituted a binding: {item.Name}");
            }
#if SUPPLEMENTAL_BENCHMARK
            await BenchmarkGate.Run(request.Output, request.RuntimeConfigPath, request.DepsPath);
            var defaultCorrectness = System.IO.Path.Combine(request.Output, "correctness.json");
            var defaultCorrectnessSha256 = ArtifactEvidence.Hash(defaultCorrectness);
            using (var correctness = JsonDocument.Parse(File.ReadAllBytes(defaultCorrectness)))
                if (correctness.RootElement.ValueKind != JsonValueKind.Array ||
                    correctness.RootElement.GetArrayLength() != request.BenchmarkGateCases ||
                    correctness.RootElement.EnumerateArray().Any(row =>
                        !row.TryGetProperty("status", out var status) || status.GetString() != "Passed"))
                    throw new InvalidOperationException("Supplemental benchmark gate evidence is incomplete or failed");
            var semanticFiles = SemanticFiles(request.Output);
#else
            await GeneratedGate.Run();
            if (Correctness.Results.Count != 389)
                throw new InvalidOperationException($"Expected 389 benchmark-gate results, got {Correctness.Results.Count}");
            Correctness.Save(request.Output);
            var defaultCorrectness = System.IO.Path.Combine(request.Output, "correctness.json");
            var defaultCorrectnessSha256 = ArtifactEvidence.Hash(defaultCorrectness);
            var structOutput = System.IO.Path.Combine(request.Output, "struct-upstream");
            // Both gates run in this same preserved consumer with the same strict resolver.
            // The original 389 results/file remain separate and unchanged.
            await StructUpstreamGate.Run(structOutput);
            var structEvidence = CheckStructUpstreamEvidence(structOutput);
            string? perf03EvidenceSha256 = null;
            Perf03RawPreflightIdentity? perf03RawEvidence = null;
            if (perf03Requested)
            {
                await Perf03CauseGate.Run(request.Output, request.Perf03CauseGateImplementationSha256!);
                perf03EvidenceSha256 = CheckPerf03CauseEvidence(request);
                perf03RawEvidence = await Perf03RawPreflightGate.Run(request.Output,
                    request.Perf03CauseGateImplementationSha256!, Perf03RawSiteMap(request));
            }
            if (Correctness.Results.Count != 389 || ArtifactEvidence.Hash(defaultCorrectness) != defaultCorrectnessSha256)
                throw new InvalidOperationException("The additional gate changed the original 389-case evidence");
#endif
            var loaded = ArtifactEvidence.Assemblies();
            CheckBindings(request, loaded);
            CheckRuntime(request);
            CheckBundle(request);
            if (ArtifactEvidence.Hash(requestPath) != requestHash)
                throw new InvalidOperationException("Artifact gate request changed while validating");
#if SUPPLEMENTAL_BENCHMARK
            CheckSemanticFiles(request.Output, semanticFiles);
#else
            if (CheckStructUpstreamEvidence(structOutput) != structEvidence)
                throw new InvalidOperationException("Struct upstream evidence changed while validating");
            if (perf03Requested && (CheckPerf03CauseEvidence(request) != perf03EvidenceSha256 ||
                Perf03RawPreflightGate.Check(request.Output, request.Perf03CauseGateImplementationSha256!,
                    Perf03RawSiteMap(request)) != perf03RawEvidence))
                throw new InvalidOperationException("PERF-03 evidence changed while validating");
#endif
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
                request.ConsumerName,
                request.BenchmarkSuite,
#if SUPPLEMENTAL_BENCHMARK
                semanticFiles,
#else
                request.StructUpstreamGateImplementationSha256,
                request.Perf03CauseGateImplementationSha256,
                request.Perf03CauseVariant,
                request.Perf03CauseCaseSet,
                request.Perf03CauseSourceFiles,
                request.Perf03CauseVariantMetadata,
                perf03CauseGate = !perf03Requested ? null : new
                {
                    schemaVersion = 1,
                    protocol = "zendiator-perf03-cause-gate-v1",
                    variant = request.Perf03CauseVariant,
                    caseSet = request.Perf03CauseCaseSet,
                    implementationSha256 = request.Perf03CauseGateImplementationSha256,
                    evidencePath = "perf03-cause/perf03-cause.json",
                    evidenceSha256 = perf03EvidenceSha256,
                    rawPreflight = new
                    {
                        schemaVersion = 1,
                        protocol = Perf03RawPreflightGate.Protocol,
                        evidencePath = Perf03RawPreflightGate.EvidencePath,
                        evidenceSha256 = perf03RawEvidence!.EvidenceSha256,
                        siteMapPath = "perf03-sources/src/Perf03CauseDiagnostics/counted-sites.json",
                        siteMapSha256 = perf03RawEvidence.MapSha256,
                        compiledSiteCount = perf03RawEvidence.SiteCount
                    }
                },
                structUpstreamGate = new
                {
                    schemaVersion = 1,
                    cases = 96,
                    correctnessPath = "struct-upstream/struct-upstream-correctness.json",
                    correctnessSha256 = structEvidence.CorrectnessSha256,
                    driverTypes = 3,
                    driverIdentificationPath = "struct-upstream/struct-upstream-driver-identification.json",
                    driverIdentificationSha256 = structEvidence.DriverSha256,
                    implementationSha256 = request.StructUpstreamGateImplementationSha256
                },
#endif
                processId = Environment.ProcessId,
                startedFrom = "preserved consumer entry, separate process",
                benchmarkGateCases = request.BenchmarkGateCases,
                correctnessSha256 = defaultCorrectnessSha256,
                bundleBeforeAndAfterVerified = true,
                request.BundleFiles,
                runtimeEvidence = ArtifactEvidence.Runtime(request.RuntimeConfigPath, request.DepsPath),
                assemblies = loaded,
                scope =
#if SUPPLEMENTAL_BENCHMARK
                    "Original supplemental benchmark semantics validated in the preserved consumer; all semantic output files are bound to this proof"
#else
                    perf03Requested
                        ? "389 existing benchmark-gate checks plus separate 96 struct-upstream parity cases and three driver type classifications; separate PERF-03 input parity and isolated sequential controls, four counted Work observations and 24 substituted deterministic protocol cases; no timed pending denominator; focused contracts and full-suite tests remain separate"
                        : "389 existing benchmark-gate checks plus separate 96 struct-upstream parity cases and three driver type classifications; focused contracts and full-suite tests remain separate"
#endif
            });
#if SUPPLEMENTAL_BENCHMARK
            CheckSemanticFiles(request.Output, semanticFiles);
#else
            if (perf03Requested && (CheckPerf03CauseEvidence(request) != perf03EvidenceSha256 ||
                Perf03RawPreflightGate.Check(request.Output, request.Perf03CauseGateImplementationSha256!,
                    Perf03RawSiteMap(request)) != perf03RawEvidence))
                throw new InvalidOperationException("PERF-03 evidence changed while writing the proof");
#endif
        }
        finally
        {
            AssemblyLoadContext.Default.Resolving -= Resolve;
        }
    }

#if SUPPLEMENTAL_BENCHMARK
    private static Dictionary<string, string> SemanticFiles(string output)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        var directories = new Stack<string>();
        directories.Push(output);
        while (directories.TryPop(out var directory))
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Reparse directories are not valid semantic evidence");
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Reparse artifacts are not valid semantic evidence");
                if ((attributes & FileAttributes.Directory) != 0)
                    directories.Push(entry);
                else
                {
                    var name = System.IO.Path.GetRelativePath(output, entry).Replace('\\', '/');
                    if (name != "artifact-proof.json")
                        files.Add(name, ArtifactEvidence.Hash(entry));
                }
            }
        }
        return files;
    }

    private static void CheckSemanticFiles(string output, Dictionary<string, string> expected)
    {
        var actual = SemanticFiles(output);
        if (actual.Count != expected.Count || expected.Any(pair =>
            !actual.TryGetValue(pair.Key, out var digest) || digest != pair.Value))
            throw new InvalidOperationException("Supplemental semantic evidence changed while validating");
    }
#else
    private static HashSet<(string Type, string Method, int Count, bool Asynchronous)> Perf03Cases()
    {
        var cases = new HashSet<(string, string, int, bool)>();
        foreach (var producer in new[] { "Iterator", "Named" })
            foreach (var token in new[] { "Default", "Live" })
                foreach (var route in new[] { "Stream0", "Relay5" })
                    foreach (var asynchronous in new[] { false, true })
                    {
                        var type = $"ZendiatorPerf03{producer}{token}{route}";
                        cases.Add((type, "Full", 1024, asynchronous));
                        if (route == "Stream0")
                            cases.Add((type, "FullDirect", 1024, asynchronous));
                    }
        cases.Add(("ZendiatorPerf03NamedLiveStream0", "FullDiagnostic", 1024, true));
        cases.Add(("ZendiatorPerf03NamedDefaultRelay5", "FullDiagnostic", 1024, true));
        return cases;
    }

    private static (string Type, string Method, int Count, bool Asynchronous) Perf03Case(JsonElement row)
    {
        if (row.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Invalid PERF-03 case identity");
        return (row.GetProperty("Type").GetString()!, row.GetProperty("Method").GetString()!,
            row.GetProperty("Count").GetInt32(), row.GetProperty("Asynchronous").GetBoolean());
    }

    private static void CheckPerf03CaseSet(JsonElement rows)
    {
        var expected = Perf03Cases();
        if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() != expected.Count)
            throw new InvalidOperationException("Expected the complete 26-case PERF-03 identity");
        foreach (var row in rows.EnumerateArray())
            if (row.EnumerateObject().Count() != 4 || !expected.Remove(Perf03Case(row)))
                throw new InvalidOperationException("PERF-03 case identity is duplicate or substituted");
        if (expected.Count != 0)
            throw new InvalidOperationException("PERF-03 case identity is incomplete");
    }

    private static bool CheckPerf03CauseIdentity(ArtifactGateRequest request)
    {
        using var selected = JsonDocument.Parse(request.Case);
        var type = selected.RootElement.GetProperty("Type").GetString()!;
        if (!type.StartsWith("ZendiatorPerf03", StringComparison.Ordinal))
        {
            if (request.Perf03CauseGateImplementationSha256 is not null || request.Perf03CauseVariant is not null ||
                request.Perf03CauseCaseSet is not null || request.Perf03CauseSourceFiles is not null || request.Perf03CauseVariantMetadata is not null)
                throw new InvalidOperationException("PERF-03 evidence is attached to an unrelated case");
            return false;
        }
        var selectedFields = selected.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
        var allowed = new HashSet<string>(new[] { "Type", "Method", "Count", "Asynchronous", "Project" }, StringComparer.Ordinal);
        if (selectedFields.Distinct(StringComparer.Ordinal).Count() != selectedFields.Length ||
            selectedFields.Any(name => !allowed.Contains(name)) ||
            (selected.RootElement.TryGetProperty("Project", out var project) && project.GetString() != "usecase"))
            throw new InvalidOperationException("PERF-03 selected-case fields differ from the fixed input matrix");
        if (!Perf03Cases().Contains(Perf03Case(selected.RootElement)) ||
            request.Perf03CauseVariant != global::Competitive.Perf03.Diagnostics.Perf03CauseVariant.Id ||
            request.Perf03CauseGateImplementationSha256 is not { Length: 64 } ||
            request.Perf03CauseGateImplementationSha256.Any(c => !(c is >= '0' and <= '9' or >= 'A' and <= 'F')) ||
            request.Perf03CauseCaseSet is not { } caseSet || request.Perf03CauseSourceFiles is null)
            throw new InvalidOperationException("Missing or substituted PERF-03 compiled diagnostic identity");
        global::Competitive.Perf03.Diagnostics.Perf03CauseVariant.RequireBoundCountedEvidence();
        CheckPerf03VariantMetadata(request.Perf03CauseVariantMetadata);
        CheckPerf03CaseSet(caseSet);
        return true;
    }

    private static void CheckPerf03VariantMetadata(JsonElement? actual)
    {
        var expected = JsonSerializer.SerializeToElement(global::Competitive.Perf03.Diagnostics.Perf03CauseVariant.Metadata);
        if (actual is not { ValueKind: JsonValueKind.Object } metadata ||
            metadata.EnumerateObject().Count() != expected.EnumerateObject().Count() ||
            expected.EnumerateObject().Any(property => !metadata.TryGetProperty(property.Name, out var value) ||
                value.ValueKind != property.Value.ValueKind || value.ToString() != property.Value.ToString()))
            throw new InvalidOperationException("PERF03 variant metadata is missing/substituted");
    }

    private static void CheckPerf03CauseSources(ArtifactGateRequest request)
    {
        const string prefix = "perf03-sources/";
        const string diagnostic = prefix + "src/Perf03CauseDiagnostics/";
        var mandatory = new HashSet<string>(StringComparer.Ordinal)
        {
            prefix + "benchmarks/Zendiator.UseCaseBenchmarks/Perf03CauseGate.cs",
            prefix + "benchmarks/Zendiator.UseCaseBenchmarks/Perf03CauseStreams.cs",
            diagnostic + "Perf03CauseVariant.cs", diagnostic + "Perf03DiagnosticControl.cs",
            diagnostic + "StreamCursorState.cs", diagnostic + "snapshot-provenance.json",
            diagnostic + "Perf03CountTrace.cs", diagnostic + "Perf03RawPreflightGate.cs", diagnostic + "Perf03YieldPump.cs",
            diagnostic + "Perf03ProtocolFixtures.cs", diagnostic + "Perf03ProtocolGate.cs",
            diagnostic + "Counted.Stream0.g.cs", diagnostic + "Counted.Relay5.g.cs",
            diagnostic + "CountedStreamCursorState.cs", diagnostic + "CountedSiteCatalog.cs",
            diagnostic + "counted-sites.json", diagnostic + ".gitattributes",
            diagnostic + "Zendiator.StreamCanonical.DF7872ACF770B8DDDA6D6A61054B21A2251A08597B558387231219CCDDFB0B4B.g.cs",
            diagnostic + "Zendiator.StreamCanonical.B80A3F15A7016AF93086943F607702393A9513056C3C751D172215C32167550B.g.cs",
            diagnostic + "originals/StreamCursorState.cs.txt",
            diagnostic + "originals/Zendiator.StreamCanonical.DF7872ACF770B8DDDA6D6A61054B21A2251A08597B558387231219CCDDFB0B4B.g.cs.txt",
            diagnostic + "originals/Zendiator.StreamCanonical.B80A3F15A7016AF93086943F607702393A9513056C3C751D172215C32167550B.g.cs.txt"
        };
        var files = request.Perf03CauseSourceFiles!;
        if (!mandatory.IsSubsetOf(files.Keys) ||
            request.BundleFiles.Keys.Count(name => name.StartsWith(prefix, StringComparison.Ordinal)) != files.Count)
            throw new InvalidOperationException("PERF-03 source archive is incomplete or substituted");
        foreach (var (name, digest) in files)
        {
            var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(request.BundleRoot, name));
            if ((!mandatory.Contains(name) && !name.StartsWith(diagnostic, StringComparison.Ordinal)) ||
                name.Contains('\\') || name.Split('/').Any(segment => segment is "" or "." or "..") ||
                !ArtifactEvidence.Within(path, System.IO.Path.Combine(request.BundleRoot, "perf03-sources")) ||
                !request.BundleFiles.TryGetValue(name, out var saved) || saved != digest || ArtifactEvidence.Hash(path) != digest)
                throw new InvalidOperationException("PERF-03 source archive identity changed");
        }
        var canonical = JsonSerializer.SerializeToUtf8Bytes(new SortedDictionary<string, string>(files, StringComparer.Ordinal));
        if (Convert.ToHexString(SHA256.HashData(canonical)) != request.Perf03CauseGateImplementationSha256)
            throw new InvalidOperationException("PERF-03 source archive digest differs from the requested implementation");
        var variantPath = System.IO.Path.Combine(request.BundleRoot, diagnostic + "Perf03CauseVariant.cs");
        var variants = System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(variantPath),
            "\\bconst\\s+string\\s+Id\\s*=\\s*\"([^\"]+)\"");
        if (variants.Count != 1 || variants[0].Groups[1].Value != request.Perf03CauseVariant)
            throw new InvalidOperationException("PERF-03 source variant differs from the compiled diagnostic variant");
    }

    private static string Perf03RawSiteMap(ArtifactGateRequest request) =>
        System.IO.Path.Combine(request.BundleRoot, "perf03-sources/src/Perf03CauseDiagnostics/counted-sites.json");

    private static string CheckPerf03CauseEvidence(ArtifactGateRequest request)
    {
        CheckPerf03CauseSources(request);
        var directory = System.IO.Path.Combine(request.Output, "perf03-cause");
        var path = System.IO.Path.Combine(directory, "perf03-cause.json");
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0 ||
            Directory.EnumerateFileSystemEntries(directory).Count() != 1 ||
            (File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new InvalidOperationException("PERF-03 semantic evidence is missing or substituted");
        var bytes = File.ReadAllBytes(path);
        using var document = JsonDocument.Parse(bytes);
        var evidence = document.RootElement;
        if (evidence.GetProperty("schemaVersion").GetInt32() != 1 ||
            evidence.GetProperty("protocol").GetString() != "zendiator-perf03-cause-gate-v1" ||
            evidence.GetProperty("variant").GetString() != request.Perf03CauseVariant ||
            evidence.GetProperty("implementationSha256").GetString() != request.Perf03CauseGateImplementationSha256 ||
            evidence.GetProperty("pendingFractionClaim").GetBoolean())
            throw new InvalidOperationException("PERF-03 semantic evidence identity or limitations changed");
        CheckPerf03VariantMetadata(evidence.GetProperty("variantMetadata"));
        CheckPerf03CaseSet(evidence.GetProperty("caseSet"));
        foreach (var (name, method, count) in new[] { ("normalInputCases", false, 24), ("isolatedControls", true, 2) })
        {
            var expected = Perf03Cases().Where(row => (row.Method == "FullDiagnostic") == method).ToHashSet();
            var rows = evidence.GetProperty(name);
            if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() != count)
                throw new InvalidOperationException("PERF-03 semantic evidence is incomplete");
            foreach (var row in rows.EnumerateArray())
            {
                if (row.GetProperty("status").GetString() != "Passed" || !expected.Remove(Perf03Case(row.GetProperty("case"))))
                    throw new InvalidOperationException("PERF-03 semantic evidence is duplicate or failed");
                if (method)
                    CheckPerf03SelectedProtocols(row);
            }
            if (expected.Count != 0)
                throw new InvalidOperationException("PERF-03 semantic evidence coverage changed");
        }
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static void CheckPerf03SelectedProtocols(JsonElement row)
    {
        bool control = global::Competitive.Perf03.Diagnostics.Perf03CauseVariant.Id == "V0";
        bool rootOwned = global::Competitive.Perf03.Diagnostics.Perf03CauseVariant.Id == "VRoot";
        if (row.GetProperty("normalParity").GetBoolean() != control ||
            !row.GetProperty("sequentialEnvelopeParity").GetBoolean())
            throw new InvalidOperationException("PERF03 control fidelity and candidate envelope were conflated");
        var expected = JsonSerializer.SerializeToElement(new
        {
            status = "Passed",
            uninstrumentedSelectedCandidate = true,
            producer = "WorkStreamCursor",
            producerSubstitution = false,
            controlledScheduling = true,
            pendingDisposeRejected = true,
            inlineConsumptionAndNextPendingMove = true,
            disposalInsideContinuation = true,
            oldUnconsumedValueTaskInvalidationChecked = rootOwned,
            durablePrecreatedTaskChecked = rootOwned,
            timedPendingFractionClaim = false
        });
        var actual = row.GetProperty("selectedVariantProtocols");
        if (actual.ValueKind != JsonValueKind.Object ||
            actual.EnumerateObject().Count() != expected.EnumerateObject().Count() ||
            expected.EnumerateObject().Any(property => !actual.TryGetProperty(property.Name, out var value) ||
                value.ValueKind != property.Value.ValueKind || value.ToString() != property.Value.ToString()))
            throw new InvalidOperationException("PERF03 selected candidate protocol evidence is missing/substituted");
    }

    private static (string CorrectnessSha256, string DriverSha256) CheckStructUpstreamEvidence(string output)
    {
        var correctness = File.ReadAllBytes(System.IO.Path.Combine(output, "struct-upstream-correctness.json"));
        var driver = File.ReadAllBytes(System.IO.Path.Combine(output, "struct-upstream-driver-identification.json"));
        using var cases = JsonDocument.Parse(correctness);
        using var types = JsonDocument.Parse(driver);
        var expected = new HashSet<(string, int, bool, bool, bool)>();
        foreach (var name in new[] { "Stream0", "Stream5", "Relay5" })
            foreach (var count in new[] { 0, 1, 16, 1024 })
                foreach (var asynchronous in new[] { false, true })
                    foreach (var early in new[] { false, true })
                        foreach (var typed in new[] { false, true })
                            expected.Add((name, count, asynchronous, early, typed));
        if (cases.RootElement.ValueKind != JsonValueKind.Array || cases.RootElement.GetArrayLength() != 96)
            throw new InvalidOperationException("Expected all 96 struct upstream parity cases");
        foreach (var row in cases.RootElement.EnumerateArray())
        {
            var name = row.GetProperty("name").GetString()!;
            var count = row.GetProperty("count").GetInt32();
            var asynchronous = row.GetProperty("asynchronous").GetBoolean();
            var early = row.GetProperty("early").GetBoolean();
            var typed = row.GetProperty("typed").GetBoolean();
            if (!expected.Remove((name, count, asynchronous, early, typed)) || row.GetProperty("status").GetString() != "Passed" ||
                row.GetProperty("Handlers").GetInt32() != 1 || row.GetProperty("Disposals").GetInt32() != 1 ||
                row.GetProperty("Items").GetInt32() != (early ? Math.Min(1, count) : count))
                throw new InvalidOperationException("Struct upstream parity evidence is duplicate, incomplete or failed");
        }
        var expectedTypes = new HashSet<string>(new[] { "ZendiatorStructUpstreamStream0", "ZendiatorStructUpstreamStream5", "ZendiatorStructUpstreamRelay5" }, StringComparer.Ordinal);
        if (expected.Count != 0 || types.RootElement.ValueKind != JsonValueKind.Array || types.RootElement.GetArrayLength() != 3)
            throw new InvalidOperationException("Expected the complete struct upstream case/type coverage");
        foreach (var row in types.RootElement.EnumerateArray())
            if (!expectedTypes.Remove(row.GetProperty("Type").GetString()!) || row.GetProperty("Library").GetString() != "Zendiator" || row.GetProperty("Cases").GetInt32() != 56)
                throw new InvalidOperationException("Struct upstream driver classification failed");
        return (Convert.ToHexString(SHA256.HashData(correctness)), Convert.ToHexString(SHA256.HashData(driver)));
    }
#endif

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
                if ((attributes & FileAttributes.Directory) != 0)
                    directories.Push(entry);
                else
                    files.Add(entry);
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
