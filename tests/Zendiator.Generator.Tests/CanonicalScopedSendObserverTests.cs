using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;
using Zendiator.SourceGenerator;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private static string ScopedObserverModule(bool isVoid)
    {
        var ns = isVoid ? "ScopedVoid" : "ScopedResponse"; var ret = isVoid ? "void" : "int";
        var success = isVoid ? "IScopedSendSuccessObserver<Request>" : "IScopedSendResponseObserver<Request,int>";
        return $$"""
            namespace {{ns}} {
            public readonly ref struct Request:ISyncRequest{{(isVoid ? "" : "<int>")}} {
                public readonly Span<int> Values;public Request(Span<int> values){Values=values;}
            }
            [ZendiatorHandler]public sealed class Handler:ISyncRequestHandler<Request{{(isVoid ? "" : ",int")}}>,IDisposable {
                private readonly ScopedFixture.Probe p;public Handler(ScopedFixture.Probe p){this.p=p;p.HandlerResolutions++;p.Trace.Add("resolve:H");}
                public {{ret}} Handle(scoped Request r,CancellationToken t){p.Calls++;p.Record("handler",r.Values,t);r.Values[1]+=5;if(p.Mode=="handler")throw p.Failure;{{(isVoid ? "" : "return r.Values[0]+r.Values[1];")}}}
                public void Dispose(){p.Disposals++;p.Trace.Add("dispose:H");}
            }
            public sealed class Observer:IScopedSendBeforeObserver<Request>,{{success}},IScopedSendExceptionObserver<Request>,IScopedSendFinallyObserver<Request>,IDisposable {
                private readonly ScopedFixture.Probe p;public Observer(ScopedFixture.Probe p){this.p=p;p.ObserverResolutions++;p.Trace.Add("resolve:O");}
                public void Before(scoped Request r,CancellationToken t){p.Record("before",r.Values,t);r.Values[0]++;if(p.Mode=="before")throw p.Failure;}
                public void After(scoped Request r,{{(isVoid ? "" : "int response,")}}CancellationToken t){p.Record("after",r.Values,t);{{(isVoid ? "" : "ScopedFixture.Probe.Check(response==16,\"response observer cannot replace result\");")}}}
                public void OnException(scoped Request r,Exception e,CancellationToken t){ScopedFixture.Probe.Check(ReferenceEquals(e,p.Failure),"observer exception identity");p.Record("exception",r.Values,t);}
                public void Finally(scoped Request r,SendExitInfo exit,CancellationToken t){ScopedFixture.Probe.Check(exit.Success==(p.Mode=="success")&&ReferenceEquals(exit.Error,p.Mode=="success"?null:p.Failure),"entered stage exit identity");p.Record("finally:"+exit.Success,r.Values,t);}
                public void Dispose(){p.Disposals++;p.Trace.Add("dispose:O");}
            }
            [ZendiatorComposition("{{ns}}")]public sealed class Composition;
            public static class Bridge {
                public static IServiceCollection Register(IServiceCollection services)=>services.AddZendiator<Composition>(c=>{
                    c.Handlers(typeof(Request),DispatchFamily.SendSync,typeof(Handler));
                    c.Pipeline(typeof(Request),DispatchFamily.SendSync,Stage.Hook<Observer>());
                });
                // Independent concrete reference selected from the declared synchronous observer contract.
                private static {{ret}} Reference(IServiceProvider sp,scoped Request request,CancellationToken token){
                    token.ThrowIfCancellationRequested();var observer=sp.GetRequiredService<Observer>();var success=false;Exception? error=null;
                    try{
                        ((IScopedSendBeforeObserver<Request>)observer).Before(request,token);token.ThrowIfCancellationRequested();
                        var handler=sp.GetRequiredService<Handler>();{{(isVoid ? "" : "var result=")}}((ISyncRequestHandler<Request{{(isVoid ? "" : ",int")}}>)handler).Handle(request,token);
                        (({{success}})observer).After(request,{{(isVoid ? "" : "result,")}}token);success=true;{{(isVoid ? "" : "return result;")}}
                    }catch(Exception failure){error=failure;try{((IScopedSendExceptionObserver<Request>)observer).OnException(request,failure,token);}catch(Exception replacement){error=replacement;throw;}throw;}
                    finally{((IScopedSendFinallyObserver<Request>)observer).Finally(request,new(success,error),token);}
                }
                public static string Observe(bool generated,string mode){
                    var p=new ScopedFixture.Probe{Mode=mode};try{
                        IServiceCollection services=new ServiceCollection();services.AddSingleton(p);Register(services);
                        using var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});using var scope=provider.CreateScope();var sp=scope.ServiceProvider;
                        var m=sp.GetRequiredService<{{ns}}.Generated.Composition.IZendiator>();
                        Span<int> values=stackalloc int[]{3,7};var request=new Request(values);if(mode=="cancel")p.Token.Cancel();int? result=null;var error="none";
                        try{if(generated){ {{(isVoid ? "m.Send(request,p.Token.Token);" : "result=m.Send(request,p.Token.Token);")}} }else{ {{(isVoid ? "Reference(sp,request,p.Token.Token);" : "result=Reference(sp,request,p.Token.Token);")}} } }
                        catch(Exception e)when(ReferenceEquals(e,p.Failure)){error="sentinel";}
                        catch(OperationCanceledException e){ScopedFixture.Probe.Check(mode=="cancel"&&e.CancellationToken==p.Token.Token,"cancellation identity");error="cancel";}
                        ScopedFixture.Probe.Check(p.Calls==(mode is "before" or "cancel"?0:1)&&p.HandlerResolutions==p.Calls,"Before failure/cancel never resolves or invokes handler");
                        ScopedFixture.Probe.Check(p.ObserverResolutions==(mode=="cancel"?0:1)&&p.Disposals==0,"entry guard precedes resolution;borrowed services live through operation");
                        var expected=mode=="cancel"?new[]{3,7}:mode=="before"?new[]{4,7}:new[]{4,12};ScopedFixture.Probe.Check(values.SequenceEqual(expected),"caller stackalloc span sees callback and handler mutations");
                        var phases=p.Trace.Where(x=>!x.StartsWith("resolve:",StringComparison.Ordinal)).ToArray();
                        var expectedPhases=mode switch{"success"=>new[]{"before:10","handler:11","after:16","finally:True:16"},"handler"=>new[]{"before:10","handler:11","exception:16","finally:False:16"},"before"=>new[]{"before:10","exception:11","finally:False:11"},_=>Array.Empty<string>()};ScopedFixture.Probe.Check(phases.SequenceEqual(expectedPhases),"entered exception/finally boundary and handler omission");
                        var beforeScope=p.Disposals;scope.Dispose();ScopedFixture.Probe.Check(p.Disposals==p.HandlerResolutions+p.ObserverResolutions,"DI owns direct class receivers");
                        return JsonSerializer.Serialize(new{profile="{{ns}}",mode,result,error,values=values.ToArray(),p.Calls,p.HandlerResolutions,p.ObserverResolutions,beforeScope,p.Disposals,p.Trace});
                    }finally{p.Token.Dispose();}
                }
            } }
            """;
    }

    private static string ScopedOldSentinel(bool value) => $$"""
        namespace {{(value ? "OldValue" : "OldClass")}} {
        public {{(value ? "readonly record struct" : "sealed record")}} Request(int Value):ISyncRequest<int>;
        [ZendiatorHandler]public sealed class Handler:ISyncRequestHandler<Request,int>{public int Handle(Request r,CancellationToken t)=>r.Value;}
        public sealed class Hook:ISendRequestTransform<Request>,ISendResponseTransform<Request,int>,ISendExceptionObserver<Request>,ISendFinallyObserver<Request>{
            public Request Before(Request r,CancellationToken t)=>r;public int After(Request r,int result,CancellationToken t)=>result+1;
            public void OnException(Request r,Exception e,CancellationToken t){}public void Finally(Request r,SendExitInfo exit,CancellationToken t){}
        }
        [ZendiatorComposition("old-{{value}}")]public sealed class Composition;
        public static class Bridge{
            public static IServiceCollection Register(IServiceCollection s)=>s.AddZendiator<Composition>(c=>{c.Handlers<Request>(DispatchFamily.SendSync,typeof(Handler));c.Pipeline<Request>(DispatchFamily.SendSync,Stage.Hook<Hook>());});
            public static int Run(){IServiceCollection s=new ServiceCollection();Register(s);using var provider=s.BuildServiceProvider();using var scope=provider.CreateScope();return scope.ServiceProvider.GetRequiredService<{{(value ? "OldValue" : "OldClass")}}.Generated.Composition.IZendiator>().Send(new Request(9));}
        } }
        """;

    private static string ScopedObserverSource(bool negative = false) => """
        using System;using System.Linq;using System.Collections.Generic;using System.Threading;using System.Threading.Tasks;using System.Text.Json;
        using Microsoft.Extensions.DependencyInjection;using Zendiator;using Zendiator.DependencyInjection;
        namespace ScopedFixture{
        public sealed class Probe{
            public readonly CancellationTokenSource Token=new();public readonly Exception Failure=new FormatException("sentinel");public string Mode="success";
            public int Calls,HandlerResolutions,ObserverResolutions,Disposals;public readonly List<string> Trace=new();
            public static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
            public void Record(string phase,scoped Span<int> values,CancellationToken token){Check(token==Token.Token,"callback token identity");Trace.Add(phase+":"+(values[0]+values[1]));}
        }
        public static class Entry{
            public static string Run(){var rows=new List<object>();
                foreach(var item in new[]{("response","success"),("response","handler"),("response","before"),("void","success"),("void","cancel")}){
                    var generated=item.Item1=="response"?ScopedResponse.Bridge.Observe(true,item.Item2):ScopedVoid.Bridge.Observe(true,item.Item2);
                    var reference=item.Item1=="response"?ScopedResponse.Bridge.Observe(false,item.Item2):ScopedVoid.Bridge.Observe(false,item.Item2);
                    Probe.Check(generated==reference,"scoped generated/reference mismatch\n"+generated+"\n"+reference);rows.Add(new{generated=JsonDocument.Parse(generated).RootElement.Clone(),reference=JsonDocument.Parse(reference).RootElement.Clone()});
                }
                Probe.Check(OldClass.Bridge.Run()==10&&OldValue.Bridge.Run()==10,"existing request and response transforms preserve class/value contracts");
                return JsonSerializer.Serialize(new{pairs=5,pairedDispatches=10,sentinelDispatches=2,dispatches=12,rows});
            }
        }}
        """ + ScopedObserverModule(false) + ScopedObserverModule(true) + ScopedOldSentinel(false) + ScopedOldSentinel(true) + (negative ? ScopedRejectedRoutes : "");

    private const string ScopedRejectedRoutes = """
        namespace ScopedRejected{
        public struct Observer:IScopedSendBeforeObserver<ScopedResponse.Request>,IScopedSendResponseObserver<ScopedResponse.Request,int>,IScopedSendExceptionObserver<ScopedResponse.Request>,IScopedSendFinallyObserver<ScopedResponse.Request>{
            public void Before(scoped ScopedResponse.Request r,CancellationToken t){}public void After(scoped ScopedResponse.Request r,int value,CancellationToken t){}
            public void OnException(scoped ScopedResponse.Request r,Exception e,CancellationToken t){}public void Finally(scoped ScopedResponse.Request r,SendExitInfo exit,CancellationToken t){}
        }
        [ZendiatorComposition("struct-observer")]public sealed class StructObserver;
        [ZendiatorComposition("wrong-family")]public sealed class WrongFamily;
        [ZendiatorComposition("multiple-routes")]public sealed class Multiple;
        public static class Registration{
            public static IServiceCollection A(IServiceCollection s)=>s.AddZendiator<StructObserver>(c=>{c.Handlers(typeof(ScopedResponse.Request),DispatchFamily.SendSync,typeof(ScopedResponse.Handler));c.Pipeline(typeof(ScopedResponse.Request),DispatchFamily.SendSync,Stage.Hook<Observer>());});
            public static IServiceCollection B(IServiceCollection s)=>s.AddZendiator<WrongFamily>(c=>{c.Handlers(typeof(ScopedResponse.Request),DispatchFamily.SendAllSync,typeof(ScopedResponse.Handler));c.Pipeline(typeof(ScopedResponse.Request),DispatchFamily.SendAllSync,Stage.Hook<ScopedResponse.Observer>());});
            public static IServiceCollection C(IServiceCollection s)=>s.AddZendiator<Multiple>(c=>{
                c.Handlers(typeof(ScopedResponse.Request),DispatchFamily.SendSync,typeof(ScopedResponse.Handler));c.Pipeline(typeof(ScopedResponse.Request),DispatchFamily.SendSync,Stage.Hook<ScopedResponse.Observer>());
                c.Handlers<OldValue.Request>(DispatchFamily.SendSync,typeof(OldValue.Handler));c.Pipeline<OldValue.Request>(DispatchFamily.SendSync,Stage.Hook<OldValue.Hook>());
            });
        }
        public static class AwaitSafety{
            public static async Task<int> LiveAcrossAwait(){Span<int> data=stackalloc int[]{1,2};var request=new ScopedResponse.Request(data);await Task.Yield();return request.Values[0];}
        } }
        """;

    private const string ScopedCompilerSafetySource = """
        namespace ScopedCompilerSafety{
        public sealed class Heap{public ScopedResponse.Request Saved;}
        public static class Safety{
            public static object Box(scoped ScopedResponse.Request request)=>request;
            public static System.Func<int> Closure(scoped ScopedResponse.Request request)=>()=>request.Values[0];
            public static ScopedResponse.Request Escape(scoped ScopedResponse.Request request)=>request;
        } }
        """;

    private static string? ScopedEvidenceFolder(string name)
    { var path = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR"); if (string.IsNullOrEmpty(path)) return null; path = Path.Combine(path, "scoped-send-" + name); Directory.CreateDirectory(path); return path; }

    private static void AssertScopedObserverOutput(Compilation output, GeneratorDriver driver)
    {
        foreach (var ns in new[] { "ScopedResponse", "ScopedVoid" })
        {
            var api = output.GetTypeByMetadataName(ns + ".Generated.Composition.IZendiator"); Assert.NotNull(api);
            var send = Assert.Single(api.GetMembers("Send").OfType<IMethodSymbol>()); Assert.Equal(ScopedKind.ScopedValue, send.Parameters[0].ScopedKind); Assert.True(send.Parameters[0].Type.IsRefLikeType);
            var mediator = output.GetTypeByMetadataName(ns + ".Generated.Composition.Zendiator")!;
            var execute = Assert.Single(mediator.GetMembers().OfType<IMethodSymbol>(), m => m.Name.StartsWith("Execute", StringComparison.Ordinal)); Assert.Equal(ScopedKind.ScopedValue, execute.Parameters[0].ScopedKind); Assert.False(execute.IsAsync);
            Assert.All(mediator.GetMembers().OfType<IFieldSymbol>(), f => Assert.False(f.Type.IsRefLikeType));
            var observer = output.GetTypeByMetadataName(ns + ".Observer")!;
            Assert.All(observer.AllInterfaces.Where(i => i.Name.StartsWith("IScopedSend", StringComparison.Ordinal)).SelectMany(i => i.GetMembers().OfType<IMethodSymbol>()), m => { Assert.True(m.ReturnsVoid); Assert.Equal(ScopedKind.ScopedValue, m.Parameters[0].ScopedKind); });
        }
        foreach (var ns in new[] { "OldClass", "OldValue" }) Assert.NotNull(output.GetTypeByMetadataName(ns + ".Generated.Composition.IZendiator"));
        var catalog = Assert.IsType<CompositionSelectionCatalog>(Assert.Single(driver.GetRunResult().Results.Single().TrackedSteps["CompositionSelectionModel"].SelectMany(s => s.Outputs)).Value);
        var routes = catalog.Compositions.Where(c => c.Key is "ScopedResponse" or "ScopedVoid").SelectMany(c => c.Pipelines).ToArray(); Assert.Equal(2, routes.Length);
        Assert.All(routes, r => { Assert.Equal(BoundRequestShape.ClosedRefLike, r.RequestShape); var stage = Assert.Single(r.Stages); Assert.NotNull(stage.ScopedObserver); Assert.Null(stage.SyncEmission); Assert.Null(stage.Capabilities); Assert.All(stage.Callbacks, c => { Assert.True(CanonicalScopedSyncNormalizer.IsObservation(c.Phase)); Assert.Equal("ScopedValue", c.Parameters[0].ScopedKind); }); });
        var registrars = output.SyntaxTrees.SelectMany(t => t.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()).Where(c => c.Identifier.ValueText == "ZendiatorGeneratedRegistrar" && c.Ancestors().OfType<NamespaceDeclarationSyntax>().Any(n => n.Name.ToString().StartsWith("ScopedResponse.Generated.", StringComparison.Ordinal) || n.Name.ToString().StartsWith("ScopedVoid.Generated.", StringComparison.Ordinal))).ToArray(); Assert.Equal(2, registrars.Length);
        Assert.All(registrars.SelectMany(r => r.DescendantNodes().OfType<InvocationExpressionSyntax>()).Where(i => i.Expression is MemberAccessExpressionSyntax m && m.Name.Identifier.ValueText is "Handlers" or "Pipeline"), i => Assert.IsType<TypeOfExpressionSyntax>(i.ArgumentList.Arguments[0].Expression));
    }

    [Fact]
    public void ScopedSendObserver_Type_registration_stackalloc_and_entered_boundaries_match_reference()
    {
        var source = ScopedObserverSource(); var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "ScopedRuntime_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics); var folder = ScopedEvidenceFolder("runtime"); SaveSendAllSync(folder, source, driver, output, diagnostics);
        Assert.Empty(diagnostics); Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)); AssertScopedObserverOutput(output, driver);
        using var bytes = new MemoryStream(); var emitted = output.Emit(bytes); Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics)); if (folder != null) File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray()); bytes.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes); var result = assembly.GetType("ScopedFixture.Entry")!.GetMethod("Run")!.CreateDelegate<Func<string>>()(); if (folder != null) File.WriteAllText(Path.Combine(folder, "runtime.json"), result);
        using var json = JsonDocument.Parse(result); Assert.Equal(12, json.RootElement.GetProperty("dispatches").GetInt32());
    }

    [Fact]
    public void ScopedSendObserver_ref_safety_compiler_errors_preserve_unrelated_generated_compositions()
    {
        var source = ScopedObserverSource(true); var compilation = SyncCompilation(source, "ScopedNegative_" + Guid.NewGuid().ToString("N")); var safety = CSharpSyntaxTree.ParseText(ScopedCompilerSafetySource, (CSharpParseOptions)compilation.SyntaxTrees.First().Options, "compiler-safety.cs");
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(compilation.AddSyntaxTrees(safety), out var output, out var diagnostics); var folder = ScopedEvidenceFolder("negative"); SaveSendAllSync(folder, source, driver, output, diagnostics); if (folder != null) File.WriteAllText(Path.Combine(folder, "compiler-safety-input.cs"), ScopedCompilerSafetySource);
        AssertScopedObserverOutput(output, driver);
        foreach (var name in new[] { "StructObserver", "WrongFamily", "Multiple" }) { Assert.Null(output.GetTypeByMetadataName("ScopedRejected.Generated." + name + ".IZendiator")); Assert.Null(output.GetTypeByMetadataName("ScopedRejected.Generated." + name + ".ZendiatorGeneratedRegistrar")); }
        Assert.NotEmpty(diagnostics); Assert.Contains(diagnostics, d => d.Id == "ZEN0034");
        var errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray(); Assert.NotEmpty(errors); Assert.All(errors, e => Assert.Same(safety, e.Location.SourceTree));
        foreach (var method in new[] { "Heap", "Box", "Closure", "Escape" })
        {
            var start = ScopedCompilerSafetySource.IndexOf(method, StringComparison.Ordinal); var end = ScopedCompilerSafetySource.IndexOf(';', start); Assert.Contains(errors, e => e.Location.SourceSpan.Start >= start && e.Location.SourceSpan.Start <= end);
        }
        // Other declaration/body errors stop Emit before async liveness analysis. Remove only their input tree,
        // retaining every generated tree from this one Driver run, to exercise the actual await-live rejection.
        var awaitOnly = output.RemoveSyntaxTrees(safety); Assert.Empty(awaitOnly.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        using var bytes = new MemoryStream(); var emitted = awaitOnly.Emit(bytes); Assert.False(emitted.Success); Assert.Contains(emitted.Diagnostics, d => d.Id == "CS4007" && d.GetMessage().Contains("Request", StringComparison.Ordinal));
        if (folder != null) File.WriteAllText(Path.Combine(folder, "negative-result.json"), JsonSerializer.Serialize(new { generatorInputs = 1, generatorDrivers = 1, inputSyntaxTrees = 2, compilerOnlyEmitProjection = 1, generatedTreesRetained = true, safety = errors.Select(d => new { d.Id, message = d.GetMessage(), span = d.Location.SourceSpan.ToString() }), awaitLive = emitted.Diagnostics.Select(d => new { d.Id, message = d.GetMessage(), severity = d.Severity.ToString() }), unrelatedGeneratedCompositions = 4, rejectedCompositions = 3, dispatches = 0 }));
    }
}
