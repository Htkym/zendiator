using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Zendiator.SourceGenerator;

namespace Zendiator.StreamScreening;

internal static class Program
{
    private static readonly CSharpParseOptions Parse = new CSharpParseOptions(LanguageVersion.Preview)
        .WithFeatures(new[] { new KeyValuePair<string, string>("InterceptorsNamespaces", "Zendiator.Generated.Interceptors") });
    private const string Imports = """
        global using System;
        global using System.Linq;
        global using System.Threading;
        global using System.Threading.Tasks;
        global using System.Collections.Generic;
        global using System.Text.Json;
        global using Microsoft.Extensions.DependencyInjection;
        global using Zendiator;
        global using Zendiator.DependencyInjection;
        global using Zendiator.StreamScreening;
        """;

    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args is ["prepare", var directory])
            {
                Prepare(Path.GetFullPath(directory));
                return 0;
            }
            if (args is ["prepare-zero", var zeroDirectory])
            {
                Prepare(Path.GetFullPath(zeroDirectory), "zero-hook-sentinel");
                return 0;
            }
            if (args is ["run", var assembly, var shape, var launchText, var rawPath])
            {
                if (!FixtureSources.Shapes.Contains(shape) || !int.TryParse(launchText, out var launch) || launch is < 1 or > 3)
                    throw new ArgumentException("Fixed shape and launch 1..3 required.");
                if (Environment.Version.ToString() != "10.0.12" ||
                    Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") != "1" ||
                    Environment.GetEnvironmentVariable("DOTNET_TieredPGO") != "1" ||
                    Environment.GetEnvironmentVariable("DOTNET_ReadyToRun") != "0")
                    throw new InvalidOperationException("Pinned runtime/JIT settings differ.");
                var bytes = await File.ReadAllBytesAsync(assembly);
                var pin = Convert.ToHexString(SHA256.HashData(bytes));
                using var stream = new MemoryStream(bytes);
                var consumer = AssemblyLoadContext.Default.LoadFromStream(stream);
                var run = consumer.GetType("Entry", throwOnError: true)!.GetMethod("Run")!
                    .CreateDelegate<Func<int, Task<string>>>();
                using var partialFile = new FileStream(rawPath + ".partial.jsonl", FileMode.CreateNew, FileAccess.ReadWrite);
                using var partial = new StreamWriter(partialFile) { AutoFlush = true };
                ScreeningRun.RawSink = row => partial.WriteLine(JsonSerializer.Serialize(row));
                string raw;
                try
                {
                    ScreeningRun.InitializeMeasurementHelpers();
                    partial.Flush();
                    // Preserve dummy records separately, then reuse this same
                    // initialized StreamWriter/FileStream for measured Raw rows.
                    partialFile.Position = 0;
                    using (var initialization = new FileStream(rawPath + ".initialization.jsonl", FileMode.CreateNew))
                        partialFile.CopyTo(initialization);
                    partialFile.SetLength(0);
                    partialFile.Position = 0;
                    raw = await run(launch).ConfigureAwait(false);
                }
                finally
                {
                    ScreeningRun.RawSink = null;
                }
                using var document = JsonDocument.Parse(raw);
                if (document.RootElement.GetProperty("shape").GetString() != shape)
                    throw new InvalidOperationException("Consumer shape differs.");
                var result = JsonSerializer.Serialize(new
                {
                    shape,
                    launch,
                    consumerSha256 = pin,
                    harnessSha256 = Hash(typeof(Program).Assembly.Location),
                    environment = new
                    {
                        runtime = Environment.Version.ToString(),
                        os = RuntimeInformation.OSDescription,
                        vmBootId = File.ReadAllText("/proc/sys/kernel/random/boot_id").Trim(),
                        architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                        processors = Environment.ProcessorCount,
                        serverGC = System.Runtime.GCSettings.IsServerGC,
                        tieredCompilation = 1,
                        tieredPGO = 1,
                        readyToRun = 0
                    },
                    consumer = document.RootElement.Clone(),
                    exitCode = 0,
                    providerAndWorkerDisposalCompleted = true
                });
                using var output = new FileStream(rawPath, FileMode.CreateNew);
                using var writer = new StreamWriter(output);
                await writer.WriteAsync(result);
                return 0;
            }
            throw new ArgumentException("Explicit prepare or run command required;no default measurement.");
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void Prepare(string directory, string? onlyShape = null)
    {
        if (Directory.Exists(directory))
            throw new IOException("Preparation root already exists.");
        Directory.CreateDirectory(directory);
        var platform = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var paths = platform.Concat(new[]
        {
            typeof(Program).Assembly.Location, typeof(IStreamRequest<>).Assembly.Location,
            typeof(global::Zendiator.DependencyInjection.ZendiatorConfiguration).Assembly.Location, typeof(ServiceCollection).Assembly.Location,
            typeof(ServiceProvider).Assembly.Location
        }).Distinct(StringComparer.Ordinal).ToArray();
        var references = paths.Select(path => MetadataReference.CreateFromFile(path)).ToArray();
        foreach (var shape in onlyShape is null ? FixtureSources.Shapes : new[] { onlyShape })
        {
            var folder = Path.Combine(directory, shape);
            Directory.CreateDirectory(folder);
            var source = FixtureSources.Create(shape);
            File.WriteAllText(Path.Combine(folder, "input.cs"), Imports + source);
            var compilation = CSharpCompilation.Create("Screening_" + shape.Replace('-', '_'),
                new[] { CSharpSyntaxTree.ParseText(Imports + source, Parse) }, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release,
                    nullableContextOptions: NullableContextOptions.Enable, deterministic: true));
            GeneratorDriver driver = CSharpGeneratorDriver.Create([new ZendiatorGenerator().AsSourceGenerator()], parseOptions: Parse);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
            var generated = driver.GetRunResult().Results.Single().GeneratedSources;
            foreach (var file in generated)
                File.WriteAllText(Path.Combine(folder, file.HintName), file.SourceText.ToString());
            File.WriteAllText(Path.Combine(folder, "generator-diagnostics.json"), JsonSerializer.Serialize(diagnostics.Select(d => new { d.Id, severity = d.Severity.ToString(), text = d.ToString() })));
            if (diagnostics.Any(d => d.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error))
                throw new InvalidOperationException("Unexpected SG diagnostics: " + string.Join(";", diagnostics.Select(d => d.ToString())));
            var bridge = FixtureSources.Bridge(shape);
            var entry = FixtureSources.Entry(shape);
            File.WriteAllText(Path.Combine(folder, "bridge.cs"), bridge);
            File.WriteAllText(Path.Combine(folder, "entry.cs"), entry);
            output = output.AddSyntaxTrees(CSharpSyntaxTree.ParseText(bridge + entry, Parse));
            var problems = output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error).ToArray();
            File.WriteAllText(Path.Combine(folder, "compiler-diagnostics.json"), JsonSerializer.Serialize(problems.Select(d => new { d.Id, severity = d.Severity.ToString(), text = d.ToString() })));
            if (problems.Length != 0)
                throw new InvalidOperationException(string.Join(";", problems.Select(d => d.ToString())));
            using var dll = new FileStream(Path.Combine(folder, "consumer.dll"), FileMode.CreateNew);
            var emitted = output.Emit(dll);
            File.WriteAllText(Path.Combine(folder, "emit-diagnostics.json"), JsonSerializer.Serialize(new
            {
                emitted.Success,
                diagnostics = emitted.Diagnostics.Select(d => new { d.Id, severity = d.Severity.ToString(), text = d.ToString() })
            }));
            if (!emitted.Success || emitted.Diagnostics.Any(d => d.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error))
                throw new InvalidOperationException(string.Join(";", emitted.Diagnostics.Select(d => d.ToString())));
            dll.Dispose();
            File.WriteAllText(Path.Combine(folder, "compile.json"), JsonSerializer.Serialize(new
            {
                shape,
                emitCount = 1,
                preflightAndMeasurementCopies = 2,
                productionEnabled = false,
                streamProtocol = "native-typed-pull-v1",
                sourceSha256 = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source))),
                dllSha256 = Hash(Path.Combine(folder, "consumer.dll")),
                diagnostics = diagnostics.Select(d => new { d.Id, severity = d.Severity.ToString() }),
                compilerWarnings = 0,
                compilerErrors = 0,
                references = paths.Select(path => new { path, sha256 = Hash(path) })
            }));
        }
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
