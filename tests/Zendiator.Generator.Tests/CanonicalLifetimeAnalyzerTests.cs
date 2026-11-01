using System.Collections.Immutable;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Zendiator.SourceGenerator;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    [Fact]
    public async Task Canonical_lifetime_uses_scoped_registration_identity_and_actual_dispatch_symbols()
    {
        var tree = CSharpSyntaxTree.ParseText(CanonicalLifetimeSource, SyncParseOptions, path: "canonical-lifetime.cs");
        var input = SyncCompilation("", "CanonicalLifetime").RemoveAllSyntaxTrees().AddSyntaxTrees(tree);
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(input, out var output, out var generatorDiagnostics);
        Assert.Empty(generatorDiagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        var diagnostics = await output.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new MediatorLifetimeAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
        var expected = new[]
        {
            ("ZEN0021", "mediator is IDisposable"),
            ("ZEN0022", "return scope.ServiceProvider.GetRequiredService<IZendiator>();"),
            ("ZEN0023", "direct.Send(new Ping())"),
            ("ZEN0023", "mediator.SendAsync(new Pong())"),
            ("ZEN0023", "mediator.StreamAsync(new Numbers())"),
            ("ZEN0023", "mediator.StreamSource(new Numbers())"),
            ("ZEN0023", "mediator.SendAll(new Ping())"),
            ("ZEN0023", "mediator.SendAllAsync(new Pong())"),
            ("ZEN0023", "fromFactory.SendAsync(new Pong())")
        };
        Assert.Equal(expected.Length, diagnostics.Length);
        foreach (var (id, text) in expected)
        {
            var diagnostic = Assert.Single(diagnostics, d => d.Id == id && d.Location.SourceSpan.Start == CanonicalLifetimeSource.IndexOf(text, StringComparison.Ordinal));
            Assert.Same(tree, diagnostic.Location.SourceTree);
            Assert.Equal(text, CanonicalLifetimeSource.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length));
            Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
            Assert.Equal("Keep the DI scope alive until dispatch completes; dispose the scope, not the mediator", diagnostic.GetMessage());
        }

        var baselinePath = Environment.GetEnvironmentVariable("ZENDIATOR_LIFETIME_BASELINE_ASSEMBLY");
        ImmutableArray<Diagnostic>? baselineDiagnostics = null;
        if (!string.IsNullOrEmpty(baselinePath))
        {
            var context = new AssemblyLoadContext("CanonicalLifetimeBaseline", isCollectible: true);
            try
            {
                using var baseline = File.OpenRead(baselinePath);
                var assembly = context.LoadFromStream(baseline);
                var analyzer = (DiagnosticAnalyzer)Activator.CreateInstance(assembly.GetType("Zendiator.SourceGenerator.MediatorLifetimeAnalyzer", throwOnError: true)!)!;
                baselineDiagnostics = await output.WithAnalyzers(ImmutableArray.Create(analyzer)).GetAnalyzerDiagnosticsAsync();
                Assert.Empty(baselineDiagnostics.Value);
            }
            finally
            {
                context.Unload();
            }
        }

        using var bytes = new MemoryStream();
        var emit = output.Emit(bytes);
        Assert.True(emit.Success, string.Join("\n", emit.Diagnostics));
        var artifactRoot = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        if (string.IsNullOrEmpty(artifactRoot))
            return;
        var directory = Path.Combine(artifactRoot, "canonical-lifetime");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "input.cs"), CanonicalLifetimeSource);
        File.WriteAllBytes(Path.Combine(directory, "generated.dll"), bytes.ToArray());
        foreach (var source in driver.GetRunResult().Results.Single().GeneratedSources)
            File.WriteAllText(Path.Combine(directory, source.HintName), source.SourceText.ToString());
        object Describe(Diagnostic d) => new
        {
            d.Id,
            message = d.GetMessage(),
            severity = d.Severity.ToString(),
            start = d.Location.SourceSpan.Start,
            length = d.Location.SourceSpan.Length,
            text = CanonicalLifetimeSource.Substring(d.Location.SourceSpan.Start, d.Location.SourceSpan.Length),
            line = d.Location.GetLineSpan().StartLinePosition.Line + 1,
            column = d.Location.GetLineSpan().StartLinePosition.Character + 1
        };
        var types = new[] { "Lifetime.Generated.App.IZendiator", "Lifetime.Generated.App.Zendiator" }
            .Select(name => output.GetTypeByMetadataName(name)!).ToArray();
        File.WriteAllText(Path.Combine(directory, "diagnostics.json"), JsonSerializer.Serialize(new
        {
            compileSuccess = emit.Success,
            runtimeReplays = 0,
            diagnostics = diagnostics.OrderBy(d => d.Location.SourceSpan.Start).Select(Describe),
            baselineDiagnostics = baselineDiagnostics?.Select(Describe),
            types = types.Select(t => new
            {
                name = t.ToDisplayString(),
                assembly = t.ContainingAssembly.Identity.ToString(),
                generatedCodeAttribute = t.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "System.CodeDom.Compiler.GeneratedCodeAttribute"),
                publicMethods = t.GetMembers().OfType<IMethodSymbol>().Where(m => m.DeclaredAccessibility == Accessibility.Public).Select(m => m.ToDisplayString())
            })
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private const string CanonicalLifetimeSource = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using System.Runtime.CompilerServices;
        using Microsoft.Extensions.DependencyInjection;
        using Zendiator;
        using Zendiator.DependencyInjection;
        using Lifetime.Generated.App;
        namespace Lifetime {
        [ZendiatorComposition("lifetime")] public sealed class App;
        public sealed record Ping : ISyncRequest<int>;
        public sealed record Pong : IRequest<int>;
        public sealed record Numbers : IStreamRequest<int>;
        [ZendiatorHandler] public sealed class SyncHandler : ISyncRequestHandler<Ping,int> {
            public int Handle(Ping r,CancellationToken token)=>42;
        }
        [ZendiatorHandler] public sealed class AsyncHandler : IRequestHandler<Pong,int> {
            public ValueTask<int> HandleAsync(Pong r,CancellationToken token)=>new(42);
        }
        [ZendiatorHandler] public sealed class StreamHandler : IStreamSourceHandler<Numbers,int,StreamHandler.Cursor> {
            public ValueTask<Cursor> OpenAsync(Numbers r,CancellationToken token)=>new(new Cursor());
            public sealed class Cursor:IAsyncEnumerator<int>{public int Current=>42;public async ValueTask<bool> MoveNextAsync(){await Task.Yield();return false;}public ValueTask DisposeAsync()=>default;}
        }
        public readonly struct TerminalSink:IStreamSink<int>{public ValueTask<bool> OnNextAsync(int item,CancellationToken token)=>new(true);}
        public sealed class SyncHook : ISendRequestTransform<Ping>,ISendResponseTransform<Ping,int>,ISendExceptionObserver<Ping>,ISendFinallyObserver<Ping> {
            public Ping Before(Ping r,CancellationToken token)=>r;
            public int After(Ping r,int response,CancellationToken token)=>response;
            public void OnException(Ping r,Exception error,CancellationToken token){}
            public void Finally(Ping r,SendExitInfo exit,CancellationToken token){}
        }
        public sealed class AsyncHook : IAsyncSendRequestTransform<Pong>,IAsyncSendResponseTransform<Pong,int>,IAsyncSendExceptionObserver<Pong>,IAsyncSendFinallyObserver<Pong> {
            public ValueTask<Pong> BeforeAsync(Pong r,CancellationToken token)=>new(r);
            public ValueTask<int> AfterAsync(Pong r,int response,CancellationToken token)=>new(response);
            public ValueTask OnExceptionAsync(Pong r,Exception error,CancellationToken token)=>default;
            public ValueTask FinallyAsync(Pong r,SendExitInfo exit,CancellationToken token)=>default;
        }
        public sealed class Filter : IStreamItemFilter<Numbers,int>{public bool Accept(Numbers r,int item,CancellationToken token)=>true;}
        public sealed class FinalFilter : IStreamItemFilter<Numbers,int>{public bool Accept(Numbers r,int item,CancellationToken token)=>true;}
        public sealed class ItemMap : IStreamItemMap<Numbers,int>{public int Map(Numbers r,int item,CancellationToken token)=>item;}
        public static class Usage {
            public static IServiceCollection Register(IServiceCollection services)=>services.AddZendiator<App>(c=>{
                c.Handlers<Ping>(DispatchFamily.SendSync,typeof(SyncHandler));c.Pipeline<Ping>(DispatchFamily.SendSync);
                c.Handlers<Pong>(DispatchFamily.SendAsync,typeof(AsyncHandler));c.Pipeline<Pong>(DispatchFamily.SendAsync);
                c.Handlers<Numbers>(DispatchFamily.Stream,typeof(StreamHandler));c.Pipeline<Numbers>(DispatchFamily.Stream,Stage.Hook<Filter>(),Stage.Hook<ItemMap>(),Stage.Hook<FinalFilter>());
                c.Handlers<Ping>(DispatchFamily.SendAllSync,typeof(SyncHandler));c.Pipeline<Ping>(DispatchFamily.SendAllSync,Stage.Hook<SyncHook>());
                c.Handlers<Pong>(DispatchFamily.SendAllAsync,typeof(AsyncHandler));c.Pipeline<Pong>(DispatchFamily.SendAllAsync,Stage.Hook<AsyncHook>());
            });
            public static bool BadDispose(IZendiator mediator)=>mediator is IDisposable;
            public static IZendiator BadEscape(IServiceProvider provider){using var scope=provider.CreateScope();return scope.ServiceProvider.GetRequiredService<IZendiator>();}
            public static async Task BadDispatch(IServiceProvider provider){
                await using var scope=provider.CreateAsyncScope();
                var direct=scope.ServiceProvider.GetRequiredService<Generated.App.Zendiator>();
                var mediator=scope.ServiceProvider.GetRequiredService<IZendiator>();
                await scope.DisposeAsync();
                _=direct.Send(new Ping());
                _=await mediator.SendAsync(new Pong());
                await foreach(var item in mediator.StreamAsync(new Numbers())){}
                await mediator.StreamSource(new Numbers()).ConsumeAsync(new TerminalSink());
                _=mediator.SendAll(new Ping());
                _=await mediator.SendAllAsync(new Pong());
            }
            public static async Task Good(IServiceProvider provider){
                await using var scope=provider.CreateAsyncScope();
                var direct=scope.ServiceProvider.GetRequiredService<Generated.App.Zendiator>();
                var mediator=scope.ServiceProvider.GetRequiredService<IZendiator>();
                _=direct.Send(new Ping());_=await mediator.SendAsync(new Pong());
                await foreach(var item in mediator.StreamAsync(new Numbers())){}
                await mediator.StreamSource(new Numbers()).ConsumeAsync(new TerminalSink());
                _=mediator.SendAll(new Ping());_=await mediator.SendAllAsync(new Pong());
            }
            public static async Task UnrelatedExtension(IServiceProvider provider){
                using var scope=provider.CreateScope();var mediator=scope.ServiceProvider.GetRequiredService<IZendiator>();scope.Dispose();
                _=await mediator.SendAsync("ordinary extension");
            }
            public static async Task BadFactory(IServiceScopeFactory factory){
                using var scope=factory.CreateScope();var fromFactory=scope.ServiceProvider.GetRequiredService<IZendiator>();scope.Dispose();
                _=await fromFactory.SendAsync(new Pong());
            }
        }
        public static class OrdinaryExtensions { public static ValueTask<int> SendAsync(this IZendiator mediator,string value)=>new(0); }
        }
        namespace Foreign {
        [AttributeUsage(AttributeTargets.Class)] public sealed class ZendiatorCompositionAttribute(string key):Attribute {public string Key{get;}=key;}
        }
        namespace Other { [Foreign.ZendiatorComposition("ordinary")] public sealed class App; }
        namespace Other.Generated.App {
        public interface IZendiator { ValueTask<int> SendAsync(object request); }
        public sealed class Zendiator : IZendiator { public ValueTask<int> SendAsync(object request)=>new(0); }
        public static class ZendiatorGeneratedRegistrar {
            public static IServiceCollection Add(IServiceCollection services,global::Zendiator.DependencyInjection.ZendiatorConfigurationSnapshot snapshot){
                Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAdd(services,ServiceDescriptor.Scoped(typeof(IZendiator),typeof(Zendiator)));return services;
            }
        }
        public static class Usage {
            public static bool OrdinaryCast(IZendiator mediator)=>mediator is IDisposable;
            public static IZendiator OrdinaryReturn(IServiceProvider provider){using var scope=provider.CreateScope();return scope.ServiceProvider.GetRequiredService<IZendiator>();}
            public static async Task OrdinaryCall(IServiceProvider provider){using var scope=provider.CreateScope();var mediator=scope.ServiceProvider.GetRequiredService<IZendiator>();scope.Dispose();_=await mediator.SendAsync(new object());}
        }
        }
        """;
}
