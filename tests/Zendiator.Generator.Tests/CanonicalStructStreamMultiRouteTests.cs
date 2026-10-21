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
    private static string StructStreamMultiSource(string original)
    {
        var root = CSharpSyntaxTree.ParseText(original, SyncParseOptions).GetCompilationUnitRoot();
        var request = Assert.Single(root.DescendantNodes().OfType<RecordDeclarationSyntax>(), r =>
            r.Identifier.ValueText == "Items" && r.Parent is BaseNamespaceDeclarationSyntax n && n.Name.ToString() == "Closed.Plain");
        return original[..request.SpanStart] + "public readonly record struct Items : IStreamRequest<int>;" + original[request.Span.End..];
    }

    [Fact]
    public async Task One_struct_Stream_and_multiple_class_routes_share_the_existing_four_route_contract()
    {
        var original = ClosedCompositionSource("accepted", true);
        var source = StructStreamMultiSource(original);
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        var folder = string.IsNullOrEmpty(evidence) ? null : Path.Combine(evidence, "struct-stream-multi");
        if (folder != null)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "reference-class-request-source.txt"), original);
            File.WriteAllText(Path.Combine(folder, "input.cs"), source);
        }
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "StructStreamMulti_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        var files = driver.GetRunResult().Results.Single().GeneratedSources;
        if (folder != null)
        {
            foreach (var file in files) File.WriteAllText(Path.Combine(folder, file.HintName), file.SourceText.ToString());
            File.WriteAllText(Path.Combine(folder, "compile.json"), JsonSerializer.Serialize(new
            {
                sources = files.Select(f => f.HintName),
                diagnostics = diagnostics.Select(d => new { d.Id, message = d.GetMessage(), span = d.Location.SourceSpan.ToString() }),
                compiler = output.GetDiagnostics().Select(d => new { d.Id, severity = d.Severity.ToString(), message = d.GetMessage() })
            }));
        }
        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        Assert.True(output.GetTypeByMetadataName("Closed.Plain.Items")!.IsValueType);
        Assert.False(output.GetTypeByMetadataName("Closed.Typed.ChangeRequest")!.IsValueType);
        var contract = output.GetTypeByMetadataName("Closed.Generated.Reads.IZendiator");
        Assert.NotNull(contract);
        Assert.Equal(2, contract.GetMembers("StreamAsync").Length);
        Assert.Single(contract.GetMembers("Send")); Assert.Single(contract.GetMembers("SendAsync"));
        Assert.Equal(4, files.Count(f => IsCanonicalRoute(f)));
        Assert.Single(files, f => f.HintName.StartsWith("Zendiator.StreamPrototype.", StringComparison.Ordinal));
        Assert.Single(files, f => f.HintName.StartsWith("Zendiator.StreamCanonical.", StringComparison.Ordinal));
        var shared = CanonicalCompositionSource(files);
        Assert.Contains("expected.StreamPipeline<global::Closed.Typed.ChangeRequest, long>", shared);
        Assert.Contains("typeof(global::Closed.Typed.ToReference<>)", shared);
        Assert.Contains("typeof(global::Closed.Typed.ToReference<int>)", shared);
        using var bytes = new MemoryStream(); var emitted = output.Emit(bytes);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        if (folder != null) File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
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
            Assert.Same(scope.ServiceProvider.GetRequiredService(Type("Generated.Reads.IZendiator")), scope.ServiceProvider.GetRequiredService(Type("Generated.Reads.IZendiator")));
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
        if (!string.IsNullOrEmpty(evidence)) File.WriteAllText(Path.Combine(folder!, "runtime.json"), JsonSerializer.Serialize(new
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
