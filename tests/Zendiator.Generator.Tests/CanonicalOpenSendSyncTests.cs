using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;
using Zendiator.SourceGenerator;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private static string OpenSendSyncModule(bool isVoid)
    {
        var ns = isVoid ? "OpenVoid" : "OpenResponse";
        var args = isVoid ? "TLeft" : "TLeft,TRight";
        var reversed = isVoid ? "TLeft" : "TRight,TLeft";
        var request = "Request<" + args + ">";
        var receiverRequest = "Request<" + reversed + ">";
        var result = isVoid ? "void" : "int";
        return $$"""
            namespace {{ns}} {
            public sealed record Request<{{args}}>(int Value):ISyncRequest{{(isVoid ? "" : "<int>")}};
            // The handler and hook use reversed request arguments. Names alone cannot close either service.
            [ZendiatorHandler]public sealed class Handler<{{args}}>:ISyncRequestHandler<{{receiverRequest}}{{(isVoid ? "" : ",int")}}>,IDisposable
                where TLeft:class,new() {{(isVoid ? "" : "where TRight:class")}} {
                private readonly OpenFixture.Probe p;public Handler(OpenFixture.Probe p){this.p=p;p.Created.Add(GetType());p.Trace.Add("resolve:H");}
                public {{result}} Handle({{receiverRequest}} r,CancellationToken t){p.Record("handler",r,r.Value,t);p.Calls++;{{(isVoid ? "" : "return r.Value*2;")}}}
                public void Dispose(){p.Disposed.Add(GetType());}
            }
            public sealed class Hook<{{args}}>:ISendRequestTransform<{{receiverRequest}}>,{{(isVoid ? "ISendSuccessObserver<" + receiverRequest + ">" : "ISendResponseTransform<" + receiverRequest + ",int>")}},ISendExceptionObserver<{{receiverRequest}}>,ISendFinallyObserver<{{receiverRequest}}>,IDisposable
                where TLeft:class,new() {{(isVoid ? "" : "where TRight:class,new()")}} {
                private readonly OpenFixture.Probe p;public Hook(OpenFixture.Probe p){this.p=p;p.Created.Add(GetType());p.Trace.Add("resolve:F");}
                public {{receiverRequest}} Before({{receiverRequest}} r,CancellationToken t){p.Record("before",r,r.Value,t);var changed=new {{receiverRequest}}(r.Value+1);p.Saved=changed;return changed;}
                public {{result}} After({{receiverRequest}} r,{{(isVoid ? "" : "int response,")}}CancellationToken t){p.Record("after",r,r.Value,t);{{(isVoid ? "" : "return response+3;")}}}
                public void OnException({{receiverRequest}} r,Exception e,CancellationToken t){p.Record("exception",r,r.Value,t);}
                public void Finally({{receiverRequest}} r,SendExitInfo exit,CancellationToken t){OpenFixture.Probe.Check(exit.Success&&exit.Error==null,"exit");p.Record("finally",r,r.Value,t);}
                public void Dispose(){p.Disposed.Add(GetType());}
            }
            [ZendiatorComposition("{{ns}}")]public sealed class Composition;
            public static class Bridge {
                public static IServiceCollection Register(IServiceCollection s)=>s.AddZendiator<Composition>(c=>{
                    c.Handlers(typeof(Request<{{(isVoid ? "" : ",")}}>),DispatchFamily.SendSync,typeof(Handler<{{(isVoid ? "" : ",")}}>));
                    c.Pipeline(typeof(Request<{{(isVoid ? "" : ",")}}>),DispatchFamily.SendSync,Stage.Hook(typeof(Hook<{{(isVoid ? "" : ",")}}>)));
                });
                // Concrete handwritten protocol; no graph or emitted schedule supplies its expected behavior.
                private static {{result}} Reference<{{args}}>(IServiceProvider sp,{{request}} r,CancellationToken t)
                    where TLeft:class,new() {{(isVoid ? "" : "where TRight:class,new()")}} {
                    ArgumentNullException.ThrowIfNull(r);t.ThrowIfCancellationRequested();var f=sp.GetRequiredService<Hook<{{reversed}}>>();var saved=r;var success=false;Exception? error=null;
                    try{saved=f.Before(saved,t);ArgumentNullException.ThrowIfNull(saved);t.ThrowIfCancellationRequested();var h=sp.GetRequiredService<Handler<{{reversed}}>>();
                        {{(isVoid ? "h.Handle(saved,t);f.After(saved,t);success=true;" : "var value=h.Handle(saved,t);value=f.After(saved,value,t);success=true;return value;")}}
                    }catch(Exception e){error=e;try{f.OnException(saved,e,t);}catch(Exception replacement){error=replacement;throw;}throw;}
                    finally{f.Finally(saved,new(success,error),t);}
                }
                public static string Observe<{{args}}>(bool generated) where TLeft:class,new() {{(isVoid ? "" : "where TRight:class,new()")}} {
                    using var token=new CancellationTokenSource();var p=new OpenFixture.Probe{Token=token.Token};IServiceCollection s=new ServiceCollection();s.AddSingleton(p);
                    s.AddScoped(typeof(Handler<{{(isVoid ? "" : ",")}}>));var existing=s.Last();Register(s);var count=s.Count;Register(s);
                    OpenFixture.Probe.Check(s.Count==count&&s.Contains(existing),"TryAdd and repeat registration");
                    foreach(var definition in new[]{typeof(Handler<{{(isVoid ? "" : ",")}}>),typeof(Hook<{{(isVoid ? "" : ",")}}>)} )
                        OpenFixture.Probe.Check(s.Count(d=>d.ServiceType==definition&&d.ImplementationType==definition&&d.Lifetime==ServiceLifetime.Scoped)==1,"open scoped definition");
                    using var provider=s.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});using var scope=provider.CreateScope();var sp=scope.ServiceProvider;
                    var m=sp.GetRequiredService<{{ns}}.Generated.Composition.IZendiator>();var r=new {{request}}(7);p.Original=r;int? value=null;
                    if(generated){ {{(isVoid ? "m.Send(r,p.Token);" : "value=m.Send(r,p.Token);")}} }else{ {{(isVoid ? "Reference(sp,r,p.Token);" : "value=Reference(sp,r,p.Token);")}} }
                    OpenFixture.Probe.Check(p.Calls==1&&p.Trace.SequenceEqual(new[]{"resolve:F","before:7","resolve:H","handler:8","after:8","finally:8"}),"callback order and single handler");
                    OpenFixture.Probe.Check(p.Created.SequenceEqual(new[]{typeof(Hook<{{reversed}}>),typeof(Handler<{{reversed}}>)}),"symbol-bound closed DI keys");
                    OpenFixture.Probe.Check(p.Disposed.Count==0,"borrowed services live through dispatch");scope.Dispose();OpenFixture.Probe.Check(p.Disposed.SequenceEqual(p.Created.AsEnumerable().Reverse()),"DI scope owns receivers");
                    {{(isVoid ? "" : "OpenFixture.Probe.Check(value==19,\"response transform\");")}}
                    return JsonSerializer.Serialize(new{profile="{{ns}}",request=typeof({{request}}).ToString(),value,p.Calls,p.Trace,created=p.Created.Select(t=>t.ToString()),disposed=p.Disposed.Select(t=>t.ToString()),registrations=count});
                }
            } }
            """;
    }

    private const string OpenSendSyncSentinels = """
        namespace OpenSentinel {
        public sealed record Closed(int Value):ISyncRequest<int>;
        [ZendiatorHandler]public sealed class ClosedHandler:ISyncRequestHandler<Closed,int>{public int Handle(Closed r,CancellationToken t)=>r.Value;}
        [ZendiatorComposition("closed")]public sealed class ClosedComposition;
        public readonly ref struct Scoped:ISyncRequest<int>;
        [ZendiatorHandler]public sealed class ScopedHandler:ISyncRequestHandler<Scoped,int>{public int Handle(scoped Scoped r,CancellationToken t)=>1;}
        public sealed class Observer:IScopedSendBeforeObserver<Scoped>,IScopedSendResponseObserver<Scoped,int>,IScopedSendExceptionObserver<Scoped>,IScopedSendFinallyObserver<Scoped>{
            public void Before(scoped Scoped r,CancellationToken t){}public void After(scoped Scoped r,int value,CancellationToken t){}
            public void OnException(scoped Scoped r,Exception e,CancellationToken t){}public void Finally(scoped Scoped r,SendExitInfo exit,CancellationToken t){}
        }
        [ZendiatorComposition("scoped")]public sealed class ScopedComposition;
        public static class Registration {
            public static IServiceCollection A(IServiceCollection s)=>s.AddZendiator<ClosedComposition>(c=>{c.Handlers<Closed>(DispatchFamily.SendSync,typeof(ClosedHandler));c.Pipeline<Closed>(DispatchFamily.SendSync);});
            public static IServiceCollection B(IServiceCollection s)=>s.AddZendiator<ScopedComposition>(c=>{c.Handlers(typeof(Scoped),DispatchFamily.SendSync,typeof(ScopedHandler));c.Pipeline(typeof(Scoped),DispatchFamily.SendSync,Stage.Hook<Observer>());});
        } }
        """;

    private static string OpenSendSyncSource() => """
        using System;using System.Linq;using System.Collections.Generic;using System.Threading;using System.Text.Json;
        using Microsoft.Extensions.DependencyInjection;using Zendiator;using Zendiator.DependencyInjection;
        namespace OpenFixture {
        public sealed class A;public sealed class B;
        public sealed class Probe {
            public CancellationToken Token;public object? Original,Saved;public int Calls;public readonly List<string> Trace=new();public readonly List<Type> Created=new(),Disposed=new();
            public static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
            public void Record(string phase,object request,int value,CancellationToken token){Check(token==Token&&ReferenceEquals(request,phase=="before"?Original:Saved),"token and request locality");Trace.Add(phase+":"+value);}
        }
        public static class Entry {
            public static string Run(){var rows=new[]{(OpenResponse.Bridge.Observe<A,B>(true),OpenResponse.Bridge.Observe<A,B>(false)),(OpenResponse.Bridge.Observe<B,A>(true),OpenResponse.Bridge.Observe<B,A>(false)),(OpenVoid.Bridge.Observe<A>(true),OpenVoid.Bridge.Observe<A>(false))};
                foreach(var row in rows)Probe.Check(row.Item1==row.Item2,"generated/reference mismatch\n"+row.Item1+"\n"+row.Item2);
                return JsonSerializer.Serialize(new{pairs=3,dispatches=6,rows=rows.Select(r=>new{generated=JsonDocument.Parse(r.Item1).RootElement.Clone(),reference=JsonDocument.Parse(r.Item2).RootElement.Clone()})});}
        } }
        """ + OpenSendSyncModule(false) + OpenSendSyncModule(true) + OpenSendSyncSentinels;

    private static string OpenSendSyncNegative()
    {
        const string type = "OpenResponse.Request<,>";
        const string handler = "OpenResponse.Handler<,>";
        const string hook = "OpenResponse.Hook<,>";
        var declarations = new List<string>(); var registrations = new List<string>();
        void Reject(string name, string h, string stage, string family = "SendSync", string extra = "")
        {
            declarations.Add($"[ZendiatorComposition(\"{name}\")]public sealed class {name};");
            registrations.Add($"public static IServiceCollection {name}(IServiceCollection s)=>s.AddZendiator<{name}>(c=>{{c.Handlers(typeof({type}),DispatchFamily.{family},typeof({h}));c.Pipeline(typeof({type}),DispatchFamily.{family},{stage});{extra}}});");
        }
        Reject("UndeterminedHook", handler, "Stage.Hook(typeof(UnusedHook<,,>))");
        Reject("UndeterminedHandler", "UnusedHandler<,,>", $"Stage.Hook(typeof({hook}))");
        Reject("ConstraintConflict", handler, "Stage.Hook(typeof(StructHook<,>))");
        Reject("ResponseMismatch", handler, "Stage.Hook(typeof(WrongResponse<,>))");
        Reject("WrongAsync", handler, $"Stage.Hook(typeof({hook}))", "SendAsync");
        Reject("WrongAll", handler, $"Stage.Hook(typeof({hook}))", "SendAllSync");
        Reject("Wrapper", handler, $"Stage.Wrapper(typeof({hook}))");
        Reject("Session", handler, $"Stage.Hook(typeof({hook}),typeof(Factory<,>))");
        Reject("Overlap", handler, $"Stage.Hook(typeof({hook}))", extra: "c.Handlers<OpenResponse.Request<OpenFixture.A,OpenFixture.B>>(DispatchFamily.SendSync,typeof(OpenResponse.Handler<OpenFixture.B,OpenFixture.A>));c.Pipeline<OpenResponse.Request<OpenFixture.A,OpenFixture.B>>(DispatchFamily.SendSync,Stage.Hook<OpenResponse.Hook<OpenFixture.B,OpenFixture.A>>());");
        Reject("Multiple", handler, $"Stage.Hook(typeof({hook}))", extra: "c.Handlers<OpenSentinel.Closed>(DispatchFamily.SendSync,typeof(OpenSentinel.ClosedHandler));c.Pipeline<OpenSentinel.Closed>(DispatchFamily.SendSync);");
        return OpenSendSyncSource() + "namespace OpenRejected {" + string.Join("\n", declarations) + "public static class Register {" + string.Join("\n", registrations) + "}" + """
            public sealed class UnusedHook<X,Y,Z>:ISendRequestTransform<OpenResponse.Request<Y,X>>{public OpenResponse.Request<Y,X> Before(OpenResponse.Request<Y,X> r,CancellationToken t)=>r;}
            [ZendiatorHandler]public sealed class UnusedHandler<X,Y,Z>:ISyncRequestHandler<OpenResponse.Request<Y,X>,int>{public int Handle(OpenResponse.Request<Y,X> r,CancellationToken t)=>0;}
            public sealed class StructHook<X,Y>:ISendRequestTransform<OpenResponse.Request<Y,X>> where X:struct{public OpenResponse.Request<Y,X> Before(OpenResponse.Request<Y,X> r,CancellationToken t)=>r;}
            public sealed class WrongResponse<X,Y>:ISendResponseTransform<OpenResponse.Request<Y,X>,string>{public string After(OpenResponse.Request<Y,X> r,string value,CancellationToken t)=>value;}
            public sealed class Factory<X,Y>;
            public sealed class NoDefault{public NoDefault(int value){}}
            public static class Consumer{public static int Invalid(OpenResponse.Generated.Composition.IZendiator m)=>m.Send(new OpenResponse.Request<NoDefault,OpenFixture.B>(1));}
            }
            """;
    }

    private static string? OpenSendSyncFolder(string name)
    {
        var root = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        if (string.IsNullOrEmpty(root)) return null;
        var folder = Path.Combine(root, "open-send-sync-" + name); Directory.CreateDirectory(folder); return folder;
    }

    private static void AssertOpenSendSyncOutput(Compilation output, GeneratorDriver driver)
    {
        var catalog = Assert.IsType<CompositionSelectionCatalog>(Assert.Single(driver.GetRunResult().Results.Single().TrackedSteps["CompositionSelectionModel"].SelectMany(s => s.Outputs)).Value);
        foreach (var ns in new[] { "OpenResponse", "OpenVoid" })
        {
            var route = Assert.Single(Assert.Single(catalog.Compositions, c => c.Key == ns).Pipelines);
            Assert.Equal(BoundRequestShape.OpenSyncReference, route.RequestShape); Assert.NotNull(route.SendGeneric); Assert.NotNull(route.SendOpenRegistration); Assert.Null(route.StreamGeneric); Assert.Null(route.StreamOpenRegistration);
            var api = output.GetTypeByMetadataName(ns + ".Generated.Composition.IZendiator"); Assert.NotNull(api); Assert.False(api.IsGenericType);
            var mediator = output.GetTypeByMetadataName(ns + ".Generated.Composition.Zendiator")!; Assert.False(mediator.IsGenericType);
            var methods = new[] { Assert.Single(api.GetMembers("Send").OfType<IMethodSymbol>()), Assert.Single(mediator.GetMembers("Send").OfType<IMethodSymbol>()), Assert.Single(mediator.GetMembers().OfType<IMethodSymbol>(), m => m.DeclaredAccessibility == Accessibility.Private && m.Name.StartsWith("Execute", StringComparison.Ordinal)) };
            Assert.All(methods, m => { Assert.Equal(ns == "OpenVoid" ? 1 : 2, m.Arity); Assert.All(m.TypeParameters, p => { Assert.True(p.HasReferenceTypeConstraint); Assert.True(p.HasConstructorConstraint); }); Assert.Equal(ns == "OpenVoid", m.ReturnsVoid); });
            var declarations = methods.Select(m => (MethodDeclarationSyntax)m.DeclaringSyntaxReferences.Single().GetSyntax()).ToArray();
            Assert.All(declarations, d => Assert.Equal(declarations[0].ConstraintClauses.ToFullString().Trim(), d.ConstraintClauses.ToFullString().Trim()));
            var call = Assert.Single(declarations[1].DescendantNodes().OfType<InvocationExpressionSyntax>(), i => i.Expression is GenericNameSyntax g && g.Identifier.ValueText == methods[2].Name);
            var bound = Assert.IsAssignableFrom<IMethodSymbol>(output.GetSemanticModel(call.SyntaxTree).GetSymbolInfo(call).Symbol);
            Assert.True(SymbolEqualityComparer.Default.Equals(bound.OriginalDefinition, methods[2]));
            Assert.Equal(methods[1].TypeParameters.Cast<ITypeSymbol>(), bound.TypeArguments, SymbolEqualityComparer.Default);
        }
        foreach (var name in new[] { "ClosedComposition", "ScopedComposition" }) Assert.NotNull(output.GetTypeByMetadataName("OpenSentinel.Generated." + name + ".IZendiator"));
        var sentinels = catalog.Compositions.Where(c => c.Key is "closed" or "scoped").SelectMany(c => c.Pipelines).ToArray(); Assert.Equal(2, sentinels.Length);
        Assert.All(sentinels, r => { Assert.Null(r.SendGeneric); Assert.Null(r.SendOpenRegistration); }); Assert.Contains(sentinels, r => r.RequestShape == BoundRequestShape.ClosedRefLike);
    }

    [Fact]
    public void OpenSendSync_inferred_arguments_constraints_and_closed_DI_keys_match_reference()
    {
        var source = OpenSendSyncSource(); var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "OpenSendSync_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        var folder = OpenSendSyncFolder("runtime"); SaveSendAllSync(folder, source, driver, output, diagnostics);
        Assert.Empty(diagnostics); Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)); AssertOpenSendSyncOutput(output, driver);
        using var bytes = new MemoryStream(); var emitted = output.Emit(bytes); Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics)); if (folder != null) File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray()); bytes.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes); var result = assembly.GetType("OpenFixture.Entry")!.GetMethod("Run")!.CreateDelegate<Func<string>>()(); if (folder != null) File.WriteAllText(Path.Combine(folder, "runtime.json"), result);
        using var json = JsonDocument.Parse(result); Assert.Equal(6, json.RootElement.GetProperty("dispatches").GetInt32());
    }

    [Fact]
    public void OpenSendSync_rejects_unproved_compositions_and_preserves_compiler_owned_where_and_unrelated_routes()
    {
        var source = OpenSendSyncNegative(); var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "OpenSendSyncNegative_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        var folder = OpenSendSyncFolder("negative"); SaveSendAllSync(folder, source, driver, output, diagnostics); AssertOpenSendSyncOutput(output, driver);
        foreach (var name in new[] { "UndeterminedHook", "UndeterminedHandler", "ConstraintConflict", "ResponseMismatch", "WrongAsync", "WrongAll", "Wrapper", "Session", "Overlap", "Multiple" })
        { Assert.Null(output.GetTypeByMetadataName("OpenRejected.Generated." + name + ".IZendiator")); Assert.Null(output.GetTypeByMetadataName("OpenRejected.Generated." + name + ".ZendiatorGeneratedRegistrar")); }
        var overlap = Assert.Single(diagnostics, d => d.Id == "ZEN0028"); Assert.True(overlap.Location.IsInSource);
        Assert.Contains("Pipeline<OpenResponse.Request<", source.Substring(overlap.Location.SourceSpan.Start, overlap.Location.SourceSpan.Length));
        var additional = Assert.Single(overlap.AdditionalLocations); Assert.True(additional.IsInSource); Assert.Contains("Pipeline(typeof(OpenResponse.Request<,>)", source.Substring(additional.SourceSpan.Start, additional.SourceSpan.Length)); Assert.True(additional.SourceSpan.Start < overlap.Location.SourceSpan.Start);
        var errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray(); Assert.NotEmpty(errors); Assert.All(errors, d => { Assert.Equal("CS0310", d.Id); Assert.Contains("Send", source.Substring(d.Location.SourceSpan.Start, d.Location.SourceSpan.Length)); Assert.Contains("NoDefault", d.GetMessage()); });
        Assert.Contains(diagnostics, d => d.Id == "ZEN0035"); Assert.Contains(diagnostics, d => d.Id == "ZEN0026"); Assert.Contains(diagnostics, d => d.Id == "ZEN0037");
        if (folder != null) File.WriteAllText(Path.Combine(folder, "negative-result.json"), JsonSerializer.Serialize(new { logicalInputs = 1, drivers = 1, dispatches = 0, unrelatedGeneratedCompositions = 4, rejectedCompositions = 10, compiler = errors.Select(d => new { d.Id, message = d.GetMessage(), span = d.Location.SourceSpan.ToString() }), generator = diagnostics.Select(d => new { d.Id, message = d.GetMessage(), primary = d.Location.SourceSpan.ToString(), additional = d.AdditionalLocations.Select(l => l.SourceSpan.ToString()) }) }));
    }
}
