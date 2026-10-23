using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using Zendiator.SourceGenerator;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private static string OpenSyncWrapperHookSource(bool isVoid = false)
    {
        var ns = isVoid ? "OpenSyncVoidWrapperHook" : "OpenSyncWrapperHook";
        var result = isVoid ? "void" : "int";
        var response = isVoid ? "" : ",int";
        return $$"""
        using System;using System.Collections.Generic;using System.Linq;using System.Text.Json;using System.Threading;
        using Microsoft.Extensions.DependencyInjection;using Zendiator;using Zendiator.DependencyInjection;
        namespace {{ns}} {
        public sealed class Marker {} public sealed class OtherMarker {}
        public sealed record Query<T>(int Value):ISyncRequest{{(isVoid ? "" : "<int>")}};
        public sealed class Probe {
            public static readonly AsyncLocal<string?> Ambient=new();
            public string Mode="";public CancellationToken Token;public object Original=null!;
            public readonly Exception Failure=new FormatException("inner finally");public int HandlerCalls,NextCalls;
            public readonly List<string> Trace=new();public readonly List<string?> AmbientSeen=new();
            public readonly List<object> Created=new(),Disposed=new(),Saved=new();
            public static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
            public void Resolve(object receiver,string role){Created.Add(receiver);Trace.Add("resolve:"+role);}
            public void Hit(string phase,object request,CancellationToken token){
                Check(token==Token&&ReferenceEquals(request,phase.StartsWith("W",StringComparison.Ordinal)||phase=="K.before"?Original:Saved.Last()),"saved request/token");
                Trace.Add(phase);AmbientSeen.Add(Ambient.Value);Ambient.Value=phase;
            }
        }
        [ZendiatorHandler]public sealed class Handler<T>:ISyncRequestHandler<Query<T>{{response}}>,IDisposable where T:class,new() {
            private readonly Probe p;public Handler(Probe p){this.p=p;p.Resolve(this,"H");}
            public {{result}} Handle(Query<T> r,CancellationToken token){p.Hit("H",r,token);p.HandlerCalls++;{{(isVoid ? "" : "return r.Value;")}}}
            public void Dispose(){p.Disposed.Add(this);}
        }
        public sealed class Hook<T>:ISendRequestTransform<Query<T>>,{{(isVoid ? "ISendSuccessObserver<Query<T>>" : "ISendResponseTransform<Query<T>,int>")}},ISendExceptionObserver<Query<T>>,ISendFinallyObserver<Query<T>>,IDisposable where T:class,new() {
            private readonly Probe p;public Hook(Probe p){this.p=p;p.Resolve(this,"K");}
            public Query<T> Before(Query<T> r,CancellationToken token){p.Hit("K.before",r,token);var saved=r with{Value=r.Value+1};p.Saved.Add(saved);return saved;}
            public {{result}} After(Query<T> r,{{(isVoid ? "" : "int value,")}}CancellationToken token){p.Hit("K.after",r,token);{{(isVoid ? "" : "return value+10;")}}}
            public void OnException(Query<T> r,Exception error,CancellationToken token){p.Hit("K.exception",r,token);}
            public void Finally(Query<T> r,SendExitInfo exit,CancellationToken token){
                p.Hit("K.finally",r,token);Probe.Check(exit.Success&&exit.Error==null,"successful inner work before finally");if(p.Mode=="finally")throw p.Failure;
            }
            public void Dispose(){p.Disposed.Add(this);}
        }
        public sealed class Logging<R{{(isVoid ? "" : ",S")}}>:ISendWrapper<R{{(isVoid ? "" : ",S")}}>,IDisposable where R:class,ISyncRequest{{(isVoid ? "" : "<S>")}} {
            private readonly Probe p;public Logging(Probe p){this.p=p;p.Resolve(this,"W");}
            public {{(isVoid ? "void" : "S")}} Invoke<N>(R r,N next,CancellationToken token) where N:ISendContinuation<R{{(isVoid ? "" : ",S")}}>{
                p.Hit("W.before",r,token);try{ {{(isVoid ? "" : "S value=default!;")}}for(var i=0;i<(p.Mode=="repeat"?2:1);i++){p.NextCalls++;{{(isVoid ? "" : "value=")}}next.Invoke(r,token);}{{(isVoid ? "" : "return value;")}} }
                finally{p.Hit("W.finally",r,token);}
            }
            public void Dispose(){p.Disposed.Add(this);}
        }
        [ZendiatorComposition("{{ns}}")]public sealed class Mixed;
        public static class Register {
            public static IServiceCollection Add(IServiceCollection s)=>s.AddZendiator<Mixed>(c=>{
                c.Handlers(typeof(Query<>),DispatchFamily.SendSync,typeof(Handler<>));
                c.Pipeline(typeof(Query<>),DispatchFamily.SendSync,Stage.Wrapper(typeof(Logging<{{(isVoid ? "" : ",")}}>)),Stage.Hook(typeof(Hook<>)));
            });
        }
        }
        """;
    }

    private static string OpenSyncWrapperHookConsumer(bool isVoid = false)
    {
        var ns = isVoid ? "OpenSyncVoidWrapperHook" : "OpenSyncWrapperHook";
        var result = isVoid ? "void" : "int";
        var response = isVoid ? "" : ",int";
        return $$"""
        namespace {{ns}} {
        public static class Entry {
            private static {{result}} Reference<T>(IServiceProvider p,Query<T> r,CancellationToken token) where T:class,new(){ArgumentNullException.ThrowIfNull(r);{{(isVoid ? "" : "return ")}}Outer(p,r,token);}
            private static {{result}} Outer<T>(IServiceProvider p,Query<T> r,CancellationToken token) where T:class,new(){
                token.ThrowIfCancellationRequested();{{(isVoid ? "" : "return ")}}p.GetRequiredService<Logging<Query<T>{{response}}>>().Invoke(r,new Next<T>(p),token);
            }
            private readonly struct Next<T>(IServiceProvider p):ISendContinuation<Query<T>{{response}}> where T:class,new(){
                public {{result}} Invoke(Query<T> r,CancellationToken token){ArgumentNullException.ThrowIfNull(r);{{(isVoid ? "" : "return ")}}Inner(p,r,token);}
            }
            // Independent orchestration of the same user callbacks.
            private static {{result}} Inner<T>(IServiceProvider p,Query<T> r,CancellationToken token) where T:class,new(){
                token.ThrowIfCancellationRequested();var hook=p.GetRequiredService<Hook<T>>();var saved=r;var success=false;Exception? error=null;
                try{
                    saved=hook.Before(saved,token);ArgumentNullException.ThrowIfNull(saved);token.ThrowIfCancellationRequested();
                    {{(isVoid ? "" : "var value=")}}p.GetRequiredService<Handler<T>>().Handle(saved,token);{{(isVoid ? "" : "value=")}}hook.After(saved,{{(isVoid ? "" : "value,")}}token);success=true;{{(isVoid ? "" : "return value;")}}
                }catch(Exception failure){error=failure;try{hook.OnException(saved,failure,token);}catch(Exception replacement){error=replacement;throw;}throw;}
                finally{hook.Finally(saved,new(success,error),token);}
            }
            public static {{result}} OtherMarkerCompileSentinel(Generated.Mixed.IZendiator m)=>m.Send(new Query<OtherMarker>(42));
            private static string Observe(bool generated,string mode){
                using var cancellation=new CancellationTokenSource();var p=new Probe{Mode=mode,Token=cancellation.Token};IServiceCollection services=new ServiceCollection();services.AddSingleton(p);
                services.AddScoped(typeof(Handler<>));services.AddScoped(typeof(Hook<>));var existing=services.Last();Register.Add(services);var count=services.Count;Register.Add(services);
                Probe.Check(services.Count==count&&services.Contains(existing),"TryAdd/lifetime");
                foreach(var type in new[]{typeof(Logging<{{(isVoid ? "" : ",")}}>),typeof(Hook<>),typeof(Handler<>),typeof(Generated.Mixed.IZendiator)})Probe.Check(services.Single(d=>d.ServiceType==type).Lifetime==(type==typeof(Generated.Mixed.IZendiator)||type==typeof(Hook<>)||type==typeof(Handler<>)?ServiceLifetime.Scoped:ServiceLifetime.Transient),"default dependencies and overridden lifetime");
                Probe.Check(!services.Any(d=>d.ServiceType==typeof(Generated.Mixed.Zendiator))&&services.Single(d=>d.ServiceType==typeof(Generated.Mixed.IZendiator)).ImplementationType==typeof(Generated.Mixed.Zendiator),"single interface facade registration");
                var mismatch=new ZendiatorConfiguration();mismatch.Handlers(typeof(Query<>),DispatchFamily.SendSync,typeof(Handler<>));mismatch.Pipeline(typeof(Query<>),DispatchFamily.SendSync,Stage.Hook(typeof(Hook<>)),Stage.Wrapper(typeof(Logging<{{(isVoid ? "" : ",")}}>)));
                IServiceCollection rejected=new ServiceCollection();var mismatchRejected=false;try{Generated.Mixed.ZendiatorGeneratedRegistrar.Add(rejected,mismatch.Snapshot());}catch(InvalidOperationException){mismatchRejected=true;}
                Probe.Check(mismatchRejected&&rejected.Count==0,"snapshot order checked before registration");
                using var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});using var scope=provider.CreateScope();var sp=scope.ServiceProvider;
                var mediator=sp.GetRequiredService<Generated.Mixed.IZendiator>();Probe.Check(ReferenceEquals(mediator,sp.GetRequiredService<Generated.Mixed.IZendiator>()),"one facade");
                var request=new Query<Marker>(42);p.Original=request;Probe.Ambient.Value="caller";int? value=null;Exception? caught=null;
                try{ {{(isVoid ? "if(generated)mediator.Send(request,p.Token);else Reference(sp,request,p.Token);" : "value=generated?mediator.Send(request,p.Token):Reference(sp,request,p.Token);")}} }catch(Exception error)when(ReferenceEquals(error,p.Failure)){caught=error;}
                Probe.Check(mode=="repeat"?{{(isVoid ? "" : "value==53&&")}}caught==null:value==null&&caught==p.Failure,"direct value/synchronous exception");
                var expected=new List<string>{"resolve:W","W.before","resolve:K","K.before","resolve:H","H","K.after","K.finally"};
                if(mode=="repeat")expected.AddRange(new[]{"K.before","H","K.after","K.finally"});expected.Add("W.finally");
                Probe.Check(p.Trace.SequenceEqual(expected)&&Probe.Ambient.Value=="W.finally","entry/unwind/native synchronous context");
                Probe.Check(p.HandlerCalls==(mode=="repeat"?2:1)&&p.NextCalls==p.HandlerCalls&&p.Saved.Count==p.HandlerCalls,"continuation multiplicity");
                Probe.Check(p.Saved.All(r=>!ReferenceEquals(r,request))&&(p.Saved.Count==1||!ReferenceEquals(p.Saved[0],p.Saved[1])),"separate request slots");
                Probe.Check(p.Created.Select(x=>x.GetType()).SequenceEqual(new[]{typeof(Logging<Query<Marker>{{response}}>),typeof(Hook<Marker>),typeof(Handler<Marker>)}),"inferred DI keys");
                Probe.Check(p.Disposed.Count==0,"no dispatch disposal");scope.Dispose();Probe.Check(p.Disposed.SequenceEqual(p.Created.AsEnumerable().Reverse()),"one reverse scope cleanup");
                return JsonSerializer.Serialize(new{mode,value,error=caught==null?null:"sentinel",p.Trace,p.AmbientSeen,callerContext=Probe.Ambient.Value,p.HandlerCalls,p.NextCalls,created=p.Created.Select(x=>x.GetType().ToString()),disposed=p.Disposed.Select(x=>x.GetType().ToString()),registrations=count,mismatchRejected});
            }
            public static string Run(){var rows=new List<object>();foreach(var mode in new[]{"repeat","finally"}){
                var reference=Observe(false,mode);var generated=Observe(true,mode);Probe.Check(reference==generated,"reference/generated mismatch\n"+reference+"\n"+generated);rows.Add(JsonDocument.Parse(generated).RootElement.Clone());
            }return JsonSerializer.Serialize(new{pairs=2,dispatches=4,rows});}
        }
        }
        """;
    }

    [Fact]
    public void OpenSendSync_wrapper_then_hook_preserves_repeat_finally_and_registration() => VerifyOpenSyncWrapperHook(false);

    [Fact]
    public void OpenSendSync_void_wrapper_then_hook_preserves_repeat_finally_and_registration() => VerifyOpenSyncWrapperHook(true);

    private static void VerifyOpenSyncWrapperHook(bool isVoid)
    {
        var ns = isVoid ? "OpenSyncVoidWrapperHook" : "OpenSyncWrapperHook";
        var source = OpenSyncWrapperHookSource(isVoid) + OpenSyncWrapperHookConsumer(isVoid);
        var compilation = SyncCompilation(source, "OpenSyncWrapperHook_" + Guid.NewGuid().ToString("N"))
            .AddSyntaxTrees(new[] { OpenSendSyncSource(), WrapperSource(false, false, "direct", "inner"),
                OpenWrapperHookSource() + OpenWrapperHookConsumer(), OpenWrapperHookSource(true) + OpenWrapperHookConsumer(true) }
                .Select(s => CSharpSyntaxTree.ParseText(s, SyncParseOptions)));
        if (isVoid) compilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(OpenSyncWrapperHookSource() + OpenSyncWrapperHookConsumer(), SyncParseOptions));
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        var folder = OpenSendSyncFolder(isVoid ? "void-wrapper-hook" : "wrapper-hook"); SaveSendAllSync(folder, source, driver, output, diagnostics);
        Assert.Empty(diagnostics); Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        foreach (var name in new[] { ns + ".Generated.Mixed", "OpenSyncWrapperHook.Generated.Mixed", "OpenResponse.Generated.Composition", "OpenVoid.Generated.Composition", "App.Generated.Reads", "OpenWrapperHook.Generated.Mixed", "OpenVoidWrapperHook.Generated.Mixed" }.Distinct())
            Assert.NotNull(output.GetTypeByMetadataName(name + ".IZendiator"));
        var api = output.GetTypeByMetadataName(ns + ".Generated.Mixed.IZendiator")!;
        Assert.False(api.IsGenericType); var method = Assert.Single(api.GetMembers("Send").OfType<IMethodSymbol>());
        Assert.Equal(isVoid ? SpecialType.System_Void : SpecialType.System_Int32, method.ReturnType.SpecialType); Assert.False(method.IsAsync);
        var parameter = Assert.Single(method.TypeParameters); Assert.True(parameter.HasReferenceTypeConstraint); Assert.True(parameter.HasConstructorConstraint);
        var concrete = output.GetTypeByMetadataName(ns + ".Generated.Mixed.Zendiator")!;
        var target = CanonicalRouteEntry(output, Assert.Single(concrete.GetMembers("Send").OfType<IMethodSymbol>()));
        Assert.Equal(isVoid ? SpecialType.System_Void : SpecialType.System_Int32, target.ReturnType.SpecialType);
        var argument = Assert.Single(target.TypeParameters); Assert.True(argument.HasReferenceTypeConstraint); Assert.True(argument.HasConstructorConstraint);
        var catalog = Assert.IsType<CompositionSelectionCatalog>(Assert.Single(driver.GetRunResult().Results.Single().TrackedSteps["CompositionSelectionModel"].SelectMany(s => s.Outputs)).Value);
        var route = Assert.Single(Assert.Single(catalog.Compositions, c => c.Key == ns).Pipelines);
        var emission = Assert.Single(CanonicalWrapperEmissionNormalizer.Create(catalog, CanonicalExecutionGraphNormalizer.Create(catalog)).Compositions, c => c.Registration.Namespace == ns + ".Generated.Mixed");
        Assert.Equal(route.SendOpenRegistration, emission.Registration.OpenRegistration); Assert.Equal(2, emission.Registration.OpenRegistration!.Stages.Count);
        Assert.NotNull(emission.SyncInner); Assert.Null(emission.AsyncInner); Assert.Empty(emission.Regions);
        using var bytes = new MemoryStream(); var emitted = output.Emit(bytes); Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        if (folder != null)
        {
            File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
            for (var i = 0; i < compilation.SyntaxTrees.Count(); i++) File.WriteAllText(Path.Combine(folder, "input-" + i + ".cs"), compilation.SyntaxTrees.ElementAt(i).ToString());
        }
        bytes.Position = 0; var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var runtime = assembly.GetType(ns + ".Entry")!.GetMethod("Run")!.CreateDelegate<Func<string>>()();
        if (folder != null) File.WriteAllText(Path.Combine(folder, "runtime.json"), runtime);
        using var json = JsonDocument.Parse(runtime); Assert.Equal(4, json.RootElement.GetProperty("dispatches").GetInt32());
    }

    [Fact]
    public void OpenSendSync_wrapper_then_hook_rejects_unproved_bindings_order_and_ownership() => RejectOpenSyncWrapperHook(false);

    [Fact]
    public void OpenSendSync_void_wrapper_then_hook_rejects_unproved_bindings_order_and_ownership() => RejectOpenSyncWrapperHook(true);

    private static void RejectOpenSyncWrapperHook(bool isVoid)
    {
        var ns = isVoid ? "OpenSyncVoidWrapperHook" : "OpenSyncWrapperHook";
        var arity = isVoid ? "" : ",";
        var arguments = isVoid ? "R" : "R,S";
        var wrapper = $"Stage.Wrapper(typeof(Logging<{arity}>))";
        const string hook = "Stage.Hook(typeof(Hook<>))";
        var unbound = $"Stage.Wrapper(typeof(UnboundLogging<{(isVoid ? "," : ",,")}>))";
        var source = OpenSyncWrapperHookSource(isVoid) + $$"""
            namespace {{ns}} {
            public sealed class UnboundLogging<{{arguments}},Unused>:ISendWrapper<{{arguments}}> where R:class,ISyncRequest{{(isVoid ? "" : "<S>")}} {
                public {{(isVoid ? "void" : "S")}} Invoke<N>(R r,N next,CancellationToken token) where N:ISendContinuation<{{arguments}}> => next.Invoke(r,token);
            }
            public sealed class Factory<{{arguments}}>:IBorrowedSessionFactory<R,Logging<{{arguments}}>> where R:class,ISyncRequest{{(isVoid ? "" : "<S>")}} {
                public Logging<{{arguments}}> Create(R request,CancellationToken token)=>throw new NotSupportedException();
            }
            public sealed class AsyncLogging<{{arguments}}>:IAsyncSendWrapper<{{arguments}}> {
                public System.Threading.Tasks.ValueTask{{(isVoid ? "" : "<S>")}} InvokeAsync<N>(R r,N next,CancellationToken token) where N:IAsyncSendContinuation<{{arguments}}> => next.InvokeAsync(r,token);
            }
            }
            """;
        foreach (var (name, invalid) in new[]
        {
            ("unbound", source.Replace(wrapper, unbound)),
            ("reverse", source.Replace(wrapper + "," + hook, hook + "," + wrapper)),
            ("session", source.Replace(wrapper, $"Stage.Wrapper(typeof(Logging<{arity}>),typeof(Factory<{arity}>))")),
            ("wrapper-only", source.Replace(wrapper + "," + hook, wrapper)),
            ("async", source.Replace(wrapper, $"Stage.Wrapper(typeof(AsyncLogging<{arity}>))")),
            ("wrong-contract", source.Replace(wrapper, "Stage.Wrapper(typeof(Hook<>))"))
        })
        {
            var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(invalid, "OpenSyncWrapperRejected_" + name + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
            SaveSendAllSync(OpenSendSyncFolder((isVoid ? "void-wrapper-hook-" : "wrapper-hook-") + name), invalid, driver, output, diagnostics);
            Assert.NotEmpty(diagnostics); Assert.All(diagnostics, d => Assert.Equal("ZEN0035", d.Id));
            if (name == "unbound")
            {
                var error = Assert.Single(diagnostics, d => invalid.Substring(d.Location.SourceSpan.Start, d.Location.SourceSpan.Length) == unbound);
                Assert.Empty(error.AdditionalLocations);
            }
            Assert.Null(output.GetTypeByMetadataName(ns + ".Generated.Mixed.IZendiator")); Assert.Null(output.GetTypeByMetadataName(ns + ".Generated.Mixed.ZendiatorGeneratedRegistrar"));
            Assert.DoesNotContain(driver.GetRunResult().Results.Single().GeneratedSources, s => s.HintName.EndsWith("Interceptors.g.cs", StringComparison.Ordinal));
            Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        }
    }
}
