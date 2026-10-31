using System.Runtime.Loader;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private static string CompositionFamily(AllGenericCase c) => (c.Async ? "Async" : "Sync") + (c.Void ? "Command" : "Response");
    private static string CompositionType(AllGenericCase c, string role) => "global::GenericCases.Composition." + role + CompositionFamily(c) + "<" + c.Argument + ">";
    private static string CompositionMode(AllGenericCase c, string role) => (c.Async ? "async-" : "") + ((role switch { "Outer" => !c.Void, "Inner" => c.Void, "Wrap" => !c.Void || c.Async, _ => throw new InvalidOperationException(role) }) ? "owned" : "borrowed");
    private static string AllGenericCompositionDefinitions()
    {
        var text = "namespace GenericCases.Composition{";
        foreach (var c in AllGenericCases.Where(c => c.Argument == "int"))
        {
            var module = AllGenericModule(c, true);
            var request = "global::GenericCases." + (c.Void ? "Command" : "Response") + "<T>";
            foreach (var (role, old, next) in new[] { ("Outer", "Hook0", "public class Hook1:"), ("Inner", "Hook1", "public sealed class GenericHook"), ("Wrap", "W", "public sealed class GenericW") })
            {
                var start = module.IndexOf("public class " + old + ":", StringComparison.Ordinal);
                var end = module.IndexOf(next, start, StringComparison.Ordinal);
                var name = role + CompositionFamily(c);
                var owned = CompositionMode(c, role).EndsWith("owned", StringComparison.Ordinal);
                var declaration = module[start..end].Replace("public class " + old + ":", "public class " + name + "<T>:", StringComparison.Ordinal).Replace("public " + old + "(", "public " + name + "(", StringComparison.Ordinal)
                    .Replace("false,true", (owned ? "true" : "false") + ",false", StringComparison.Ordinal);
                text += Regex.Replace(declaration, @"\bPing\b", request);
                var factory = "F" + name;
                var resource = name + "<T>";
                var label = "F" + role;
                text += $$"""
                    public sealed class {{factory}}<T>:I{{(c.Async ? "Async" : "")}}{{(owned ? "Owned" : "Borrowed")}}SessionFactory<{{request}},{{resource}}>,IDisposable{
                        private readonly Probe p;public {{factory}}(Probe p){this.p=p;p.Factories.Add("{{label}}");p.FactoryTypes.Add("{{label}}",GetType().ToString());}
                        public {{(c.Async ? "ValueTask<" + resource + "> CreateAsync" : resource + " Create")}}({{request}} r,CancellationToken t){r.Mark(p,"{{label}}.acquire",t);var value=new {{resource}}(p);return {{(c.Async ? c.Void ? "new(value)" : "p.Acquire(value,\"" + label + "\")" : "value")}};}
                        public void Dispose()=>p.FactoryEnds.Add("{{label}}");
                    }
                    """;
            }
            foreach (var id in new[] { 2, 1 })
            {
                var name = "FHandler" + id + CompositionFamily(c);
                var resource = "global::GenericCases.Handlers." + CompositionFamily(c) + "<T,global::GenericCases.Tag" + id + ">";
                text += $$"""
                    public sealed class {{name}}<T>:I{{(c.Async ? "Async" : "")}}{{(id == 2 ? "Owned" : "Borrowed")}}SessionFactory<{{request}},{{resource}}>,IDisposable{
                        private readonly Probe p;public {{name}}(Probe p){this.p=p;p.Factories.Add("GHF{{id}}");p.FactoryTypes.Add("GHF{{id}}",GetType().ToString());}
                        public {{(c.Async ? "ValueTask<" + resource + "> CreateAsync" : resource + " Create")}}({{request}} r,CancellationToken t){r.Mark(p,"GHF{{id}}.acquire",t);var value=new {{resource}}(p);return {{(c.Async ? "new(value)" : "value")}};}
                        public void Dispose()=>p.FactoryEnds.Add("GHF{{id}}");
                    }
                    """;
            }
        }
        return text + "}";
    }
    private static string AllGenericCompositionConfiguration(IEnumerable<AllGenericCase> cases)
    {
        var handlers = new List<string>();
        var pipelines = new List<string>();
        foreach (var c in cases)
        {
            var entries = new[] { 2, 1, 3 }.Select(id => id == 3 ? "typeof(" + ClosedHandlerName(c, id) + ")" : "HandlerEntry.Session<" + ClosedHandlerName(c, id) + "," + CompositionType(c, "FHandler" + id) + ">()");
            handlers.Add($"c.Handlers<{c.Request}>(DispatchFamily.{c.Family},{string.Join(",", entries)});");
            pipelines.Add($"c.Pipeline<{c.Request}>(DispatchFamily.{c.Family},Stage.Hook<{CompositionType(c, "Outer")},{CompositionType(c, "FOuter")}>(),Stage.Wrapper<{CompositionType(c, "Wrap")},{CompositionType(c, "FWrap")}>(),Stage.Hook<{CompositionType(c, "Inner")},{CompositionType(c, "FInner")}>());");
        }
        return string.Join("", handlers) + string.Join("", pipelines.AsEnumerable().Reverse());
    }
    private static string AllGenericCompositionExtras(AllGenericCase c)
    {
        var aliases = new Dictionary<string, string> { ["Hook0"] = "Outer", ["Hook1"] = "Inner", ["W"] = "Wrap", ["KFactory"] = "FOuter", ["K1Factory"] = "FInner", ["WFactory"] = "FWrap" };
        var text = "namespace GenericCases." + c.Name + "{using Ping=" + c.Request + ";" + string.Join("", aliases.Select(p => "using C" + p.Key + "=" + CompositionType(c, p.Value) + ";")) + string.Join("", new[] { 1, 2, 3 }.Select(id => "using G" + id + "=" + ClosedHandlerName(c, id) + ";")) + string.Join("", new[] { 1, 2 }.Select(id => "using GHF" + id + "=" + CompositionType(c, "FHandler" + id) + ";"));
        foreach (var id in new[] { 2, 1, 3 })
        {
            var k = c.Async ? "a" : "s";
            var reference = CanonicalCapabilityReference.Source(c.Async, c.Void, k, k, k, k, false, CompositionMode(c, "Outer"), AllGenericHandlerMode(c, id), true, CompositionMode(c, "Wrap"), CompositionMode(c, "Inner"));
            reference = Regex.Replace(reference, @"\bOracle\b", "GenericReference" + id);
            reference = Regex.Replace(reference, @"\bHandler\b", "G" + id);
            reference = Regex.Replace(reference, @"\bHFactory\b", "GHF" + id);
            foreach (var alias in aliases)
                reference = Regex.Replace(reference, @"\b" + alias.Key + @"\b", "C" + alias.Key);
            text += reference.Replace("namespace App {", "", StringComparison.Ordinal)[..^1];
        }
        var ret = c.Async ? c.Void ? "ValueTask" : "ValueTask<int[]>" : c.Void ? "void" : "int[]";
        var calls = string.Join("", new[] { 2, 1, 3 }.Select((id, i) => (c.Void ? "" : "results[" + i + "]=") + (c.Async ? "await " : "") + "GenericReference" + id + ".Send(p,r,t)" + (c.Async ? ".ConfigureAwait(false)" : "") + ";"));
        return text + "public static class GenericReferenceAll{public static " + (c.Async ? "async " : "") + ret + " Run(IServiceProvider p,Ping r,CancellationToken t){ArgumentNullException.ThrowIfNull(r);t.ThrowIfCancellationRequested();" + (c.Void ? "" : "var results=new int[3];") + calls + (c.Void ? "" : "return results;") + "}}}";
    }
    private static string AllGenericCompositionTypeCheck()
    {
        var resources = new List<string>();
        var factories = new List<string>();
        foreach (var c in AllGenericWrapperCases)
        {
            var order = new List<string>();
            foreach (var id in c.Name == "AVString" ? new[] { 2, 1 } : new[] { 2, 1, 3 })
            {
                order.Add(CompositionType(c, "Outer"));
                order.Add(CompositionType(c, "Wrap"));
                if (c.Name == "SVInt" && id == 1)
                    continue;
                order.Add(CompositionType(c, "Inner"));
                order.Add(ClosedHandlerName(c, id));
                if (c.Name == "SRInt" && id == 1)
                {
                    order.Add(CompositionType(c, "Inner"));
                    order.Add(ClosedHandlerName(c, id));
                }
            }
            resources.Add("\"" + c.Name + "\"=>new[]{" + string.Join(",", order.Select(t => "typeof(" + t + ").ToString()")) + "}");
            var roles = new[] { ("FOuter", "FOuter"), ("FWrap", "FWrap"), ("FInner", "FInner"), ("GHF2", "FHandler2"), ("GHF1", "FHandler1") }.Where(p => c.Name != "SVInt" || p.Item1 != "GHF1");
            factories.Add("\"" + c.Name + "\"=>new Dictionary<string,string>{" + string.Join(",", roles.Select(p => "[\"" + p.Item1 + "\"]=typeof(" + CompositionType(c, p.Item2) + ").ToString()")) + "}");
        }
        return "var expectedResourceTypes=profile switch{" + string.Join(",", resources) + ",_=>throw new InvalidOperationException(profile)};var actualResourceTypes=p.Resources.Keys.Select(r=>r.GetType().ToString()).ToArray();P.Check(actualResourceTypes.SequenceEqual(expectedResourceTypes),\"generic resource actual request argument, role and retry identity\");var expectedFactoryTypes=profile switch{" + string.Join(",", factories) + ",_=>throw new InvalidOperationException(profile)};P.Check(p.FactoryTypes.SequenceEqual(expectedFactoryTypes),\"generic factory actual request argument and role identity\");";
    }
    private static string AllGenericFactoryGateExtras(AllGenericCase c)
    {
        var text = AllClosedHandlerExtras(c, genericHook: true);
        foreach (var (name, resource, mode) in new[] { ("GF1", ClosedHandlerName(c with { Argument = "T" }, 1), AllGenericHandlerMode(c, 1)), ("GF2", ClosedHandlerName(c with { Argument = "T" }, 2), AllGenericHandlerMode(c, 2)), ("GKFactory", ClosedHookName(c with { Argument = "T" }), AllGenericHookMode(c)) })
        {
            var start = text.IndexOf("public sealed class " + name + ":", StringComparison.Ordinal);
            var marker = "public void Dispose()=>p.FactoryEnds.Add(\"" + name + "\");";
            var end = text.IndexOf('}', text.IndexOf(marker, start, StringComparison.Ordinal) + marker.Length) + 1;
            var request = c.Request.Replace("<" + c.Argument + ">", "<T>", StringComparison.Ordinal);
            var declaration = $$"""
                public sealed class {{name}}<T>:I{{(c.Async ? "Async" : "")}}{{(mode.EndsWith("owned", StringComparison.Ordinal) ? "Owned" : "Borrowed")}}SessionFactory<{{request}},{{resource}}>,IDisposable{
                    private readonly Probe p;public {{name}}(Probe p){this.p=p;p.Factories.Add("{{name}}");}
                    public {{(c.Async ? "ValueTask<" + resource + "> CreateAsync" : resource + " Create")}}({{request}} r,CancellationToken t){var value=new {{resource}}(p);return {{(c.Async ? "new(value)" : "value")}};}
                    public void Dispose()=>p.FactoryEnds.Add("{{name}}");
                }
                """;
            text = text[..start] + declaration + text[end..];
            text = text.Replace("<" + name + ">", "<" + name + "<" + c.Argument + ">>", StringComparison.Ordinal);
        }
        return text;
    }
    private static string AllGenericFactoryGateConfiguration(IEnumerable<AllGenericCase> cases)
    {
        var text = AllClosedHandlerConfiguration(cases, genericHook: true);
        foreach (var c in cases)
            foreach (var name in new[] { "GF1", "GF2", "GKFactory" })
                text = text.Replace("global::GenericCases." + c.Name + "." + name + ">", "global::GenericCases." + c.Name + "." + name + "<" + c.Argument + ">>", StringComparison.Ordinal);
        return text;
    }
    private static string AllGenericCompositionVisibilityExtras(AllGenericCase c)
    {
        var text = "namespace GenericCases." + c.Name + "{";
        foreach (var (role, baseType) in new[] { ("Hook", "KFactory"), ("Handler", "F1") })
        {
            text += $$"""
                internal class Internal{{role}}Factory<T>:{{baseType}}{public Internal{{role}}Factory(Probe p):base(p){} }
                internal static class Internal{{role}}FactoryHost{public class Nested<T>:{{baseType}}{public Nested(Probe p):base(p){} } }
                public static class Public{{role}}FactoryHost{public class Nested<T>:{{baseType}}{public Nested(Probe p):base(p){} } }
                public class Argument{{role}}Factory<T>:{{baseType}}{public Argument{{role}}Factory(Probe p):base(p){} }
                internal class Plain{{role}}Factory:{{baseType}}{public Plain{{role}}Factory(Probe p):base(p){} }
                """;
        }
        return text + "internal class InternalWrapper<T>:W{}internal static class InternalWrapperHost{public class Nested<T>:W{}}public static class PublicWrapperHost{public class Nested<T>:W{}}internal class PlainWrapper:W{} }";
    }
    private static IEnumerable<(string Name, string Config, bool Accepted)> AllGenericCompositionGateEntries(AllGenericCase c)
    {
        var a = "global::GenericCases." + c.Name + ".";
        foreach (var role in new[] { "Hook", "Handler" })
            foreach (var (name, type, accepted) in new[] { ("Internal", "Internal" + role + "Factory<string>", false), ("Containing", "Internal" + role + "FactoryHost.Nested<string>", false), ("Argument", "Argument" + role + "Factory<global::GenericCases.HiddenTag>", false), ("PublicNested", "Public" + role + "FactoryHost.Nested<string>", true), ("OldInternalPlain", "Plain" + role + "Factory", true) })
            {
                var config = AllGenericConfiguration([c], role == "Handler" ? "wrapper" : "").Replace(a + (role == "Hook" ? "KFactory>" : "F1>"), a + type + ">", StringComparison.Ordinal);
                yield return (role + name, config, accepted);
            }
        var otherArgument = c.Argument == "int" ? "string" : "int";
        yield return ("FactoryMismatchRequest", AllGenericFactoryGateConfiguration([c]).Replace(a + "GKFactory<" + c.Argument + ">", a + "GKFactory<" + otherArgument + ">", StringComparison.Ordinal), false);
        yield return ("FactoryMismatchResource", AllGenericConfiguration([c]).Replace(a + "KFactory>", a + "GenericHookResourceFactory<string>>", StringComparison.Ordinal), false);
        foreach (var (name, type, accepted) in new[] { ("Internal", "InternalWrapper<string>", false), ("Containing", "InternalWrapperHost.Nested<string>", false), ("Argument", "GenericW<global::GenericCases.HiddenTag>", false), ("PublicNested", "PublicWrapperHost.Nested<string>", true), ("OldInternalPlain", "PlainWrapper", true) })
            yield return ("Wrapper" + name, AllGenericConfiguration([c], "wrapper").Replace(a + "W>", a + type + ">", StringComparison.Ordinal), accepted);
    }
    private static string AllGenericCompositionGateCases()
    {
        var text = "";
        foreach (var c in new[] { AllGenericCases[0], AllGenericCases[5] })
            foreach (var entry in AllGenericCompositionGateEntries(c))
            {
                var marker = "CompositionVisibility" + c.Name + entry.Name;
                text += "[ZendiatorComposition(\"" + marker + "\")]public sealed class " + marker + ";public static class " + marker + "Bridge{public static IServiceCollection Register(IServiceCollection s)=>s.AddZendiator<" + marker + ">(c=>{" + entry.Config + "});}";
            }
        var legacy = new AllGenericCase("Legacy", false, false, "int", true);
        var config = AllGenericConfiguration([legacy], "wrapper").Replace(".KFactory>", ".InternalHookFactory<string>>", StringComparison.Ordinal).Replace(".F1>", ".InternalHandlerFactory<string>>", StringComparison.Ordinal).Replace(".W>", ".InternalWrapper<string>>", StringComparison.Ordinal);
        return text + "[ZendiatorComposition(\"legacy-internal-composition\")]public sealed class LegacyInternalComposition;public static class LegacyInternalCompositionBridge{public static IServiceCollection Register(IServiceCollection s)=>s.AddZendiator<LegacyInternalComposition>(c=>{" + config + "});}";
    }
    private static void AssertAllGenericCompositionGate(string source, Compilation output, (string HintName, string Text)[] files, IEnumerable<Diagnostic> diagnostics)
    {
        foreach (var c in new[] { AllGenericCases[0], AllGenericCases[5] })
            foreach (var entry in AllGenericCompositionGateEntries(c))
            {
                var marker = "CompositionVisibility" + c.Name + entry.Name;
                var ns = "AllGeneric.Generated." + marker;
                Assert.Equal(entry.Accepted, output.GetTypeByMetadataName(ns + ".IZendiator") != null);
                Assert.Equal(entry.Accepted, output.GetTypeByMetadataName(ns + ".ZendiatorGeneratedRegistrar") != null);
                Assert.Equal(entry.Accepted, files.Any(f => f.HintName.EndsWith("Interceptors.g.cs", StringComparison.Ordinal) && f.Text.Contains(ns + ".ZendiatorGeneratedRegistrar", StringComparison.Ordinal)));
                var start = source.IndexOf("public static class " + marker + "Bridge", StringComparison.Ordinal);
                var end = source.IndexOf("});}", start, StringComparison.Ordinal);
                var found = diagnostics.Where(d => d.Location.SourceSpan.Start >= start && d.Location.SourceSpan.Start < end).ToArray();
                if (entry.Accepted)
                    Assert.Empty(found);
                else
                    Assert.Equal(entry.Name.StartsWith("FactoryMismatch", StringComparison.Ordinal) ? "ZEN0026" : "ZEN0037", Assert.Single(found).Id);
            }
        Assert.NotNull(output.GetTypeByMetadataName("AllGeneric.Generated.LegacyInternalComposition.IZendiator"));
    }
    private static string AllGenericCompositionProbeSource() => AllGenericWrapperProbeSource
        .Replace("public bool Wait,Fail,Retry,Skip;", "public readonly Dictionary<string,string> FactoryTypes=new();public ValueTask<T> Acquire<T>(T value,string role){if(role!=\"FWrap\"||Branch!=2)return new(value);Check(Gate==null,\"one pending generic factory\");var g=new AsyncOnceGate<T>(value);Gate=g;return g.Task;}public bool Wait,Fail,Retry,Skip;", StringComparison.Ordinal)
        .Replace("||phase.StartsWith(\"F\",StringComparison.Ordinal)", "||phase.StartsWith(\"GHF\",StringComparison.Ordinal)", StringComparison.Ordinal)
        .Replace("Events.Add(new{phase", "if(phase==\"FOuter.acquire\")Check(identity==\"original\"&&value==1,\"outer factory request identity\");if(phase==\"FWrap.acquire\")Check(identity==\"outer\"&&value==11,\"wrapper factory request identity\");if(phase==\"FInner.acquire\")Check(value is 11 or 31,\"inner factory original or retry request\");Events.Add(new{phase", StringComparison.Ordinal);
    private const string AllGenericCompositionCleanupCheck = """
        var expectedCleanup=profile switch{
            "SRInt" or "ARString"=>new[]{"cleanup:H2","cleanup:W","cleanup:K0","cleanup:W","cleanup:K0","cleanup:W","cleanup:K0"},
            "SVInt"=>new[]{"cleanup:H2","cleanup:K1","cleanup:K1"},
            "AVString"=>new[]{"cleanup:H2","cleanup:K1","cleanup:W","cleanup:K1","cleanup:W"},_=>throw new InvalidOperationException(profile)};
        P.Check(p.Trace.Where(e=>e.StartsWith("cleanup:",StringComparison.Ordinal)).SequenceEqual(expectedCleanup),"owned unwind order across generic hook, wrapper and handler; borrowed exclusion");
        """;
    [Fact]
    public async Task AllGenericComposition_four_profiles_preserve_factory_types_owned_borrowed_pending_retry_bypass_and_stop()
    {
        var source = AllGenericWrapperSource(true, true);
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "AllGenericCompositionRuntime_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        var folder = AllGenericFolder("composition-runtime");
        SaveSendAllSync(folder, source, driver, output, diagnostics);
        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        var contract = AssertAllGenericContract(output, driver, false, AllGenericWrapperCases);
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
