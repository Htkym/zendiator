using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;
using Zendiator.SourceGenerator;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private static string OpenSendAllSyncSource()
    {
        var closed = SendAllSyncSource(false);
        return OpenSendAsyncSource() + closed[closed.IndexOf("namespace AllSync", StringComparison.Ordinal)..] + """
            namespace OpenAllFixture {
            public interface ISecond;public interface IHook;
            public sealed class A:ISecond,IHook;public sealed class B:ISecond,IHook;
            public sealed class Probe {
                public readonly CancellationTokenSource Cancellation=new();public readonly Exception Failure=new FormatException("second branch");
                public string Mode="";public object? Original,Saved;public int Branch;
                public readonly List<string> Trace=new(),Handlers=new();public readonly List<Type> Created=new(),Disposed=new();
                public static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
                public void Hit(string phase,object r,int value,int[] values,CancellationToken t){Check(t==Cancellation.Token&&ReferenceEquals(r,phase=="before"?Original:Saved),"branch token and request locality");Check(values.SequenceEqual(new[]{3,4,5,-9}),"no destination write before all Finally callbacks complete");Trace.Add(phase+":"+Branch+":"+value+":"+values[0]);}
                public int Body(string handler,object r,int value,int[] values,CancellationToken t){Hit("handler:"+handler,r,value,values,t);Handlers.Add(handler);if(Mode=="body"&&Branch==2)throw Failure;return value+values[0]+(handler=="Z"?20:10);}
            }
            public static class Entry {
                public static string Run(){var rows=new[]{(OpenAll.Bridge.Observe<A,B>(true,"array"),OpenAll.Bridge.Observe<A,B>(false,"array")),(OpenAll.Bridge.Observe<A,B>(true,"span"),OpenAll.Bridge.Observe<A,B>(false,"span")),(OpenAll.Bridge.Observe<A,B>(true,"body"),OpenAll.Bridge.Observe<A,B>(false,"body"))};
                    foreach(var row in rows)Probe.Check(row.Item1==row.Item2,"generated/reference mismatch\n"+row.Item1+"\n"+row.Item2);
                    return JsonSerializer.Serialize(new{pairs=3,dispatches=6,rows=rows.Select(r=>new{generated=JsonDocument.Parse(r.Item1).RootElement.Clone(),reference=JsonDocument.Parse(r.Item2).RootElement.Clone()})});}
            } }
            namespace OpenAll {
            public sealed record Request<TLeft,TRight>(int Value,int[] Values):ISyncMultiRequest<int>;
            // Declared Z then A, deliberately opposite name order and with different request parameter order.
            [ZendiatorHandler]public sealed class ZHandler<TLeft,TRight>:ISyncRequestHandler<Request<TRight,TLeft>,int>,IDisposable where TLeft:class,new() where TRight:class {
                private readonly OpenAllFixture.Probe p;public ZHandler(OpenAllFixture.Probe p){this.p=p;p.Created.Add(GetType());}
                public int Handle(Request<TRight,TLeft> r,CancellationToken t)=>p.Body("Z",r,r.Value,r.Values,t);
                public void Dispose(){p.Disposed.Add(GetType());}
            }
            [ZendiatorHandler]public sealed class AHandler<TLeft,TRight>:ISyncRequestHandler<Request<TLeft,TRight>,int>,IDisposable where TLeft:class,OpenAllFixture.ISecond,new() where TRight:class {
                private readonly OpenAllFixture.Probe p;public AHandler(OpenAllFixture.Probe p){this.p=p;p.Created.Add(GetType());}
                public int Handle(Request<TLeft,TRight> r,CancellationToken t)=>p.Body("A",r,r.Value,r.Values,t);
                public void Dispose(){p.Disposed.Add(GetType());}
            }
            public sealed class Hook<TLeft,TRight>:ISendRequestTransform<Request<TRight,TLeft>>,ISendResponseTransform<Request<TRight,TLeft>,int>,ISendExceptionObserver<Request<TRight,TLeft>>,ISendFinallyObserver<Request<TRight,TLeft>>,IDisposable where TLeft:class,OpenAllFixture.IHook where TRight:class {
                private readonly OpenAllFixture.Probe p;public Hook(OpenAllFixture.Probe p){this.p=p;p.Created.Add(GetType());}
                public Request<TRight,TLeft> Before(Request<TRight,TLeft> r,CancellationToken t){p.Branch++;p.Hit("before",r,r.Value,r.Values,t);OpenAllFixture.Probe.Check(r.Value==7,"each branch starts from original");var saved=new Request<TRight,TLeft>(r.Value+1,r.Values);p.Saved=saved;return saved;}
                public int After(Request<TRight,TLeft> r,int value,CancellationToken t){p.Hit("after",r,r.Value,r.Values,t);return value+100;}
                public void OnException(Request<TRight,TLeft> r,Exception e,CancellationToken t){OpenAllFixture.Probe.Check(ReferenceEquals(e,p.Failure),"entered exception identity");p.Hit("exception",r,r.Value,r.Values,t);}
                public void Finally(Request<TRight,TLeft> r,SendExitInfo exit,CancellationToken t){var failed=p.Mode=="body"&&p.Branch==2;OpenAllFixture.Probe.Check(exit.Success!=failed&&ReferenceEquals(exit.Error,failed?p.Failure:null),"Finally outcome and identity");p.Hit("finally:"+exit.Success,r,r.Value,r.Values,t);}
                public void Dispose(){p.Disposed.Add(GetType());}
            }
            [ZendiatorComposition("openAll")]public sealed class Composition;
            public static class Bridge {
                public static IServiceCollection Register(IServiceCollection s)=>s.AddZendiator<Composition>(c=>{
                    c.Handlers(typeof(Request<,>),DispatchFamily.SendAllSync,typeof(ZHandler<,>),typeof(AHandler<,>));
                    c.Pipeline(typeof(Request<,>),DispatchFamily.SendAllSync,Stage.Hook(typeof(Hook<,>)));
                });
                // Independent handwritten branch protocol; no generator graph or schedule supplies expected behavior.
                private static int Branch<TLeft,TRight>(IServiceProvider sp,Request<TLeft,TRight> request,int branch,CancellationToken t)
                    where TLeft:class,OpenAllFixture.ISecond,new() where TRight:class,OpenAllFixture.IHook,new(){
                    ArgumentNullException.ThrowIfNull(request);t.ThrowIfCancellationRequested();var hook=sp.GetRequiredService<Hook<TRight,TLeft>>();var saved=request;bool success=false;Exception? error=null;
                    try{saved=hook.Before(saved,t);ArgumentNullException.ThrowIfNull(saved);t.ThrowIfCancellationRequested();
                        var value=branch==0?sp.GetRequiredService<ZHandler<TRight,TLeft>>().Handle(saved,t):sp.GetRequiredService<AHandler<TLeft,TRight>>().Handle(saved,t);
                        value=hook.After(saved,value,t);success=true;return value;
                    }catch(Exception failure){error=failure;try{hook.OnException(saved,failure,t);}catch(Exception replacement){error=replacement;throw;}throw;}
                    finally{hook.Finally(saved,new(success,error),t);}
                }
                private static int[] Reference<TLeft,TRight>(IServiceProvider sp,Request<TLeft,TRight> request,CancellationToken t)
                    where TLeft:class,OpenAllFixture.ISecond,new() where TRight:class,OpenAllFixture.IHook,new(){
                    ArgumentNullException.ThrowIfNull(request);t.ThrowIfCancellationRequested();return new[]{Branch(sp,request,0,t),Branch(sp,request,1,t)};
                }
                private static int ReferenceSpan<TLeft,TRight>(IServiceProvider sp,Request<TLeft,TRight> request,Span<int> destination,CancellationToken t)
                    where TLeft:class,OpenAllFixture.ISecond,new() where TRight:class,OpenAllFixture.IHook,new(){
                    ArgumentNullException.ThrowIfNull(request);if(destination.Length<2)throw new ArgumentException("Destination is too short.",nameof(destination));var values=Reference(sp,request,t);values.AsSpan().CopyTo(destination);return values.Length;
                }
                public static string Observe<TLeft,TRight>(bool generated,string mode) where TLeft:class,OpenAllFixture.ISecond,new() where TRight:class,OpenAllFixture.IHook,new(){
                    var p=new OpenAllFixture.Probe{Mode=mode};using var token=p.Cancellation;IServiceCollection s=new ServiceCollection();s.AddSingleton(p);s.AddScoped(typeof(ZHandler<,>));var existing=s.Last();Register(s);var count=s.Count;Register(s);
                    OpenAllFixture.Probe.Check(s.Count==count&&s.Contains(existing),"TryAdd and repeated snapshot registration");
                    foreach(var definition in new[]{typeof(ZHandler<,>),typeof(AHandler<,>),typeof(Hook<,>)})OpenAllFixture.Probe.Check(s.Count(d=>d.ServiceType==definition&&d.ImplementationType==definition&&d.Lifetime==ServiceLifetime.Scoped)==1,"both ordered open handler definitions and hook registered");
                    using var provider=s.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});using var scope=provider.CreateScope();var sp=scope.ServiceProvider;var m=sp.GetRequiredService<OpenAll.Generated.Composition.IZendiator>();
                    var values=new[]{3,4,5,-9};var request=new Request<TLeft,TRight>(7,values);p.Original=request;int[]? result=null;var copied=-1;Exception? error=null;
                    try{if(mode=="array")result=generated?m.SendAll(request,token.Token):Reference(sp,request,token.Token);else copied=generated?m.SendAll(request,values.AsSpan(),token.Token):ReferenceSpan(sp,request,values.AsSpan(),token.Token);}catch(Exception e){error=e;}
                    var failed=mode=="body";OpenAllFixture.Probe.Check(failed?ReferenceEquals(error,p.Failure):error==null,"escaping exception identity");
                    OpenAllFixture.Probe.Check(p.Branch==2&&p.Handlers.SequenceEqual(new[]{"Z","A"}),"declared sequential order and one call per handler");
                    var expected=new List<string>{"before:1:7:3","handler:Z:1:8:3","after:1:8:3","finally:True:1:8:3","before:2:7:3","handler:A:2:8:3"};
                    expected.AddRange(failed?new[]{"exception:2:8:3","finally:False:2:8:3"}:new[]{"after:2:8:3","finally:True:2:8:3"});OpenAllFixture.Probe.Check(p.Trace.SequenceEqual(expected),"each entered branch completes its own unwind");
                    OpenAllFixture.Probe.Check(mode=="array"?result!.SequenceEqual(new[]{131,121})&&copied==-1:mode=="span"?copied==2:copied==-1,"result aggregation");
                    OpenAllFixture.Probe.Check(values.SequenceEqual(mode=="span"?new[]{131,121,5,-9}:new[]{3,4,5,-9}),"atomic Span commit and untouched tail");
                    OpenAllFixture.Probe.Check(p.Created.SequenceEqual(new[]{typeof(Hook<TRight,TLeft>),typeof(ZHandler<TRight,TLeft>),typeof(AHandler<TLeft,TRight>)}),"different structurally closed receiver keys");
                    OpenAllFixture.Probe.Check(p.Disposed.Count==0,"dispatch borrows services");scope.Dispose();OpenAllFixture.Probe.Check(p.Disposed.SequenceEqual(p.Created.AsEnumerable().Reverse()),"DI scope owns all receivers");
                    return JsonSerializer.Serialize(new{mode,result,copied,destination=values,p.Branch,p.Handlers,p.Trace,error=error==null?"none":ReferenceEquals(error,p.Failure)?"sentinel":error.GetType().Name,created=p.Created.Select(t=>t.ToString()),disposed=p.Disposed.Select(t=>t.ToString()),registrations=count});
                }
            } }
            """;
    }

    private static string OpenSendAllSyncNegative()
    {
        const string request = "OpenAll.Request<,>"; const string first = "OpenAll.ZHandler<,>"; const string second = "OpenAll.AHandler<,>"; const string hook = "OpenAll.Hook<,>";
        var declarations = new List<string>(); var registrations = new List<string>();
        void Reject(string name, string handlers, string stage, string extra = "", string r = request)
        {
            declarations.Add($"[ZendiatorComposition(\"{name}\")]public sealed class {name};");
            registrations.Add($"public static IServiceCollection {name}(IServiceCollection s)=>s.AddZendiator<{name}>(c=>{{c.Handlers(typeof({r}),DispatchFamily.SendAllSync,{handlers});c.Pipeline(typeof({r}),DispatchFamily.SendAllSync,{stage});{extra}}});");
        }
        var normal = $"typeof({first}),typeof({second})";
        Reject("Undetermined", $"typeof({first}),typeof(Unused<,,>)", $"Stage.Hook(typeof({hook}))");
        Reject("ConstraintConflict", $"typeof({first}),typeof(StructHandler<,>)", $"Stage.Hook(typeof({hook}))");
        Reject("PatternMismatch", $"typeof({first}),typeof(OtherHandler<,>)", $"Stage.Hook(typeof({hook}))");
        Reject("Wrapper", normal, $"Stage.Wrapper(typeof({hook}))");
        Reject("HookSession", normal, $"Stage.Hook(typeof({hook}),typeof(Factory<,>))");
        Reject("HandlerSession", $"typeof({first}),HandlerEntry.Session(typeof({second}),typeof(Factory<,>))", $"Stage.Hook(typeof({hook}))");
        Reject("Overlap", normal, $"Stage.Hook(typeof({hook}))", "c.Handlers<OpenAll.Request<OpenAllFixture.A,OpenAllFixture.B>>(DispatchFamily.SendAllSync,typeof(OpenAll.ZHandler<OpenAllFixture.B,OpenAllFixture.A>),typeof(OpenAll.AHandler<OpenAllFixture.A,OpenAllFixture.B>));c.Pipeline<OpenAll.Request<OpenAllFixture.A,OpenAllFixture.B>>(DispatchFamily.SendAllSync,Stage.Hook<OpenAll.Hook<OpenAllFixture.B,OpenAllFixture.A>>());");
        Reject("Multiple", normal, $"Stage.Hook(typeof({hook}))", "c.Handlers<AllSync.Response.Ping>(DispatchFamily.SendAllSync,typeof(AllSync.Response.H2),typeof(AllSync.Response.H1));c.Pipeline<AllSync.Response.Ping>(DispatchFamily.SendAllSync,Stage.Hook<AllSync.Response.Hook>());");
        Reject("Three", normal + ",typeof(Third<,>)", $"Stage.Hook(typeof({hook}))");
        declarations.Add("[ZendiatorComposition(\"Void\")]public sealed class Void;");
        registrations.Add("public static IServiceCollection Void(IServiceCollection s)=>s.AddZendiator<Void>(c=>{c.Handlers(typeof(VoidRequest<>),DispatchFamily.SendAllSync,typeof(VoidFirst<>),typeof(VoidSecond<>));c.Pipeline(typeof(VoidRequest<>),DispatchFamily.SendAllSync,Stage.Hook(typeof(VoidHook<>)));});");
        return OpenSendAllSyncSource() + "namespace OpenAllRejected {" + string.Join("\n", declarations) + "public static class Registration {" + string.Join("\n", registrations) + "}" + """
            [ZendiatorHandler]public sealed class Unused<X,Y,Z>:ISyncRequestHandler<OpenAll.Request<X,Y>,int>{public int Handle(OpenAll.Request<X,Y> r,CancellationToken t)=>0;}
            [ZendiatorHandler]public sealed class StructHandler<X,Y>:ISyncRequestHandler<OpenAll.Request<X,Y>,int> where X:struct{public int Handle(OpenAll.Request<X,Y> r,CancellationToken t)=>0;}
            public sealed record OtherRequest<X,Y>:ISyncMultiRequest<int>;
            [ZendiatorHandler]public sealed class OtherHandler<X,Y>:ISyncRequestHandler<OtherRequest<X,Y>,int>{public int Handle(OtherRequest<X,Y> r,CancellationToken t)=>0;}
            [ZendiatorHandler]public sealed class Third<X,Y>:ISyncRequestHandler<OpenAll.Request<X,Y>,int>{public int Handle(OpenAll.Request<X,Y> r,CancellationToken t)=>0;}
            public sealed record VoidRequest<X>:ISyncMultiRequest;
            [ZendiatorHandler]public sealed class VoidFirst<X>:ISyncRequestHandler<VoidRequest<X>>{public void Handle(VoidRequest<X> r,CancellationToken t){}}
            [ZendiatorHandler]public sealed class VoidSecond<X>:ISyncRequestHandler<VoidRequest<X>>{public void Handle(VoidRequest<X> r,CancellationToken t){}}
            public sealed class VoidHook<X>:ISendRequestTransform<VoidRequest<X>>,ISendSuccessObserver<VoidRequest<X>>,ISendExceptionObserver<VoidRequest<X>>,ISendFinallyObserver<VoidRequest<X>>{
                public VoidRequest<X> Before(VoidRequest<X> r,CancellationToken t)=>r;public void After(VoidRequest<X> r,CancellationToken t){}public void OnException(VoidRequest<X> r,Exception e,CancellationToken t){}public void Finally(VoidRequest<X> r,SendExitInfo e,CancellationToken t){}
            }
            public sealed class Factory<X,Y>;
            public sealed class NoDefault:OpenAllFixture.ISecond,OpenAllFixture.IHook{public NoDefault(int value){}}
            public static class Consumer{public static int[] Invalid(OpenAll.Generated.Composition.IZendiator m)=>m.SendAll(new OpenAll.Request<NoDefault,OpenAllFixture.B>(7,new[]{3,4,5,-9}));}
            }
            """;
    }

    private static string? OpenSendAllSyncFolder(string name)
    {
        var root = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        if (string.IsNullOrEmpty(root)) return null;
        var folder = Path.Combine(root, "open-sendall-sync-" + name); Directory.CreateDirectory(folder); return folder;
    }

    private static void AssertOpenSendAllSyncOutput(Compilation output, GeneratorDriver driver)
    {
        AssertOpenSendAsyncOutput(output, driver); AssertSendAllSyncContract(output);
        var catalog = Assert.IsType<CompositionSelectionCatalog>(Assert.Single(driver.GetRunResult().Results.Single().TrackedSteps["CompositionSelectionModel"].SelectMany(s => s.Outputs)).Value);
        var route = Assert.Single(Assert.Single(catalog.Compositions, c => c.Key == "openAll").Pipelines); Assert.Equal(BoundRequestShape.OpenAllSyncReference, route.RequestShape);
        Assert.Equal(2, route.Handlers.Count); Assert.Contains("ZHandler", route.Handlers[0].Name); Assert.Contains("AHandler", route.Handlers[1].Name);
        var model = CanonicalSendAllSyncEmissionNormalizer.Create(catalog, CanonicalExecutionGraphNormalizer.Create(catalog));
        var emission = Assert.Single(model.Compositions, c => c.Registration.Namespace == "OpenAll.Generated.Composition"); Assert.Equal(2, emission.Branches.Count);
        Assert.All(emission.Branches, b => { Assert.Equal(route.SendGeneric, b.Generic); Assert.Equal(route.SendOpenRegistration, b.OpenRegistration); });
        var plan = CanonicalRegistrationPlan.SendAllSync(emission); var registration = Assert.Single(plan.Routes); var open = Assert.IsType<BoundStreamOpenRegistration>(registration.Open);
        Assert.Equal(new[] { "global::OpenAll.ZHandler<,>", "global::OpenAll.AHandler<,>" }, open.HandlerDefinitionNames);
        Assert.Equal(new[] { "global::OpenAll.Hook<,>", "global::OpenAll.ZHandler<,>", "global::OpenAll.AHandler<,>" }, registration.ServiceTypes);
        var api = output.GetTypeByMetadataName("OpenAll.Generated.Composition.IZendiator")!; Assert.NotNull(api); Assert.False(api.IsGenericType);
        var mediator = output.GetTypeByMetadataName("OpenAll.Generated.Composition.Zendiator")!; Assert.NotNull(mediator); Assert.False(mediator.IsGenericType);
        var publicApi = api.GetMembers("SendAll").OfType<IMethodSymbol>().ToArray(); Assert.Equal(2, publicApi.Length);
        var publicImplementation = mediator.GetMembers("SendAll").OfType<IMethodSymbol>().ToArray(); Assert.Equal(2, publicImplementation.Length);
        var methods = publicApi.Concat(publicImplementation).Concat(publicImplementation.Select(m => CanonicalRouteEntry(output, m))).ToArray();
        Assert.All(methods, m => { Assert.Equal(2, m.Arity); Assert.All(m.TypeParameters, p => { Assert.True(p.HasReferenceTypeConstraint); Assert.True(p.HasConstructorConstraint); }); Assert.Contains(m.TypeParameters, p => p.ConstraintTypes.Any(t => t.Name == "ISecond")); Assert.Contains(m.TypeParameters, p => p.ConstraintTypes.Any(t => t.Name == "IHook")); });
        Assert.All(methods, method => Assert.Equal(CanonicalConstraintClauses(methods[0]), CanonicalConstraintClauses(method)));
    }

    [Fact]
    public void OpenSendAllSync_two_definitions_inferred_constraints_order_atomic_Span_and_failure_match_reference()
    {
        var source = OpenSendAllSyncSource(); var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "OpenSendAllSync_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        var folder = OpenSendAllSyncFolder("runtime"); SaveSendAllSync(folder, source, driver, output, diagnostics);
        Assert.Empty(diagnostics); Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)); AssertOpenSendAllSyncOutput(output, driver);
        using var bytes = new MemoryStream(); var emitted = output.Emit(bytes); Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics)); if (folder != null) File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray()); bytes.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes); var result = assembly.GetType("OpenAllFixture.Entry")!.GetMethod("Run")!.CreateDelegate<Func<string>>()(); if (folder != null) File.WriteAllText(Path.Combine(folder, "runtime.json"), result);
        using var json = JsonDocument.Parse(result); Assert.Equal(3, json.RootElement.GetProperty("pairs").GetInt32()); Assert.Equal(6, json.RootElement.GetProperty("dispatches").GetInt32());
    }

    [Fact]
    public void OpenSendAllSync_rejects_independent_unproved_branches_and_preserves_compiler_where_and_sentinels()
    {
        var source = OpenSendAllSyncNegative(); var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "OpenSendAllSyncNegative_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        var folder = OpenSendAllSyncFolder("negative"); SaveSendAllSync(folder, source, driver, output, diagnostics); AssertOpenSendAllSyncOutput(output, driver);
        AssertComposedProfile(output, "OpenAllRejected.Generated.Multiple", ("SendAll", 4));
        foreach (var name in new[] { "Undetermined", "ConstraintConflict", "PatternMismatch", "Wrapper", "HookSession", "HandlerSession", "Overlap", "Three" })
        {
            var ns = "OpenAllRejected.Generated." + name; Assert.Null(output.GetTypeByMetadataName(ns + ".IZendiator")); Assert.Null(output.GetTypeByMetadataName(ns + ".ZendiatorGeneratedRegistrar"));
            var start = source.IndexOf("public static IServiceCollection " + name + "(", StringComparison.Ordinal); var end = source.IndexOf("});", start, StringComparison.Ordinal) + 3;
            Assert.Contains(diagnostics, d => d.Location.IsInSource && d.Location.SourceSpan.Start >= start && d.Location.SourceSpan.End <= end && d.Id == (name == "Overlap" ? "ZEN0028" : "ZEN0035"));
            foreach (var interceptor in driver.GetRunResult().Results.Single().GeneratedSources.Where(f => f.SourceText.ToString().Contains("InterceptsLocation", StringComparison.Ordinal))) Assert.DoesNotContain("typeof(global::OpenAllRejected." + name + ")", interceptor.SourceText.ToString(), StringComparison.Ordinal);
        }
        var acceptedVoid = output.GetTypeByMetadataName("OpenAllRejected.Generated.Void.IZendiator"); Assert.NotNull(acceptedVoid);
        var voidMethod = Assert.Single(acceptedVoid.GetMembers("SendAll").OfType<IMethodSymbol>()); Assert.True(voidMethod.ReturnsVoid); Assert.Equal(1, voidMethod.Arity);
        Assert.NotNull(output.GetTypeByMetadataName("OpenAllRejected.Generated.Void.ZendiatorGeneratedRegistrar"));
        foreach (var name in new[] { "Undetermined", "ConstraintConflict", "PatternMismatch", "HandlerSession" })
        {
            var start = source.IndexOf("public static IServiceCollection " + name + "(", StringComparison.Ordinal); var end = source.IndexOf("});", start, StringComparison.Ordinal) + 3;
            var entry = Assert.Single(diagnostics, d => d.Id == "ZEN0035" && d.Location.SourceSpan.Start >= start && d.Location.SourceSpan.End <= end && (source.Substring(d.Location.SourceSpan.Start, d.Location.SourceSpan.Length).StartsWith("typeof(", StringComparison.Ordinal) || source.Substring(d.Location.SourceSpan.Start, d.Location.SourceSpan.Length).StartsWith("HandlerEntry.Session", StringComparison.Ordinal)));
            Assert.Empty(entry.AdditionalLocations);
        }
        var overlap = Assert.Single(diagnostics, d => d.Id == "ZEN0028"); Assert.Contains("Pipeline<OpenAll.Request<", source.Substring(overlap.Location.SourceSpan.Start, overlap.Location.SourceSpan.Length));
        var additional = Assert.Single(overlap.AdditionalLocations); Assert.True(additional.IsInSource); Assert.Contains("Pipeline(typeof(OpenAll.Request<,>)", source.Substring(additional.SourceSpan.Start, additional.SourceSpan.Length)); Assert.True(additional.SourceSpan.Start < overlap.Location.SourceSpan.Start);
        var errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray(); Assert.NotEmpty(errors); Assert.All(errors, d => { Assert.Equal("CS0310", d.Id); Assert.Contains("SendAll", source.Substring(d.Location.SourceSpan.Start, d.Location.SourceSpan.Length)); Assert.Contains("NoDefault", d.GetMessage()); });
        if (folder != null) File.WriteAllText(Path.Combine(folder, "negative-result.json"), JsonSerializer.Serialize(new { logicalInputs = 1, drivers = 1, dispatches = 0, validGeneratedCompositions = 13, newlyComposedProfiles = new[] { "Multiple" }, rejectedCompositions = 8, compiler = errors.Select(d => new { d.Id, message = d.GetMessage(), span = d.Location.SourceSpan.ToString() }), generator = diagnostics.Select(d => new { d.Id, message = d.GetMessage(), primary = d.Location.SourceSpan.ToString(), additional = d.AdditionalLocations.Select(l => l.SourceSpan.ToString()) }) }));
    }
}
