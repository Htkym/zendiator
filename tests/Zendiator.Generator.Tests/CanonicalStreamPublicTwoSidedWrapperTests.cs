using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    [Fact]
    public Task Stream_two_sided_public_open_normal_registration() => CompileTwoSidedWrapperInput("open");

    [Fact]
    public Task Stream_two_sided_open_generic_mixed_public_registration() => CompileTwoSidedWrapperInput("open-mixed");

    [Fact]
    public async Task Stream_same_side_pair_public_registration_and_three_hook_boundary()
    {
        foreach (var (shape, placement) in new[] { ("open", "before"), ("open", "after"), ("closed-mixed", "before"), ("same-side-three-hooks-blocked", "after") })
            await CompileTwoSidedWrapperInput(shape, placement);
    }

    [Fact]
    public async Task Stream_two_sided_closed_mixed_and_rejection_boundaries()
    {
        foreach (var shape in new[] { "closed-mixed", "same-side-three-hooks-blocked" })
            await CompileTwoSidedWrapperInput(shape);
    }

    private static async Task CompileTwoSidedWrapperInput(string shape, string placement = "split")
    {
        var closed = shape == "closed-mixed";
        var mixed = closed || shape == "open-mixed";
        var types = StreamGenericTypes.Replace("public System.Collections.Generic.IAsyncEnumerable<T> HandleAsync",
            "public static CancellationToken Token; public static object? LastRequest; public System.Collections.Generic.IAsyncEnumerable<T> HandleAsync")
            .Replace("{ p.Call(this);return p.Source(r.Value); }", "{ Token=c;LastRequest=r;p.Call(this);return p.Source(r.Value); }");
        var pipeline = shape == "same-side-three-hooks-blocked" ?
            "Stage.Wrapper(typeof(GenericWrapper<>)),Stage.Hook(typeof(GenericFirst<>)),Stage.Hook(typeof(GenericMap<>)),Stage.Hook(typeof(GenericLast<>))" :
            placement == "before" ? "Stage.Hook(typeof(GenericFirst<>)),Stage.Hook(typeof(GenericLast<>)),Stage.Wrapper(typeof(GenericWrapper<>))" :
            placement == "after" ? "Stage.Wrapper(typeof(GenericWrapper<>)),Stage.Hook(typeof(GenericFirst<>)),Stage.Hook(typeof(GenericLast<>))" :
            "Stage.Hook(typeof(GenericFirst<>)),Stage.Wrapper(typeof(GenericWrapper<>)),Stage.Hook(typeof(GenericLast<>))";
        var body = "c.Handlers(typeof(GenericItems<>),DispatchFamily.Stream,typeof(GenericHandler<>));" +
            "c.Pipeline(typeof(GenericItems<>),DispatchFamily.Stream," + pipeline + ");";
        if (closed)
        {
            foreach (var name in new[] { "GenericItems", "GenericHandler", "GenericFirst", "GenericMap", "GenericLast", "GenericWrapper" }) types = types.Replace(name + "<T>", name);
            types = System.Text.RegularExpressions.Regex.Replace(types, @"\bT\b", "int");
            body = body.Replace("<>", "");
        }
        if (mixed) body += "c.Handlers<TwoSidedPing>(DispatchFamily.SendSync,typeof(TwoSidedPingHandler));c.Pipeline<TwoSidedPing>(DispatchFamily.SendSync);";
        var source = "using System.Linq;using System.Collections.Generic;using Microsoft.Extensions.DependencyInjection;" + ListFixture(body) + types;
        if (mixed) source += """
            public sealed record TwoSidedPing(int Value):ISyncRequest<int>;
            [ZendiatorHandler]public sealed class TwoSidedPingHandler:ISyncRequestHandler<TwoSidedPing,int>{public int Handle(TwoSidedPing r,CancellationToken c)=>r.Value+1;}
            """;
        if (shape is "open" or "closed-mixed" or "open-mixed")
        {
            var consumer = OpenZeroConsumer.Replace("__WHERE__", "")
                .Replace("p.Called.Count==calls+i", "p.Called.Count==calls+4*i")
                .Replace("p.Created.Count==__CLOSED_TYPES__&&p.Called.Count==2*__CLOSED_TYPES__", "p.Created.Count==4*__CLOSED_TYPES__&&p.Called.Count==8*__CLOSED_TYPES__")
                .Replace("p.Released.Count==__CLOSED_TYPES__", "p.Released.Count==4*__CLOSED_TYPES__")
                .Replace("__CALLS__", "results.Add(await Read(mediator,new GenericItems<int>(7),p));" + (closed ? "" : "results.Add(await Read(mediator,new GenericItems<string>(\"item\"),p));"))
                .Replace("__REQUEST__", "new GenericItems<int>(7)").Replace("__CLOSED_TYPES__", closed ? "1" : "2");
            if (placement != "split") consumer = consumer
                .Replace("var dangling=mediator.StreamAsync", "Check(p.Called.Select(call=>p.Created.Single(created=>ReferenceEquals(created.Instance,call)).Role).SequenceEqual(Enumerable.Repeat(new[]{\"W\",\"H\",\"F1\",\"F2\"}," + (closed ? "2" : "4") + ").SelectMany(roles=>roles)),\"declared hook order\");var dangling=mediator.StreamAsync")
                .Replace("released=p.Released.Count,p.SourceDisposals", "released=p.Released.Count,p.SourceDisposals,roles=p.Called.Select(call=>p.Created.Single(created=>ReferenceEquals(created.Instance,call)).Role).ToArray()");
            if (closed)
            {
                foreach (var name in new[] { "GenericItems", "GenericHandler", "GenericFirst", "GenericLast", "GenericWrapper" }) consumer = consumer.Replace(name + "<T>", name).Replace(name + "<>", name).Replace(name + "<int>", name);
                consumer = System.Text.RegularExpressions.Regex.Replace(consumer.Replace("Read<T>(", "Read("), @"\bT\b", "int")
                    .Replace("StreamAsync<int>(", "StreamAsync(");
            }
            if (mixed) consumer = consumer
                .Replace("var results=new List<object>();", "Check(mediator.Send(new TwoSidedPing(41))==42,\"mixed Send before Stream\");var results=new List<object>();")
                .Replace("var dangling=mediator.StreamAsync", "Check(mediator.Send(new TwoSidedPing(9))==10,\"mixed Send after Stream\");var dangling=mediator.StreamAsync");
            source += consumer;
        }
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "TwoSidedWrapper_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        var generated = driver.GetRunResult().Results.Single().GeneratedSources;
        using var bytes = new MemoryStream(); var emitted = output.Emit(bytes);
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_STREAM_PUBLIC_EVIDENCE_DIR");
        var folder = evidence == null ? null : Path.Combine(evidence, "two-sided-wrapper", placement == "split" ? shape : shape + "-" + placement);
        if (folder != null)
        {
            Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, "input.cs"), source); File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
            foreach (var file in generated) File.WriteAllText(Path.Combine(folder, file.HintName), file.SourceText.ToString());
            File.WriteAllText(Path.Combine(folder, "compile.json"), JsonSerializer.Serialize(new
            {
                shape,
                placement,
                emitted.Success,
                diagnostics = diagnostics.Select(d => new { d.Id, message = d.GetMessage(), inSource = d.Location.IsInSource }),
                compiler = emitted.Diagnostics.Select(d => new { d.Id, severity = d.Severity.ToString(), message = d.GetMessage(), inSource = d.Location.IsInSource }),
                sources = generated.Select(f => f.HintName)
            }));
        }
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        if (shape.EndsWith("blocked", StringComparison.Ordinal))
        {
            Assert.Equal(mixed ? 2 : 1, diagnostics.Length); Assert.All(diagnostics, d => { Assert.Equal("ZEN0037", d.Id); Assert.True(d.Location.IsInSource); });
            AssertNoCanonicalOutput(generated); Assert.DoesNotContain(generated, f => f.HintName.EndsWith(".Interceptors.g.cs", StringComparison.Ordinal)); return;
        }
        Assert.Empty(diagnostics); Assert.Empty(emitted.Diagnostics.Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        if (mixed)
        {
            Assert.Equal(2, generated.Count(IsCanonicalRoute)); Assert.Contains("Send(", CanonicalRouteSource(generated, "App.TwoSidedPing"));
            var shared = CanonicalCompositionSource(generated);
            Assert.Contains(closed ? "expected.Handlers<global::App.GenericItems>" : "expected.Handlers(typeof(global::App.GenericItems<>)", shared);
            Assert.Contains(closed ? "expected.Pipeline<global::App.GenericItems>" : "expected.Pipeline(typeof(global::App.GenericItems<>)", shared);
            Assert.Contains("expected.Handlers<global::App.TwoSidedPing>", shared);
        }
        else Assert.Single(generated, f => f.HintName.StartsWith("Zendiator.Canonical.Stream.Public.", StringComparison.Ordinal));
        bytes.Position = 0; var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var runtime = await (Task<string>)assembly.GetType("App.OpenZeroConsumer")!.GetMethod("Run")!.Invoke(null, null)!;
        if (folder != null) File.WriteAllText(Path.Combine(folder, "runtime.json"), runtime);
    }

    [Fact]
    public Task Stream_two_sided_wrapper_existing_open_fixture_keeps_reference_equivalence() =>
        Stream_open_request_typed_candidates_match_reference_after_closing_patterns("wrapper", "int");

    [Fact]
    public void Stream_two_sided_wrapper_closed_generic_fixture_checks_its_membership_boundary() =>
        StreamGenericFixtureFor("int", wrapper: true);
}
