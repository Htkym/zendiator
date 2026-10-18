using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    [Fact]
    public async Task Stream_public_open_owned_callback_and_cleanup_failures_preserve_finally_order()
    {
        var source = WithPublicOpenOwnedSend(WithAsyncPublicOpenOwnedMap(TypedIntegrationSource("open-session"), true), true)
            .Replace("public static readonly TaskCompletionSource Entered=", "public static TaskCompletionSource Entered=");
        source += """
            public static class PublicSessionFailureEntry {
                public static void Register(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)=>Startup.Configure(services);
                public static void Reset() {
                    OpenMap<OpenRequest<OpenPayload>,OpenPayload>.Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);
                    OpenMap<OpenRequest<OpenPayload>,OpenPayload>.Gate=null;
                }
                public static System.Collections.Generic.IAsyncEnumerable<object> Stream(System.IServiceProvider scope,object request,CancellationToken token)=>
                    global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::App.Generated.Reads.IZendiator>(scope)
                        .StreamAsync((OpenRequest<OpenPayload>)request,token);
            }
            """;
        const string shape = "mixed-callback-cleanup-boundaries";
        var assembly = CompilePublicOpenOwned(shape, source)!; var entry = assembly.GetType("App.PublicSessionFailureEntry")!;
        var register = entry.GetMethod("Register")!.CreateDelegate<Action<IServiceCollection>>();
        var reset = entry.GetMethod("Reset")!.CreateDelegate<Action>();
        var stream = entry.GetMethod("Stream")!.CreateDelegate<Func<IServiceProvider, object, CancellationToken, IAsyncEnumerable<object>>>();
        var send = assembly.GetType("App.PublicOpenOwnedSend")!.GetMethod("Send")!.CreateDelegate<Func<IServiceProvider, int, int>>();
        var control = assembly.GetType("App.PublicOpenOwnedAsyncControl")!;
        var payload = Activator.CreateInstance(assembly.GetType("App.OpenPayload")!)!;
        var request = Activator.CreateInstance(assembly.GetType("App.OpenRequest`1")!.MakeGenericType(payload.GetType()), payload)!;
        var mapType = assembly.GetType("App.OpenMap`2")!.MakeGenericType(request.GetType(), payload.GetType());
        foreach (var cleanupFails in new[] { false, true })
        {
            using var api = new CancellationTokenSource();
            var probe = new CanonicalStreamTypedIntegrationProbe { Request = request, ExpectedToken = api.Token };
            var cleanupGate = cleanupFails ? new AsyncOnceGate<bool>(true) : null;
            var cleanupEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (cleanupGate != null) probe.SessionCleanup = (role, _) =>
            {
                if (role != "H") return default;
                cleanupEntered.TrySetResult(); return cleanupGate.Void;
            };
            var services = new ServiceCollection(); services.AddSingleton(probe); register(services); reset();
            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            await using var scope = provider.CreateAsyncScope(); Assert.Equal(42, send(scope.ServiceProvider, 41));
            var cursor = stream(scope.ServiceProvider, request, api.Token).GetAsyncEnumerator(); Assert.Empty(probe.Events);
            var callbackError = new InvalidOperationException("Map callback failed"); var cleanupError = new ApplicationException("Handler cleanup failed");
            Task<bool>? move = null; Exception? observedCallback = null; string[] atMoveFailure = [];
            var sourceDisposalsAtMoveFailure = -1; var sessionDisposalsAtMoveFailure = -1;
            async Task Consume()
            {
                await using (cursor)
                {
                    move = cursor.MoveNextAsync().AsTask();
                    try { await move; }
                    catch (Exception error)
                    {
                        observedCallback = error; atMoveFailure = probe.Events.ToArray();
                        sourceDisposalsAtMoveFailure = probe.SourceDisposals; sessionDisposalsAtMoveFailure = probe.SessionDisposals;
                        Assert.Equal(0, sourceDisposalsAtMoveFailure); Assert.Equal(0, sessionDisposalsAtMoveFailure); throw;
                    }
                }
            }
            var operation = Consume();
            await ((Task)control.GetProperty("Entered")!.GetValue(null)!).WaitAsync(TimeSpan.FromSeconds(5));
            var mapGate = (IAsyncOnceGate)mapType.GetField("Gate")!.GetValue(null)!;
            Assert.False(operation.IsCompleted); Assert.Equal(0, mapGate.Consumptions);
            Assert.Equal(new[] { "M:1", "H:2" }, probe.Created.Select(c => c.Role + ":" + c.Id));
            Assert.Equal(1, probe.Starts); Assert.Equal(1, probe.SourceEnumerations); Assert.Equal(1, probe.Moves); Assert.Equal(1, probe.Reads);
            Assert.Equal(0, probe.SourceDisposals); Assert.Equal(0, probe.SessionDisposals); Assert.Equal(0, probe.FactoryDisposals);
            mapGate.Fail(callbackError); string[] duringCleanup = [];
            if (cleanupGate != null)
            {
                await cleanupEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.False(operation.IsCompleted);
                Assert.Same(callbackError, observedCallback); Assert.Equal(1, mapGate.Consumptions); Assert.Equal(0, cleanupGate.Consumptions);
                Assert.Equal(1, probe.SourceDisposals); Assert.Equal(1, probe.SessionDisposals); Assert.Equal(0, probe.FactoryDisposals);
                Assert.Equal("Session.Dispose:H:2", probe.Events[^1]); duringCleanup = probe.Events.ToArray(); cleanupGate.Fail(cleanupError);
            }
            var escaped = await Record.ExceptionAsync(() => operation.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Same(callbackError, observedCallback); Assert.Same(cleanupFails ? cleanupError : callbackError, escaped);
            Assert.True(move!.IsFaulted); Assert.False(move.IsCanceled); Assert.True(operation.IsFaulted); Assert.False(operation.IsCanceled);
            Assert.Equal(1, mapGate.Consumptions); Assert.Equal(cleanupFails ? 1 : 0, cleanupGate?.Consumptions ?? 0);
            Assert.Equal(new[] { "Session.Dispose:H:2", "Session.Dispose:M:1" }, probe.Events.Where(e => e.StartsWith("Session.Dispose:", StringComparison.Ordinal)));
            Assert.True(probe.Events.IndexOf("H.Dispose") < probe.Events.IndexOf("Session.Dispose:H:2"));
            Assert.Equal(1, probe.SourceDisposals); Assert.Equal(2, probe.SessionDisposals); Assert.Equal(0, probe.FactoryDisposals);
            var afterCleanup = probe.Events.ToArray(); await cursor.DisposeAsync(); await cursor.DisposeAsync(); Assert.False(await cursor.MoveNextAsync());
            Assert.Throws<InvalidOperationException>(() => cursor.Current); Assert.Equal(afterCleanup, probe.Events);
            Assert.Equal(10, send(scope.ServiceProvider, 9)); await scope.DisposeAsync();
            Assert.Equal(1, probe.SourceDisposals); Assert.Equal(2, probe.SessionDisposals); Assert.Equal(2, probe.FactoryDisposals);
            var folder = PublicOpenOwnedFolder(shape);
            if (folder != null) File.WriteAllText(Path.Combine(folder, cleanupFails ? "handler-cleanup-fault.json" : "callback-fault.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                cleanupFails,
                callbackIdentityPreservedAtMove = ReferenceEquals(callbackError, observedCallback),
                escapingIdentityPreserved = ReferenceEquals(cleanupFails ? cleanupError : callbackError, escaped),
                errorType = escaped!.GetType().FullName,
                callbackConsumed = mapGate.Consumptions,
                cleanupConsumed = cleanupGate?.Consumptions ?? 0,
                moveFaulted = move.IsFaulted,
                operationFaulted = operation.IsFaulted,
                sourceDisposalsAtMoveFailure,
                sessionDisposalsAtMoveFailure,
                probe.Starts,
                probe.SourceEnumerations,
                probe.Moves,
                probe.Reads,
                probe.SourceDisposals,
                probe.SessionDisposals,
                probe.FactoryDisposals,
                publicSendsChecked = 2,
                atMoveFailure,
                duringCleanup,
                afterCleanup,
                events = probe.Events
            }));
        }
    }
}
