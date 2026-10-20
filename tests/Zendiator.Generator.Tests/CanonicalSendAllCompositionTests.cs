using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private const string SendAllCompositionConfiguration = """
        c.Handlers<B.Ping>(DispatchFamily.SendAllSync,typeof(B.H2),typeof(B.H1),typeof(B.H3));
        c.Handlers<A.Ping>(DispatchFamily.SendAsync,HandlerEntry.Session<A.Handler,A.Factory>());
        c.Handlers<S.Items>(DispatchFamily.Stream,typeof(S.H));
        c.Handlers<C.Ping>(DispatchFamily.SendAllSync,typeof(C.H2),typeof(C.H1),typeof(C.H3));
        c.Handlers<R.Ping>(DispatchFamily.SendSync,HandlerEntry.Session<R.Handler,R.Factory>());
        c.Pipeline<S.Items>(DispatchFamily.Stream,Stage.Hook<S.F1>(),Stage.Hook<S.M>(),Stage.Hook<S.F2>());
        c.Pipeline<C.Ping>(DispatchFamily.SendAllSync,Stage.Hook<C.Hook>());
        c.Pipeline<R.Ping>(DispatchFamily.SendSync,Stage.Hook<R.Hook>());
        c.Pipeline<B.Ping>(DispatchFamily.SendAllSync,Stage.Hook<B.Hook>());
        c.Pipeline<A.Ping>(DispatchFamily.SendAsync,Stage.Hook<A.Hook>());
        """;

    private static string SendAllCompositionSource(bool gate)
    {
        var all = SendAllSyncSource(false);
        all = all[..all.IndexOf("[ZendiatorComposition(\"send-all-response\")]", StringComparison.Ordinal)] + "}";
        var modules = all + "\n" +
            ClosedModule(PublicValueRequestSource(false), "AllMixStream", "SharedState", "Items", "H", "F1", "M", "F2", "Source") + "\n" +
            ClosedModule(ManagedShapeSource(false, false, "large", false), "AllMixSync", "Ping", "Reply", "Hook", "Handler", "Factory") + "\n" +
            ClosedModule(ManagedShapeSource(true, false, "array", false), "AllMixAsync", "Ping", "Hook", "Handler", "Factory");
        return modules + """
            namespace AllMix {
            using System;using System.Collections.Generic;using System.Linq;using System.Text.Json;using System.Threading;using System.Threading.Tasks;
            using Zendiator;using Zendiator.DependencyInjection;using Microsoft.Extensions.DependencyInjection;using Microsoft.Extensions.DependencyInjection.Extensions;
            using Zendiator.Generator.Tests;
            using B=global::AllSync.Response;using C=global::AllSync.Command;
            using S=global::Closed.AllMixStream;using R=global::Closed.AllMixSync;using A=global::Closed.AllMixAsync;
            [ZendiatorComposition("all-mix")]public sealed class Many;
            public static class Bridge {
                public static IServiceCollection Register(IServiceCollection s)=>s.AddZendiator<Many>(c=>{__CONFIG__});
                __CONSUMER__
            }
            __REJECTED__
            }
            """.Replace("__CONFIG__", SendAllCompositionConfiguration)
            .Replace("__CONSUMER__", gate ? "" : SendAllCompositionConsumer)
            .Replace("__REJECTED__", gate ? """
                public sealed class OtherOpaque:ISendWrapper<B.Ping,int>{public int Invoke<N>(B.Ping request,N next,CancellationToken token)where N:ISendContinuation<B.Ping,int> => next.Invoke(request,token);}
                [ZendiatorComposition("all-mix-rejected")]public sealed class Rejected;
                public static class RejectedBridge {
                    public static IServiceCollection RegisterRejected(IServiceCollection s)=>s.AddZendiator<Rejected>(c=>{__BAD_CONFIG__});
                } /* rejected end */
                """.Replace("__BAD_CONFIG__", SendAllCompositionConfiguration.Replace("Stage.Hook<B.Hook>()", "Stage.Hook<B.Hook>(),Stage.Wrapper<B.Opaque>(),Stage.Wrapper<OtherOpaque>()")) : "");
    }

    private const string SendAllCompositionConsumer = """
        private static void Check(bool value,string message)=>AllSync.Probe.Check(value,message);
        private static object AllResult(AllSync.Probe p)=>new{p.Trace,p.Resolved,p.Ended,p.Handlers,p.Branch};
        private static object Managed(ManagedShapeProbe p)=>new{
            p.Trace,requests=p.Requests.Select(r=>new{r.Phase,r.Value}),created=p.Created.Count,released=p.Released.Count,
            consumptions=p.Gates.Select(g=>g.Consumptions).ToArray()
        };
        public static async Task<string> Run(){
            var responseProbe=new AllSync.Probe();var voidProbe=new AllSync.Probe();
            var sync=new ManagedShapeProbe();var query=new ManagedShapeProbe{Pending=true};var streamProbe=new PublicValueRequestProbe();
            try{
                IServiceCollection services=new ServiceCollection();Register(services);
                var allTypes=new[]{typeof(B.Hook),typeof(B.H2),typeof(B.H1),typeof(B.H3),typeof(C.Hook),typeof(C.H2),typeof(C.H1),typeof(C.H3)};
                foreach(var type in allTypes){var d=services.Single(d=>d.ServiceType==type);Check(d.Lifetime==ServiceLifetime.Scoped&&d.ImplementationType==type,"all branches registered by generated registrar");}
                services.Replace(ServiceDescriptor.Scoped<B.Hook>(_=>new B.Hook(responseProbe)));
                services.Replace(ServiceDescriptor.Scoped<B.H2>(_=>new B.H2(responseProbe)));
                services.Replace(ServiceDescriptor.Scoped<B.H1>(_=>new B.H1(responseProbe)));
                services.Replace(ServiceDescriptor.Scoped<B.H3>(_=>new B.H3(responseProbe)));
                services.Replace(ServiceDescriptor.Scoped<C.Hook>(_=>new C.Hook(voidProbe)));
                services.Replace(ServiceDescriptor.Scoped<C.H2>(_=>new C.H2(voidProbe)));
                services.Replace(ServiceDescriptor.Scoped<C.H1>(_=>new C.H1(voidProbe)));
                services.Replace(ServiceDescriptor.Scoped<C.H3>(_=>new C.H3(voidProbe)));
                services.Replace(ServiceDescriptor.Scoped<S.H>(_=>new S.H(streamProbe)));
                services.Replace(ServiceDescriptor.Scoped<S.F1>(_=>new S.F1(streamProbe)));
                services.Replace(ServiceDescriptor.Scoped<S.M>(_=>new S.M(streamProbe)));
                services.Replace(ServiceDescriptor.Scoped<S.F2>(_=>new S.F2(streamProbe)));
                services.Replace(ServiceDescriptor.Scoped<R.Factory>(_=>new R.Factory(sync)));
                services.Replace(ServiceDescriptor.Scoped<R.Hook>(_=>new R.Hook(sync)));
                services.Replace(ServiceDescriptor.Scoped<A.Factory>(_=>new A.Factory(query)));
                services.Replace(ServiceDescriptor.Scoped<A.Hook>(_=>new A.Hook(query)));
                Check(services.GroupBy(d=>d.ServiceType).All(g=>g.Count()==1),"unique DI descriptors");
                await using var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});
                await using var scope=provider.CreateAsyncScope();
                var mediator=scope.ServiceProvider.GetRequiredService<global::AllMix.Generated.Many.IZendiator>();
                Check(ReferenceEquals(mediator,scope.ServiceProvider.GetRequiredService<global::AllMix.Generated.Many.Zendiator>()),"shared mediator identity");
                var pending=mediator.SendAsync(new A.Ping(20),query.Token.Token);var gate=query.Gates.Single();
                Check(!pending.IsCompleted&&gate.Consumptions==0&&query.Created.Count==1&&query.Released.Count==0,"one pending async response");
                Check(responseProbe.Trace.Count==0&&voidProbe.Trace.Count==0&&sync.Trace.Count==0&&streamProbe.Resolved.Count==0,"pending activates only own route");
                var responseState=new AllSync.SharedData();var responseRequest=new B.Ping(1,responseState);responseProbe.Original=responseRequest;
                var copied=mediator.SendAll(responseRequest,responseState.Values,responseProbe.Cancellation.Token);
                Check(copied==3&&responseState.Values.SequenceEqual(new[]{115,114,116,-9})&&responseState.Version==3,"Span commit and tail with three branches");
                Check(responseProbe.Handlers.SequenceEqual(new[]{2,1,3})&&voidProbe.Trace.Count==0&&sync.Trace.Count==0&&streamProbe.Resolved.Count==0,"response branch order and isolation");
                var voidState=new AllSync.SharedData();var voidRequest=new C.Ping(1,voidState);voidProbe.Original=voidRequest;
                mediator.SendAll(voidRequest,voidProbe.Cancellation.Token);
                Check(voidProbe.Handlers.SequenceEqual(new[]{2,1,3})&&voidState.Version==3&&voidState.Values.SequenceEqual(new[]{3,4,5,-9}),"void branches use distinct request and shared state");
                var syncReply=mediator.Send(new R.Ping(10),sync.Token.Token);Check(syncReply.A==12,"single sync route response");
                Check(!pending.IsCompleted&&gate.Consumptions==0&&query.Trace.SequenceEqual(new[]{"before","factory","body"})&&query.Released.Count==0,"sync routes leave pending state untouched");
                var streamState=new S.SharedState();var stream=mediator.StreamAsync(new S.Items(streamState));var cursor=stream.GetAsyncEnumerator();
                Check(streamProbe.Starts==0&&streamProbe.Resolved.Count==0,"Stream remains lazy before enumeration");
                var values=new List<int>();try{while(await cursor.MoveNextAsync())values.Add(cursor.Current);}finally{await cursor.DisposeAsync();}
                Check(values.SequenceEqual(new[]{2,4})&&streamProbe.Starts==1&&streamProbe.SourceDisposals==1,"Stream route result and source lifecycle");
                Check(streamProbe.Seen.Count==10&&streamProbe.Seen.All(s=>s.Marker==7&&s.Pad==11&&ReferenceEquals(s.Shared,streamState)),"Stream request identity stays independent");
                Check(!pending.IsCompleted&&gate.Consumptions==0&&query.Released.Count==0,"Stream leaves pending response untouched");
                gate.Complete();var response=await pending;
                Check(response.SequenceEqual(new[]{22})&&gate.Consumptions==1&&ReferenceEquals(query.HandlerResponse,query.AfterInput)&&ReferenceEquals(query.AfterOutput,response),"async response identity and single consumption");
                var managedOrder=new[]{"before","factory","body","dispose","after","finally"};
                Check(sync.Trace.SequenceEqual(managedOrder)&&query.Trace.SequenceEqual(managedOrder)&&sync.Created.Count==1&&sync.Released.Count==1&&query.Created.Count==1&&query.Released.Count==1,"owned handlers cleaned up independently");
                Check(sync.Requests.Select(r=>r.Value).SequenceEqual(new[]{10,11,11,11,11})&&query.Requests.Select(r=>r.Value).SequenceEqual(new[]{20,21,21,21,21}),"single route saved request state");
                var allPhases=Enumerable.Range(0,3).SelectMany(_=>new[]{"before","body","after","finally"});
                foreach(var p in new[]{responseProbe,voidProbe}){
                    Check(p.Branch==3&&p.Trace.Select(x=>x.Split(':')[0]).SequenceEqual(allPhases),"all branch callback order");
                    Check(p.Resolved.SequenceEqual(new[]{"K","H2","H1","H3"})&&p.Ended.Count==0,"borrowed branch receivers live until scope");
                }
                Check(streamProbe.Ended.Count==0,"borrowed Stream receivers live until scope");
                await scope.DisposeAsync();
                foreach(var p in new[]{responseProbe,voidProbe})Check(p.Ended.SequenceEqual(p.Resolved.AsEnumerable().Reverse()),"scope disposes every borrowed branch receiver once");
                Check(streamProbe.Ended.SequenceEqual(new[]{"F2","M","F1","H"}),"scope owns Stream receivers");
                return JsonSerializer.Serialize(new{generatorInputs=1,runtimeOperations=5,registrationFingerprintMatched=true,
                    defaultScopedBranchServices=allTypes.Select(t=>t.FullName),copied,destination=responseState.Values,responseVersion=responseState.Version,voidVersion=voidState.Version,
                    allResponse=AllResult(responseProbe),allVoid=AllResult(voidProbe),syncValue=syncReply.A,sync=Managed(sync),query=Managed(query),response,values,
                    pendingConsumptions=gate.Consumptions,streamProbe.Starts,streamProbe.SourceDisposals,streamEnds=streamProbe.Ended});
            }finally{responseProbe.Cancellation.Dispose();voidProbe.Cancellation.Dispose();sync.Token.Dispose();query.Token.Dispose();}
        }
        """;

    private static void AssertSendAllCompositionContract(Compilation output)
    {
        var contract = output.GetTypeByMetadataName("AllMix.Generated.Many.IZendiator"); Assert.NotNull(contract);
        var all = contract.GetMembers("SendAll").OfType<IMethodSymbol>().ToArray(); Assert.Equal(3, all.Length);
        Assert.Contains(all, m => m.ReturnType is IArrayTypeSymbol a && a.ElementType.SpecialType == SpecialType.System_Int32);
        Assert.Contains(all, m => m.ReturnType.SpecialType == SpecialType.System_Int32 && m.Parameters[1].Type.Name == "Span");
        Assert.Contains(all, m => m.ReturnsVoid && m.Parameters[0].Type.ToDisplayString() == "AllSync.Command.Ping");
        foreach (var (method, request) in new[] { ("Send", "Closed.AllMixSync.Ping"), ("SendAsync", "Closed.AllMixAsync.Ping"), ("StreamAsync", "Closed.AllMixStream.Items") })
            Assert.Equal(request, Assert.Single(contract.GetMembers(method).OfType<IMethodSymbol>()).Parameters[0].Type.ToDisplayString());
    }

    private static object AssertSendAllCompositionRegistration(Compilation output)
    {
        var types = output.SyntaxTrees.SelectMany(t => t.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()).ToArray();
        var registrar = Assert.Single(types, t => t.Identifier.ValueText == "ZendiatorGeneratedRegistrar");
        var calls = registrar.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(c => c.Expression is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "expected" }, Name: GenericNameSyntax }).ToArray();
        string Request(InvocationExpressionSyntax c) => ((GenericNameSyntax)((MemberAccessExpressionSyntax)c.Expression).Name).TypeArgumentList.Arguments[0].ToString();
        var handlers = calls.Where(c => ((MemberAccessExpressionSyntax)c.Expression).Name.Identifier.ValueText == "Handlers").ToArray();
        var pipelines = calls.Where(c => ((MemberAccessExpressionSyntax)c.Expression).Name.Identifier.ValueText == "Pipeline").ToArray();
        Assert.Equal(new[] { "global::AllSync.Response.Ping", "global::Closed.AllMixAsync.Ping", "global::Closed.AllMixStream.Items", "global::AllSync.Command.Ping", "global::Closed.AllMixSync.Ping" }, handlers.Select(Request));
        Assert.Equal(new[] { "global::Closed.AllMixStream.Items", "global::AllSync.Command.Ping", "global::Closed.AllMixSync.Ping", "global::AllSync.Response.Ping", "global::Closed.AllMixAsync.Ping" }, pipelines.Select(Request));
        foreach (var name in new[] { "Response", "Command" })
        {
            var call = Assert.Single(handlers, c => Request(c) == "global::AllSync." + name + ".Ping");
            Assert.Equal(new[] { "global::AllSync." + name + ".H2", "global::AllSync." + name + ".H1", "global::AllSync." + name + ".H3" }, call.ArgumentList.Arguments.Skip(1).Select(a => Assert.IsType<TypeOfExpressionSyntax>(a.Expression).Type.ToString()));
        }
        var members = types.Where(t => t.Identifier.ValueText == "Zendiator").SelectMany(t => t.Members).ToArray();
        var mediator = output.GetTypeByMetadataName("AllMix.Generated.Many.Zendiator")!;
        var branches = mediator.GetMembers().OfType<IMethodSymbol>().Where(m => m.DeclaredAccessibility == Accessibility.Private &&
            m.Name.StartsWith("Branch", StringComparison.Ordinal)).Select(m => m.Name).ToArray();
        Assert.Equal(6, branches.Length);
        foreach (var entry in mediator.GetMembers("SendAll").OfType<IMethodSymbol>().GroupBy(CanonicalEntrySuffix).Select(g => g.First()))
        {
            var local = CanonicalEntryMethods(entry, "Branch").Select(m => m.Name).ToArray();
            Assert.Equal(3, local.Length);
            Assert.Equal(local.Length, local.Distinct(StringComparer.Ordinal).Count());
        }
        Assert.Equal(branches.Length, branches.Distinct(StringComparer.Ordinal).Count());
        Assert.Single(members.OfType<ConstructorDeclarationSyntax>());
        var responseOverloads = members.OfType<MethodDeclarationSyntax>().Where(m => m.Identifier.ValueText == "SendAll" && m.ParameterList.Parameters[0].Type!.ToString() == "global::AllSync.Response.Ping").ToArray();
        Assert.Equal(2, responseOverloads.Length);
        Assert.Single(mediator.GetMembers("SendAll").OfType<IMethodSymbol>()
            .Where(m => m.Parameters[0].Type.ToDisplayString() == "AllSync.Response.Ping").Select(CanonicalEntrySuffix).Distinct(StringComparer.Ordinal));
        var span = Assert.Single(responseOverloads, m => m.ParameterList.Parameters[1].Identifier.ValueText == "destination");
        var forwarded = Assert.Single(span.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Select(c => output.GetSemanticModel(c.SyntaxTree).GetSymbolInfo(c).Symbol).OfType<IMethodSymbol>(),
            m => m.Name == "SendAll" && SymbolEqualityComparer.Default.Equals(m.ContainingType, mediator));
        Assert.IsAssignableFrom<IArrayTypeSymbol>(forwarded.ReturnType);
        return new { handlerOrder = handlers.Select(Request), pipelineOrder = pipelines.Select(Request), privateBranchNames = branches };
    }

    [Fact]
    public void SendAll_composition_gate_preserves_all_branches_ordinals_and_rejects_multiple_wrapper_mixture_as_a_whole()
    {
        var source = SendAllCompositionSource(true);
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "AllCompositionGate_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        var folder = string.IsNullOrEmpty(evidence) ? null : Path.Combine(evidence, "sendall-composition-gate");
        SaveSendAllSync(folder, source, driver, output, diagnostics);
        Assert.Equal(5, diagnostics.Length); Assert.All(diagnostics, d => Assert.Equal("ZEN0037", d.Id));
        var start = source.IndexOf("public static IServiceCollection RegisterRejected(", StringComparison.Ordinal);
        var end = source.IndexOf("/* rejected end */", start, StringComparison.Ordinal);
        Assert.All(diagnostics, d => Assert.InRange(d.Location.SourceSpan.Start, start, end));
        var anchors = diagnostics.Select(d => source.Substring(d.Location.SourceSpan.Start, d.Location.SourceSpan.Length)).ToArray();
        foreach (var request in new[] { "S.Items", "C.Ping", "R.Ping", "B.Ping", "A.Ping" }) Assert.Single(anchors, a => a.StartsWith("c.Pipeline<" + request + ">", StringComparison.Ordinal));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        AssertSendAllCompositionContract(output); var registration = AssertSendAllCompositionRegistration(output);
        Assert.Null(output.GetTypeByMetadataName("AllMix.Generated.Rejected.IZendiator")); Assert.Null(output.GetTypeByMetadataName("AllMix.Generated.Rejected.ZendiatorGeneratedRegistrar"));
        var files = driver.GetRunResult().Results.Single().GeneratedSources;
        Assert.Contains(files, f => f.HintName.EndsWith("Interceptors.g.cs", StringComparison.Ordinal) && f.SourceText.ToString().Contains("AllMix.Generated.Many.ZendiatorGeneratedRegistrar", StringComparison.Ordinal));
        Assert.DoesNotContain(files, f => f.HintName.EndsWith("Interceptors.g.cs", StringComparison.Ordinal) && f.SourceText.ToString().Contains("AllMix.Generated.Rejected.ZendiatorGeneratedRegistrar", StringComparison.Ordinal));
        if (folder != null) File.WriteAllText(Path.Combine(folder, "gate-result.json"), JsonSerializer.Serialize(new { generatorInputs = 1, runtimeOperations = 0, acceptedRoutes = 5, rejectedPipelineDiagnostics = 5, unsupportedMultipleWrapperWholeCompositionRejected = true, registration }));
    }

    [Fact]
    public async Task SendAll_composition_response_void_Stream_and_single_Send_share_identity_without_pending_state_or_ownership_leaks()
    {
        var source = SendAllCompositionSource(false);
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "AllCompositionRuntime_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        var folder = string.IsNullOrEmpty(evidence) ? null : Path.Combine(evidence, "sendall-composition-runtime");
        SaveSendAllSync(folder, source, driver, output, diagnostics);
        Assert.Empty(diagnostics); Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        AssertSendAllCompositionContract(output); var registration = AssertSendAllCompositionRegistration(output);
        Assert.Equal(5, driver.GetRunResult().Results.Single().GeneratedSources.Count(f => IsCanonicalRoute(f)));
        using var bytes = new MemoryStream(); var emitted = output.Emit(bytes); Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        if (folder != null) { File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray()); File.WriteAllText(Path.Combine(folder, "registration.json"), JsonSerializer.Serialize(registration)); }
        bytes.Position = 0; var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var run = assembly.GetType("AllMix.Bridge")!.GetMethod("Run")!.CreateDelegate<Func<Task<string>>>();
        var result = await run().WaitAsync(TimeSpan.FromSeconds(15)); if (folder != null) File.WriteAllText(Path.Combine(folder, "runtime.json"), result);
        using var observed = JsonDocument.Parse(result); var value = observed.RootElement;
        Assert.Equal(5, value.GetProperty("runtimeOperations").GetInt32()); Assert.Equal(3, value.GetProperty("copied").GetInt32());
        Assert.Equal(new[] { 115, 114, 116, -9 }, value.GetProperty("destination").EnumerateArray().Select(x => x.GetInt32()));
        Assert.Equal(new[] { 22 }, value.GetProperty("response").EnumerateArray().Select(x => x.GetInt32()));
        Assert.Equal(new[] { 2, 4 }, value.GetProperty("values").EnumerateArray().Select(x => x.GetInt32()));
        Assert.Equal(1, value.GetProperty("pendingConsumptions").GetInt32());
    }
}
