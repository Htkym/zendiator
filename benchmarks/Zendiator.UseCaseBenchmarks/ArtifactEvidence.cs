using System.Reflection;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Competitive;

internal sealed record ProcessIdentityEvidence(int Pid, string Platform, string StartToken);
internal sealed record LoadedAssemblyEvidence(string Name, string FullName, string Path, string Mvid, string Sha256);
internal sealed record ConfigFileEvidence(string Path, string Sha256);
internal sealed record RuntimeEvidence(string Framework, string FrameworkVersion, string Architecture,
    string OsArchitecture, string FrameworkDirectory, string HostPath, string HostSha256,
    bool ServerGC, string LatencyMode, ConfigFileEvidence RuntimeConfig, ConfigFileEvidence Deps,
    ConfigFileEvidence[] AppDeps, ConfigFileEvidence[] NativeRuntime, Dictionary<string, string?> Controls);

internal static class ArtifactEvidence
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static readonly string[] ControlNames =
    [
        "DOTNET_gcServer", "COMPlus_gcServer", "DOTNET_gcConcurrent", "COMPlus_gcConcurrent",
        "DOTNET_GCHeapCount", "COMPlus_GCHeapCount", "DOTNET_ReadyToRun", "COMPlus_ReadyToRun",
        "DOTNET_TieredCompilation", "COMPlus_TieredCompilation", "DOTNET_TieredPGO", "COMPlus_TieredPGO",
        "DOTNET_TC_QuickJitForLoops", "COMPlus_TC_QuickJitForLoops", "DOTNET_EnableDiagnostics",
        "DOTNET_GCStress", "COMPlus_GCStress", "DOTNET_ADDITIONAL_DEPS", "DOTNET_SHARED_STORE",
        "DOTNET_STARTUP_HOOKS", "DOTNET_PROCESSOR_COUNT"
    ];

    public static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    public static ProcessIdentityEvidence ProcessIdentity()
    {
        if (OperatingSystem.IsWindows())
        {
            using var process = Process.GetCurrentProcess();
            return new(Environment.ProcessId, "windows", process.StartTime.ToUniversalTime().Ticks
                .ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        if (OperatingSystem.IsLinux())
        {
            var stat = File.ReadAllText("/proc/self/stat");
            var fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var boot = File.ReadAllText("/proc/sys/kernel/random/boot_id").Trim();
            return new(Environment.ProcessId, "linux", boot + ":" + fields[19]);
        }
        throw new PlatformNotSupportedException("Process identity is implemented for Windows/Linux only");
    }

    public static LoadedAssemblyEvidence[] Assemblies()
    {
        // Load the evidence writer's dependencies before taking the process snapshot.
        _ = typeof(JsonSerializer).Assembly;
        _ = SHA256.HashData(Array.Empty<byte>());
        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
            .Select(static assembly => new LoadedAssemblyEvidence(
                assembly.GetName().Name ?? throw new InvalidOperationException("Unnamed loaded assembly"),
                assembly.FullName ?? throw new InvalidOperationException("Missing assembly identity"),
                System.IO.Path.GetFullPath(assembly.Location), assembly.ManifestModule.ModuleVersionId.ToString(),
                Hash(assembly.Location)))
            .OrderBy(static item => item.Name, StringComparer.Ordinal).ToArray();
    }

    public static RuntimeEvidence Runtime(string? runtimeConfig = null, string? deps = null)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("Native runtime evidence is implemented for Windows/Linux only");
        var entry = Assembly.GetEntryAssembly()?.Location
            ?? throw new InvalidOperationException("No managed entry assembly");
        var framework = RuntimeInformation.FrameworkDescription;
        if (!framework.StartsWith(".NET ", StringComparison.Ordinal) ||
            !Version.TryParse(framework[5..], out _))
            throw new InvalidOperationException("Unsupported runtime identity; do not substitute a runtime");
        var host = Environment.ProcessPath ?? throw new InvalidOperationException("No executable identity");
        var runtimePath = runtimeConfig ?? System.IO.Path.ChangeExtension(entry, ".runtimeconfig.json");
        var depsPath = deps ?? System.IO.Path.ChangeExtension(entry, ".deps.json");
        var appDeps = (AppContext.GetData("APP_CONTEXT_DEPS_FILES") as string
            ?? throw new InvalidOperationException("No actual dependency context"))
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(static file => new ConfigFileEvidence(System.IO.Path.GetFullPath(file), Hash(file))).ToArray();
        if (!appDeps.Any(file => SamePath(file.Path, depsPath)))
            throw new InvalidOperationException("Actual dependency context does not use the saved child deps file");
        using var process = Process.GetCurrentProcess();
        var native = process.Modules.Cast<ProcessModule>().Where(static module => module.ModuleName.ToLowerInvariant() is
                "coreclr.dll" or "clrjit.dll" or "libcoreclr.so" or "libclrjit.so" or "libcoreclr.dylib" or "libclrjit.dylib")
            .Select(static module => new ConfigFileEvidence(System.IO.Path.GetFullPath(module.FileName), Hash(module.FileName)))
            .OrderBy(static item => item.Path, StringComparer.Ordinal).ToArray();
        if (native.Length == 0) throw new InvalidOperationException("No loaded native runtime evidence");
        return new(framework, framework[5..], RuntimeInformation.ProcessArchitecture.ToString(),
            RuntimeInformation.OSArchitecture.ToString(),
            System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)
                ?? throw new InvalidOperationException("No framework directory"),
            System.IO.Path.GetFullPath(host), Hash(host), GCSettings.IsServerGC, GCSettings.LatencyMode.ToString(),
            new(System.IO.Path.GetFullPath(runtimePath), Hash(runtimePath)),
            new(System.IO.Path.GetFullPath(depsPath), Hash(depsPath)),
            appDeps, native,
            ControlNames.ToDictionary(static name => name, static name => Environment.GetEnvironmentVariable(name),
                                      StringComparer.Ordinal));
    }

    public static void Write(string path, object value)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, JsonOptions));
        File.Move(temporary, path, true);
    }

    public static bool SamePath(string left, string right) => string.Equals(
        System.IO.Path.GetFullPath(left), System.IO.Path.GetFullPath(right),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    public static bool Within(string file, string directory)
    {
        var relative = System.IO.Path.GetRelativePath(System.IO.Path.GetFullPath(directory),
                                                      System.IO.Path.GetFullPath(file));
        return !System.IO.Path.IsPathRooted(relative) && relative != ".." &&
            !relative.StartsWith(".." + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }
}
