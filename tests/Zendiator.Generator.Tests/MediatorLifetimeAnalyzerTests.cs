using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Zendiator.SourceGenerator;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed class MediatorLifetimeAnalyzerTests
{
    private const string Fixture = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.Extensions.DependencyInjection;
        using Zendiator;
        namespace App;
        [GenerateZendiator] public sealed partial class Zendiator;
        public readonly record struct Ping : IRequest<int>;
        public sealed class Handler : IRequestHandler<Ping, int>
        {
            public ValueTask<int> HandleAsync(Ping request, CancellationToken token) => new(42);
        }
        public static class Usage
        {
            // BODY
        }
        """;
    private static readonly MetadataReference[] References = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path)).ToArray();

    private static async Task<string[]> Warnings(string body)
    {
        var parse = new CSharpParseOptions(LanguageVersion.Preview);
        var input = CSharpCompilation.Create("AnalyzerFixture",
            [CSharpSyntaxTree.ParseText(Fixture.Replace("// BODY", body), parse)], References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        var driver = CSharpGeneratorDriver.Create([new ZendiatorGenerator().AsSourceGenerator()], parseOptions: parse);
        driver.RunGeneratorsAndUpdateCompilation(input, out var output, out var generatorDiagnostics);
        Assert.Empty(generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        var diagnostics = await output.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new MediatorLifetimeAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
        return diagnostics.Select(d => d.Id).OrderBy(id => id).ToArray();
    }

    [Theory]
    [InlineData("public static bool Check(IZendiator mediator) => mediator is IDisposable;", "ZEN0021")]
    [InlineData("public static IDisposable? Cast(IZendiator mediator) => mediator as IDisposable;", "ZEN0021")]
    [InlineData("public static IDisposable Cast(IZendiator mediator) => (IDisposable)mediator;", "ZEN0021")]
    [InlineData("public static bool Check(IZendiator mediator) => mediator is not IDisposable;", "ZEN0021")]
    public async Task Explicit_disposable_handling_warns(string body, string expected) =>
        Assert.Equal([expected], await Warnings(body));

    [Theory]
    [InlineData("public static IZendiator Escape(IServiceProvider provider) { using var scope = provider.CreateScope(); var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>(); return mediator; }")]
    [InlineData("public static IZendiator Escape(IServiceProvider provider) { using var scope = provider.CreateScope(); return scope.ServiceProvider.GetRequiredService<IZendiator>(); }")]
    [InlineData("public static IZendiator Escape(IServiceProvider provider) { using (var scope = provider.CreateScope()) { return scope.ServiceProvider.GetRequiredService<IZendiator>(); } }")]
    [InlineData("public static IZendiator Escape(IServiceProvider provider) { using var scope = provider.CreateScope(); var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>(); return (IZendiator)mediator; }")]
    public async Task Returning_from_using_scope_warns(string body) =>
        Assert.Equal(["ZEN0022"], await Warnings(body));

    [Theory]
    [InlineData("public static async Task<int> Bad(IServiceProvider provider) { var scope = provider.CreateScope(); var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>(); scope.Dispose(); return await mediator.SendAsync(new Ping()); }")]
    [InlineData("public static int Bad(IServiceProvider provider) { var scope = provider.CreateScope(); var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>(); scope.Dispose(); return mediator.SendAsync(new Ping()).Result; }")]
    [InlineData("public static async Task<int> Bad(IServiceProvider provider) { var scope = provider.CreateAsyncScope(); var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>(); await scope.DisposeAsync(); return await mediator.SendAsync(new Ping()); }")]
    public async Task Sending_after_direct_disposal_warns(string body) =>
        Assert.Equal(["ZEN0023"], await Warnings(body));

    [Theory]
    [InlineData("public static async Task<int> Good(IServiceProvider provider) { using var scope = provider.CreateScope(); var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>(); return await mediator.SendAsync(new Ping()); }")]
    [InlineData("public static IZendiator Good(IServiceProvider provider) => provider.GetRequiredService<IZendiator>();")]
    [InlineData("public static async Task<int> Conditional(IServiceProvider provider, bool stop) { var scope = provider.CreateScope(); var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>(); if (stop) scope.Dispose(); return await mediator.SendAsync(new Ping()); }")]
    [InlineData("public static async Task<int> OtherScope(IServiceProvider provider) { var first = provider.CreateScope(); var mediator = first.ServiceProvider.GetRequiredService<IZendiator>(); var second = provider.CreateScope(); second.Dispose(); return await mediator.SendAsync(new Ping()); }")]
    [InlineData("public static async Task<int> Reassigned(IServiceProvider provider) { var first = provider.CreateScope(); var mediator = first.ServiceProvider.GetRequiredService<IZendiator>(); first.Dispose(); var second = provider.CreateScope(); mediator = second.ServiceProvider.GetRequiredService<IZendiator>(); return await mediator.SendAsync(new Ping()); }")]
    [InlineData("public static bool Unrelated(Other.IZendiator mediator) => mediator is IDisposable; public static class Other { public interface IZendiator; }")]
    [InlineData("public static Func<ValueTask<int>> Deferred(IServiceProvider provider) { var scope = provider.CreateScope(); var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>(); scope.Dispose(); return () => mediator.SendAsync(new Ping()); }")]
    [InlineData("public static async Task<int> NotAwaited(IServiceProvider provider) { var scope = provider.CreateAsyncScope(); var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>(); scope.DisposeAsync(); return await mediator.SendAsync(new Ping()); }")]
    public async Task Unproven_lifetime_mistakes_do_not_warn(string body) =>
        Assert.Empty(await Warnings(body));
}
