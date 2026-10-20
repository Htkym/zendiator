using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private static string ValueVoidSource(bool async)
    {
        var source = CapabilityIntegrationSource(async, true, "---s", "direct", "owned", false);
        var root = CSharpSyntaxTree.ParseText(source, SyncParseOptions).GetCompilationUnitRoot();
        var request = Assert.Single(root.DescendantNodes().OfType<RecordDeclarationSyntax>(), r => r.Identifier.ValueText == "Ping");
        return source[..request.SpanStart] + "public readonly record struct Ping(int Value):" + (async ? "IRequest;" : "ISyncRequest;") + source[request.Span.End..];
    }

    private static string ValueSendStreamSource(bool gate)
    {
        var modules = ClosedModule(PublicValueRequestSource(false), "ValueStream", "SharedState", "Items", "H", "F1", "M", "F2", "Source") + "\n" +
            ClosedModule(ManagedShapeSource(false, true, "large", false), "ValueSync", "Ping", "Reply", "Hook", "Handler", "Factory") + "\n" +
            ClosedModule(ManagedShapeSource(true, true, "array", false), "ValueAsync", "Ping", "Reply", "Hook", "Handler", "Factory") + "\n" +
            ClosedModule(ValueVoidSource(false), "ValueSyncVoid", "Ping", "Hook0", "Handler", "HFactory") + "\n" +
            ClosedModule(ValueVoidSource(true), "ValueAsyncVoid", "Ping", "Hook0", "Handler", "HFactory");
        return modules + """
            namespace ValueSendMix {
            using System;using System.Collections.Generic;using System.Linq;using System.Text.Json;using System.Threading;using System.Threading.Tasks;
            using Zendiator;using Zendiator.DependencyInjection;using Microsoft.Extensions.DependencyInjection;
            using Zendiator.Generator.Tests;
            using S=global::Closed.ValueStream;using R=global::Closed.ValueSync;using A=global::Closed.ValueAsync;
            using V=global::Closed.ValueSyncVoid;using W=global::Closed.ValueAsyncVoid;
            [ZendiatorComposition("value-send-stream")]public sealed class Many;
            public static class Bridge {
                public static IServiceCollection Register(IServiceCollection s)=>s.AddZendiator<Many>(c=>{
                    c.Handlers<S.Items>(DispatchFamily.Stream,typeof(S.H));
                    c.Handlers<R.Ping>(DispatchFamily.SendSync,HandlerEntry.Session<R.Handler,R.Factory>());
                    c.Handlers<V.Ping>(DispatchFamily.SendSync,HandlerEntry.Session<V.Handler,V.HFactory>());
                    c.Handlers<A.Ping>(DispatchFamily.SendAsync,HandlerEntry.Session<A.Handler,A.Factory>());
                    c.Handlers<W.Ping>(DispatchFamily.SendAsync,HandlerEntry.Session<W.Handler,W.HFactory>());
                    c.Pipeline<S.Items>(DispatchFamily.Stream,Stage.Hook<S.F1>(),Stage.Hook<S.M>(),Stage.Hook<S.F2>());
                    c.Pipeline<R.Ping>(DispatchFamily.SendSync,Stage.Hook<R.Hook>());
                    c.Pipeline<V.Ping>(DispatchFamily.SendSync,Stage.Hook<V.Hook0>());
                    c.Pipeline<A.Ping>(DispatchFamily.SendAsync,Stage.Hook<A.Hook>());
                    c.Pipeline<W.Ping>(DispatchFamily.SendAsync,Stage.Hook<W.Hook0>());
                });
                __CONSUMER__
            }
            __REJECTED__
            }
            """.Replace("__CONSUMER__", gate ? "" : ValueSendStreamConsumer).Replace("__REJECTED__", gate ? """
            public readonly record struct GenericSend<T>(T Value):ISyncRequest<int>;
            [ZendiatorHandler]public sealed class GenericHandler:ISyncRequestHandler<GenericSend<int>,int>{
                public int Handle(GenericSend<int> r,CancellationToken t)=>r.Value;
            }
            [ZendiatorComposition("generic-send-stream")]public sealed class GenericMix;
            public static class GenericMixBridge {
                public static IServiceCollection RegisterGenericMix(IServiceCollection s)=>s.AddZendiator<GenericMix>(c=>{
                    c.Handlers<S.Items>(DispatchFamily.Stream,typeof(S.H));
                    c.Handlers<GenericSend<int>>(DispatchFamily.SendSync,typeof(GenericHandler));
                    c.Pipeline<S.Items>(DispatchFamily.Stream,Stage.Hook<S.F1>(),Stage.Hook<S.M>(),Stage.Hook<S.F2>());
                    c.Pipeline<GenericSend<int>>(DispatchFamily.SendSync);
                });
            }
            """ : "");
    }

    private const string ValueSendStreamConsumer = """
        private static void Check(bool value,string message)=>PublicValueRequestProbe.Check(value,message);
        private static object Managed(ManagedShapeProbe p)=>new{
            p.Trace,requests=p.Requests.Select(r=>new{r.Phase,r.Value}),created=p.Created.Count,released=p.Released.Count,
            consumptions=p.Gates.Select(g=>g.Consumptions).ToArray()
        };
        private static object VoidResult(CapabilityIntegrationProbe p,Func<object,int> value)=>new{
            p.Trace,requests=p.Calls.Select(c=>new{c.Phase,value=value(c.Request!)}),
            created=p.CreatedHandlers.Count,released=p.ReleasedHandlers.Count,p.HandlerFactoryDisposals
        };
        public static async Task<string> Run(){
            var streamProbe=new PublicValueRequestProbe();var sync=new ManagedShapeProbe();var query=new ManagedShapeProbe{Pending=true};
            var syncVoid=new CapabilityIntegrationProbe();var asyncVoid=new CapabilityIntegrationProbe();
            try{
                IServiceCollection services=new ServiceCollection();
                services.AddScoped<S.H>(_=>new S.H(streamProbe));services.AddScoped<S.F1>(_=>new S.F1(streamProbe));
                services.AddScoped<S.M>(_=>new S.M(streamProbe));services.AddScoped<S.F2>(_=>new S.F2(streamProbe));
                services.AddScoped<R.Factory>(_=>new R.Factory(sync));services.AddScoped<R.Hook>(_=>new R.Hook(sync));
                services.AddScoped<A.Factory>(_=>new A.Factory(query));services.AddScoped<A.Hook>(_=>new A.Hook(query));
                services.AddScoped<V.HFactory>(_=>new V.HFactory(syncVoid));services.AddScoped<V.Hook0>(_=>new V.Hook0(syncVoid));
                services.AddScoped<W.HFactory>(_=>new W.HFactory(asyncVoid));services.AddScoped<W.Hook0>(_=>new W.Hook0(asyncVoid));
                Register(services);Check(services.GroupBy(d=>d.ServiceType).All(g=>g.Count()==1),"unique DI descriptors");
                await using var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});
                var scope=provider.CreateAsyncScope();
                var mediator=scope.ServiceProvider.GetRequiredService<global::ValueSendMix.Generated.Many.IZendiator>();
                Check(ReferenceEquals(mediator,scope.ServiceProvider.GetRequiredService<global::ValueSendMix.Generated.Many.Zendiator>()),"shared composition identity");
                var shared=new S.SharedState();var stream=mediator.StreamAsync(new S.Items(shared));var cursor=stream.GetAsyncEnumerator();
                Check(streamProbe.Starts==0&&streamProbe.Resolved.Count==0,"Stream dispatch/cursor lazy");
                var pending=mediator.SendAsync(new A.Ping(20),query.Token.Token);var gate=query.Gates.Single();
                Check(!pending.IsCompleted&&gate.Consumptions==0&&query.Created.Count==1&&query.Released.Count==0,"one pending response");
                Check(query.Trace.SequenceEqual(new[]{"before","factory","body"})&&sync.Trace.Count==0&&syncVoid.Trace.Count==0&&asyncVoid.Trace.Count==0,"pending route isolated");
                var syncReply=mediator.Send(new R.Ping(10),sync.Token.Token);
                mediator.Send(new V.Ping(30),syncVoid.Cancellation.Token);
                await mediator.SendAsync(new W.Ping(40),asyncVoid.Cancellation.Token);
                Check(syncReply.A==12,"sync value response");
                Check(streamProbe.Starts==0&&query.Released.Count==0&&gate.Consumptions==0&&!pending.IsCompleted,"other Send routes leave Stream and pending response untouched");
                var values=new List<int>();
                try{while(await cursor.MoveNextAsync())values.Add(cursor.Current);}finally{await cursor.DisposeAsync();}
                Check(values.SequenceEqual(new[]{2,4})&&streamProbe.Starts==1&&streamProbe.SourceDisposals==1,"Stream result/source lifecycle");
                Check(streamProbe.Seen.Count==10&&streamProbe.Seen.All(s=>s.Marker==7&&s.Pad==11&&ReferenceEquals(s.Shared,shared)),"Stream request remains route specific");
                Check(query.Trace.SequenceEqual(new[]{"before","factory","body"})&&query.Released.Count==0&&gate.Consumptions==0&&!pending.IsCompleted,"Stream leaves pending Send state untouched");
                gate.Complete();var response=await pending;
                Check(response.SequenceEqual(new[]{22})&&gate.Consumptions==1,"pending response single consumption");
                Check(ReferenceEquals(query.HandlerResponse,query.AfterInput)&&ReferenceEquals(query.AfterOutput,response),"array response identity");
                var managedOrder=new[]{"before","factory","body","dispose","after","finally"};
                Check(sync.Trace.SequenceEqual(managedOrder)&&query.Trace.SequenceEqual(managedOrder),"response callback/owned cleanup order");
                Check(sync.Created.Count==1&&sync.Released.Count==1&&query.Created.Count==1&&query.Released.Count==1,"one owned response handler per route");
                Check(sync.Requests.Select(r=>r.Value).SequenceEqual(new[]{10,11,11,11,11})&&query.Requests.Select(r=>r.Value).SequenceEqual(new[]{20,21,21,21,21}),"response request state separated");
                var voidOrder=new[]{"handler.factory","handler","handler.cleanup","h0.finally"};
                Check(syncVoid.Trace.SequenceEqual(voidOrder)&&asyncVoid.Trace.SequenceEqual(voidOrder),"void callback/owned cleanup order");
                Check(syncVoid.CreatedHandlers.Count==1&&syncVoid.ReleasedHandlers.Count==1&&asyncVoid.CreatedHandlers.Count==1&&asyncVoid.ReleasedHandlers.Count==1,"one owned void handler per route");
                Check(syncVoid.Calls.Count==3&&syncVoid.Calls.All(c=>c.Request is V.Ping{Value:30})&&asyncVoid.Calls.Count==3&&asyncVoid.Calls.All(c=>c.Request is W.Ping{Value:40}),"void request identity and state separated");
                Check(streamProbe.Ended.Count==0&&syncVoid.HandlerFactoryDisposals==0&&asyncVoid.HandlerFactoryDisposals==0,"borrowed services remain until scope");
                await scope.DisposeAsync();
                Check(streamProbe.Ended.SequenceEqual(new[]{"F2","M","F1","H"})&&syncVoid.HandlerFactoryDisposals==1&&asyncVoid.HandlerFactoryDisposals==1,"scope owns borrowed components and factories");
                return JsonSerializer.Serialize(new{generatorInputs=1,runtimeOperations=5,syncValue=syncReply.A,response,values,
                    pendingConsumptions=gate.Consumptions,streamProbe.Starts,streamProbe.SourceDisposals,streamEnds=streamProbe.Ended,
                    sync=Managed(sync),query=Managed(query),syncVoid=VoidResult(syncVoid,r=>((V.Ping)r).Value),asyncVoid=VoidResult(asyncVoid,r=>((W.Ping)r).Value)});
            }finally{sync.Token.Dispose();query.Token.Dispose();syncVoid.Cancellation.Dispose();asyncVoid.Cancellation.Dispose();}
        }
        """;

    private static void SaveValueSendStream(string? folder, string source, GeneratorDriver driver, Compilation output, IEnumerable<Diagnostic> diagnostics)
    {
        if (folder == null) return;
        Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, "input.cs"), source);
        var files = driver.GetRunResult().Results.Single().GeneratedSources;
        foreach (var file in files) File.WriteAllText(Path.Combine(folder, file.HintName), file.SourceText.ToString());
        File.WriteAllText(Path.Combine(folder, "compile.json"), JsonSerializer.Serialize(new
        {
            sources = files.Select(f => f.HintName),
            diagnostics = diagnostics.Select(d => new { d.Id, message = d.GetMessage(), span = d.Location.SourceSpan.ToString() }),
            compiler = output.GetDiagnostics().Select(d => new { d.Id, severity = d.Severity.ToString(), message = d.GetMessage() })
        }));
    }

    private static void AssertValueSendStreamContract(Compilation output)
    {
        var contract = output.GetTypeByMetadataName("ValueSendMix.Generated.Many.IZendiator"); Assert.NotNull(contract);
        foreach (var (method, request) in new[] { ("StreamAsync", "ValueStream.Items"), ("Send", "ValueSync.Ping"), ("Send", "ValueSyncVoid.Ping"), ("SendAsync", "ValueAsync.Ping"), ("SendAsync", "ValueAsyncVoid.Ping") })
        {
            var type = output.GetTypeByMetadataName("Closed." + request); Assert.NotNull(type); Assert.True(type.IsValueType);
            Assert.Single(contract.GetMembers(method).OfType<IMethodSymbol>(), m => SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, type));
        }
        Assert.Single(contract.GetMembers("StreamAsync")); Assert.Equal(2, contract.GetMembers("Send").Length); Assert.Equal(2, contract.GetMembers("SendAsync").Length);
    }

    [Fact]
    public void Value_Send_Stream_composition_reuses_the_existing_single_route_closed_generic_value_backend()
    {
        var source = ValueSendStreamSource(true) + """
            namespace ValueSendMix {
            [Zendiator.ZendiatorComposition("generic-send-single")]public sealed class GenericSingle;
            public static class GenericSingleBridge {
                public static Microsoft.Extensions.DependencyInjection.IServiceCollection RegisterSingle(Microsoft.Extensions.DependencyInjection.IServiceCollection services)=>
                    Zendiator.DependencyInjection.ZendiatorServiceCollectionExtensions.AddZendiator<GenericSingle>(services,c=>{
                        c.Handlers<GenericSend<int>>(Zendiator.DispatchFamily.SendSync,typeof(GenericHandler));
                        c.Pipeline<GenericSend<int>>(Zendiator.DispatchFamily.SendSync);
                    });
            }
            }
            """;
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "ValueSendStreamGate_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        var folder = string.IsNullOrEmpty(evidence) ? null : Path.Combine(evidence, "value-send-generic-composition");
        SaveValueSendStream(folder, source, driver, output, diagnostics);
        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        AssertValueSendStreamContract(output);
        Assert.NotNull(output.GetTypeByMetadataName("ValueSendMix.Generated.Many.ZendiatorGeneratedRegistrar"));
        AssertComposedProfile(output, "ValueSendMix.Generated.GenericMix", ("Send", 1), ("StreamAsync", 1));
        AssertComposedProfile(output, "ValueSendMix.Generated.GenericSingle", ("Send", 1));
        var files = driver.GetRunResult().Results.Single().GeneratedSources;
        Assert.Contains(files, f => f.HintName.EndsWith("Interceptors.g.cs", StringComparison.Ordinal) && f.SourceText.ToString().Contains("ValueSendMix.Generated.Many.ZendiatorGeneratedRegistrar", StringComparison.Ordinal));
        foreach (var name in new[] { "GenericMix", "GenericSingle" }) Assert.Contains(files, f => f.HintName.EndsWith("Interceptors.g.cs", StringComparison.Ordinal) && f.SourceText.ToString().Contains("ValueSendMix.Generated." + name + ".ZendiatorGeneratedRegistrar", StringComparison.Ordinal));
        using var bytes = new MemoryStream(); var emitted = output.Emit(bytes); Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        if (folder != null) File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
        bytes.Position = 0; var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var requestType = assembly.GetType("ValueSendMix.GenericSend`1")!.MakeGenericType(typeof(int));
        var request = Activator.CreateInstance(requestType, [17])!;
        var handlerType = assembly.GetType("ValueSendMix.GenericHandler")!;
        var expected = (int)handlerType.GetMethod("Handle")!.Invoke(Activator.CreateInstance(handlerType), [request, CancellationToken.None])!;
        Assert.Equal(17, expected);
        var probe = new PublicValueRequestProbe(); IServiceCollection services = new ServiceCollection(); services.AddSingleton(probe);
        assembly.GetType("ValueSendMix.GenericSingleBridge")!.GetMethod("RegisterSingle")!.Invoke(null, [services]);
        assembly.GetType("ValueSendMix.GenericMixBridge")!.GetMethod("RegisterGenericMix")!.Invoke(null, [services]);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }); using var scope = provider.CreateScope();
        var results = new List<int>();
        foreach (var name in new[] { "GenericSingle", "GenericMix" })
        {
            var api = scope.ServiceProvider.GetRequiredService(assembly.GetType("ValueSendMix.Generated." + name + ".Zendiator")!);
            results.Add((int)api.GetType().GetMethod("Send", [requestType, typeof(CancellationToken)])!.Invoke(api, [request, CancellationToken.None])!);
        }
        Assert.All(results, value => Assert.Equal(expected, value)); Assert.Equal(0, probe.Starts); Assert.Empty(probe.Resolved);
        if (folder != null) File.WriteAllText(Path.Combine(folder, "gate-result.json"), JsonSerializer.Serialize(new { generatorInputs = 1, generatedDispatches = 2, referenceCalls = 1, acceptedRoutes = 8, reference = expected, single = results[0], mixed = results[1], streamStarts = probe.Starts, backendFilesChanged = false }));
    }

    [Fact]
    public async Task Value_Send_response_void_and_Stream_share_composition_with_pending_state_and_ownership_isolation()
    {
        var source = ValueSendStreamSource(false);
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "ValueSendStreamRuntime_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        var folder = string.IsNullOrEmpty(evidence) ? null : Path.Combine(evidence, "value-send-stream");
        SaveValueSendStream(folder, source, driver, output, diagnostics);
        if (folder != null)
            foreach (var async in new[] { false, true })
            {
                File.WriteAllText(Path.Combine(folder, "void-class-reference-" + async + ".txt"), CapabilityIntegrationSource(async, true, "---s", "direct", "owned", false));
                File.WriteAllText(Path.Combine(folder, "void-value-source-" + async + ".txt"), ValueVoidSource(async));
            }
        Assert.Empty(diagnostics); Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        AssertValueSendStreamContract(output);
        Assert.Equal(5, driver.GetRunResult().Results.Single().GeneratedSources.Count(f => IsCanonicalRoute(f)));
        using var bytes = new MemoryStream(); var emitted = output.Emit(bytes); Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        if (folder != null) File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
        bytes.Position = 0; var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var run = assembly.GetType("ValueSendMix.Bridge")!.GetMethod("Run")!.CreateDelegate<Func<Task<string>>>();
        var result = await run().WaitAsync(TimeSpan.FromSeconds(15));
        if (folder != null) File.WriteAllText(Path.Combine(folder, "runtime.json"), result);
        using var observed = JsonDocument.Parse(result); var value = observed.RootElement;
        Assert.Equal(5, value.GetProperty("runtimeOperations").GetInt32()); Assert.Equal(12, value.GetProperty("syncValue").GetInt32());
        Assert.Equal(new[] { 22 }, value.GetProperty("response").EnumerateArray().Select(x => x.GetInt32()));
        Assert.Equal(new[] { 2, 4 }, value.GetProperty("values").EnumerateArray().Select(x => x.GetInt32()));
        Assert.Equal(1, value.GetProperty("pendingConsumptions").GetInt32());
    }
}
