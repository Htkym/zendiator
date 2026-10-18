using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Zendiator.SourceGenerator;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private static string WithPublicOpenOwnedSend(string source, bool consumer = false)
    {
        source = source.Replace("c.Handlers(typeof(OpenRequest<>),",
            "c.Handlers<OwnedPing>(DispatchFamily.SendSync,typeof(OwnedPingHandler));c.Pipeline<OwnedPing>(DispatchFamily.SendSync);c.Handlers(typeof(OpenRequest<>),");
        source += """
            public sealed record OwnedPing(int Value):ISyncRequest<int>;
            [ZendiatorHandler] public sealed class OwnedPingHandler:ISyncRequestHandler<OwnedPing,int> {
                public int Handle(OwnedPing request,CancellationToken token)=>request.Value+1;
            }
            """;
        return source + (consumer ? """
            public static class PublicOpenOwnedSend {
                public static int Send(System.IServiceProvider scope,int value)=>
                    global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::App.Generated.Reads.IZendiator>(scope)
                        .Send(new OwnedPing(value));
            }
            """ : "");
    }

    private static string WithAsyncPublicOpenOwnedMap(string source, bool pending)
    {
        source = source.Replace("IStreamItemMap<R,T,OpenFinal<T>>", "IAsyncStreamItemMap<R,T,OpenFinal<T>>")
            .Replace("public OpenFinal<T> Map(R r,T item,CancellationToken c)=>new(p.MapOpen(r!,item,c));", """
                public static readonly TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);
                public static global::Zendiator.Generator.Tests.AsyncOnceGate<OpenFinal<T>>? Gate;
                public static bool InitiallyCompleted;
                public ValueTask<OpenFinal<T>> MapAsync(R r,T item,CancellationToken c) {
                    var gate=new global::Zendiator.Generator.Tests.AsyncOnceGate<OpenFinal<T>>(new OpenFinal<T>(p.MapOpen(r!,item,c)));
                    Gate=gate;
                    __COMPLETE__
                    InitiallyCompleted=gate.Task.IsCompleted;
                    Entered.TrySetResult();
                    return gate.Task;
                }
                """.Replace("__COMPLETE__", pending ? "" : "gate.Complete();"));
        return source + """
            public static class PublicOpenOwnedAsyncControl {
                public static Task Entered=>OpenMap<OpenRequest<OpenPayload>,OpenPayload>.Entered.Task;
                public static int Consumptions=>OpenMap<OpenRequest<OpenPayload>,OpenPayload>.Gate!.Consumptions;
                public static bool InitiallyCompleted=>OpenMap<OpenRequest<OpenPayload>,OpenPayload>.InitiallyCompleted;
                public static void Release()=>OpenMap<OpenRequest<OpenPayload>,OpenPayload>.Gate!.Complete();
            }
            """;
    }

    private static string WithAsyncPublicOpenOwnedHandler(string source, bool pending)
    {
        source = source.Replace("IOwnedSessionFactory<OpenRequest<T>,OpenHandler<T>>", "IAsyncOwnedSessionFactory<OpenRequest<T>,OpenHandler<T>>")
            .Replace("public OpenHandler<T> Create(OpenRequest<T> r,CancellationToken c)=>p.Create(\"H\",r,c,id=>new OpenHandler<T>(p,id));", """
                public static readonly TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);
                public static global::Zendiator.Generator.Tests.AsyncOnceGate<OpenHandler<T>>? Gate;
                public static bool InitiallyCompleted;
                public ValueTask<OpenHandler<T>> CreateAsync(OpenRequest<T> r,CancellationToken c) {
                    var gate=new global::Zendiator.Generator.Tests.AsyncOnceGate<OpenHandler<T>>(p.Create("H",r,c,id=>new OpenHandler<T>(p,id)));
                    Gate=gate;
                    __COMPLETE__
                    InitiallyCompleted=gate.Task.IsCompleted;
                    Entered.TrySetResult();
                    return gate.Task;
                }
                """.Replace("__COMPLETE__", pending ? "" : "gate.Complete();"));
        return source + """
            public static class PublicOpenOwnedHandlerControl {
                public static Task Entered=>OpenHandlerFactory<OpenPayload>.Entered.Task;
                public static int Consumptions=>OpenHandlerFactory<OpenPayload>.Gate!.Consumptions;
                public static bool InitiallyCompleted=>OpenHandlerFactory<OpenPayload>.InitiallyCompleted;
                public static void Release()=>OpenHandlerFactory<OpenPayload>.Gate!.Complete();
            }
            """;
    }

    private static Assembly? CompilePublicOpenOwned(string shape, params string[] sources)
    {
        var mixed = shape.StartsWith("mixed-", StringComparison.Ordinal); var contract = mixed ? shape.Substring(6) : shape;
        var compilation = SyncCompilation("", "PublicOpenOwned_" + Guid.NewGuid().ToString("N")).RemoveAllSyntaxTrees()
            .AddSyntaxTrees(sources.Select(s => Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(s, SyncParseOptions)));
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        var generated = driver.GetRunResult().Results.Single().GeneratedSources; using var bytes = new MemoryStream(); var emitted = output.Emit(bytes);
        var folder = PublicOpenOwnedFolder(shape);
        if (folder != null)
        {
            for (var i = 0; i < sources.Length; i++) File.WriteAllText(Path.Combine(folder, "input-" + i + ".cs"), sources[i]);
            File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
            foreach (var file in generated) File.WriteAllText(Path.Combine(folder, file.HintName), file.SourceText.ToString());
            File.WriteAllText(Path.Combine(folder, "compile.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                shape,
                emitted.Success,
                composition = Selection(driver),
                diagnostics = diagnostics.Select(d => new
                {
                    d.Id,
                    severity = d.Severity.ToString(),
                    message = d.GetMessage(),
                    inSource = d.Location.IsInSource,
                    anchor = d.Location.IsInSource ? d.Location.SourceTree!.GetText().ToString(d.Location.SourceSpan) : null
                }),
                compiler = emitted.Diagnostics.Select(d => new { d.Id, severity = d.Severity.ToString(), message = d.GetMessage() }),
                sources = generated.Select(f => f.HintName)
            }));
        }
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error));
        if (contract is "undetermined-factory" or "constraint-conflict")
        {
            Assert.Contains(diagnostics, d => d.Id == "ZEN0035" && d.Severity == DiagnosticSeverity.Error && d.Location.IsInSource &&
                d.Location.SourceTree!.GetText().ToString(d.Location.SourceSpan).StartsWith("Stage.Hook", StringComparison.Ordinal));
            Assert.DoesNotContain(generated, f => f.HintName.StartsWith("Zendiator.Canonical.Stream.", StringComparison.Ordinal));
            if (mixed)
            {
                AssertNoCanonicalOutput(generated);
                Assert.DoesNotContain(generated, f => f.HintName.EndsWith(".Interceptors.g.cs", StringComparison.Ordinal));
            }
            return null;
        }
        if (mixed && contract is ("async-map-acquire-blocked" or "borrowed-map-blocked"))
        {
            Assert.Equal(2, diagnostics.Length);
            Assert.All(diagnostics, d => { Assert.Equal("ZEN0037", d.Id); Assert.True(d.Location.IsInSource); });
            AssertNoCanonicalOutput(generated);
            Assert.DoesNotContain(generated, f => f.HintName.EndsWith(".Interceptors.g.cs", StringComparison.Ordinal));
            return null;
        }
        Assert.Empty(diagnostics);
        if (shape == "send-multi-compile-only")
        {
            Assert.Equal(2, generated.Count(f => f.HintName.StartsWith("Zendiator.Route.", StringComparison.Ordinal)));
            Assert.Single(generated, f => f.HintName.StartsWith("Zendiator.Composition.", StringComparison.Ordinal)); return null;
        }
        var route = Assert.Single(Selection(driver).Pipelines, r => r.Family == BoundDispatchFamily.Stream); var flow = Assert.IsType<BoundStreamItemFlow>(route.StreamItemFlow);
        var generic = Assert.IsType<BoundStreamGenericEmission>(route.StreamGeneric); var registration = Assert.IsType<BoundStreamOpenRegistration>(route.StreamOpenRegistration);
        Assert.Equal("global::App.OpenRequest<>", registration.RequestDefinitionName); Assert.Equal("global::App.OpenFinal<>", registration.FinalDefinitionName);
        Assert.Equal("global::App.OpenHandler<>", registration.HandlerDefinitionName); Assert.Equal("global::App.OpenHandlerFactory<>", registration.HandlerFactoryDefinitionName);
        var stage = Assert.Single(registration.Stages); Assert.Equal("global::App.OpenMap<,>", stage.TypeName); Assert.Equal("global::App.OpenMapFactory<,>", stage.FactoryName);
        var facade = mixed ? Assert.Single(generated, f => IsCanonicalRoute(f) && f.SourceText.ToString().Contains(" StreamAsync", StringComparison.Ordinal)).SourceText.ToString() :
            Assert.Single(generated, f => f.HintName.StartsWith("Zendiator.Canonical.Stream.Public.", StringComparison.Ordinal)).SourceText.ToString();
        var registrar = mixed ? CanonicalCompositionSource(generated).Replace("<, >", "<,>") : facade;
        if (mixed)
        {
            Assert.Equal(2, generated.Count(IsCanonicalRoute));
            Assert.Contains(" Send(global::App.OwnedPing request", CanonicalRouteSource(generated, "App.OwnedPing"));
            Assert.Contains("expected.Handlers<global::App.OwnedPing>", registrar);
            Assert.Contains("expected.Pipeline<global::App.OwnedPing>", registrar);
        }
        Assert.Contains("IAsyncEnumerable<" + flow.Final.Name + "> StreamAsync" + generic.TypeParameters, facade);
        Assert.Contains(generic.TypeConstraints, facade); Assert.Equal(" where __ZendiatorType0 : class, new()", generic.TypeConstraints);
        Assert.Contains("HandlerEntry.Session(typeof(global::App.OpenHandler<>), typeof(global::App.OpenHandlerFactory<>))", registrar);
        Assert.Contains("expected.StreamPipeline(typeof(global::App.OpenRequest<>), typeof(global::App.OpenFinal<>),", registrar);
        Assert.Contains("Stage.Hook(typeof(global::App.OpenMap<,>), typeof(global::App.OpenMapFactory<,>))", registrar);
        foreach (var factory in new[] { "OpenHandlerFactory<>", "OpenMapFactory<,>" })
            Assert.Contains("ServiceDescriptor.Scoped(typeof(global::App." + factory + "), typeof(global::App." + factory + "))", registrar);
        var backend = Assert.Single(generated, f => f.HintName.StartsWith("Zendiator.StreamCanonical.", StringComparison.Ordinal)).SourceText.ToString();
        Assert.Contains("IAsyncEnumerator<" + flow.Source.Name + ">? _source", backend);
        Assert.Contains("private " + flow.Final.Name + " _current", backend);
        Assert.Contains("typeof(global::App.OpenHandlerFactory<__ZendiatorType0>)", backend);
        Assert.Contains("typeof(global::App.OpenMapFactory<global::App.OpenRequest<__ZendiatorType0>, __ZendiatorType0>)", backend);
        Assert.DoesNotContain("IAsyncEnumerable<object>", facade);
        bytes.Position = 0; return AssemblyLoadContext.Default.LoadFromStream(bytes);
    }

    [Fact]
    public async Task Stream_public_open_owned_map_uses_declared_definitions_closed_factories_and_typed_final()
    {
        CompilePublicOpenOwned("send-multi-compile-only", MultiRouteSource("mixed", false), MultiRouteSource("mixed", true), MultiRegistration("mixed"));
        CompilePublicOpenOwned("undetermined-factory", TypedIntegrationSource("factory-undetermined"));
        await RunPublicOpenOwned(false);
    }

    [Fact]
    public async Task Stream_public_open_owned_map_mixed_registration_preserves_typed_final_and_session_ownership()
    {
        foreach (var shape in new[] { "factory-undetermined", "constraint-conflict" })
            CompilePublicOpenOwned("mixed-" + (shape == "factory-undetermined" ? "undetermined-factory" : shape),
                WithPublicOpenOwnedSend(TypedIntegrationSource(shape)));
        var source = TypedIntegrationSource("open-session");
        CompilePublicOpenOwned("mixed-async-map-acquire-blocked", WithPublicOpenOwnedSend(source
            .Replace("IOwnedSessionFactory<R,OpenMap<R,T>>", "IAsyncOwnedSessionFactory<R,OpenMap<R,T>>")
            .Replace("public OpenMap<R,T> Create(R r,CancellationToken c)=>p.Create(\"M\",r!,c,id=>new OpenMap<R,T>(p,id));",
                "public ValueTask<OpenMap<R,T>> CreateAsync(R r,CancellationToken c)=>new(p.Create(\"M\",r!,c,id=>new OpenMap<R,T>(p,id)));")));
        CompilePublicOpenOwned("mixed-borrowed-map-blocked", WithPublicOpenOwnedSend(source
            .Replace("Stage.Hook(typeof(OpenMap<,>),typeof(OpenMapFactory<,>))", "Stage.Hook(typeof(OpenMap<,>))")));
        await RunPublicOpenOwned(true);
    }

    [Fact]
    public async Task Stream_public_open_owned_async_map_preserves_single_consumption_typed_item_and_ownership()
    {
        await RunPublicOpenOwned(false, true);
        await RunPublicOpenOwned(true, true);
    }

    [Fact]
    public async Task Stream_public_open_owned_async_handler_preserves_acquisition_order_and_single_consumption()
    {
        await RunPublicOpenOwned(false, asyncHandler: true);
        await RunPublicOpenOwned(true, asyncMap: true, asyncHandler: true);
    }

    private static async Task RunPublicOpenOwned(bool mixed, bool asyncMap = false, bool asyncHandler = false)
    {
        var source = TypedIntegrationSource("open-session") + """
            public static class PublicOpenOwnedEntry {
                public static void Register(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)=>Startup.Configure(services);
                public static System.Collections.Generic.IAsyncEnumerable<OpenFinal<OpenPayload>> Stream(System.IServiceProvider scope,OpenRequest<OpenPayload> request)=>
                    global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::App.Generated.Reads.IZendiator>(scope).StreamAsync(request);
                public static async System.Threading.Tasks.Task<long[]> Run(System.IServiceProvider scope,object request) {
                    var typed=(OpenRequest<OpenPayload>)request;
                    await using System.Collections.Generic.IAsyncEnumerator<OpenFinal<OpenPayload>> cursor=Stream(scope,typed).GetAsyncEnumerator();
                    if(!await cursor.MoveNextAsync()) throw new System.InvalidOperationException("Missing final item.");
                    OpenFinal<OpenPayload> final=cursor.Current;
                    if(!System.Object.ReferenceEquals(final.Value,typed.Value) || await cursor.MoveNextAsync()) throw new System.InvalidOperationException("Changed final.");
                    return new long[]{final.Value.Value};
                }
            }
            """;
        if (asyncMap) source = WithAsyncPublicOpenOwnedMap(source, mixed);
        if (asyncHandler) source = WithAsyncPublicOpenOwnedHandler(source, mixed);
        if (mixed) source = WithPublicOpenOwnedSend(source, true);
        var shape = (mixed ? "mixed-" : "") + (asyncHandler ? "async-handler-public" : asyncMap ? "async-public" : "public");
        var assembly = CompilePublicOpenOwned(shape, source)!; var entry = assembly.GetType("App.PublicOpenOwnedEntry")!;
        var register = entry.GetMethod("Register")!.CreateDelegate<Action<IServiceCollection>>();
        var run = entry.GetMethod("Run")!.CreateDelegate<Func<IServiceProvider, object, Task<long[]>>>();
        var send = mixed ? assembly.GetType("App.PublicOpenOwnedSend")!.GetMethod("Send")!.CreateDelegate<Func<IServiceProvider, int, int>>() : null;
        var payloadType = assembly.GetType("App.OpenPayload")!; var payload = Activator.CreateInstance(payloadType)!;
        var requestType = assembly.GetType("App.OpenRequest`1")!.MakeGenericType(payloadType); var request = Activator.CreateInstance(requestType, payload)!;
        var finalType = assembly.GetType("App.OpenFinal`1")!.MakeGenericType(payloadType);
        var api = assembly.GetType("App.Generated.Reads.IZendiator")!.GetMethod("StreamAsync")!.MakeGenericMethod(payloadType);
        Assert.Equal(typeof(IAsyncEnumerable<>).MakeGenericType(finalType), api.ReturnType);
        var helper = Assert.Single(assembly.GetTypes(), t => t.Name.StartsWith("__ZendiatorStreamCanonical_", StringComparison.Ordinal));
        var cursorType = Assert.Single(helper.GetNestedTypes(BindingFlags.NonPublic), t => t.Name.StartsWith("Cursor", StringComparison.Ordinal)).MakeGenericType(payloadType);
        Assert.Equal(finalType, cursorType.GetField("_current", BindingFlags.NonPublic | BindingFlags.Instance)!.FieldType);
        Assert.Equal(finalType, cursorType.GetProperty("Current")!.PropertyType);
        Assert.Equal(typeof(IAsyncEnumerator<>).MakeGenericType(payloadType), cursorType.GetField("_source", BindingFlags.NonPublic | BindingFlags.Instance)!.FieldType);
        var probe = new CanonicalStreamTypedIntegrationProbe { Request = request }; var services = new ServiceCollection(); services.AddSingleton(probe); register(services);
        if (mixed) { var count = services.Count; register(services); Assert.Equal(count, services.Count); }
        var factoryDefinitions = new[] { assembly.GetType("App.OpenHandlerFactory`1")!, assembly.GetType("App.OpenMapFactory`2")! };
        Assert.All(factoryDefinitions, t => Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, d => d.ServiceType == t).Lifetime));
        foreach (var name in new[] { "OpenHandler`1", "OpenMap`2" }) Assert.DoesNotContain(services, d => d.ServiceType == assembly.GetType("App." + name));
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        var scope = provider.CreateAsyncScope(); Assert.Empty(probe.Events);
        if (send != null) Assert.Equal(42, send(scope.ServiceProvider, 41));
        Assert.Empty(probe.Events);
        var operation = run(scope.ServiceProvider, request);
        var initiallyCompleted = operation.IsCompleted; var callbackInitiallyCompleted = false; var mapConsumptions = 0;
        var handlerInitiallyCompleted = false; var handlerConsumptions = 0;
        Type? handlerControl = null;
        if (asyncHandler)
        {
            handlerControl = assembly.GetType("App.PublicOpenOwnedHandlerControl")!;
            await ((Task)handlerControl.GetProperty("Entered")!.GetValue(null)!).WaitAsync(TimeSpan.FromSeconds(5));
            handlerInitiallyCompleted = (bool)handlerControl.GetProperty("InitiallyCompleted")!.GetValue(null)!;
            Assert.Equal(!mixed, handlerInitiallyCompleted);
            if (mixed)
            {
                Assert.False(operation.IsCompleted);
                Assert.Equal(0, (int)handlerControl.GetProperty("Consumptions")!.GetValue(null)!);
                Assert.Equal(0, probe.Starts); Assert.Equal(0, probe.Moves); Assert.Equal(0, probe.Reads);
                Assert.Equal(2, probe.Created.Count); Assert.Equal(0, probe.SourceDisposals);
                Assert.Equal(0, probe.SessionDisposals); Assert.Equal(0, probe.FactoryDisposals);
                handlerControl.GetMethod("Release")!.Invoke(null, null);
            }
        }
        Type? control = null;
        if (asyncMap)
        {
            control = assembly.GetType("App.PublicOpenOwnedAsyncControl")!;
            await ((Task)control.GetProperty("Entered")!.GetValue(null)!).WaitAsync(TimeSpan.FromSeconds(5));
            callbackInitiallyCompleted = (bool)control.GetProperty("InitiallyCompleted")!.GetValue(null)!;
            Assert.Equal(!mixed, callbackInitiallyCompleted);
            if (mixed)
            {
                Assert.False(operation.IsCompleted);
                Assert.Equal(0, (int)control.GetProperty("Consumptions")!.GetValue(null)!);
                if (asyncHandler) Assert.Equal(1, (int)handlerControl!.GetProperty("Consumptions")!.GetValue(null)!);
                Assert.Equal(1, probe.Starts); Assert.Equal(1, probe.Moves); Assert.Equal(1, probe.Reads);
                Assert.Equal(2, probe.Created.Count); Assert.Equal(0, probe.SourceDisposals);
                Assert.Equal(0, probe.SessionDisposals); Assert.Equal(0, probe.FactoryDisposals);
                control.GetMethod("Release")!.Invoke(null, null);
            }
        }
        var values = await operation.WaitAsync(TimeSpan.FromSeconds(5)); Assert.Equal(new long[] { 7 }, values);
        if (asyncMap) { mapConsumptions = (int)control!.GetProperty("Consumptions")!.GetValue(null)!; Assert.Equal(1, mapConsumptions); }
        if (asyncHandler) { handlerConsumptions = (int)handlerControl!.GetProperty("Consumptions")!.GetValue(null)!; Assert.Equal(1, handlerConsumptions); }
        if (send != null) Assert.Equal(10, send(scope.ServiceProvider, 9));
        Assert.Equal(new[] { "M:1", "H:2" }, probe.Created.Select(c => c.Role + ":" + c.Id));
        Assert.Equal(2, probe.Instances.Distinct(ReferenceEqualityComparer.Instance).Count());
        var actualFactories = new[] { factoryDefinitions[0].MakeGenericType(payloadType), factoryDefinitions[1].MakeGenericType(requestType, payloadType) };
        Assert.All(actualFactories, t => Assert.Equal(t, scope.ServiceProvider.GetRequiredService(t).GetType()));
        Assert.Equal(1, probe.Starts); Assert.Equal(1, probe.SourceEnumerations); Assert.Equal(2, probe.Moves); Assert.Equal(1, probe.Reads); Assert.Equal(1, probe.SourceDisposals);
        Assert.Equal(new[] { "Session.Dispose:H:2", "Session.Dispose:M:1" }, probe.Events.Where(e => e.StartsWith("Session.Dispose:", StringComparison.Ordinal)));
        Assert.True(probe.Events.IndexOf("H.Dispose") < probe.Events.IndexOf("Session.Dispose:H:2"));
        Assert.Equal(2, probe.SessionDisposals); Assert.Equal(0, probe.FactoryDisposals);
        var beforeScope = probe.Events.ToArray(); await scope.DisposeAsync(); Assert.Equal(2, probe.SessionDisposals); Assert.Equal(2, probe.FactoryDisposals);
        var folder = PublicOpenOwnedFolder(shape);
        if (folder != null) File.WriteAllText(Path.Combine(folder, "runtime.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            values,
            mixed,
            asyncMap,
            asyncHandler,
            handlerInitiallyCompleted,
            handlerConsumptions,
            initiallyCompleted,
            callbackInitiallyCompleted,
            mapConsumptions,
            publicSendsChecked = mixed ? 2 : 0,
            runtimeEnumerations = 1,
            publicReturnType = api.ReturnType.ToString(),
            currentType = finalType.ToString(),
            sourceType = payloadType.ToString(),
            factoryDefinitions = factoryDefinitions.Select(t => t.ToString()),
            actualClosedFactories = actualFactories.Select(t => t.ToString()),
            payloadIdentityPreserved = true,
            requestTokenCheckedByProbe = true,
            probe.Starts,
            probe.SourceEnumerations,
            probe.Moves,
            probe.Reads,
            probe.SourceDisposals,
            ownedSessionDisposalsBeforeScope = 2,
            ownedSessionDisposalsAfterScope = probe.SessionDisposals,
            borrowedFactoryDisposalsBeforeScope = 0,
            borrowedFactoryDisposalsAfterScope = probe.FactoryDisposals,
            eventsBeforeScope = beforeScope,
            events = probe.Events
        }));
    }

    private static string? PublicOpenOwnedFolder(string shape)
    {
        var root = Environment.GetEnvironmentVariable("ZENDIATOR_PUBLIC_OPEN_OWNED_EVIDENCE_DIR"); if (string.IsNullOrEmpty(root)) return null;
        var folder = Path.Combine(root, shape); Directory.CreateDirectory(folder); return folder;
    }
}
