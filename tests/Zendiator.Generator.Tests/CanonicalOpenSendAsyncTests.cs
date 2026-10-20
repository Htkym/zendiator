using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;
using Zendiator.SourceGenerator;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private static string OpenSendAsyncModule(bool isVoid)
    {
        var ns = isVoid ? "OpenAsyncVoid" : "OpenAsyncResponse";
        var args = isVoid ? "TLeft" : "TLeft,TRight";
        var reversed = isVoid ? "TLeft" : "TRight,TLeft";
        var request = "Request<" + args + ">";
        var receiverRequest = "Request<" + reversed + ">";
        var result = isVoid ? "ValueTask" : "ValueTask<int>";
        var constraints = "where TLeft:class,new() " + (isVoid ? "" : "where TRight:class,new()");
        return $$"""
            namespace {{ns}} {
            public sealed record Request<{{args}}>(int Value):IRequest{{(isVoid ? "" : "<int>")}};
            [ZendiatorHandler]public sealed class Handler<{{args}}>:IRequestHandler<{{receiverRequest}}{{(isVoid ? "" : ",int")}}>,IDisposable
                where TLeft:class,new() {{(isVoid ? "" : "where TRight:class")}} {
                private readonly OpenAsyncFixture.Probe p;public Handler(OpenAsyncFixture.Probe p){this.p=p;p.Created.Add(GetType());p.Trace.Add("resolve:H");}
                public {{result}} HandleAsync({{receiverRequest}} r,CancellationToken t){p.Hit("handler",r,r.Value,t);p.Calls++;return p.{{(isVoid ? "Done(\"handler\")" : "Value(\"handler\",r.Value*2)")}};}
                public void Dispose(){p.Disposed.Add(GetType());}
            }
            public class Hook<{{args}}>:IAsyncSendRequestTransform<{{receiverRequest}}>,{{(isVoid ? "IAsyncSendSuccessObserver<" + receiverRequest + ">" : "IAsyncSendResponseTransform<" + receiverRequest + ",int>")}},IAsyncSendExceptionObserver<{{receiverRequest}}>,IAsyncSendFinallyObserver<{{receiverRequest}}>,IDisposable
                {{constraints}} {
                private readonly OpenAsyncFixture.Probe p;public Hook(OpenAsyncFixture.Probe p){this.p=p;p.Created.Add(GetType());p.Trace.Add("resolve:F");}
                public ValueTask<{{receiverRequest}}> BeforeAsync({{receiverRequest}} r,CancellationToken t){p.Hit("before",r,r.Value,t);var saved=new {{receiverRequest}}(r.Value+1);p.Saved=saved;return p.Value("before",saved);}
                public {{result}} AfterAsync({{receiverRequest}} r,{{(isVoid ? "" : "int value,")}}CancellationToken t){p.Hit("after",r,r.Value,t);return p.{{(isVoid ? "Done(\"after\")" : "Value(\"after\",value+3)")}};}
                public ValueTask OnExceptionAsync({{receiverRequest}} r,Exception e,CancellationToken t){OpenAsyncFixture.Probe.Check(ReferenceEquals(e,p.Failure),"exception identity");p.Hit("exception",r,r.Value,t);return p.Done("exception");}
                public ValueTask FinallyAsync({{receiverRequest}} r,SendExitInfo exit,CancellationToken t){OpenAsyncFixture.Probe.Check(exit.Success==(p.Mode!="oce")&&ReferenceEquals(exit.Error,p.Failure),"exit identity");p.Hit("finally",r,r.Value,t);return p.Done("finally");}
                public void Dispose(){p.Disposed.Add(GetType());}
            }
            [ZendiatorComposition("{{ns}}")]public sealed class Composition;
            public static class Bridge {
                public static IServiceCollection Register(IServiceCollection s)=>s.AddZendiator<Composition>(c=>{
                    c.Handlers(typeof(Request<{{(isVoid ? "" : ",")}}>),DispatchFamily.SendAsync,typeof(Handler<{{(isVoid ? "" : ",")}}>));
                    c.Pipeline(typeof(Request<{{(isVoid ? "" : ",")}}>),DispatchFamily.SendAsync,Stage.Hook(typeof(Hook<{{(isVoid ? "" : ",")}}>)));
                });
                private static {{result}} Reference<{{args}}>(IServiceProvider sp,{{request}} r,CancellationToken t) {{constraints}} {
                    ArgumentNullException.ThrowIfNull(r);return Execute<{{args}}>(sp,r,t);
                }
                // Independent handwritten protocol. Its async method boundary preserves caller ExecutionContext.
                private static async {{result}} Execute<{{args}}>(IServiceProvider sp,{{request}} r,CancellationToken t) {{constraints}} {
                    t.ThrowIfCancellationRequested();var f=sp.GetRequiredService<Hook<{{reversed}}>>();var saved=r;var success=false;Exception? error=null;
                    try{saved=await f.BeforeAsync(saved,t).ConfigureAwait(false);ArgumentNullException.ThrowIfNull(saved);t.ThrowIfCancellationRequested();var h=sp.GetRequiredService<Handler<{{reversed}}>>();
                        {{(isVoid ? "await h.HandleAsync(saved,t).ConfigureAwait(false);await f.AfterAsync(saved,t).ConfigureAwait(false);success=true;" : "var value=await h.HandleAsync(saved,t).ConfigureAwait(false);value=await f.AfterAsync(saved,value,t).ConfigureAwait(false);success=true;return value;")}}
                    }catch(Exception e){error=e;try{await f.OnExceptionAsync(saved,e,t).ConfigureAwait(false);}catch(Exception replacement){error=replacement;throw;}throw;}
                    finally{await f.FinallyAsync(saved,new(success,error),t).ConfigureAwait(false);}
                }
                public static async Task<string> Observe<{{args}}>(bool generated,string mode) {{constraints}} {
                    using var token=new CancellationTokenSource();var p=new OpenAsyncFixture.Probe{Cancellation=token,Mode=mode};IServiceCollection s=new ServiceCollection();s.AddSingleton(p);
                    s.AddScoped(typeof(Handler<{{(isVoid ? "" : ",")}}>));var existing=s.Last();Register(s);var count=s.Count;Register(s);
                    OpenAsyncFixture.Probe.Check(s.Count==count&&s.Contains(existing),"TryAdd and repeated registration");
                    foreach(var definition in new[]{typeof(Handler<{{(isVoid ? "" : ",")}}>),typeof(Hook<{{(isVoid ? "" : ",")}}>)} )
                        OpenAsyncFixture.Probe.Check(s.Count(d=>d.ServiceType==definition&&d.ImplementationType==definition&&d.Lifetime==ServiceLifetime.Scoped)==1,"open scoped definitions");
                    using var provider=s.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});using var scope=provider.CreateScope();var sp=scope.ServiceProvider;
                    var m=sp.GetRequiredService<{{ns}}.Generated.Composition.IZendiator>();var r=new {{request}}(7);p.Original=r;OpenAsyncFixture.Probe.Ambient.Value="caller";
                    var dispatch=generated?m.SendAsync(r,token.Token):Reference(sp,r,token.Token);
                    var sync=dispatch.IsCompletedSuccessfully;OpenAsyncFixture.Probe.Check(OpenAsyncFixture.Probe.Ambient.Value=="caller","public async boundary");
                    var task=dispatch.AsTask();
                    if(mode=="pending"){
                        OpenAsyncFixture.Probe.Check(!sync&&!task.IsCompleted&&p.Calls==0&&p.Pending!=null&&p.Pending.Consumptions==0&&p.Created.Count==1,"actual Before suspension");p.Pending!.Complete();
                    }else if(mode=="sync")OpenAsyncFixture.Probe.Check(sync,"synchronously completed void ValueTask");
                    int? value=null;OperationCanceledException? caught=null;
                    try{ {{(isVoid ? "await task.WaitAsync(TimeSpan.FromSeconds(5));" : "value=await task.WaitAsync(TimeSpan.FromSeconds(5));")}} }catch(OperationCanceledException e){caught=e;}
                    OpenAsyncFixture.Probe.Check(OpenAsyncFixture.Probe.Ambient.Value=="caller","caller context after await");
                    OpenAsyncFixture.Probe.Check(mode=="oce"?task.IsCanceled&&caught?.CancellationToken==token.Token:task.IsCompletedSuccessfully&&caught==null,"task status and OCE token");
                    OpenAsyncFixture.Probe.Check(p.Calls==1&&p.Trace.SequenceEqual(new[]{"resolve:F","before:7","resolve:H","handler:8",(mode=="oce"?"exception:8":"after:8"),"finally:8"}),"entered callback order");
                    OpenAsyncFixture.Probe.Check(p.AmbientSeen.SequenceEqual(new[]{"caller","before","handler",mode=="oce"?"exception":"after"}),"callback AsyncLocal");
                    OpenAsyncFixture.Probe.Check(p.Gates.Count==4&&p.Gates.All(g=>g.Gate.Consumptions==1),"single GetResult per callback");
                    OpenAsyncFixture.Probe.Check(p.Created.SequenceEqual(new[]{typeof(Hook<{{reversed}}>),typeof(Handler<{{reversed}}>)}),"symbol-bound reversed closed DI keys");
                    OpenAsyncFixture.Probe.Check(p.Disposed.Count==0,"borrowed receiver lifetime");scope.Dispose();OpenAsyncFixture.Probe.Check(p.Disposed.SequenceEqual(p.Created.AsEnumerable().Reverse()),"DI scope owns receivers");
                    {{(isVoid ? "" : "OpenAsyncFixture.Probe.Check(mode==\"oce\"?value==null:value==19,\"response transform\");")}}
                    return JsonSerializer.Serialize(new{profile="{{ns}}",mode,request=typeof({{request}}).ToString(),value,sync,status=task.Status.ToString(),p.Calls,p.Trace,p.AmbientSeen,gates=p.Gates.Select(g=>new{g.Phase,g.Gate.Consumptions}),created=p.Created.Select(t=>t.ToString()),disposed=p.Disposed.Select(t=>t.ToString()),registrations=count,oceToken=caught?.CancellationToken==token.Token});
                }
            } }
            """;
    }

    private static string OpenSendAsyncSource() => "using System.Threading.Tasks;using Zendiator.Generator.Tests;\n" + OpenSendSyncSource() + """
        namespace OpenAsyncFixture {
        public sealed class Probe {
            public static readonly AsyncLocal<string?> Ambient=new();
            public CancellationTokenSource Cancellation=null!;public string Mode="";public object? Original,Saved;public int Calls;
            public Exception? Failure;public IAsyncOnceGate? Pending;
            public readonly List<string> Trace=new();public readonly List<string?> AmbientSeen=new();public readonly List<Type> Created=new(),Disposed=new();
            public readonly List<(string Phase,IAsyncOnceGate Gate)> Gates=new();
            public static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
            public void Hit(string phase,object request,int value,CancellationToken token){Check(token==Cancellation.Token&&ReferenceEquals(request,phase=="before"?Original:Saved),"token and saved request");Trace.Add(phase+":"+value);AmbientSeen.Add(Ambient.Value);Ambient.Value=phase;}
            public ValueTask<T> Value<T>(string phase,T value){var g=new AsyncOnceGate<T>(value);Gates.Add((phase,g));if(Mode=="pending"&&phase=="before")Pending=g;else if(Mode=="oce"&&phase=="handler"){Cancellation.Cancel();Failure=new OperationCanceledException("handler phase",Cancellation.Token);g.Fail(Failure);}else g.Complete();return g.Task;}
            public ValueTask Done(string phase){var g=new AsyncOnceGate<int>(0);Gates.Add((phase,g));g.Complete();return g.Void;}
        }
        public static class Entry {
            public static async Task<string> Run(){var rows=new[]{
                (await OpenAsyncResponse.Bridge.Observe<OpenFixture.A,OpenFixture.B>(true,"pending"),await OpenAsyncResponse.Bridge.Observe<OpenFixture.A,OpenFixture.B>(false,"pending")),
                (await OpenAsyncResponse.Bridge.Observe<OpenFixture.B,OpenFixture.A>(true,"oce"),await OpenAsyncResponse.Bridge.Observe<OpenFixture.B,OpenFixture.A>(false,"oce")),
                (await OpenAsyncVoid.Bridge.Observe<OpenFixture.A>(true,"sync"),await OpenAsyncVoid.Bridge.Observe<OpenFixture.A>(false,"sync"))};
                foreach(var row in rows)Probe.Check(row.Item1==row.Item2,"generated/reference mismatch\n"+row.Item1+"\n"+row.Item2);
                return JsonSerializer.Serialize(new{pairs=3,dispatches=6,rows=rows.Select(r=>new{generated=JsonDocument.Parse(r.Item1).RootElement.Clone(),reference=JsonDocument.Parse(r.Item2).RootElement.Clone()})});
            }
        } }
        namespace AsyncSentinel {
        public sealed record Request(int Value):IRequest<int>;
        [ZendiatorHandler]public sealed class Handler:IRequestHandler<Request,int>{public ValueTask<int> HandleAsync(Request r,CancellationToken t)=>new(r.Value);}
        [ZendiatorComposition("closedAsync")]public sealed class Composition;
        public static class Registration{public static IServiceCollection Add(IServiceCollection s)=>s.AddZendiator<Composition>(c=>{c.Handlers<Request>(DispatchFamily.SendAsync,typeof(Handler));c.Pipeline<Request>(DispatchFamily.SendAsync);});}
        }
        namespace AsyncStreamSentinel {
        public sealed record Request<T>(T Value):IStreamRequest<T>;
        [ZendiatorHandler]public sealed class Handler<T>:IStreamRequestHandler<Request<T>,T> where T:class,new(){public IAsyncEnumerable<T> HandleAsync(Request<T> r,CancellationToken t)=>throw new NotImplementedException();}
        public sealed class First<R,T>:IAsyncStreamItemFilter<R,T>{public ValueTask<bool> AcceptAsync(R r,T item,CancellationToken t)=>new(true);}
        public sealed class Map<R,T>:IAsyncStreamItemMap<R,T>{public ValueTask<T> MapAsync(R r,T item,CancellationToken t)=>new(item);}
        public sealed class Last<R,T>:IStreamItemFilter<R,T>{public bool Accept(R r,T item,CancellationToken t)=>true;}
        [ZendiatorComposition("openStream")]public sealed class Composition;
        public static class Registration{public static IServiceCollection Add(IServiceCollection s)=>s.AddZendiator<Composition>(c=>{c.Handlers(typeof(Request<>),DispatchFamily.Stream,typeof(Handler<>));c.Pipeline(typeof(Request<>),DispatchFamily.Stream,Stage.Hook(typeof(First<,>)),Stage.Hook(typeof(Map<,>)),Stage.Hook(typeof(Last<,>)));});}
        }
        """ + OpenSendAsyncModule(false) + OpenSendAsyncModule(true);

    private static string OpenSendAsyncNegative()
    {
        const string request = "OpenAsyncResponse.Request<,>";
        const string handler = "OpenAsyncResponse.Handler<,>";
        const string hook = "OpenAsyncResponse.Hook<,>";
        var declarations = new List<string>(); var registrations = new List<string>();
        void Reject(string name, string h, string stage, string family = "SendAsync", string extra = "")
        {
            declarations.Add($"[ZendiatorComposition(\"{name}\")]public sealed class {name};");
            registrations.Add($"public static IServiceCollection {name}(IServiceCollection s)=>s.AddZendiator<{name}>(c=>{{c.Handlers(typeof({request}),DispatchFamily.{family},typeof({h}));c.Pipeline(typeof({request}),DispatchFamily.{family},{stage});{extra}}});");
        }
        Reject("Undetermined", handler, "Stage.Hook(typeof(UnusedHook<,,>))");
        Reject("ConstraintConflict", handler, "Stage.Hook(typeof(StructHook<,>))");
        Reject("Wrapper", handler, $"Stage.Wrapper(typeof({hook}))");
        Reject("Session", handler, $"Stage.Hook(typeof({hook}),typeof(Factory<,>))");
        Reject("WrongAll", handler, $"Stage.Hook(typeof({hook}))", "SendAllAsync");
        Reject("Overlap", handler, $"Stage.Hook(typeof({hook}))", extra: "c.Handlers<OpenAsyncResponse.Request<OpenFixture.A,OpenFixture.B>>(DispatchFamily.SendAsync,typeof(OpenAsyncResponse.Handler<OpenFixture.B,OpenFixture.A>));c.Pipeline<OpenAsyncResponse.Request<OpenFixture.A,OpenFixture.B>>(DispatchFamily.SendAsync,Stage.Hook<OpenAsyncResponse.Hook<OpenFixture.B,OpenFixture.A>>());");
        Reject("Multiple", handler, $"Stage.Hook(typeof({hook}))", extra: "c.Handlers<AsyncSentinel.Request>(DispatchFamily.SendAsync,typeof(AsyncSentinel.Handler));c.Pipeline<AsyncSentinel.Request>(DispatchFamily.SendAsync);");
        return OpenSendAsyncSource() + "namespace AsyncRejected {" + string.Join("\n", declarations) + "public static class Registration {" + string.Join("\n", registrations) + "}" + """
            public sealed class UnusedHook<X,Y,Z>:OpenAsyncResponse.Hook<X,Y> where X:class,new() where Y:class,new(){public UnusedHook(OpenAsyncFixture.Probe p):base(p){}}
            public sealed class StructHook<X,Y>:IAsyncSendRequestTransform<OpenAsyncResponse.Request<Y,X>>,IAsyncSendResponseTransform<OpenAsyncResponse.Request<Y,X>,int>,IAsyncSendExceptionObserver<OpenAsyncResponse.Request<Y,X>>,IAsyncSendFinallyObserver<OpenAsyncResponse.Request<Y,X>> where X:struct {
                public ValueTask<OpenAsyncResponse.Request<Y,X>> BeforeAsync(OpenAsyncResponse.Request<Y,X> r,CancellationToken t)=>new(r);
                public ValueTask<int> AfterAsync(OpenAsyncResponse.Request<Y,X> r,int value,CancellationToken t)=>new(value);
                public ValueTask OnExceptionAsync(OpenAsyncResponse.Request<Y,X> r,Exception e,CancellationToken t)=>default;
                public ValueTask FinallyAsync(OpenAsyncResponse.Request<Y,X> r,SendExitInfo e,CancellationToken t)=>default;
            }
            public sealed class Factory<X,Y>;
            public sealed class NoDefault{public NoDefault(int value){}}
            public static class Consumer{public static ValueTask<int> Invalid(OpenAsyncResponse.Generated.Composition.IZendiator m)=>m.SendAsync(new OpenAsyncResponse.Request<NoDefault,OpenFixture.B>(1));}
            }
            """;
    }

    private static string? OpenSendAsyncFolder(string name)
    {
        var root = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        if (string.IsNullOrEmpty(root)) return null;
        var folder = Path.Combine(root, "open-send-async-" + name); Directory.CreateDirectory(folder); return folder;
    }

    private static void AssertOpenSendAsyncOutput(Compilation output, GeneratorDriver driver)
    {
        AssertOpenSendSyncOutput(output, driver);
        var catalog = Assert.IsType<CompositionSelectionCatalog>(Assert.Single(driver.GetRunResult().Results.Single().TrackedSteps["CompositionSelectionModel"].SelectMany(s => s.Outputs)).Value);
        var model = CanonicalAsyncEmissionNormalizer.Create(catalog, CanonicalExecutionGraphNormalizer.Create(catalog));
        foreach (var ns in new[] { "OpenAsyncResponse", "OpenAsyncVoid" })
        {
            var route = Assert.Single(Assert.Single(catalog.Compositions, c => c.Key == ns).Pipelines);
            Assert.Equal(BoundRequestShape.OpenAsyncReference, route.RequestShape); Assert.NotNull(route.SendGeneric); Assert.NotNull(route.SendOpenRegistration); Assert.Null(route.StreamGeneric); Assert.Null(route.StreamOpenRegistration);
            var emission = Assert.Single(model.Compositions, c => c.Namespace == ns + ".Generated.Composition");
            Assert.Equal(route.SendGeneric, emission.Generic); Assert.Equal(route.SendOpenRegistration, emission.OpenRegistration); Assert.Equal(emission.OpenRegistration, CanonicalAsyncEmissionNormalizer.Registration(emission).OpenRegistration);
            var api = output.GetTypeByMetadataName(ns + ".Generated.Composition.IZendiator"); Assert.NotNull(api); Assert.False(api.IsGenericType);
            var mediator = output.GetTypeByMetadataName(ns + ".Generated.Composition.Zendiator")!; Assert.False(mediator.IsGenericType);
            var methods = new[] { Assert.Single(api.GetMembers("SendAsync").OfType<IMethodSymbol>()), Assert.Single(mediator.GetMembers("SendAsync").OfType<IMethodSymbol>()) };
            Assert.All(methods, m => { Assert.Equal(ns == "OpenAsyncVoid" ? 1 : 2, m.Arity); Assert.All(m.TypeParameters, p => { Assert.True(p.HasReferenceTypeConstraint); Assert.True(p.HasConstructorConstraint); }); Assert.Equal(ns == "OpenAsyncVoid" ? 0 : 1, ((INamedTypeSymbol)m.ReturnType).Arity); });
            var declarations = methods.Select(m => (MethodDeclarationSyntax)m.DeclaringSyntaxReferences.Single().GetSyntax()).ToArray();
            Assert.All(declarations, d => Assert.Equal(declarations[0].ConstraintClauses.ToFullString().Trim(), d.ConstraintClauses.ToFullString().Trim()));
            var invoke = AssertOpenAsyncInvoker(output, mediator, methods[1]);
            Assert.Equal(ns == "OpenAsyncVoid" ? 0 : 1, ((INamedTypeSymbol)invoke.ReturnType).Arity);
        }
        var closed = output.GetTypeByMetadataName("AsyncSentinel.Generated.Composition.IZendiator")!; Assert.NotNull(closed); Assert.Equal(0, Assert.Single(closed.GetMembers("SendAsync").OfType<IMethodSymbol>()).Arity);
        var stream = output.GetTypeByMetadataName("AsyncStreamSentinel.Generated.Composition.IZendiator")!; Assert.NotNull(stream); Assert.Equal(1, Assert.Single(stream.GetMembers("StreamAsync").OfType<IMethodSymbol>()).Arity);
    }

    private static IMethodSymbol AssertOpenAsyncInvoker(Compilation output, INamedTypeSymbol mediator, IMethodSymbol entry)
    {
        var invoker = Assert.Single(mediator.GetTypeMembers(), t => t.Name.StartsWith("SendInvoker", StringComparison.Ordinal));
        Assert.True(invoker.IsReadOnly); Assert.Equal(entry.Arity, invoker.Arity);
        var declaration = (MethodDeclarationSyntax)entry.DeclaringSyntaxReferences.Single().GetSyntax();
        Assert.Equal(declaration.ConstraintClauses.ToFullString().Trim(), ((StructDeclarationSyntax)invoker.DeclaringSyntaxReferences.Single().GetSyntax()).ConstraintClauses.ToFullString().Trim());
        Assert.All(invoker.TypeParameters, p => { Assert.True(p.HasReferenceTypeConstraint); Assert.True(p.HasConstructorConstraint); });
        var invoke = Assert.Single(invoker.GetMembers("InvokeAsync").OfType<IMethodSymbol>()); Assert.Equal(0, invoke.Arity);
        var creation = Assert.Single(declaration.DescendantNodes().OfType<ObjectCreationExpressionSyntax>());
        var constructor = Assert.IsAssignableFrom<IMethodSymbol>(output.GetSemanticModel(creation.SyntaxTree).GetSymbolInfo(creation).Symbol);
        Assert.True(SymbolEqualityComparer.Default.Equals(invoker, constructor.ContainingType.OriginalDefinition));
        Assert.Equal(entry.TypeParameters.Cast<ITypeSymbol>(), constructor.ContainingType.TypeArguments, SymbolEqualityComparer.Default);
        var call = Assert.Single(declaration.DescendantNodes().OfType<InvocationExpressionSyntax>(), i => i.Expression is MemberAccessExpressionSyntax m && m.Name.Identifier.ValueText == "InvokeAsync");
        var bound = Assert.IsAssignableFrom<IMethodSymbol>(output.GetSemanticModel(call.SyntaxTree).GetSymbolInfo(call).Symbol);
        Assert.True(SymbolEqualityComparer.Default.Equals(bound.OriginalDefinition, invoke));
        Assert.True(SymbolEqualityComparer.Default.Equals(bound.ContainingType, constructor.ContainingType));
        return invoke;
    }

    [Fact]
    public async Task OpenSendAsync_inferred_constraints_closed_DI_pending_OCE_and_sync_void_match_reference()
    {
        var source = OpenSendAsyncSource(); var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "OpenSendAsync_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        var folder = OpenSendAsyncFolder("runtime"); SaveSendAllSync(folder, source, driver, output, diagnostics);
        Assert.Empty(diagnostics); Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)); AssertOpenSendAsyncOutput(output, driver);
        using var bytes = new MemoryStream(); var emitted = output.Emit(bytes); Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics)); if (folder != null) File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray()); bytes.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes); var result = await assembly.GetType("OpenAsyncFixture.Entry")!.GetMethod("Run")!.CreateDelegate<Func<Task<string>>>()(); if (folder != null) File.WriteAllText(Path.Combine(folder, "runtime.json"), result);
        using var json = JsonDocument.Parse(result); Assert.Equal(6, json.RootElement.GetProperty("dispatches").GetInt32());
    }

    [Fact]
    public void OpenSendAsync_rejects_independent_unproved_routes_preserves_compiler_where_and_sentinels()
    {
        var source = OpenSendAsyncNegative(); var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "OpenSendAsyncNegative_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        var folder = OpenSendAsyncFolder("negative"); SaveSendAllSync(folder, source, driver, output, diagnostics); AssertOpenSendAsyncOutput(output, driver);
        AssertComposedProfile(output, "AsyncRejected.Generated.Multiple", ("SendAsync", 2));
        foreach (var name in new[] { "Undetermined", "ConstraintConflict", "Wrapper", "Session", "WrongAll", "Overlap" })
        {
            Assert.Null(output.GetTypeByMetadataName("AsyncRejected.Generated." + name + ".IZendiator")); Assert.Null(output.GetTypeByMetadataName("AsyncRejected.Generated." + name + ".ZendiatorGeneratedRegistrar"));
            var start = source.IndexOf("public static IServiceCollection " + name + "(", StringComparison.Ordinal); var end = source.IndexOf("});", start, StringComparison.Ordinal) + 3;
            Assert.Contains(diagnostics, d => d.Location.IsInSource && d.Location.SourceSpan.Start >= start && d.Location.SourceSpan.End <= end && d.Id == (name == "Overlap" ? "ZEN0028" : "ZEN0035"));
            foreach (var interceptor in driver.GetRunResult().Results.Single().GeneratedSources.Where(f => f.SourceText.ToString().Contains("InterceptsLocation", StringComparison.Ordinal))) Assert.DoesNotContain("typeof(global::AsyncRejected." + name + ")", interceptor.SourceText.ToString(), StringComparison.Ordinal);
        }
        foreach (var name in new[] { "Undetermined", "ConstraintConflict" })
        {
            var start = source.IndexOf("public static IServiceCollection " + name + "(", StringComparison.Ordinal); var end = source.IndexOf("});", start, StringComparison.Ordinal) + 3;
            var error = Assert.Single(diagnostics, d => d.Id == "ZEN0035" && d.Location.SourceSpan.Start >= start && d.Location.SourceSpan.End <= end && source.Substring(d.Location.SourceSpan.Start, d.Location.SourceSpan.Length).StartsWith("Stage.Hook", StringComparison.Ordinal));
            Assert.StartsWith("Stage.Hook", source.Substring(error.Location.SourceSpan.Start, error.Location.SourceSpan.Length), StringComparison.Ordinal); Assert.Empty(error.AdditionalLocations);
        }
        var overlap = Assert.Single(diagnostics, d => d.Id == "ZEN0028"); Assert.Contains("Pipeline<OpenAsyncResponse.Request<", source.Substring(overlap.Location.SourceSpan.Start, overlap.Location.SourceSpan.Length));
        var additional = Assert.Single(overlap.AdditionalLocations); Assert.True(additional.IsInSource); Assert.Contains("Pipeline(typeof(OpenAsyncResponse.Request<,>)", source.Substring(additional.SourceSpan.Start, additional.SourceSpan.Length)); Assert.True(additional.SourceSpan.Start < overlap.Location.SourceSpan.Start);
        var errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray(); Assert.NotEmpty(errors); Assert.All(errors, d => { Assert.Equal("CS0310", d.Id); Assert.Contains("SendAsync", source.Substring(d.Location.SourceSpan.Start, d.Location.SourceSpan.Length)); Assert.Contains("NoDefault", d.GetMessage()); });
        if (folder != null) File.WriteAllText(Path.Combine(folder, "negative-result.json"), JsonSerializer.Serialize(new { logicalInputs = 1, drivers = 1, dispatches = 0, unrelatedGeneratedCompositions = 8, newlyComposedProfiles = new[] { "Multiple" }, rejectedCompositions = 6, compiler = errors.Select(d => new { d.Id, message = d.GetMessage(), span = d.Location.SourceSpan.ToString() }), generator = diagnostics.Select(d => new { d.Id, message = d.GetMessage(), primary = d.Location.SourceSpan.ToString(), additional = d.AdditionalLocations.Select(l => l.SourceSpan.ToString()) }) }));
    }
}
