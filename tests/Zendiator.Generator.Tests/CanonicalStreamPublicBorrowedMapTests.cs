using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private static string ClosedBorrowedMapSource() => ListFixture(
        "c.Handlers<BasicRequest>(DispatchFamily.Stream,typeof(BasicHandler));c.StreamPipeline<BasicRequest,string>(Stage.Hook<BasicMap>());") + """
        public sealed record BasicRequest(int Value):IStreamRequest<int>;
        [ZendiatorHandler] public sealed class BasicHandler(global::Zendiator.Generator.Tests.CanonicalStreamTypedIntegrationProbe p):IStreamRequestHandler<BasicRequest,int>,IAsyncDisposable {
            public System.Collections.Generic.IAsyncEnumerable<int> HandleAsync(BasicRequest r,CancellationToken c)=>p.Start(r,r.Value,c);
            public ValueTask DisposeAsync(){p.DisposeFactory("borrowed-H");return default;}
        }
        public sealed class BasicMap(global::Zendiator.Generator.Tests.CanonicalStreamTypedIntegrationProbe p):IStreamItemMap<BasicRequest,int,string>,IAsyncDisposable {
            public string Map(BasicRequest r,int item,CancellationToken c)=>p.MapOpen(r,item,c).ToString(System.Globalization.CultureInfo.InvariantCulture);
            public ValueTask DisposeAsync(){p.DisposeFactory("borrowed-M");return default;}
        }
        """;

    private static string WithClosedBorrowedMapSend(string source, bool consumer = false) => WithPublicOpenOwnedSend(source.Replace("c.Handlers<BasicRequest>",
        "c.Handlers<OwnedPing>(DispatchFamily.SendSync,typeof(OwnedPingHandler));c.Pipeline<OwnedPing>(DispatchFamily.SendSync);c.Handlers<BasicRequest>"), consumer);

    [Fact]
    public async Task Stream_public_closed_borrowed_map_exposes_typed_final_without_session_factories()
    {
        var source = ClosedBorrowedMapSource();
        CompilePublicBorrowedMap("closed-mixed-borrowed-factory-blocked", WithClosedBorrowedMapSend(source.Replace("Stage.Hook<BasicMap>()", "Stage.Hook<BasicMap,BasicFactory>()") + """
            public sealed class BasicFactory(global::Zendiator.Generator.Tests.CanonicalStreamTypedIntegrationProbe p):IBorrowedSessionFactory<BasicRequest,BasicMap> {
                public BasicMap Create(BasicRequest r,CancellationToken c)=>new(p);
            }
            """), true, blocked: true, closed: true);
        source += """
            public static class PublicBasicMapEntry {
                public static void Register(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)=>Startup.Configure(services);
                public static System.Collections.Generic.IAsyncEnumerable<string> Stream(System.IServiceProvider scope,object request,CancellationToken token)=>
                    global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::App.Generated.Reads.IZendiator>(scope)
                        .StreamAsync((BasicRequest)request,token);
            }
            """;
        foreach (var mixed in new[] { false, true })
        {
            var shape = mixed ? "closed-mixed-borrowed-public" : "closed-borrowed-public";
            var assembly = CompilePublicBorrowedMap(shape, mixed ? WithClosedBorrowedMapSend(source, true) : source, mixed, closed: true)!;
            var entry = assembly.GetType("App.PublicBasicMapEntry")!;
            var register = entry.GetMethod("Register")!.CreateDelegate<Action<IServiceCollection>>();
            var stream = entry.GetMethod("Stream")!.CreateDelegate<Func<IServiceProvider, object, CancellationToken, IAsyncEnumerable<string>>>();
            var send = mixed ? assembly.GetType("App.PublicOpenOwnedSend")!.GetMethod("Send")!.CreateDelegate<Func<IServiceProvider, int, int>>() : null;
            var request = Activator.CreateInstance(assembly.GetType("App.BasicRequest")!, 7)!;
            using var api = new CancellationTokenSource(); var probe = new CanonicalStreamTypedIntegrationProbe { Request = request, ExpectedToken = api.Token };
            var services = new ServiceCollection(); services.AddSingleton(probe); register(services);
            foreach (var name in new[] { "BasicHandler", "BasicMap" })
                Assert.Equal(ServiceLifetime.Transient, Assert.Single(services, d => d.ServiceType == assembly.GetType("App." + name)).Lifetime);
            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            await using var scope = provider.CreateAsyncScope(); if (send != null) Assert.Equal(42, send(scope.ServiceProvider, 41));
            var cursor = stream(scope.ServiceProvider, request, api.Token).GetAsyncEnumerator(); Assert.Empty(probe.Events);
            Assert.True(await cursor.MoveNextAsync()); Assert.Equal("7", cursor.Current); Assert.Equal("7", cursor.Current);
            Assert.False(await cursor.MoveNextAsync()); await cursor.DisposeAsync(); await cursor.DisposeAsync();
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
                value = "7",
                publicReturnType = entry.GetMethod("Stream")!.ReturnType.ToString(),
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
