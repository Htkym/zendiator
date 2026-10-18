using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Zendiator.SourceGenerator;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private static string PublicOpenBorrowedMapSource() => TypedIntegrationSource("open-session")
        .Replace("HandlerEntry.Session(typeof(OpenHandler<>),typeof(OpenHandlerFactory<>))", "typeof(OpenHandler<>)")
        .Replace("Stage.Hook(typeof(OpenMap<,>),typeof(OpenMapFactory<,>))", "Stage.Hook(typeof(OpenMap<,>))")
        .Replace("CanonicalStreamTypedIntegrationProbe p,int id)", "CanonicalStreamTypedIntegrationProbe p)")
        .Replace("new OpenHandler<T>(p,id)", "new OpenHandler<T>(p)").Replace("new OpenMap<R,T>(p,id)", "new OpenMap<R,T>(p)")
        .Replace("public ValueTask DisposeAsync()=>p.DisposeSession(\"H\",id);", "public ValueTask DisposeAsync(){p.DisposeFactory(\"borrowed-H\");return default;}")
        .Replace("public ValueTask DisposeAsync()=>p.DisposeSession(\"M\",id);", "public ValueTask DisposeAsync(){p.DisposeFactory(\"borrowed-M\");return default;}");

    private static Assembly? CompilePublicOpenBorrowedMap(string shape, string source, bool mixed, bool blocked = false)
    {
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "PublicBorrowedMap_" + Guid.NewGuid().ToString("N")),
            out var output, out var diagnostics);
        var generated = driver.GetRunResult().Results.Single().GeneratedSources; using var bytes = new MemoryStream(); var emitted = output.Emit(bytes);
        var folder = PublicOpenOwnedFolder(shape);
        if (folder != null)
        {
            File.WriteAllText(Path.Combine(folder, "input.cs"), source); File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
            foreach (var file in generated) File.WriteAllText(Path.Combine(folder, file.HintName), file.SourceText.ToString());
            File.WriteAllText(Path.Combine(folder, "compile.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                shape,
                emitted.Success,
                composition = Selection(driver),
                diagnostics = diagnostics.Select(d => new { d.Id, severity = d.Severity.ToString(), inSource = d.Location.IsInSource, message = d.GetMessage() }),
                compiler = emitted.Diagnostics.Select(d => new { d.Id, severity = d.Severity.ToString(), message = d.GetMessage() }),
                sources = generated.Select(f => f.HintName)
            }));
        }
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error));
        if (blocked)
        {
            Assert.Equal(2, diagnostics.Length); Assert.All(diagnostics, d => { Assert.Equal("ZEN0037", d.Id); Assert.True(d.Location.IsInSource); });
            AssertNoCanonicalOutput(generated); Assert.DoesNotContain(generated, f => f.HintName.EndsWith(".Interceptors.g.cs", StringComparison.Ordinal)); return null;
        }
        Assert.Empty(diagnostics);
        var route = Assert.Single(Selection(driver).Pipelines, p => p.Family == BoundDispatchFamily.Stream);
        var flow = Assert.IsType<BoundStreamItemFlow>(route.StreamItemFlow); var generic = Assert.IsType<BoundStreamGenericEmission>(route.StreamGeneric);
        var registration = Assert.IsType<BoundStreamOpenRegistration>(route.StreamOpenRegistration);
        Assert.Equal("global::App.OpenRequest<>", registration.RequestDefinitionName); Assert.Equal("global::App.OpenFinal<>", registration.FinalDefinitionName);
        Assert.Equal("global::App.OpenHandler<>", registration.HandlerDefinitionName); Assert.Null(registration.HandlerFactoryDefinitionName);
        var stage = Assert.Single(registration.Stages); Assert.Equal("global::App.OpenMap<,>", stage.TypeName); Assert.Null(stage.FactoryName);
        var facade = mixed ? Assert.Single(generated, f => IsCanonicalRoute(f) && f.SourceText.ToString().Contains(" StreamAsync", StringComparison.Ordinal)).SourceText.ToString() :
            Assert.Single(generated, f => f.HintName.StartsWith("Zendiator.Canonical.Stream.Public.", StringComparison.Ordinal)).SourceText.ToString();
        Assert.Contains("IAsyncEnumerable<" + flow.Final.Name + "> StreamAsync" + generic.TypeParameters, facade); Assert.Contains(generic.TypeConstraints, facade);
        Assert.DoesNotContain("IAsyncEnumerable<object>", facade);
        if (mixed) { Assert.Equal(2, generated.Count(IsCanonicalRoute)); Assert.Single(generated, f => f.HintName.StartsWith("Zendiator.Composition.", StringComparison.Ordinal)); }
        bytes.Position = 0; return AssemblyLoadContext.Default.LoadFromStream(bytes);
    }

    [Fact]
    public async Task Stream_public_open_borrowed_map_uses_normal_DI_without_session_factories()
    {
        var source = PublicOpenBorrowedMapSource();
        CompilePublicOpenBorrowedMap("mixed-borrowed-factory-blocked", WithPublicOpenOwnedSend(source
            .Replace("Stage.Hook(typeof(OpenMap<,>))", "Stage.Hook(typeof(OpenMap<,>),typeof(OpenMapFactory<,>))")
            .Replace("IOwnedSessionFactory<R,OpenMap<R,T>>", "IAsyncBorrowedSessionFactory<R,OpenMap<R,T>>")
            .Replace("public OpenMap<R,T> Create(R r,CancellationToken c)=>p.Create(\"M\",r!,c,id=>new OpenMap<R,T>(p));",
                "public ValueTask<OpenMap<R,T>> CreateAsync(R r,CancellationToken c)=>new(p.Create(\"M\",r!,c,id=>new OpenMap<R,T>(p)));")), true, true);
        source += """
            public static class PublicBorrowedMapEntry {
                public static void Register(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)=>Startup.Configure(services);
                public static System.Collections.Generic.IAsyncEnumerable<OpenFinal<OpenPayload>> Stream(System.IServiceProvider scope,object request,CancellationToken token)=>
                    global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::App.Generated.Reads.IZendiator>(scope)
                        .StreamAsync((OpenRequest<OpenPayload>)request,token);
            }
            """;
        foreach (var mixed in new[] { false, true })
        {
            var shape = mixed ? "mixed-borrowed-public" : "borrowed-public";
            var assembly = CompilePublicOpenBorrowedMap(shape, mixed ? WithPublicOpenOwnedSend(source, true) : source, mixed)!;
            var entry = assembly.GetType("App.PublicBorrowedMapEntry")!; var register = entry.GetMethod("Register")!.CreateDelegate<Action<IServiceCollection>>();
            var send = mixed ? assembly.GetType("App.PublicOpenOwnedSend")!.GetMethod("Send")!.CreateDelegate<Func<IServiceProvider, int, int>>() : null;
            var payload = Activator.CreateInstance(assembly.GetType("App.OpenPayload")!)!;
            var request = Activator.CreateInstance(assembly.GetType("App.OpenRequest`1")!.MakeGenericType(payload.GetType()), payload)!;
            var final = assembly.GetType("App.OpenFinal`1")!.MakeGenericType(payload.GetType());
            Assert.Equal(typeof(IAsyncEnumerable<>).MakeGenericType(final), entry.GetMethod("Stream")!.ReturnType);
            using var api = new CancellationTokenSource(); var probe = new CanonicalStreamTypedIntegrationProbe { Request = request, ExpectedToken = api.Token };
            var services = new ServiceCollection(); services.AddSingleton(probe); register(services);
            foreach (var name in new[] { "OpenHandler`1", "OpenMap`2" })
                Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, d => d.ServiceType == assembly.GetType("App." + name)).Lifetime);
            foreach (var name in new[] { "OpenHandlerFactory`1", "OpenMapFactory`2" })
                Assert.DoesNotContain(services, d => d.ServiceType == assembly.GetType("App." + name));
            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            await using var scope = provider.CreateAsyncScope(); if (send != null) Assert.Equal(42, send(scope.ServiceProvider, 41));
            var stream = (IAsyncEnumerable<object>)entry.GetMethod("Stream")!.Invoke(null, [scope.ServiceProvider, request, api.Token])!;
            var cursor = stream.GetAsyncEnumerator(); Assert.Empty(probe.Events);
            Assert.True(await cursor.MoveNextAsync()); var item = cursor.Current; Assert.Equal(final, item.GetType()); Assert.Same(payload, final.GetProperty("Value")!.GetValue(item));
            _ = cursor.Current; Assert.False(await cursor.MoveNextAsync()); await cursor.DisposeAsync(); await cursor.DisposeAsync();
            Assert.Equal(1, probe.Starts); Assert.Equal(1, probe.SourceEnumerations); Assert.Equal(2, probe.Moves); Assert.Equal(1, probe.Reads); Assert.Equal(1, probe.SourceDisposals);
            Assert.Empty(probe.Created); Assert.Equal(0, probe.SessionDisposals); Assert.Equal(0, probe.FactoryDisposals);
            if (send != null) Assert.Equal(10, send(scope.ServiceProvider, 9));
            var beforeScope = probe.Events.ToArray(); await scope.DisposeAsync();
            Assert.Equal(0, probe.SessionDisposals); Assert.Equal(2, probe.FactoryDisposals);
            Assert.Equal(new[] { "Factory.Dispose:borrowed-M", "Factory.Dispose:borrowed-H" }, probe.Events.Where(e => e.StartsWith("Factory.Dispose:", StringComparison.Ordinal)));
            var folder = PublicOpenOwnedFolder(shape);
            if (folder != null) File.WriteAllText(Path.Combine(folder, "runtime.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                mixed,
                finalType = final.ToString(),
                payloadIdentityPreserved = ReferenceEquals(payload, final.GetProperty("Value")!.GetValue(item)),
                probe.Starts,
                probe.SourceEnumerations,
                probe.Moves,
                probe.Reads,
                probe.SourceDisposals,
                ownedSessionsCreated = probe.Created.Count,
                probe.SessionDisposals,
                borrowedDisposalsBeforeScope = 0,
                borrowedDisposalsAfterScope = probe.FactoryDisposals,
                publicSendsChecked = mixed ? 2 : 0,
                beforeScope,
                events = probe.Events
            }));
        }
    }
}
