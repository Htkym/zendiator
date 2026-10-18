using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    [Fact]
    public async Task Mixed_open_zero_Stream_and_closed_SendSync_share_public_registration_without_losing_constraints_or_routes()
    {
        foreach (var shape in new[] { "general", "constrained", "consumer-constraint", "three-stage-blocked" })
        {
            var constrained = shape is "constrained" or "consumer-constraint";
            var handlers = "c.Handlers(typeof(GenericItems<>),DispatchFamily.Stream,typeof(GenericHandler<>));" +
                "c.Handlers<MixedPing>(DispatchFamily.SendSync,typeof(MixedPingHandler));";
            var stream = "c.Pipeline(typeof(GenericItems<>),DispatchFamily.Stream" +
                (shape == "three-stage-blocked" ? ",Stage.Hook(typeof(GenericFirst<>)),Stage.Hook(typeof(GenericMap<>)),Stage.Hook(typeof(GenericLast<>))" : "") + ");";
            var send = "c.Pipeline<MixedPing>(DispatchFamily.SendSync);";
            var types = StreamGenericTypes.Replace("public System.Collections.Generic.IAsyncEnumerable<T> HandleAsync",
                "public static CancellationToken Token; public static object? LastRequest; public System.Collections.Generic.IAsyncEnumerable<T> HandleAsync")
                .Replace("{ p.Call(this);return p.Source(r.Value); }", "{ Token=c;LastRequest=r;p.Call(this);return p.Source(r.Value); }");
            if (constrained) types = types.Replace("IStreamRequestHandler<GenericItems<T>,T>, IDisposable {",
                "IStreamRequestHandler<GenericItems<T>,T>, IDisposable where T:class,new() {");
            var source = "using System.Linq;using System.Collections.Generic;using Microsoft.Extensions.DependencyInjection;" +
                ListFixture(handlers + (constrained ? stream + send : send + stream)) + types + """
                public sealed record MixedPing(int Value):ISyncRequest<int>;
                [ZendiatorHandler] public sealed class MixedPingHandler:ISyncRequestHandler<MixedPing,int> {
                    public int Handle(MixedPing r,CancellationToken c)=>r.Value+1;
                }
                """;
            if (shape == "consumer-constraint") source += """
                public sealed class NoDefault { public NoDefault(int value){} }
                public static class InvalidMixedConsumer {
                    public static object Call(IServiceProvider p)=>p.GetRequiredService<global::App.Generated.Reads.IZendiator>()
                        .StreamAsync(new GenericItems<NoDefault>(new NoDefault(1)));
                }
                """;
            else if (shape != "three-stage-blocked") source += OpenZeroConsumer.Replace("__WHERE__", constrained ? "where T:class,new()" : "")
                .Replace("__CALLS__", "Check(mediator.Send(new MixedPing(41))==42,\"mixed Send before Stream\");" +
                    (constrained ? "results.Add(await Read(mediator,new GenericItems<OpenValue>(new OpenValue()),p));" :
                        "results.Add(await Read(mediator,new GenericItems<int>(7),p));results.Add(await Read(mediator,new GenericItems<string>(\"item\"),p));") +
                    "Check(mediator.Send(new MixedPing(9))==10,\"mixed Send after Stream\");")
                .Replace("__REQUEST__", constrained ? "new GenericItems<OpenValue>(new OpenValue())" : "new GenericItems<int>(7)")
                .Replace("__CLOSED_TYPES__", constrained ? "1" : "2");
            var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "MixedOpenZero_" + Guid.NewGuid().ToString("N")),
                out var output, out var diagnostics);
            var generated = driver.GetRunResult().Results.Single().GeneratedSources;
            using var bytes = new MemoryStream();
            var emitted = output.Emit(bytes);
            var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_STREAM_PUBLIC_EVIDENCE_DIR");
            var folder = evidence == null ? null : Path.Combine(evidence, "mixed-open-zero", shape);
            if (folder != null)
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "input.cs"), source);
                File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
                foreach (var file in generated) File.WriteAllText(Path.Combine(folder, file.HintName), file.SourceText.ToString());
                File.WriteAllText(Path.Combine(folder, "compile.json"), JsonSerializer.Serialize(new
                {
                    shape,
                    emitted.Success,
                    diagnostics = diagnostics.Select(d => new { d.Id, severity = d.Severity.ToString(), message = d.GetMessage(), inSource = d.Location.IsInSource }),
                    compiler = emitted.Diagnostics.Select(d => new { d.Id, severity = d.Severity.ToString(), message = d.GetMessage(), inSource = d.Location.IsInSource }),
                    sources = generated.Select(f => f.HintName)
                }));
            }
            if (shape == "three-stage-blocked")
            {
                Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
                Assert.Equal(2, diagnostics.Length);
                Assert.All(diagnostics, d => { Assert.Equal("ZEN0037", d.Id); Assert.True(d.Location.IsInSource); });
                AssertNoCanonicalOutput(generated);
                Assert.DoesNotContain(generated, f => f.HintName.EndsWith(".Interceptors.g.cs", StringComparison.Ordinal));
                continue;
            }
            Assert.Empty(diagnostics);
            var streamRoute = Assert.Single(generated, f => IsCanonicalRoute(f) &&
                f.SourceText.ToString().Contains("global::App.GenericItems<", StringComparison.Ordinal)).SourceText.ToString();
            Assert.Contains("__ZendiatorStreamCanonical_", streamRoute);
            Assert.Contains("Send(", CanonicalRouteSource(generated, "App.MixedPing"));
            var shared = CanonicalCompositionSource(generated);
            Assert.Contains("expected.Handlers(typeof(global::App.GenericItems<>)", shared);
            Assert.Contains("expected.Pipeline(typeof(global::App.GenericItems<>), global::Zendiator.DispatchFamily.Stream);", shared);
            Assert.Contains("expected.Handlers<global::App.MixedPing>", shared);
            Assert.Contains("expected.Pipeline<global::App.MixedPing>", shared);
            Assert.Equal(2, generated.Count(IsCanonicalRoute));
            if (constrained) Assert.Contains(" : class, new()", streamRoute);
            if (shape == "consumer-constraint")
            {
                Assert.False(emitted.Success);
                Assert.Contains(emitted.Diagnostics, d => d.Id == "CS0310" && d.Severity == DiagnosticSeverity.Error && d.Location.IsInSource);
                continue;
            }
            Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
            Assert.Empty(emitted.Diagnostics.Where(d => d.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error));
            bytes.Position = 0;
            var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
            var runtime = await (Task<string>)assembly.GetType("App.OpenZeroConsumer")!.GetMethod("Run")!.Invoke(null, null)!;
            if (folder != null) File.WriteAllText(Path.Combine(folder, "runtime.json"), runtime);
        }
    }
}
