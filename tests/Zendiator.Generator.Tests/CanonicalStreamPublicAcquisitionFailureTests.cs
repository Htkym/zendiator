using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    [Fact]
    public async Task Stream_public_open_owned_acquisition_failures_release_only_transferred_sessions()
    {
        var source = WithPublicOpenOwnedSend(TypedIntegrationSource("open-session"), true)
            .Replace("IOwnedSessionFactory<OpenRequest<T>,OpenHandler<T>>", "IAsyncOwnedSessionFactory<OpenRequest<T>,OpenHandler<T>>")
            .Replace("IOwnedSessionFactory<R,OpenMap<R,T>>", "IAsyncOwnedSessionFactory<R,OpenMap<R,T>>")
            .Replace("public OpenHandler<T> Create(OpenRequest<T> r,CancellationToken c)=>p.Create(\"H\",r,c,id=>new OpenHandler<T>(p,id));",
                "public ValueTask<OpenHandler<T>> CreateAsync(OpenRequest<T> r,CancellationToken c)=>PublicAcquisitionEntry.Acquire(\"H\",r,c,()=>p.Create(\"H\",r,c,id=>new OpenHandler<T>(p,id)));")
            .Replace("public OpenMap<R,T> Create(R r,CancellationToken c)=>p.Create(\"M\",r!,c,id=>new OpenMap<R,T>(p,id));",
                "public ValueTask<OpenMap<R,T>> CreateAsync(R r,CancellationToken c)=>PublicAcquisitionEntry.Acquire(\"M\",r!,c,()=>p.Create(\"M\",r!,c,id=>new OpenMap<R,T>(p,id)));");
        source += """
            public static class PublicAcquisitionEntry {
                private static string _target="";
                private static object _request=null!;
                private static CancellationToken _token;
                private static readonly System.Collections.Generic.List<string> _calls=new();
                public static TaskCompletionSource Entered {get;private set;}=null!;
                public static global::Zendiator.Generator.Tests.IAsyncOnceGate Gate {get;private set;}=null!;
                public static string[] Calls=>_calls.ToArray();
                public static void Reset(string target,object request,CancellationToken token) {
                    _target=target;_request=request;_token=token;_calls.Clear();Gate=null!;
                    Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);
                }
                public static ValueTask<S> Acquire<S>(string role,object request,CancellationToken token,System.Func<S> create) where S:class {
                    if(!System.Object.ReferenceEquals(request,_request)||token!=_token) throw new System.InvalidOperationException("Acquisition request/token changed.");
                    _calls.Add(role);
                    if(role!=_target) return new(create());
                    var gate=new global::Zendiator.Generator.Tests.AsyncOnceGate<S>(null!);
                    Gate=gate;Entered.TrySetResult();return gate.Task;
                }
                public static void Register(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)=>Startup.Configure(services);
                public static System.Collections.Generic.IAsyncEnumerable<object> Stream(System.IServiceProvider scope,object request,CancellationToken token)=>
                    global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::App.Generated.Reads.IZendiator>(scope)
                        .StreamAsync((OpenRequest<OpenPayload>)request,token);
            }
            """;
        const string shape = "mixed-acquisition-boundaries";
        var assembly = CompilePublicOpenOwned(shape, source)!; var entry = assembly.GetType("App.PublicAcquisitionEntry")!;
        var register = entry.GetMethod("Register")!.CreateDelegate<Action<IServiceCollection>>();
        var reset = entry.GetMethod("Reset")!.CreateDelegate<Action<string, object, CancellationToken>>();
        var stream = entry.GetMethod("Stream")!.CreateDelegate<Func<IServiceProvider, object, CancellationToken, IAsyncEnumerable<object>>>();
        var send = assembly.GetType("App.PublicOpenOwnedSend")!.GetMethod("Send")!.CreateDelegate<Func<IServiceProvider, int, int>>();
        var payload = Activator.CreateInstance(assembly.GetType("App.OpenPayload")!)!;
        var request = Activator.CreateInstance(assembly.GetType("App.OpenRequest`1")!.MakeGenericType(payload.GetType()), payload)!;
        foreach (var target in new[] { "M", "H" })
            foreach (var outcome in new[] { "fault", "null", "canceled" })
            {
                using var api = new CancellationTokenSource();
                var probe = new CanonicalStreamTypedIntegrationProbe { Request = request, ExpectedToken = api.Token };
                var services = new ServiceCollection(); services.AddSingleton(probe); register(services); reset(target, request, api.Token);
                await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
                await using var scope = provider.CreateAsyncScope();
                Assert.Equal(42, send(scope.ServiceProvider, 41));
                var cursor = stream(scope.ServiceProvider, request, api.Token).GetAsyncEnumerator();
                Assert.Empty(probe.Events);
                var move = cursor.MoveNextAsync().AsTask();
                await ((TaskCompletionSource)entry.GetProperty("Entered")!.GetValue(null)!).Task.WaitAsync(TimeSpan.FromSeconds(5));
                var gate = (IAsyncOnceGate)entry.GetProperty("Gate")!.GetValue(null)!;
                Assert.False(move.IsCompleted); Assert.Equal(0, gate.Consumptions);
                var prefix = target == "M" ? Array.Empty<string>() : new[] { "M:1" };
                Assert.Equal(prefix, probe.Created.Select(c => c.Role + ":" + c.Id));
                Assert.Equal(0, probe.SessionDisposals); Assert.Equal(0, probe.FactoryDisposals);
                var calls = (string[])entry.GetProperty("Calls")!.GetValue(null)!;
                Assert.Equal(target == "M" ? new[] { "M" } : new[] { "M", "H" }, calls);
                var failure = new InvalidOperationException("acquisition failed");
                if (outcome == "null") gate.Complete();
                else if (outcome == "canceled") { api.Cancel(); gate.Fail(new OperationCanceledException(api.Token)); }
                else gate.Fail(failure);
                var error = await Record.ExceptionAsync(() => move.WaitAsync(TimeSpan.FromSeconds(5)));
                if (outcome == "canceled")
                {
                    var canceled = Assert.IsAssignableFrom<OperationCanceledException>(error);
                    Assert.Equal(api.Token, canceled.CancellationToken); Assert.True(api.IsCancellationRequested);
                }
                else if (outcome == "null") Assert.IsType<InvalidOperationException>(error);
                else Assert.Same(failure, error);
                Assert.Equal(outcome == "canceled", move.IsCanceled); Assert.Equal(outcome != "canceled", move.IsFaulted);
                Assert.Equal(1, gate.Consumptions);
                Assert.Equal(prefix.Length, probe.SessionDisposals);
                Assert.Equal(prefix, probe.Created.Select(c => c.Role + ":" + c.Id));
                Assert.Equal(prefix.Select(p => "Session.Dispose:" + p), probe.Events.Where(e => e.StartsWith("Session.Dispose:", StringComparison.Ordinal)));
                var afterFailure = probe.Events.ToArray();
                await cursor.DisposeAsync(); await cursor.DisposeAsync();
                Assert.Equal(afterFailure, probe.Events);
                Assert.Equal(0, probe.Starts); Assert.Equal(0, probe.SourceEnumerations); Assert.Equal(0, probe.Moves); Assert.Equal(0, probe.Reads); Assert.Equal(0, probe.SourceDisposals);
                Assert.DoesNotContain("Map:open", probe.Events);
                Assert.Equal(10, send(scope.ServiceProvider, 9));
                Assert.Equal(0, probe.FactoryDisposals);
                await scope.DisposeAsync();
                Assert.Equal(prefix.Length, probe.SessionDisposals); Assert.Equal(calls.Length, probe.FactoryDisposals);
                var folder = PublicOpenOwnedFolder(shape);
                if (folder != null) File.WriteAllText(Path.Combine(folder, target + "-" + outcome + ".json"), System.Text.Json.JsonSerializer.Serialize(new
                {
                    target,
                    outcome,
                    calls,
                    ownedPrefix = prefix,
                    errorType = error!.GetType().FullName,
                    faultIdentityPreserved = outcome == "fault" && ReferenceEquals(failure, error),
                    errorTokenMatches = error is OperationCanceledException canceled && canceled.CancellationToken == api.Token,
                    move.IsCanceled,
                    move.IsFaulted,
                    gate.Consumptions,
                    probe.Starts,
                    probe.SourceEnumerations,
                    probe.Moves,
                    probe.Reads,
                    probe.SourceDisposals,
                    ownedDisposalsAtFailure = prefix.Length,
                    probe.SessionDisposals,
                    factoryDisposalsBeforeScope = 0,
                    probe.FactoryDisposals,
                    publicSendsChecked = 2,
                    eventsAtFailure = afterFailure,
                    events = probe.Events
                }));
            }
    }
}
