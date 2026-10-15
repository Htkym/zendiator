using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private const string ClosedHandlers = """
        c.Handlers<Plain.Items>(DispatchFamily.Stream,typeof(Plain.ItemsHandler));
        c.Handlers<Command.Ping>(DispatchFamily.SendSync,HandlerEntry.Session<Command.Handler,Command.HFactory>());
        c.Handlers<Typed.ChangeRequest>(DispatchFamily.Stream,typeof(Typed.ChangeHandler));
        c.Handlers<Query.Ping>(DispatchFamily.SendAsync,HandlerEntry.Session<Query.Handler,Query.Factory>());
        """;
    private static readonly string[] ClosedPipelines =
    [
        "c.Pipeline<Query.Ping>(DispatchFamily.SendAsync,Stage.Wrapper<Query.W>(),Stage.Hook<Query.Hook>());",
        "c.StreamPipeline<Typed.ChangeRequest,long>(Stage.Hook(typeof(Typed.ToReference<>)),Stage.Hook<Typed.ToFinal>());",
        "c.Pipeline<Command.Ping>(DispatchFamily.SendSync,Stage.Hook<Command.Hook0>());",
        "c.Pipeline<Plain.Items>(DispatchFamily.Stream,Stage.Hook<Plain.Positive>(),Stage.Hook<Plain.DoubleItem>(),Stage.Hook<Plain.Small>());"
    ];

    private static string ClosedModule(string source, string name, params string[] keep)
    {
        var root = CSharpSyntaxTree.ParseText(source, SyncParseOptions).GetCompilationUnitRoot();
        var members = root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>()
            .Where(t => keep.Contains(t.Identifier.ValueText) && t.Parent is BaseNamespaceDeclarationSyntax or CompilationUnitSyntax);
        var usings = root.DescendantNodes().OfType<UsingDirectiveSyntax>().DistinctBy(u => u.ToString());
        return SyntaxFactory.NamespaceDeclaration(SyntaxFactory.ParseName("Closed." + name))
            .WithUsings(SyntaxFactory.List(usings)).WithMembers(SyntaxFactory.List<MemberDeclarationSyntax>(members))
            .NormalizeWhitespace().ToFullString();
    }

    private static string ClosedCompositionSource(string shape, bool consumer)
    {
        var modules = ClosedModule(TypeChangeSource("success"), "Typed", "ChangeRequest", "ChangeHandler", "ToReference", "ToFinal") + "\n" +
            ClosedModule(MixedStreamSendSource(false), "Plain", "Items", "Probe", "ItemsHandler", "Positive", "DoubleItem", "Small") + "\n" +
            ClosedModule(CapabilityIntegrationSource(false, true, "---s", "direct", "owned", false), "Command", "Ping", "Hook0", "Handler", "HFactory") + "\n" +
            ClosedModule(ManagedShapeSource(true, false, "array", true), "Query", "Ping", "Hook", "Handler", "Factory", "W");
        var pipelines = shape == "reordered" ? ClosedPipelines.Reverse() : ClosedPipelines.AsEnumerable();
        var config = ClosedHandlers + "\n" + string.Join("\n", pipelines);
        if (shape == "unsupported") config = config.Replace("Stage.Hook<Plain.Positive>(),Stage.Hook<Plain.DoubleItem>(),Stage.Hook<Plain.Small>()", "Stage.Hook<Plain.DoubleItem>()");
        var duplicate = shape == "duplicate" ? "public static IServiceCollection Duplicate(IServiceCollection s)=>s.AddZendiator<Reads>(c=>{" + config + "});" : "";
        return modules + "\nnamespace Closed{using System;using System.Collections.Generic;using System.Threading;using System.Threading.Tasks;using Zendiator;using Zendiator.DependencyInjection;using Microsoft.Extensions.DependencyInjection;" +
            "[ZendiatorComposition(\"closed-set\")]public sealed class Reads;public static class Bridge{" + duplicate +
            "public static IServiceCollection Register(IServiceCollection s)=>s.AddZendiator<Reads>(c=>{" + config + "});" +
            (consumer ? """
                public static IAsyncEnumerable<long> Typed(IServiceProvider p,object r)=>p.GetRequiredService<global::Closed.Generated.Reads.IZendiator>().StreamAsync((global::Closed.Typed.ChangeRequest)r);
                public static IAsyncEnumerable<int> Plain(IServiceProvider p,object r)=>p.GetRequiredService<global::Closed.Generated.Reads.IZendiator>().StreamAsync((global::Closed.Plain.Items)r);
                public static void Command(IServiceProvider p,object r,CancellationToken t)=>p.GetRequiredService<global::Closed.Generated.Reads.IZendiator>().Send((global::Closed.Command.Ping)r,t);
                public static ValueTask<int[]> Query(IServiceProvider p,object r,CancellationToken t)=>p.GetRequiredService<global::Closed.Generated.Reads.IZendiator>().SendAsync((global::Closed.Query.Ping)r,t);
                """ : "") + "}}";
    }

    [Fact]
    public async Task Closed_composition_shares_registration_preserves_typed_StreamPipeline_and_route_identity()
    {
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        void Save(string name, string input, GeneratorDriver driver, Compilation output, IEnumerable<Diagnostic> diagnostics, byte[]? dll = null)
        {
            if (string.IsNullOrEmpty(evidence)) return;
            var folder = Path.Combine(evidence, "closed-composition", name); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "input.cs"), input);
            var files = driver.GetRunResult().Results.Single().GeneratedSources;
            foreach (var file in files) File.WriteAllText(Path.Combine(folder, file.HintName), file.SourceText.ToString());
            if (dll != null) File.WriteAllBytes(Path.Combine(folder, "generated.dll"), dll);
            File.WriteAllText(Path.Combine(folder, "compile.json"), JsonSerializer.Serialize(new
            {
                sources = files.Select(f => f.HintName),
                diagnostics = diagnostics.Select(d => new { d.Id, message = d.GetMessage(), span = d.Location.SourceSpan.ToString() }),
                compiler = output.GetDiagnostics().Select(d => new { d.Id, severity = d.Severity.ToString(), message = d.GetMessage() })
            }));
        }
        var source = ClosedCompositionSource("accepted", true);
        var assemblyName = "ClosedComposition_" + Guid.NewGuid().ToString("N");
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, assemblyName), out var output, out var diagnostics);
        Save("accepted", source, driver, output, diagnostics);
        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error));
        var files = driver.GetRunResult().Results.Single().GeneratedSources;
        Assert.Equal(4, files.Count(f => IsCanonicalRoute(f)));
        Assert.Single(files, f => f.HintName.StartsWith("Zendiator.Composition.", StringComparison.Ordinal));
        Assert.Single(files, f => f.HintName.EndsWith(".Interceptors.g.cs", StringComparison.Ordinal));
        Assert.Single(files, f => f.HintName.StartsWith("Zendiator.StreamCanonical.", StringComparison.Ordinal));
        Assert.Single(files, f => f.HintName.StartsWith("Zendiator.StreamPrototype.", StringComparison.Ordinal));
        var shared = CanonicalCompositionSource(files);
        Assert.Contains("expected.StreamPipeline<global::Closed.Typed.ChangeRequest, long>", shared);
        Assert.Contains("typeof(global::Closed.Typed.ToReference<>)", shared);
        Assert.Contains("typeof(global::Closed.Typed.ToReference<int>)", shared);
        string Handler(string request) => "expected.Handlers<global::Closed." + request + ">";
        string Pipeline(string request, bool typed = false) => typed ? "expected.StreamPipeline<global::Closed." + request + ", long>" : "expected.Pipeline<global::Closed." + request + ">";
        static void Ordered(string text, params string[] tokens)
        {
            var prior = -1;
            foreach (var token in tokens) { var position = text.IndexOf(token, StringComparison.Ordinal); Assert.True(position > prior, token); prior = position; }
        }
        Ordered(shared, Handler("Plain.Items"), Handler("Command.Ping"), Handler("Typed.ChangeRequest"), Handler("Query.Ping"));
        Ordered(shared, Pipeline("Query.Ping"), Pipeline("Typed.ChangeRequest", true), Pipeline("Command.Ping"), Pipeline("Plain.Items"));
        using var bytes = new MemoryStream(); var emitted = output.Emit(bytes);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        Save("accepted", source, driver, output, diagnostics, bytes.ToArray());

        foreach (var shape in new[] { "unsupported", "duplicate" })
        {
            var rejected = ClosedCompositionSource(shape, false);
            var rd = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(rejected, "ClosedRejected_" + shape), out var ro, out var errors);
            Save(shape, rejected, rd, ro, errors);
            Assert.Contains(errors, d => d.Id == (shape == "duplicate" ? "ZEN0035" : "ZEN0037") && d.Location.SourceTree != null &&
                rejected.Substring(d.Location.SourceSpan.Start, d.Location.SourceSpan.Length).Contains(shape == "duplicate" ? "AddZendiator" : "c.", StringComparison.Ordinal));
            AssertNoCanonicalOutput(rd.GetRunResult().Results.Single().GeneratedSources);
            Assert.DoesNotContain(rd.GetRunResult().Results.Single().GeneratedSources, f => f.HintName.EndsWith(".Interceptors.g.cs", StringComparison.Ordinal));
            Assert.Empty(ro.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error && !d.Id.StartsWith("ZEN", StringComparison.Ordinal)));
        }
        var changed = ClosedCompositionSource("reordered", true);
        driver = driver.RunGeneratorsAndUpdateCompilation(SyncCompilation(changed, assemblyName), out output, out diagnostics);
        Save("reordered", changed, driver, output, diagnostics);
        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error));
        var updated = driver.GetRunResult().Results.Single().GeneratedSources;
        var stable = files.Where(f => IsCanonicalRoute(f) || f.HintName.StartsWith("Zendiator.Stream", StringComparison.Ordinal)).ToArray();
        Assert.Equal(6, stable.Length);
        foreach (var file in stable) Assert.Equal(file.SourceText.ToString(), Assert.Single(updated, f => f.HintName == file.HintName).SourceText.ToString());
        var reordered = CanonicalCompositionSource(updated);
        Assert.NotEqual(shared, reordered);
        Ordered(reordered, Handler("Plain.Items"), Handler("Command.Ping"), Handler("Typed.ChangeRequest"), Handler("Query.Ping"));
        Ordered(reordered, Pipeline("Plain.Items"), Pipeline("Command.Ping"), Pipeline("Typed.ChangeRequest", true), Pipeline("Query.Ping"));

        bytes.Position = 0; var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var bridge = assembly.GetType("Closed.Bridge")!;
        Type Type(string name) => assembly.GetType("Closed." + name)!;
        var register = bridge.GetMethod("Register")!.CreateDelegate<Func<IServiceCollection, IServiceCollection>>();
        var send = bridge.GetMethod("Command")!.CreateDelegate<Action<IServiceProvider, object, CancellationToken>>();
        var query = bridge.GetMethod("Query")!.CreateDelegate<Func<IServiceProvider, object, CancellationToken, ValueTask<int[]>>>();
        var typed = bridge.GetMethod("Typed")!.CreateDelegate<Func<IServiceProvider, object, IAsyncEnumerable<long>>>();
        var plain = bridge.GetMethod("Plain")!.CreateDelegate<Func<IServiceProvider, object, IAsyncEnumerable<int>>>();
        var typedRequest = Activator.CreateInstance(Type("Typed.ChangeRequest"))!;
        var typedProbe = new CanonicalStreamTypeChangeProbe { Request = typedRequest };
        var commandProbe = new CapabilityIntegrationProbe(); var queryProbe = new ManagedShapeProbe { Pending = true };
        var plainProbe = Activator.CreateInstance(Type("Plain.Probe"))!;
        IServiceCollection services = new ServiceCollection(); services.AddSingleton(typedProbe); services.AddSingleton(commandProbe); services.AddSingleton(queryProbe);
        services.AddSingleton(Type("Plain.Probe"), plainProbe); services.AddSingleton(Type("Command.HFactory")); register(services);
        Assert.All(services.GroupBy(d => d.ServiceType), group => Assert.Single(group));
        Assert.Equal(ServiceLifetime.Singleton, Assert.Single(services, d => d.ServiceType == Type("Command.HFactory")).Lifetime);
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        long typedValue; int[] response; var plainValues = new List<int>();
        using (var scope = provider.CreateScope())
        {
            Assert.Same(scope.ServiceProvider.GetRequiredService(Type("Generated.Reads.IZendiator")), scope.ServiceProvider.GetRequiredService(Type("Generated.Reads.Zendiator")));
            var typedSequence = typed(scope.ServiceProvider, typedRequest);
            var plainSequence = plain(scope.ServiceProvider, Activator.CreateInstance(Type("Plain.Items"))!);
            Assert.Empty(typedProbe.Trace); Assert.Empty(commandProbe.Trace); Assert.Empty(queryProbe.Trace);
            send(scope.ServiceProvider, Activator.CreateInstance(Type("Command.Ping"), 5)!, commandProbe.Cancellation.Token);
            Assert.Single(commandProbe.CreatedHandlers); Assert.Single(commandProbe.ReleasedHandlers);
            Assert.Equal(new[] { "handler.factory", "handler", "handler.cleanup", "h0.finally" }, commandProbe.Trace);
            Assert.Empty(typedProbe.Trace); Assert.Empty(queryProbe.Trace);
            var pending = query(scope.ServiceProvider, Activator.CreateInstance(Type("Query.Ping"), 10)!, queryProbe.Token.Token);
            Assert.False(pending.IsCompleted); var gate = Assert.Single(queryProbe.Gates); Assert.Equal(0, gate.Consumptions);
            Assert.Empty(typedProbe.Trace); gate.Complete(); response = await pending;
            Assert.Equal(new[] { 22 }, response); Assert.Equal(1, gate.Consumptions);
            Assert.Single(queryProbe.Created); Assert.Single(queryProbe.Released);
            Assert.Same(queryProbe.HandlerResponse, queryProbe.AfterInput); Assert.Same(queryProbe.AfterOutput, response);
            Assert.Equal(new[] { "before", "factory", "body", "dispose", "after", "finally" }, queryProbe.Trace);
            await using (var cursor = typedSequence.GetAsyncEnumerator())
            {
                var move = cursor.MoveNextAsync(); await typedProbe.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.False(move.IsCompleted); typedProbe.Gate.Complete(70L); Assert.True(await move);
                typedValue = cursor.Current; Assert.Equal(70L, typedValue); Assert.False(await cursor.MoveNextAsync());
            }
            Assert.Equal(1, typedProbe.Starts); Assert.Equal(1, typedProbe.Disposals); Assert.Equal(1, typedProbe.Gate.Consumptions);
            await foreach (var item in plainSequence) plainValues.Add(item);
            Assert.Equal(new[] { 2, 4 }, plainValues);
            Assert.Equal(0, commandProbe.HandlerFactoryDisposals);
            Assert.Empty((List<string>)Type("Plain.Probe").GetField("Ended")!.GetValue(plainProbe)!);
        }
        Assert.Equal(new[] { "F2", "M", "F1", "H" }, (List<string>)Type("Plain.Probe").GetField("Ended")!.GetValue(plainProbe)!);
        provider.Dispose(); Assert.Equal(1, commandProbe.HandlerFactoryDisposals);
        if (!string.IsNullOrEmpty(evidence)) File.WriteAllText(Path.Combine(evidence, "closed-composition", "accepted", "runtime.json"), JsonSerializer.Serialize(new
        {
            typedValue,
            plainValues,
            response,
            queryConsumptions = queryProbe.Gates.Single().Consumptions,
            typedConsumptions = typedProbe.Gate.Consumptions,
            typedProbe.Starts,
            typedProbe.Disposals,
            commandProbe.Trace,
            commandCreated = commandProbe.CreatedHandlers.Count,
            commandReleased = commandProbe.ReleasedHandlers.Count,
            commandProbe.HandlerFactoryDisposals,
            queryTrace = queryProbe.Trace,
            queryCreated = queryProbe.Created.Count,
            queryReleased = queryProbe.Released.Count,
            plainEnds = (List<string>)Type("Plain.Probe").GetField("Ended")!.GetValue(plainProbe)!
        }));
    }
}
