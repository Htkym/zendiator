using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Zendiator.SourceGenerator;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed class DiConfigurationTests
{
    [Theory]
    [InlineData("ZendiatorServiceCollectionExtensions.AddZendiator(services)", true)]
    [InlineData("ZendiatorServiceCollectionExtensions.AddZendiator(configure: c => c.Namespace = \"App.Generated\", services: services)", true)]
    [InlineData("((ZendiatorServiceCollectionExtensions.AddZendiator))(services)", false)]
    public void Static_registration_requires_an_interceptable_call_target(string call, bool supported)
    {
        var source = Head + "public static class Startup { public static IServiceCollection Register(IServiceCollection services) => " + call + "; }";
        var result = Run(source, supported);
        if (supported)
            Assert.NotEmpty(result.GeneratedTrees);
        else
        {
            var diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal("ZEN0020", diagnostic.Id);
            Assert.Equal(call, source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length));
        }
    }

    [Fact]
    public void Conditional_registration_preserves_null_short_circuit_and_evaluates_receiver_once()
    {
        var source = Head + """
            [ZendiatorComposition("app")] public sealed class Marker;
            public sealed record Ping : IRequest<int>;
            [Handler] public sealed class PingHandler : IRequestHandler<Ping,int>
            {
                public ValueTask<int> HandleAsync(Ping p, CancellationToken ct) => new(42);
            }
            public static class Startup
            {
                public static IServiceCollection? Register(Func<IServiceCollection?> get) =>
                    get()?.@AddZendiator<Marker>(c => c.Handlers<Ping>(DispatchFamily.SendAsync, typeof(PingHandler)));
            }
            """;
        Driver().RunGeneratorsAndUpdateCompilation(Compilation(source, "ConditionalRegistration"), out var output, out var diagnostics);
        Assert.Empty(diagnostics);
        using var bytes = new MemoryStream();
        var emitted = output.Emit(bytes);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        bytes.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var register = assembly.GetType("App.Startup")!.GetMethod("Register")!
            .CreateDelegate<Func<Func<IServiceCollection?>, IServiceCollection?>>();
        var calls = 0;
        IServiceCollection? services = null;
        IServiceCollection? Get()
        {
            calls++;
            return services;
        }
        Assert.Null(register(Get));
        services = new ServiceCollection();
        Assert.Same(services, register(Get));
        Assert.Contains(services, descriptor => descriptor.ServiceType == assembly.GetType("App.Generated.Marker.IZendiator"));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Only_package_registration_method_groups_get_a_build_error()
    {
        var input = Compilation(Head + """
            [ZendiatorComposition("app")] public sealed class Marker;
            public static class Other { public static void AddZendiator() { } }
            public static class Startup
            {
                public static void Register(IServiceCollection services)
                {
                    Func<IServiceCollection, Action<ZendiatorConfiguration>, IServiceCollection> open =
                        ZendiatorServiceCollectionExtensions.AddZendiator<Marker>;
                    Func<Action<ZendiatorConfiguration>, IServiceCollection> closed = services.AddZendiator<Marker>;
                    Action unrelated = Other.AddZendiator;
                    _ = nameof(ZendiatorServiceCollectionExtensions.AddZendiator);
                }
            }
            """);
        Assert.Empty(input.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        var diagnostics = await input.WithAnalyzers([new RegistrationCallAnalyzer()]).GetAnalyzerDiagnosticsAsync();
        Assert.Equal(new[] { "ZendiatorServiceCollectionExtensions.AddZendiator<Marker>", "services.AddZendiator<Marker>" },
            diagnostics.OrderBy(d => d.Location.SourceSpan.Start).Select(d =>
            {
                Assert.Equal("ZEN0020", d.Id);
                Assert.Equal(DiagnosticSeverity.Error, d.Severity);
                return d.Location.SourceTree!.GetText().ToString(d.Location.SourceSpan);
            }));
    }

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Legacy_di_keeps_its_routes_beside_unselected_or_separately_selected_marked_handlers(bool selected)
    {
        var source = Head + Fixtures + """
            public sealed record Ping : IRequest<int>;
            public sealed class PingHandler : IRequestHandler<Ping,int> {
                public ValueTask<int> HandleAsync(Ping request, CancellationToken ct) => new(1);
            }
            public sealed record NewPing : IRequest<int>;
            public sealed record NewVoid : IRequest;
            public sealed record NewSync : ISyncRequest<int>;
            public sealed record NewSyncVoid : ISyncRequest;
            public sealed record NewStream : IStreamRequest<int>;
            [Handler] public sealed class Marked : IRequestHandler<NewPing,int>, IRequestHandler<NewVoid>,
                ISyncRequestHandler<NewSync,int>, ISyncRequestHandler<NewSyncVoid>, IStreamSourceHandler<NewStream,int,Marked.Cursor>, INotificationHandler<UserCreated> {
                public ValueTask<int> HandleAsync(NewPing request, CancellationToken ct) => new(2);
                public ValueTask HandleAsync(NewVoid request, CancellationToken ct) => default;
                public int Handle(NewSync request, CancellationToken ct) => 3;
                public void Handle(NewSyncVoid request, CancellationToken ct) {}
                public ValueTask<Cursor> OpenAsync(NewStream request, CancellationToken ct) => throw new NotImplementedException();
                public sealed class Cursor:System.Collections.Generic.IAsyncEnumerator<int>{public int Current=>0;public ValueTask<bool> MoveNextAsync()=>new(false);public ValueTask DisposeAsync()=>default;}
                public ValueTask HandleAsync(UserCreated request, CancellationToken ct) => default;
            }
            [ZendiatorComposition("reads")] public sealed class Reads;
            public static class Startup {
                public static void Legacy(IServiceCollection services) => services.AddZendiator(c => {
                    c.Namespace = "Legacy.Generated";
                    c.AddNotification<UserCreated>();
                });
            }
            """;
        if (selected)
            source += "public static class NewStartup { public static void Register(IServiceCollection services) => services.AddZendiator<Reads>(c => c.UseLocalHandlers()); }";
        var driver = Driver().RunGeneratorsAndUpdateCompilation(Compilation(source), out var output, out var diagnostics);
        if (selected)
            Assert.Equal("ZEN0035", Assert.Single(diagnostics).Id);
        else
            Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error && d.Id != "ZEN0035"));
        var generated = driver.GetRunResult().GeneratedTrees.Single(t => t.ToString().Contains("namespace Legacy.Generated;")).ToString();
        Assert.Contains("global::App.PingHandler", generated);
        Assert.Contains("global::App.AuditHandler", generated);
        Assert.Contains("global::App.EmailHandler", generated);
        Assert.DoesNotContain("global::App.Marked", generated);
        Assert.DoesNotContain("global::App.NewPing", generated);
        Assert.DoesNotContain("global::App.NewSync", generated);
        Assert.DoesNotContain("global::App.NewStream", generated);
    }


    [Fact]
    public void Moving_registration_refreshes_interceptors_but_reuses_mediator_emission()
    {
        var source = Head + """
            public sealed class App
            {
                public void Register(IServiceCollection services) =>
                    services.AddZendiator(static configuration => configuration.Namespace = "App.Generated");
            }
            """;
        var input = Compilation(source);
        var driver = Driver().RunGenerators(input);
        var first = driver.GetRunResult().GeneratedTrees.Select(t => t.ToString()).ToArray();
        driver = driver.RunGeneratorsAndUpdateCompilation(Compilation("\n" + source), out var output, out var diagnostics);
        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        var changed = driver.GetRunResult().GeneratedTrees.Select(t => t.ToString()).ToArray();
        Assert.Equal(first.Single(t => t.Contains("interface IZendiator")), changed.Single(t => t.Contains("interface IZendiator")));
        Assert.NotEqual(first.Single(t => t.Contains("AddZendiatorShims")), changed.Single(t => t.Contains("AddZendiatorShims")));
        var steps = driver.GetRunResult().Results.Single().TrackedSteps;
        Assert.All(steps["SourceEmission"].SelectMany(s => s.Outputs), o => Assert.Equal(IncrementalStepRunReason.Cached, o.Reason));
        Assert.All(steps["InterceptorsEmission"].SelectMany(s => s.Outputs), o => Assert.Equal(IncrementalStepRunReason.Modified, o.Reason));
    }

    [Fact]
    public void Bare_call_without_attributes_generates_default_registration()
    {
        var result = Run(Head + Fixtures + "public sealed class App { public void Register(" + Services + " services) { services.AddZendiator(); } }", true);
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains(result.GeneratedTrees, tree => tree.FilePath.EndsWith("Zendiator.Interceptors.g.cs", StringComparison.Ordinal));
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
    [InlineData("configuration.DependencyLifetime = (ServiceLifetime)42;")]
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
    public void Ordered_list_recording_copies_inputs_preserves_order_and_freezes_with_its_snapshot()
    {
        var configuration = new global::Zendiator.DependencyInjection.ZendiatorConfiguration();
        var stages = new[] { Stage.Hook<object>(), Stage.Wrapper<string>() };
        var handlers = new[] { typeof(int), typeof(Guid) };
        configuration.Pipeline<object>(DispatchFamily.SendAllAsync, stages);
        configuration.Handlers<object>(DispatchFamily.SendAllAsync, handlers);
        stages[0] = Stage.Hook<DateTime>();
        handlers[0] = typeof(DateTime);
        var snapshot = configuration.Snapshot();
        Assert.Equal(new[] { typeof(object), typeof(string) }, snapshot.Pipelines[0].Stages.Select(s => s.Type));
        Assert.Equal(new[] { typeof(int), typeof(Guid) }, snapshot.RouteHandlers[0].Handlers.Select(e => e.Type));
        Assert.Throws<NotSupportedException>(() => ((IList<Stage>)snapshot.Pipelines[0].Stages)[0] = default);
        Assert.Throws<NotSupportedException>(() => ((IList<HandlerEntry>)snapshot.RouteHandlers[0].Handlers)[0] = typeof(DateTime));
        Assert.Throws<InvalidOperationException>(() => configuration.Pipeline<object>());
        Assert.Throws<InvalidOperationException>(() => configuration.Handlers<object>(DispatchFamily.Notification));
        var other = new global::Zendiator.DependencyInjection.ZendiatorConfiguration();
        other.Pipeline<object>(DispatchFamily.SendAllAsync, Stage.Wrapper<string>(), Stage.Hook<object>());
        other.Handlers<object>(DispatchFamily.SendAllAsync, typeof(Guid), typeof(int));
        Assert.NotEqual(snapshot.GetFingerprint(), other.Snapshot().GetFingerprint());
        var invalid = new global::Zendiator.DependencyInjection.ZendiatorConfiguration();
        Assert.Throws<ArgumentException>(() => invalid.Pipeline<object>(default(Stage)));
        Assert.Throws<ArgumentOutOfRangeException>(() => invalid.Handlers<object>((DispatchFamily)999));
    }

    [Fact]
    public void Handler_factory_entry_recording_copies_factory_identity_and_freezes_authoritative_entries()
    {
        var configuration = new global::Zendiator.DependencyInjection.ZendiatorConfiguration();
        var rest = new[] { HandlerEntry.Session<string, Uri>(), HandlerEntry.Service<Guid>() };
        configuration.Handlers<object>(DispatchFamily.SendAllAsync, typeof(int), rest);
        rest[0] = HandlerEntry.Service<DateTime>();
        var snapshot = configuration.Snapshot();
        var entries = snapshot.RouteHandlers[0].Handlers;
        Assert.Equal(new[] { typeof(int), typeof(string), typeof(Guid) }, entries.Select(e => e.Type));
        Assert.Equal(new Type?[] { null, typeof(Uri), null }, entries.Select(e => e.FactoryType));
        Assert.Throws<NotSupportedException>(() => ((IList<HandlerEntry>)entries)[1] = default);
        Assert.Throws<InvalidOperationException>(() => configuration.Handlers<object>(DispatchFamily.SendAsync, HandlerEntry.Service<object>()));
        var invalid = new global::Zendiator.DependencyInjection.ZendiatorConfiguration();
        Assert.Throws<ArgumentException>(() => invalid.Handlers<object>(DispatchFamily.SendAsync, default(HandlerEntry)));
        Assert.Throws<ArgumentNullException>(() => HandlerEntry.Service(null!));
    }

    [Fact]
    public void Handler_and_Stage_factory_identity_changes_fingerprint_while_plain_service_spelling_is_equivalent()
    {
        string HandlerFingerprint(HandlerEntry entry)
        {
            var c = new global::Zendiator.DependencyInjection.ZendiatorConfiguration();
            c.Handlers<object>(DispatchFamily.SendAsync, entry);
            return c.Snapshot().GetFingerprint();
        }
        string StageFingerprint(Stage stage)
        {
            var c = new global::Zendiator.DependencyInjection.ZendiatorConfiguration();
            c.Pipeline<object>(DispatchFamily.SendAsync, stage);
            return c.Snapshot().GetFingerprint();
        }
        var plain = new global::Zendiator.DependencyInjection.ZendiatorConfiguration();
        plain.Handlers<object>(DispatchFamily.SendAsync, typeof(object));
        Assert.Equal(plain.Snapshot().GetFingerprint(), HandlerFingerprint(HandlerEntry.Service<object>()));
        Assert.NotEqual(HandlerFingerprint(HandlerEntry.Session<object, string>()), HandlerFingerprint(HandlerEntry.Session<object, Uri>()));
        Assert.NotEqual(HandlerFingerprint(HandlerEntry.Service<object>()), HandlerFingerprint(HandlerEntry.Session<object, string>()));
        Assert.NotEqual(StageFingerprint(Stage.Hook<object, string>()), StageFingerprint(Stage.Hook<object, Uri>()));
        Assert.NotEqual(StageFingerprint(Stage.Hook<object>()), StageFingerprint(Stage.Hook<object, string>()));
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
}
