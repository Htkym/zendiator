using System.Runtime.Loader;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private static string ClosedHookName(AllGenericCase c) => "global::GenericCases.Hooks." + (c.Async ? "Async" : "Sync") + (c.Void ? "Command" : "Response") + "<" + c.Argument + ">";
    private static string AllClosedHookDefinitions()
    {
        var text = "namespace GenericCases.Hooks{";
        foreach (var c in AllGenericCases.Where(c => c.Argument == "int"))
        {
            var module = AllGenericModule(c);
            var start = module.IndexOf("public class Hook0:", StringComparison.Ordinal);
            var end = module.IndexOf("public sealed class GenericHook", start, StringComparison.Ordinal);
            var name = (c.Async ? "Async" : "Sync") + (c.Void ? "Command" : "Response");
            var request = "global::GenericCases." + (c.Void ? "Command" : "Response") + "<T>";
            var declaration = module[start..end].Replace("public class Hook0:", "public class " + name + "<T>:", StringComparison.Ordinal).Replace("public Hook0(", "public " + name + "(", StringComparison.Ordinal)
                .Replace("this.p=p;p.Created", "this.p=p;p.Trace.Add(\"hook-type:\"+GetType());p.Created", StringComparison.Ordinal);
            text += Regex.Replace(declaration, @"\bPing\b", request);
        }
        return text + "}";
    }
    private static string AllClosedHookFactory(AllGenericCase c) => $$"""
        public sealed class GKFactory:I{{(c.Async ? "Async" : "")}}OwnedSessionFactory<Ping,GHook>,IDisposable{
            private readonly Probe p;public GKFactory(Probe p){this.p=p;p.Factories.Add("GKFactory");}
            public {{(c.Async ? "ValueTask<GHook> CreateAsync" : "GHook Create")}}(Ping r,CancellationToken t){r.Mark(p,"GKFactory.acquire",t);var value=new GHook(p);return {{(c.Async ? "new(value)" : "value")}};}
            public void Dispose()=>p.FactoryEnds.Add("GKFactory");
        }
        """;
    private static string AllClosedHookTypeCheck()
    {
        var cases = AllClosedHandlerCases.Select(c => "\"" + c.Name + "\"=>typeof(" + ClosedHookName(c) + ").ToString()");
        return "var expectedHookType=profile switch{" + string.Join(",", cases) + ",_=>throw new InvalidOperationException(profile)};var actualHookTypes=p.Trace.Where(e=>e.StartsWith(\"hook-type:\",StringComparison.Ordinal)).Select(e=>e.Substring(\"hook-type:\".Length)).ToArray();P.Check(actualHookTypes.Length==p.Branch&&actualHookTypes.All(t=>t==expectedHookType),\"actual generic hook request type and per-branch acquisition identity\");";
    }
    private static string AllClosedHookVisibilityExtras(AllGenericCase c) => $$"""
        namespace GenericCases.{{c.Name}}{
        internal static class InternalHookHost{public sealed class Nested<T>:Hook0{public Nested(Probe p):base(p){} } }
        public static class PublicHookHost{public sealed class Nested<T>:Hook0{public Nested(Probe p):base(p){} } }
        internal sealed class InternalHook<T>:Hook0{public InternalHook(Probe p):base(p){} }
        internal sealed class InternalPlainHook:Hook0{public InternalPlainHook(Probe p):base(p){} }
        public sealed class ArgumentHook<T>:Hook0{public ArgumentHook(Probe p):base(p){} }
        public sealed class ConstrainedHook<T>:Hook0 where T:class{public ConstrainedHook(Probe p):base(p){} }
        public sealed class OtherFullHook:Hook0{public OtherFullHook(Probe p):base(p){} }
        public sealed class PartialGenericHook<T>:{{(c.Async ? "IAsyncSend" : "ISend")}}RequestTransform<{{c.Request}}>{
            public {{(c.Async ? "ValueTask<" + c.Request + "> BeforeAsync" : c.Request + " Before")}}({{c.Request}} r,CancellationToken t)=>{{(c.Async ? "new(r)" : "r")}};
        }
        public sealed class GenericHookResourceFactory<T>:I{{(c.Async ? "Async" : "")}}OwnedSessionFactory<{{c.Request}},GenericHook<T>>{
            private readonly Probe p;public GenericHookResourceFactory(Probe p){this.p=p;}
            public {{(c.Async ? "ValueTask<GenericHook<T>> CreateAsync" : "GenericHook<T> Create")}}({{c.Request}} r,CancellationToken t)=>{{(c.Async ? "new(new GenericHook<T>(p))" : "new(p)")}};
        }
        }
        """;
    private static IEnumerable<(string Name, string Config, bool Accepted, string? Diagnostic)> AllClosedHookGateEntries(AllGenericCase c)
    {
        var a = "global::GenericCases." + c.Name + ".";
        string Config(string type) => AllGenericConfiguration([c], "hook").Replace(a + "GenericHook<string>", type, StringComparison.Ordinal);
        foreach (var (name, type, accepted, diagnostic) in new[]
        {
            ("Internal", a + "InternalHook<string>", false, "ZEN0037"),
            ("Containing", a + "InternalHookHost.Nested<string>", false, "ZEN0037"),
            ("Argument", a + "ArgumentHook<global::GenericCases.HiddenTag>", false, "ZEN0037"),
            ("PublicNested", a + "PublicHookHost.Nested<string>", true, ""),
            ("OldInternalPlain", a + "InternalPlainHook", true, ""),
            ("Constraint", a + "ConstrainedHook<int>", false, "CS0452"),
            ("WrongRequest", ClosedHookName(c with { Argument = c.Argument == "int" ? "string" : "int" }), false, "ZEN0026"),
            ("Partial", a + "PartialGenericHook<string>", false, "ZEN0037")
        })
            yield return (name, Config(type), accepted, diagnostic);
        yield return ("GenericFactory", AllGenericConfiguration([c], "hook").Replace("Stage.Hook<" + a + "GenericHook<string>>()", "Stage.Hook<" + a + "GenericHook<string>," + a + "GenericHookResourceFactory<string>>()", StringComparison.Ordinal), true, "");
        yield return ("MultipleHooks", AllGenericConfiguration([c], "hook").Replace("Stage.Hook<" + a + "GenericHook<string>>()", "Stage.Hook<" + a + "GenericHook<string>>(),Stage.Hook<" + a + "OtherFullHook>()", StringComparison.Ordinal), false, "ZEN0037");
        yield return ("TwoHooksWrapper", AllGenericConfiguration([c], "wrapper").Replace("Stage.Wrapper<" + a + "W>()", "Stage.Wrapper<" + a + "W>(),Stage.Hook<" + a + "OtherFullHook>()", StringComparison.Ordinal), true, "");
    }
    private static string AllClosedHookGateCases()
    {
        var text = "";
        foreach (var c in new[] { AllGenericCases[0], AllGenericCases[5] })
            foreach (var entry in AllClosedHookGateEntries(c))
            {
                var marker = "HookVisibility" + c.Name + entry.Name;
                text += "[ZendiatorComposition(\"" + marker + "\")]public sealed class " + marker + ";public static class " + marker + "Bridge{public static IServiceCollection Register(IServiceCollection s)=>s.AddZendiator<" + marker + ">(c=>{" + entry.Config + "});}";
            }
        var legacy = new AllGenericCase("Legacy", false, false, "int", true);
        return text + "[ZendiatorComposition(\"legacy-internal-generic-hook\")]public sealed class LegacyInternalGenericHook;public static class LegacyInternalGenericHookBridge{public static IServiceCollection Register(IServiceCollection s)=>s.AddZendiator<LegacyInternalGenericHook>(c=>{" + AllGenericConfiguration([legacy], "hook").Replace("GenericHook<string>", "InternalHook<string>", StringComparison.Ordinal) + "});}";
    }
    private static void AssertAllClosedHookGate(string source, Compilation output, (string HintName, string Text)[] files, IEnumerable<Diagnostic> diagnostics, IEnumerable<Diagnostic> compiler)
    {
        foreach (var c in new[] { AllGenericCases[0], AllGenericCases[5] })
            foreach (var entry in AllClosedHookGateEntries(c))
            {
                var marker = "HookVisibility" + c.Name + entry.Name;
                var ns = "AllGeneric.Generated." + marker;
                Assert.Equal(entry.Accepted, output.GetTypeByMetadataName(ns + ".IZendiator") != null);
                Assert.Equal(entry.Accepted, output.GetTypeByMetadataName(ns + ".ZendiatorGeneratedRegistrar") != null);
                Assert.Equal(entry.Accepted, files.Any(f => f.HintName.EndsWith("Interceptors.g.cs", StringComparison.Ordinal) && f.Text.Contains(ns + ".ZendiatorGeneratedRegistrar", StringComparison.Ordinal)));
                var start = source.IndexOf("public static class " + marker + "Bridge", StringComparison.Ordinal);
                var end = source.IndexOf("});}", start, StringComparison.Ordinal);
                var found = diagnostics.Where(d => d.Location.SourceSpan.Start >= start && d.Location.SourceSpan.Start < end).ToArray();
                if (entry.Accepted || entry.Diagnostic == "CS0452")
                    Assert.Empty(found);
                else
                    Assert.Equal(entry.Diagnostic, Assert.Single(found).Id);
                if (entry.Diagnostic == "CS0452")
                    Assert.Single(compiler, d => d.Id == "CS0452" && d.Location.SourceSpan.Start >= start && d.Location.SourceSpan.Start < end);
            }
        Assert.NotNull(output.GetTypeByMetadataName("AllGeneric.Generated.LegacyInternalGenericHook.IZendiator"));
    }
    [Fact]
    public async Task AllClosedHook_four_profiles_preserve_actual_type_arguments_pending_and_ownership()
    {
        var source = AllClosedHandlerSource(true);
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "AllClosedHookRuntime_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        var folder = AllGenericFolder("hook-runtime");
        SaveSendAllSync(folder, source, driver, output, diagnostics);
        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        var contract = AssertAllGenericContract(output, driver, false, AllClosedHandlerCases);
        using var bytes = new MemoryStream();
        var emitted = output.Emit(bytes);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        if (folder != null)
        {
            File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
            File.WriteAllText(Path.Combine(folder, "contract.json"), JsonSerializer.Serialize(contract));
        }
        bytes.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var result = await assembly.GetType("AllGeneric.Bridge")!.GetMethod("Run")!.CreateDelegate<Func<Task<string>>>()().WaitAsync(TimeSpan.FromSeconds(20));
        if (folder != null)
            File.WriteAllText(Path.Combine(folder, "runtime.json"), result);
        using var json = JsonDocument.Parse(result);
        Assert.Equal(8, json.RootElement.GetProperty("dispatches").GetInt32());
        Assert.Equal(0, json.RootElement.GetProperty("interleaveDispatches").GetInt32());
    }
}
