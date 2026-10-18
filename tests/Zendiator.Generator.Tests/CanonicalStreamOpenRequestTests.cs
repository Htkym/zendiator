using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Zendiator.SourceGenerator;
using Zendiator.Tests.Contracts;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private static string StreamOpenSource(string shape, string? closed = null)
    {
        var types = StreamGenericTypes;
        var request = "GenericItems<>"; var handler = "GenericHandler<>";
        var first = "GenericFirst<>"; var map = "GenericMap<>"; var last = "GenericLast<>";
        if (shape == "fixed")
            types = types.Replace("(T Value) : IStreamRequest<T>", "(int Value) : IStreamRequest<int>")
                .Replace("GenericItems<T>,T>", "GenericItems<T>,int>").Replace("IAsyncEnumerable<T>", "IAsyncEnumerable<int>")
                .Replace("T item", "int item").Replace("public T Map", "public int Map");
        if (shape == "reordered")
        {
            types = types.Replace("GenericItems<T>(T Value)", "GenericItems<T,U>(T Value)")
                .Replace("GenericItems<T>,T>", "GenericItems<T,U>,T>").Replace("GenericItems<T> r", "GenericItems<T,U> r")
                .Replace("class GenericHandler<T>", "class GenericHandler<U,T>")
                .Replace("class GenericFirst<T>", "class GenericFirst<T,U>").Replace("class GenericMap<T>", "class GenericMap<T,U>")
                .Replace("class GenericLast<T>", "class GenericLast<T,U>").Replace("class GenericWrapper<T>", "class GenericWrapper<T,U>");
            request = "GenericItems<,>"; handler = "GenericHandler<,>";
            first = "GenericFirst<,>"; map = "GenericMap<,>"; last = "GenericLast<,>";
        }
        if (shape == "nested")
        {
            var marker = types.IndexOf("public sealed class GenericFirst", StringComparison.Ordinal);
            types = types[..marker].Replace("IStreamRequestHandler<GenericItems<T>,T>", "IStreamRequestHandler<GenericItems<System.Collections.Generic.List<T[]>>,System.Collections.Generic.List<T[]>>")
                .Replace("IAsyncEnumerable<T> HandleAsync(GenericItems<T>", "IAsyncEnumerable<System.Collections.Generic.List<T[]>> HandleAsync(GenericItems<System.Collections.Generic.List<T[]>>") + types[marker..];
        }
        if (shape == "partial")
        {
            types = types.Replace("GenericItems<T>(T Value)", "GenericItems<T,U>(T Value)")
                .Replace("GenericItems<T>,T>", "GenericItems<T,string>,T>").Replace("GenericItems<T> r", "GenericItems<T,string> r");
            request = "GenericItems<,>";
        }
        if (shape is "constrained" or "constrained-base")
        {
            types = types.Replace("IStreamItemFilter<GenericItems<T>,T>, IDisposable {", "IStreamItemFilter<GenericItems<T>,T>, IDisposable where T:class? {")
                .Replace("IStreamItemMap<GenericItems<T>,T>, IDisposable {", "IStreamItemMap<GenericItems<T>,T>, IDisposable where T:class,new() {")
                + "public sealed class OpenValue { public int Number=7; }";
            if (shape == "constrained-base") types = types
                .Replace("IStreamRequestHandler<GenericItems<T>,T>, IDisposable {", "IStreamRequestHandler<GenericItems<T>,T>, IDisposable where T:IOpenValue {")
                .Replace("where T:class,new() {", "where T:OpenBase,new() {")
                .Replace("public sealed class OpenValue {", "public sealed class OpenValue:OpenBase,IOpenValue {")
                + "public interface IOpenValue {} public class OpenBase {}";
        }
        var wrapper = shape == "wrapper";
        if (closed != null) request = "GenericItems<" + closed + ">";
        var body = "c.Handlers(typeof(" + request + "),DispatchFamily.Stream,typeof(" + handler + "));";
        // A changed Type entry and the preserved generic entry both traverse the same binding.
        body += closed != null && shape == "fixed" ? "c.Pipeline<" + request + ">(DispatchFamily.Stream," :
            "c.Pipeline(typeof(" + request + "),DispatchFamily.Stream,";
        body += "Stage.Hook(typeof(" + first + "))," + (wrapper ? "Stage.Wrapper(typeof(GenericWrapper<>))" : "Stage.Hook(typeof(" + map + "))") +
            ",Stage.Hook(typeof(" + last + ")));";
        return ListFixture(body) + types;
    }
    private sealed record OpenFixture(Assembly Assembly, Type Helper, CanonicalStreamConnectionKind Kind);
    private static readonly Dictionary<string, OpenFixture> OpenFixtures = [];
    private static OpenFixture OpenFixtureFor(string shape, string? closed = null)
    {
        var key = shape + "-" + (closed ?? "open");
        lock (OpenFixtures)
        {
            if (OpenFixtures.TryGetValue(key, out var cached)) return cached;
            var source = StreamOpenSource(shape, closed);
            var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "StreamOpen_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
            using var bytes = new MemoryStream(); var emitted = output.Emit(bytes);
            SaveOpenEvidence(key, source, driver, bytes.ToArray(), new
            {
                emitted.Success,
                generatorDiagnostics = diagnostics.Select(d => new { d.Id, message = d.GetMessage() }),
                compilerDiagnostics = emitted.Diagnostics.Select(d => new { d.Id, message = d.GetMessage() })
            });
            var publicAccepted = shape == "wrapper" && closed == null;
            if (publicAccepted) Assert.Empty(diagnostics);
            else Assert.Equal("ZEN0037", Assert.Single(diagnostics).Id);
            var route = Assert.Single(Selection(driver).Pipelines);
            var connection = Assert.Single(StreamConnections(driver).Routes);
            var expected = shape == "fixed" || closed == "int" ? CanonicalStreamConnectionKind.Candidate : CanonicalStreamConnectionKind.CanonicalCandidate;
            Assert.Equal(expected, connection.Kind);
            if (publicAccepted) Assert.Single(driver.GetRunResult().Results.Single().GeneratedSources, s =>
                s.HintName.StartsWith("Zendiator.Canonical.Stream.Public.", StringComparison.Ordinal));
            else Assert.DoesNotContain(driver.GetRunResult().Results.Single().GeneratedSources, s =>
                s.HintName.StartsWith("Zendiator.Route.", StringComparison.Ordinal) || s.HintName.StartsWith("Zendiator.Composition.", StringComparison.Ordinal) ||
                s.HintName.Contains("Interceptors", StringComparison.Ordinal));
            Assert.Equal(closed == null, route.StreamGeneric != null);
            if (shape == "constrained") Assert.Contains(" where __ZendiatorType0 : class, new()", route.StreamGeneric!.TypeConstraints);
            if (shape == "constrained-base") Assert.Equal(" where __ZendiatorType0 : global::App.OpenBase, global::App.IOpenValue, new()", route.StreamGeneric!.TypeConstraints);
            Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
            Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
            SaveOpenEvidence(key, source, driver, bytes.ToArray(), new
            {
                emitted.Success,
                kind = connection.Kind.ToString(),
                route.RequestName,
                handlers = route.Handlers.Select(h => h.Name),
                stages = route.Stages.Select(s => s.Name),
                generic = route.StreamGeneric,
                candidateOnly = !publicAccepted,
                productionEnabled = publicAccepted
            });
            bytes.Position = 0; var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
            var helper = Assert.Single(assembly.GetTypes(), t => t.Name.StartsWith(expected == CanonicalStreamConnectionKind.Candidate ? "__ZendiatorStreamPrototype_" : "__ZendiatorStreamCanonical_", StringComparison.Ordinal));
            var fixture = new OpenFixture(assembly, helper, expected); OpenFixtures.Add(key, fixture); return fixture;
        }
    }
    private static void SaveOpenEvidence(string label, string source, GeneratorDriver driver, byte[] bytes, object result)
    {
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        if (string.IsNullOrEmpty(evidence)) return;
        var folder = Path.Combine(evidence, "stream-open", label); Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "input.cs"), source); File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes);
        foreach (var file in driver.GetRunResult().Results.Single().GeneratedSources) File.WriteAllText(Path.Combine(folder, file.HintName), file.SourceText.ToString());
        File.WriteAllText(Path.Combine(folder, "binding.json"), System.Text.Json.JsonSerializer.Serialize(result));
    }
    private static object OpenItemReference<TRequest, TItem, THandler, TFirst, TMap, TLast>(IServiceProvider provider, object request)
        where TRequest : IStreamRequest<TItem>
        where THandler : IStreamRequestHandler<TRequest, TItem>
        where TFirst : IStreamItemFilter<TRequest, TItem> where TMap : IStreamItemMap<TRequest, TItem> where TLast : IStreamItemFilter<TRequest, TItem> =>
        CanonicalStreamItemReference.FilterMapFilter<TRequest, TItem, TFirst, TMap, TLast>(provider, (TRequest)request,
            (r, c) => provider.GetRequiredService<THandler>().HandleAsync(r, c));
    private static object OpenWrapperReference<TRequest, TItem, THandler, TFirst, TWrapper, TLast>(IServiceProvider provider, object request)
        where TRequest : IStreamRequest<TItem>
        where THandler : IStreamRequestHandler<TRequest, TItem>
        where TFirst : IStreamItemFilter<TRequest, TItem> where TWrapper : IStreamWrapper<TRequest, TItem> where TLast : IStreamItemFilter<TRequest, TItem> =>
        CanonicalStreamWrapperReference.FilterWrapperFilter<TRequest, TItem, TFirst, TWrapper, TLast>(provider, (TRequest)request,
            (r, c) => provider.GetRequiredService<THandler>().HandleAsync(r, c));
    private static async Task<string> ObserveOpen(OpenFixture f, string shape, Type element, bool reference)
    {
        var args = shape == "reordered" ? new[] { typeof(string), element } : new[] { element };
        var item = shape == "fixed" ? typeof(int) : shape == "nested" ? typeof(List<>).MakeGenericType(element.MakeArrayType()) : element;
        var requestArgs = shape is "reordered" or "partial" ? new[] { element, typeof(string) } : new[] { item == element || shape == "fixed" ? element : item };
        var requestType = f.Assembly.GetType("App.GenericItems`" + requestArgs.Length)!.MakeGenericType(requestArgs);
        var hookArgs = shape == "reordered" ? new[] { element, typeof(string) } : shape == "nested" ? new[] { item } : new[] { element };
        var services = new[] {
            f.Assembly.GetType("App.GenericHandler`" + args.Length)!.MakeGenericType(args),
            f.Assembly.GetType("App.GenericFirst`" + hookArgs.Length)!.MakeGenericType(hookArgs),
            f.Assembly.GetType("App." + (shape == "wrapper" ? "GenericWrapper" : "GenericMap") + "`" + hookArgs.Length)!.MakeGenericType(hookArgs),
            f.Assembly.GetType("App.GenericLast`" + hookArgs.Length)!.MakeGenericType(hookArgs) };
        object value = item == typeof(int) ? 7 : item == typeof(string) ? "value" : Activator.CreateInstance(item)!;
        var request = Activator.CreateInstance(requestType, [value])!;
        var p = new StreamGenericScopeProbe(); IServiceCollection collection = new ServiceCollection(); collection.AddSingleton(p);
        foreach (var service in services) collection.AddScoped(service, provider => { p.FactoryCalls++; return Activator.CreateInstance(service, [provider.GetRequiredService<StreamGenericScopeProbe>()])!; });
        using var provider = collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var scope = provider.CreateScope();
        try
        {
            var helper = f.Helper.IsGenericTypeDefinition ? f.Helper.MakeGenericType(args) : f.Helper;
            var stream = reference ? typeof(GeneratorTests).GetMethod(shape == "wrapper" ? nameof(OpenWrapperReference) : nameof(OpenItemReference), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod([requestType, item, .. services]).Invoke(null, [scope.ServiceProvider, request])! :
                helper.GetMethod("Create", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [scope.ServiceProvider, request, default(CancellationToken)])!;
            Assert.Empty(p.Created); Assert.Equal(0, p.FactoryCalls);
            var drain = typeof(GeneratorTests).GetMethod(nameof(DrainGenericTyped), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(item);
            await ((Task)drain.Invoke(null, [stream, value])!).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, p.SourceDisposals); Assert.Equal(4, p.FactoryCalls); Assert.Empty(p.Released);
            Assert.All(p.Called, instance => Assert.Contains(p.Created, c => ReferenceEquals(c.Instance, instance)));
            var calls = p.Called.Select(instance => p.Created.Single(c => ReferenceEquals(c.Instance, instance)).Role).ToArray();
            scope.Dispose(); Assert.Equal(4, p.Released.Count);
            return System.Text.Json.JsonSerializer.Serialize(new { calls, p.FactoryCalls, p.SourceDisposals, released = p.Released.Count });
        }
        finally { scope.Dispose(); }
    }
    [Theory]
    [InlineData("general", "int")]
    [InlineData("general", "string")]
    [InlineData("fixed", "string")]
    [InlineData("reordered", "int")]
    [InlineData("nested", "int")]
    [InlineData("partial", "int")]
    [InlineData("wrapper", "int")]
    [InlineData("constrained", "value")]
    public async Task Stream_open_request_typed_candidates_match_reference_after_closing_patterns(string shape, string kind)
    {
        var f = OpenFixtureFor(shape);
        var element = kind == "int" ? typeof(int) : kind == "string" ? typeof(string) : f.Assembly.GetType("App.OpenValue")!;
        var reference = await ObserveOpen(f, shape, element, true); var generated = await ObserveOpen(f, shape, element, false);
        Assert.Equal(reference, generated);
        if (shape == "constrained") Assert.Throws<ArgumentException>(() => f.Helper.MakeGenericType(typeof(int)));
        string? baseReference = null, baseGenerated = null;
        if (shape == "constrained")
        {
            // Same case also closes a handler-interface-first / hook-base-later constraint union.
            var withBase = OpenFixtureFor("constrained-base"); var baseElement = withBase.Assembly.GetType("App.OpenValue")!;
            baseReference = await ObserveOpen(withBase, "constrained-base", baseElement, true);
            baseGenerated = await ObserveOpen(withBase, "constrained-base", baseElement, false);
            Assert.Equal(baseReference, baseGenerated);
            Assert.Throws<ArgumentException>(() => withBase.Helper.MakeGenericType(typeof(int)));
        }
        string? closedResult = null;
        if (shape is "general" or "fixed")
        {
            var closed = OpenFixtureFor(shape, kind); closedResult = await ObserveOpen(closed, shape, element, false);
            Assert.Equal(reference, closedResult);
        }
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(evidence)) File.WriteAllText(Path.Combine(evidence, "stream-open", "runtime-" + shape + "-" + kind + ".json"),
            System.Text.Json.JsonSerializer.Serialize(new
            {
                shape,
                kind,
                reference,
                generated,
                closedResult,
                baseReference,
                baseGenerated,
                actualHelper = f.Helper.FullName,
                candidateOnly = shape != "wrapper",
                productionEnabled = shape == "wrapper"
            }));
    }
    [Theory]
    [InlineData("unused")]
    [InlineData("unused-handler")]
    [InlineData("repeat")]
    [InlineData("contradictory")]
    [InlineData("closed-constraint")]
    [InlineData("overlap")]
    [InlineData("duplicate")]
    [InlineData("ambiguous-open")]
    public void Stream_open_request_invalid_patterns_keep_diagnostics_and_block_candidates(string failure)
    {
        var source = StreamOpenSource(failure == "repeat" ? "reordered" : "general", failure is "duplicate" or "closed-constraint" ? "int" : null);
        if (failure == "unused") source = source.Replace("Stage.Hook(typeof(GenericFirst<>))", "Stage.Hook(typeof(Unused<,>))") +
            "public sealed class Unused<T,U>:IStreamItemFilter<GenericItems<T>,T>{public bool Accept(GenericItems<T> r,T item,CancellationToken c)=>true;}";
        if (failure == "unused-handler") source = source.Replace("typeof(GenericHandler<>)", "typeof(GenericHandler<,>)")
            .Replace("class GenericHandler<T>", "class GenericHandler<T,U>");
        if (failure == "repeat") source = source.Replace("Stage.Hook(typeof(GenericFirst<,>))", "Stage.Hook(typeof(Repeat<>))") +
            "public sealed class Repeat<T>:IStreamItemFilter<GenericItems<T,T>,T>{public bool Accept(GenericItems<T,T> r,T item,CancellationToken c)=>true;}";
        if (failure is "contradictory" or "closed-constraint") source = source.Replace("Stage.Hook(typeof(GenericFirst<>))", "Stage.Hook(typeof(ClassOnly<>))") +
            "public sealed class ClassOnly<T>:IStreamItemFilter<GenericItems<T>,T> where T:class{public bool Accept(GenericItems<T> r,T item,CancellationToken c)=>true;}";
        if (failure == "contradictory") source = source.Replace("Stage.Hook(typeof(GenericMap<>))", "Stage.Hook(typeof(StructMap<>))") +
            "public sealed class StructMap<T>:IStreamItemMap<GenericItems<T>,T> where T:struct{public T Map(GenericItems<T> r,T item,CancellationToken c)=>item;}";
        if (failure == "duplicate") source = source.Replace("Stage.Hook(typeof(GenericFirst<>))", "Stage.Hook(typeof(GenericFirst<>)),Stage.Hook(typeof(GenericFirst<int>))");
        if (failure == "overlap") source = source.Replace("c.Handlers(typeof(GenericItems<>),DispatchFamily.Stream,typeof(GenericHandler<>));",
            "c.Handlers(typeof(GenericItems<>),DispatchFamily.Stream,typeof(GenericHandler<>));c.Handlers<GenericItems<int>>(DispatchFamily.Stream,typeof(GenericHandler<int>));");
        if (failure == "ambiguous-open") source = source.Replace("typeof(GenericHandler<>));", "typeof(GenericHandler<>),typeof(OtherOpenHandler<>));") +
            "[ZendiatorHandler] public sealed class OtherOpenHandler<T>:IStreamRequestHandler<GenericItems<T>,T>{public System.Collections.Generic.IAsyncEnumerable<T> HandleAsync(GenericItems<T> r,CancellationToken c)=>throw new NotImplementedException();}";
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "StreamOpenReject_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        using var bytes = new MemoryStream(); var emitted = output.Emit(bytes);
        SaveOpenEvidence("reject-" + failure, source, driver, bytes.ToArray(), new
        {
            emitted.Success,
            failure,
            candidateOnly = true,
            diagnostics = diagnostics.Select(d => new { d.Id, message = d.GetMessage(), location = d.Location.SourceSpan.ToString(), additional = d.AdditionalLocations.Select(l => l.SourceSpan.ToString()) }),
            compilerDiagnostics = emitted.Diagnostics.Select(d => new { d.Id, message = d.GetMessage() })
        });
        Assert.Contains(diagnostics, d => d.Id != "ZEN0037" && d.Severity == DiagnosticSeverity.Error);
        Assert.All(diagnostics.Where(d => d.Id != "ZEN0037"), d => Assert.True(d.Location.IsInSource));
        if (failure is "overlap" or "duplicate") Assert.Contains(diagnostics, d => d.AdditionalLocations.Count == 1);
        Assert.DoesNotContain(driver.GetRunResult().Results.Single().GeneratedSources, s => s.HintName.StartsWith("Zendiator.Stream", StringComparison.Ordinal));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
    }
}
