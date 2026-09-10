using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Zendiator.SourceGenerator;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed class DiConfigurationTests
{
    private const string Services = "global::Microsoft.Extensions.DependencyInjection.IServiceCollection";
    private const string Head = "using Zendiator; using Zendiator.DependencyInjection; using Microsoft.Extensions.DependencyInjection; using System; using System.Threading; using System.Threading.Tasks; namespace App; ";
    private const string Fixtures = """
        public sealed class Marker;
        public sealed class ContractsMarker;
        public sealed class PassBehavior;
        public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
            where TRequest : IRequest<TResponse>
        {
            public ValueTask<TResponse> HandleAsync<N>(TRequest r, N n, CancellationToken c)
                where N : struct, IRequestContinuation<TRequest, TResponse> => n.InvokeAsync(r, c);
        }
        public sealed class CommandBehavior<TRequest> : IPipelineBehavior<TRequest>
            where TRequest : IRequest
        {
            public ValueTask HandleAsync<N>(TRequest r, N n, CancellationToken c)
                where N : struct, IRequestContinuation<TRequest> => n.InvokeAsync(r, c);
        }
        public sealed class SyncBehavior<TRequest, TResponse> : ISyncPipelineBehavior<TRequest, TResponse>
            where TRequest : ISyncRequest<TResponse>, allows ref struct
        {
            public TResponse Handle<N>(TRequest r, N n, CancellationToken c)
                where N : struct, ISyncRequestContinuation<TRequest, TResponse> => n.Invoke(r, c);
        }
        public sealed record UserCreated(int UserId) : INotification;
        public sealed class AuditHandler : INotificationHandler<UserCreated>
        {
            public ValueTask HandleAsync(UserCreated n, CancellationToken c) => default;
        }
        public sealed class EmailHandler : INotificationHandler<UserCreated>
        {
            public ValueTask HandleAsync(UserCreated n, CancellationToken c) => default;
        }
        """;
    private static readonly MetadataReference[] References = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
        .Select(p => MetadataReference.CreateFromFile(p)).ToArray();
    private static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.Preview)
        .WithFeatures(new[] { new KeyValuePair<string, string>("InterceptorsNamespaces", "Zendiator.Generated.Interceptors") });
    private static CSharpCompilation Compilation(string source, string name = "Test") => CSharpCompilation.Create(name,
        [CSharpSyntaxTree.ParseText(source, ParseOptions)], References,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    private static GeneratorDriver Driver() => CSharpGeneratorDriver.Create([new ZendiatorGenerator().AsSourceGenerator()],
        parseOptions: ParseOptions, driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, true));

    private static GeneratorDriverRunResult Run(string source, bool success)
    {
        var input = Compilation(source);
        var driver = Driver().RunGeneratorsAndUpdateCompilation(input, out var output, out var diagnostics);
        if (success)
        {
            Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
            Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        }
        return driver.GetRunResult();
    }

    private const string FullLambda = """
        var collection = new ServiceCollection();
        collection.AddZendiator(static configuration =>
        {
            configuration.Namespace = "App.Generated";
            configuration.ServiceLifetime = ServiceLifetime.Scoped;
            configuration.RegisterServicesFromAssemblyContaining<Marker>();
            configuration.RegisterServicesFromAssembly(typeof(ContractsMarker).Assembly);
            configuration.AddOpenBehavior(typeof(LoggingBehavior<,>), order: 0);
            configuration.AddOpenBehavior(typeof(CommandBehavior<>), order: 10);
            configuration.AddOpenBehavior(typeof(SyncBehavior<,>), order: 20);
            configuration.AddNotification<UserCreated>();
            configuration.ConfigureHandlerOrder(typeof(AuditHandler), order: 0);
            configuration.ConfigureHandlerOrder(typeof(EmailHandler), order: 10);
        });
        """;

    [Fact]
    public void Bare_call_without_attributes_is_silent_for_now()
    {
        var result = Run(Head + Fixtures + "public sealed class App { public void Register(" + Services + " services) { services.AddZendiator(); } }", true);
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void Full_lambda_generates_registrar_and_shims()
    {
        var result = Run(Head + Fixtures + "public sealed class App { public void Register(" + Services + " services) { " + FullLambda + " } }", true);
        var mediator = result.GeneratedTrees.Single(t => t.ToString().Contains("internal const string StructureFingerprint")).ToString();
        Assert.Contains("internal const string StructureFingerprint", mediator);
        Assert.DoesNotContain("class ZendiatorServiceCollectionExtensions", mediator);
        Assert.DoesNotContain("MakeGeneric", mediator);
        var shims = result.GeneratedTrees.Single(t => t.ToString().Contains("AddZendiatorShims")).ToString();
        Assert.Contains("InterceptsLocation", shims);
        Assert.Contains("file sealed class InterceptsLocationAttribute", shims);
    }

    [Fact]
    public void Null_configuration_is_rejected()
    {
        var result = Run(Head + Fixtures + "public sealed class App { public void Register(" + Services + " services) { services.AddZendiator(null); } }", false);
        Assert.Contains(result.Diagnostics, d => d.Id == "ZEN0017");
    }

    [Fact]
    public void Const_namespace_is_accepted()
    {
        var source = Head + Fixtures + "public sealed class App { public void Register(" + Services + " services) { const string Generated = \"App.Generated\"; services.AddZendiator(static configuration => { configuration.Namespace = Generated; }); } }";
        var result = Run(source, false);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Same_structure_in_different_spellings_shares()
    {
        var source = Head + "using M = App.Marker; " + Fixtures + "public sealed class App { public void Register(" + Services + " services) { "
            + "services.AddZendiator(static configuration => { configuration.RegisterServicesFromAssemblyContaining<M>(); }); "
            + "services.AddZendiator(static configuration => { configuration.RegisterServicesFromAssembly(typeof(global::App.Marker).Assembly); }); } }";
        var result = Run(source, false);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "ZEN0016");
    }

    [Fact]
    public void Same_structure_twice_is_silent()
    {
        var source = Head + Fixtures + "public sealed class App { public void Register(" + Services + " services) { "
            + "services.AddZendiator(static configuration => { configuration.RegisterServicesFromAssemblyContaining<Marker>(); }); "
            + "services.AddZendiator(static configuration => { configuration.RegisterServicesFromAssemblyContaining<Marker>(); }); } }";
        var result = Run(source, false);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Different_structures_conflict()
    {
        var contracts = Compilation("namespace Contracts; public sealed class RemoteMarker;", "Contracts");
        using var stream = new MemoryStream();
        Assert.True(contracts.Emit(stream).Success);
        var reference = MetadataReference.CreateFromImage(stream.ToArray());
        var source = Head + Fixtures + "public sealed class App { public void Register(" + Services + " services) { "
            + "services.AddZendiator(static configuration => { configuration.RegisterServicesFromAssemblyContaining<Marker>(); }); "
            + "services.AddZendiator(static configuration => { configuration.RegisterServicesFromAssemblyContaining<Contracts.RemoteMarker>(); }); } }";
        var input = Compilation(source).AddReferences(reference);
        var driver = Driver().RunGeneratorsAndUpdateCompilation(input, out _, out _);
        var result = driver.GetRunResult();
        Assert.Contains(result.Diagnostics, d => d.Id == "ZEN0016");
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void Attributes_plus_lambda_conflicts()
    {
        var source = Head + "[GenerateZendiator] public sealed partial class Zendiator; " + Fixtures + "public sealed class App { public void Register(" + Services + " services) { "
            + "services.AddZendiator(static configuration => { configuration.RegisterServicesFromAssemblyContaining<Marker>(); }); } }";
        var result = Run(source, false);
        Assert.Contains(result.Diagnostics, d => d.Id == "ZEN0015");
    }

    [Fact]
    public void Attributes_plus_bare_call_stays_silent()
    {
        var source = Head + "[GenerateZendiator] public sealed partial class Zendiator; "
            + "public readonly record struct Ping : IRequest<int>; "
            + "public sealed class PingHandler : IRequestHandler<Ping, int> { public ValueTask<int> HandleAsync(Ping r, CancellationToken c) => new(1); } "
            + "public sealed class App { public void Register(" + Services + " services) { services.AddZendiator(); } }";
        var result = Run(source, true);
        Assert.Single(result.GeneratedTrees);
    }

    [Theory]
    [InlineData("if (true) { configuration.RegisterServicesFromAssemblyContaining<Marker>(); }")]
    [InlineData("foreach (var type in new[] { typeof(Marker) }) { configuration.AddNotification<UserCreated>(); }")]
    [InlineData("configuration.RegisterServicesFromAssemblyContaining<Marker>(); return;")]
    [InlineData("System.Console.WriteLine(configuration.Namespace);")]
    [InlineData("configuration.ToString();")]
    [InlineData("configuration.Namespace = 42;")]
    public void Unsupported_statements_and_shapes_are_diagnosed(string body)
    {
        var source = Head + Fixtures + "public sealed class App { public void Register(" + Services + " services) { "
            + "services.AddZendiator(configuration => { " + body + " }); } }";
        var result = Run(source, false);
        Assert.Contains(result.Diagnostics, d => d.Id == "ZEN0017");
    }

    [Theory]
    [InlineData("services.AddZendiator(App.Configure);", "public static void Configure(ZendiatorConfiguration configuration) { configuration.RegisterServicesFromAssemblyContaining<Marker>(); }")]
    [InlineData("System.Action<ZendiatorConfiguration> configure = static configuration => { configuration.RegisterServicesFromAssemblyContaining<Marker>(); }; services.AddZendiator(configure);", "")]
    [InlineData("services.AddZendiator(async configuration => { await System.Threading.Tasks.Task.Yield(); });", "")]
    [InlineData("string prefix = string.Concat(\"A\", \"pp\"); services.AddZendiator(configuration => { configuration.Namespace = prefix; });", "")]
    [InlineData("var other = configuration; services.AddZendiator(configuration => { other.RegisterServicesFromAssemblyContaining<Marker>(); });", "")]
    public void Unsupported_callback_forms_are_diagnosed(string registration, string helper)
    {
        var source = Head + Fixtures + "public sealed class App { " + helper + " public void Register(" + Services + " services) { " + registration + " } }";
        var result = Run(source, false);
        Assert.Contains(result.Diagnostics, d => d.Id == "ZEN0017");
    }

    [Theory]
    [InlineData("configuration.Namespace = \"0Bad\";")]
    [InlineData("configuration.Namespace = \"App.Generated\"; configuration.Namespace = \"App.Other\";")]
    [InlineData("configuration.AddOpenBehavior(typeof(PassBehavior), order: 0); configuration.AddOpenBehavior(typeof(PassBehavior), order: 1);")]
    [InlineData("configuration.AddOpenBehavior(typeof(PassBehavior), order: 0); configuration.AddOpenBehavior(typeof(LoggingBehavior<,>), order: 0);")]
    [InlineData("configuration.AddNotification<UserCreated>(); configuration.AddNotification<UserCreated>();")]
    [InlineData("configuration.ConfigureHandlerOrder(typeof(AuditHandler), order: 0); configuration.ConfigureHandlerOrder(typeof(AuditHandler), order: 1);")]
    [InlineData("configuration.ServiceLifetime = (ServiceLifetime)42;")]
    public void Invalid_values_are_diagnosed(string body)
    {
        var source = Head + Fixtures + "public sealed class App { public void Register(" + Services + " services) { "
            + "services.AddZendiator(static configuration => { " + body + " }); } }";
        var result = Run(source, false);
        Assert.Contains(result.Diagnostics, d => d.Id == "ZEN0018");
    }

    [Fact]
    public void Unrelated_addzendiator_is_ignored()
    {
        var source = Head + Fixtures + """
            public static class OtherExtensions
            {
                public static IServiceCollection AddZendiator(this IServiceCollection services) { return services; }
            }
            public sealed class App { public void Register(IServiceCollection services) { OtherExtensions.AddZendiator(services); } }
            """;
        var result = Run(source, false);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void Expression_bodied_calls_participate_in_structure_comparison()
    {
        var source = Head + Fixtures + "public sealed class App { public void Register(" + Services + " services) { "
            + "services.AddZendiator(static configuration => configuration.RegisterServicesFromAssemblyContaining<Marker>()); "
            + "services.AddZendiator(static configuration => configuration.Namespace = \"App.Generated\"); } }";
        var result = Run(source, false);
        Assert.Contains(result.Diagnostics, d => d.Id == "ZEN0016");
    }

    [Fact]
    public void Old_style_call_under_zendiator_namespace_binds_generated()
    {
        var source = "using Zendiator; using Zendiator.DependencyInjection; using Microsoft.Extensions.DependencyInjection; using System; using System.Threading; using System.Threading.Tasks; namespace Zendiator.Foo; "
            + "[GenerateZendiator] public sealed partial class Zendiator; "
            + "public readonly record struct Ping : IRequest<int>; "
            + "public sealed class PingHandler : IRequestHandler<Ping, int> { public ValueTask<int> HandleAsync(Ping r, CancellationToken c) => new(1); } "
            + "public sealed class App { public void Register(IServiceCollection services) { services.AddZendiator(); } }";
        var result = Run(source, true);
        Assert.Single(result.GeneratedTrees);
    }

    [Fact]
    public void Di_lambda_under_zendiator_namespace_generates()
    {
        var source = "using Zendiator; using Zendiator.DependencyInjection; using Microsoft.Extensions.DependencyInjection; using System; namespace Zendiator.Foo; "
            + "public sealed class App { public void Register(IServiceCollection services) { services.AddZendiator(static configuration => { configuration.Namespace = \"Foo.Generated\"; }); } }";
        var result = Run(source, true);
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var mediator = result.GeneratedTrees.Single(t => t.ToString().Contains("namespace Foo.Generated;")).ToString();
        Assert.Contains("interface IZendiator", mediator);
    }

    [Fact]
    public void Static_form_call_generates_plain_shim()
    {
        var source = Head + Fixtures + "public sealed class App { public void Register(" + Services + " services) { "
            + "global::Zendiator.DependencyInjection.ZendiatorServiceCollectionExtensions.AddZendiator(services, static configuration => { configuration.RegisterServicesFromAssemblyContaining<Marker>(); }); } }";
        var result = Run(source, true);
        var shims = result.GeneratedTrees.Single(t => t.ToString().Contains("AddZendiatorShims")).ToString();
        Assert.Contains("InterceptsLocation", shims);
        Assert.DoesNotContain("(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services,", shims);
    }

    [Fact]
    public void Shim_invokes_the_callback_exactly_once_textually()
    {
        var source = Head + Fixtures + "public sealed class App { public void Register(" + Services + " services) { "
            + "services.AddZendiator(static configuration => { configuration.RegisterServicesFromAssemblyContaining<Marker>(); }); } }";
        var result = Run(source, true);
        var shims = result.GeneratedTrees.Single(t => t.ToString().Contains("AddZendiatorShims")).ToString();
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(shims, "configure\\(configuration\\);"));
        Assert.Contains("ThrowIfNull(configure)", shims);
    }

    [Fact]
    public void Attribute_and_config_orders_must_agree()
    {
        var source = Head + Fixtures + "public sealed class App { public void Register(" + Services + " services) { "
            + "services.AddZendiator(static configuration => { configuration.ConfigureHandlerOrder(typeof(AuditHandler), order: 9); }); } }";
        var withAttr = source.Replace(
            "public sealed class AuditHandler : INotificationHandler<UserCreated>",
            "[HandlerOrder(Order = 1)] public sealed class AuditHandler : INotificationHandler<UserCreated>");
        var conflicted = Run(withAttr, false);
        Assert.Contains(conflicted.Diagnostics, d => d.Id == "ZEN0018");
        var agreed = Run(withAttr.Replace("order: 9", "order: 1"), true);
        Assert.Empty(agreed.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void Di_call_binds_package_before_generation()
    {
        var source = Head + Fixtures + "public sealed class App { public void Register(" + Services + " services) { "
            + "services.AddZendiator(static configuration => { configuration.RegisterServicesFromAssemblyContaining<Marker>(); }); } }";
        var input = Compilation(source);
        var tree = input.SyntaxTrees[0];
        var model = input.GetSemanticModel(tree);
        var invocation = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().First(i => i.Expression.ToString().Contains("AddZendiator"));
        var symbol = model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        Assert.NotNull(symbol);
        Assert.Equal("ZendiatorServiceCollectionExtensions", symbol.OriginalDefinition.ContainingType.Name);
        Assert.Equal("Zendiator.DependencyInjection", symbol.OriginalDefinition.ContainingType.ContainingNamespace.ToDisplayString());
    }
}
