using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Zendiator.SourceGenerator;
using Xunit;

namespace Zendiator.Generator.Tests;

// User callbacks and observable inputs only. This probe does not invoke a graph, lowering or oracle.
public sealed class CanonicalSyncRuntimeProbe
{
    public static readonly AsyncLocal<string?> Ambient = new();
    public readonly List<string> Trace = new();
    public readonly List<(string Phase, object Request, CancellationToken Token, object Receiver)> Calls = new();
    public readonly List<object> Created = new();
    public readonly List<object> Released = new();
    public readonly List<string?> AmbientObserved = new();
    public readonly List<string> CultureObserved = new();
    public readonly CancellationTokenSource Cancellation = new();
    public Exception? DiError, ConstructorError, FactoryError, HandlerError, CleanupError;
    public bool NullService, NullSession, CancelInFactory, CancelInHandler;
    public int FactoryDisposals;
    public Action? DuringHandler;

    public void ConstructHandler(object receiver) { Trace.Add("handler.ctor"); if (ConstructorError!=null) throw ConstructorError; Created.Add(receiver); }
    public void ConstructFactory() { Trace.Add("factory.ctor"); if (ConstructorError!=null) throw ConstructorError; }
    public void Factory(object receiver, object request, CancellationToken token)
    {
        Trace.Add("factory"); Calls.Add(("factory", request, token, receiver));
        AmbientObserved.Add(Ambient.Value); Ambient.Value = "factory";
        CultureObserved.Add(CultureInfo.CurrentCulture.Name); CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("fr-FR");
        if (CancelInFactory) Cancellation.Cancel();
        if (FactoryError != null) throw FactoryError;
    }
    public int Handle(object receiver, object request, CancellationToken token)
    {
        Trace.Add("handler"); Calls.Add(("handler", request, token, receiver));
        AmbientObserved.Add(Ambient.Value); Ambient.Value = "handler";
        CultureObserved.Add(CultureInfo.CurrentCulture.Name); CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("es-ES");
        if (CancelInHandler) Cancellation.Cancel();
        DuringHandler?.Invoke();
        if (HandlerError != null) throw HandlerError;
        return 73;
    }
    public void Cleanup(object receiver)
    {
        Trace.Add("handler.dispose"); Released.Add(receiver);
        AmbientObserved.Add(Ambient.Value); Ambient.Value = "cleanup";
        CultureObserved.Add(CultureInfo.CurrentCulture.Name); CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("en-US");
        if (CleanupError != null) throw CleanupError;
    }
    public void DisposeFactory() { Trace.Add("factory.dispose"); FactoryDisposals++; }
}

public sealed partial class GeneratorTests
{
    private sealed record SyncFixture(Assembly Assembly, Type Mediator, Type Interface, Type Service, Type Request,
        MethodInfo Register, MethodInfo Dispatch, MethodInfo ResolveService, string Source);
    private static readonly ConcurrentDictionary<(string Receiver, bool Void), SyncFixture> SyncFixtures = new();
    private static readonly CSharpParseOptions SyncParseOptions = new CSharpParseOptions(LanguageVersion.Preview)
        .WithFeatures(new[] { new KeyValuePair<string, string>("InterceptorsNamespaces", "Zendiator.Generated.Interceptors") });

    private static string SyncRuntimeSource(string receiver, bool isVoid, bool named = true, bool extension = true,
        bool emptyPipeline = false, bool dispatch = true)
    {
        var result = isVoid ? "" : ",int";
        var ownership = receiver == "owned" ? "IOwnedSessionFactory" : "IBorrowedSessionFactory";
        var entry = receiver == "direct" ? "typeof(Handler<string>)" : "HandlerEntry.Session<Handler<string>,Factory<string>>()";
        var configure = "c => " + (emptyPipeline ? "{ " : "") + "c.Handlers<Ping>(DispatchFamily.SendSync," + entry + ")" +
            (emptyPipeline ? "; c.Pipeline<Ping>(DispatchFamily.SendSync); }" : "");
        var call = (extension ? "services.AddZendiator" : "global::Zendiator.DependencyInjection.ZendiatorServiceCollectionExtensions.AddZendiator") +
            (named ? "<Reads>" : "") + "(" + (extension ? "" : "services,") + configure + ")";
        var ns = named ? "App.Generated.Reads" : "Zendiator.Generated.Default";
        var send = "((global::" + ns + ".IZendiator)mediator).Send((Ping)request!,token);";
        var factories = receiver == "direct" ? "" : $$"""
                public class FactoryBase<T> : {{ownership}}<Ping,Handler<T>>, IDisposable where T:class {
                    private readonly Probe _probe;
                    public FactoryBase(Probe probe) { _probe=probe; probe.ConstructFactory(); }
                    Handler<T> {{ownership}}<Ping,Handler<T>>.Create(Ping request,CancellationToken token) {
                        _probe.Factory(this,request,token);
                        return _probe.NullSession ? null! : new Handler<T>(_probe);
                    }
                    void IDisposable.Dispose()=>_probe.DisposeFactory();
                }
                public sealed class Factory<T> : FactoryBase<T> where T:class { public Factory(Probe probe):base(probe){} }
                """;
        return $$"""
            using System; using System.Threading; using Zendiator; using Zendiator.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection;
            using Probe = global::Zendiator.Generator.Tests.CanonicalSyncRuntimeProbe;
            namespace App {
                public sealed record Ping : ISyncRequest{{(isVoid ? "" : "<int>")}};
                [ZendiatorHandler] public sealed partial class Handler<T> : ISyncRequestHandler<Ping{{result}}>, IDisposable where T:class {
                    private readonly Probe _probe;
                    public Handler(Probe probe) { _probe=probe; probe.ConstructHandler(this); }
                    {{(isVoid ? "void" : "int")}} ISyncRequestHandler<Ping{{result}}>.Handle(Ping request,CancellationToken token) {
                        {{(isVoid ? "" : "return ")}}_probe.Handle(this,request,token);
                    }
                    void IDisposable.Dispose()=>_probe.Cleanup(this);
                }
                {{factories}}
                [ZendiatorComposition("reads")] public sealed class Reads;
                public static class Bridge {
                    public static IServiceCollection Register(IServiceCollection services)=>{{call}};
                    public static object? ResolveService(IServiceProvider provider) {
                        var probe=provider.GetRequiredService<Probe>();
                        probe.Trace.Add("{{(receiver=="direct"?"resolve-handler":"resolve-factory")}}");
                        if (probe.DiError!=null) throw probe.DiError;
                        if (probe.NullService) return null;
                        return new {{(receiver=="direct"?"Handler<string>":"Factory<string>")}}(probe);
                    }
                    {{(dispatch ? "public static object? Dispatch(object mediator,object? request,CancellationToken token) { " + (isVoid ? "" : "return ") + send + (isVoid ? "return null;" : "") + " }" : "")}}
                }
            }
            """;
    }

    private static CSharpCompilation SyncCompilation(string source, string assemblyName) => CSharpCompilation.Create(assemblyName,
        [CSharpSyntaxTree.ParseText(source, SyncParseOptions)], References.Where(r=>!string.Equals(r.Display,typeof(CanonicalSyncRuntimeProbe).Assembly.Location,StringComparison.OrdinalIgnoreCase))
            .Append(MetadataReference.CreateFromFile(typeof(CanonicalSyncRuntimeProbe).Assembly.Location)),
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    private static GeneratorDriver SyncDriver() => CSharpGeneratorDriver.Create([new ZendiatorGenerator().AsSourceGenerator()],
        parseOptions: SyncParseOptions, driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, true));

    private static SyncFixture CompileSyncFixture(string receiver, bool isVoid, bool named = true, bool extension = true, bool emptyPipeline = false,
        string? sourceOverride = null, string applicationNamespace = "App", bool candidateOnly = false)
    {
        var source = sourceOverride ?? SyncRuntimeSource(receiver,isVoid,named,extension,emptyPipeline);
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source,"CanonicalSync_"+Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        if (candidateOnly) Assert.Equal("ZEN0037", Assert.Single(diagnostics).Id);
        else Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        using var bytes = new MemoryStream();
        var emitted = output.Emit(bytes);
        Assert.True(emitted.Success,string.Join("\n",emitted.Diagnostics));
        var sources = driver.GetRunResult().Results.Single().GeneratedSources;
        Assert.Equal(2,sources.Count(s=>s.HintName!="Zendiator.GeneratedCatalog.g.cs"));
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(evidence))
        {
            var folder = Path.Combine(evidence,receiver+"-"+(isVoid?"void":"response")+"-"+named+"-"+extension+"-"+emptyPipeline+"-"+
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..8]);
            Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder,"input.cs"),source);
            foreach (var generated in sources) File.WriteAllText(Path.Combine(folder,generated.HintName),generated.SourceText.ToString());
            File.WriteAllBytes(Path.Combine(folder,"generated.dll"),bytes.ToArray());
            File.WriteAllText(Path.Combine(folder,"compile.json"),JsonSerializer.Serialize(new { assemblyName=output.AssemblyName,
                generatedSources=sources.Select(s=>s.HintName),emitted.Success,warnings=0,errors=0,
                generatorErrors=diagnostics.Count(d=>d.Severity==DiagnosticSeverity.Error),productionEnabled=!candidateOnly,
                candidateOnly, generatorDiagnostics=diagnostics.Select(d=>new { d.Id,severity=d.Severity.ToString(),message=d.GetMessage() }) }));
        }
        bytes.Position=0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var ns = named ? applicationNamespace+".Generated.Reads" : "Zendiator.Generated.Default";
        var bridge=assembly.GetType(applicationNamespace+".Bridge")!;
        return new(assembly,assembly.GetType(ns+".Zendiator")!,assembly.GetType(ns+".IZendiator")!,
            assembly.GetType(applicationNamespace+(receiver=="direct"?".Handler`1":".Factory`1"))!.MakeGenericType(typeof(string)),
            assembly.GetType(applicationNamespace+".Ping")!,bridge.GetMethod("Register")!,bridge.GetMethod("Dispatch")!,bridge.GetMethod("ResolveService")!,source);
    }
    private static SyncFixture SyncRuntimeFixture(string receiver,bool isVoid) => SyncFixtures.GetOrAdd((receiver,isVoid),key=>CompileSyncFixture(key.Receiver,key.Void));

    private sealed class RecordingSyncProvider(IServiceProvider inner,CanonicalSyncRuntimeProbe probe,Type service) : IServiceProvider
    {
        public object? GetService(Type type)
        {
            if (type==service) { probe.Trace.Add(service.Name.StartsWith("Handler",StringComparison.Ordinal)?"resolve-handler":"resolve-factory");
                if (probe.DiError!=null) throw probe.DiError; if (probe.NullService) return null; }
            return inner.GetService(type);
        }
    }
    private static ServiceProvider SyncProvider(SyncFixture f,CanonicalSyncRuntimeProbe probe,bool recording=true,ServiceLifetime? lifetime=null)
    {
        IServiceCollection services=new ServiceCollection(); services.AddSingleton(probe);
        // Observe resolution through a real Microsoft DI registration, not through the old candidate-only provider decorator.
        if (recording) services.AddScoped(f.Service,f.ResolveService.CreateDelegate<Func<IServiceProvider,object>>());
        if (lifetime!=null) services.Add(ServiceDescriptor.Describe(f.Service,f.Service,lifetime.Value));
        Assert.Same(services,f.Register.Invoke(null,[services]));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes=true,ValidateOnBuild=true });
    }
    private static (object? Result,Exception? Error) InvokeSync(SyncFixture f,object mediator,object? request,CancellationToken token)
    {
        try { return (f.Dispatch.Invoke(null,[mediator,request,token]),null); }
        catch (TargetInvocationException e) { return (null,e.InnerException); }
    }

    public static IEnumerable<object[]> SyncRuntimeCases()
    {
        foreach (var receiver in new[] { "direct","borrowed","owned" }) foreach (var isVoid in new[] { false,true })
        {
            foreach (var scenario in new[] { "success","null-request","pre-cancel","di-error","di-null","constructor-error","handler-error","handler-oce","cancel-in-handler" })
                yield return [receiver,isVoid,scenario];
            if (receiver!="direct") foreach (var scenario in new[] { "factory-error","factory-oce","factory-null","cancel-in-factory" })
                yield return [receiver,isVoid,scenario];
            if (receiver=="owned") foreach (var scenario in new[] { "cleanup-error-success","cleanup-oce-success","cleanup-error-over-body",
                "cleanup-oce-over-body","cleanup-error-over-oce","cleanup-oce-over-oce" }) yield return [receiver,isVoid,scenario];
        }
    }

    [Theory]
    [MemberData(nameof(SyncRuntimeCases))]
    public void Generated_SendSync_executes_public_registration_and_matches_independent_D03_D04_trace_identity_and_ownership(string receiver,bool isVoid,string scenario)
    {
        var f=SyncRuntimeFixture(receiver,isVoid); var probe=new CanonicalSyncRuntimeProbe();
        var diError=new InvalidOperationException("DI identity"); var factoryError=new Exception("factory identity"); var handlerError=new Exception("handler identity");
        var cleanupError=new Exception("cleanup identity"); using var otherToken=new CancellationTokenSource();
        var constructorError=new Exception("constructor identity"); probe.ConstructorError=scenario=="constructor-error"?constructorError:null;
        var factoryOce=new OperationCanceledException("factory OCE",otherToken.Token);
        var handlerOce=new OperationCanceledException("handler OCE",otherToken.Token);
        var cleanupOce=new OperationCanceledException("cleanup OCE",otherToken.Token);
        probe.DiError=scenario=="di-error"?diError:null; probe.NullService=scenario=="di-null"; probe.NullSession=scenario=="factory-null";
        probe.FactoryError=scenario=="factory-error"?factoryError:scenario=="factory-oce"?factoryOce:null;
        probe.HandlerError=scenario is "handler-error" or "cleanup-error-over-body" or "cleanup-oce-over-body"?handlerError:
            scenario is "handler-oce" or "cleanup-error-over-oce" or "cleanup-oce-over-oce"?handlerOce:null;
        probe.CleanupError=scenario.StartsWith("cleanup-error",StringComparison.Ordinal)?cleanupError:
            scenario.StartsWith("cleanup-oce",StringComparison.Ordinal)?cleanupOce:null;
        probe.CancelInFactory=scenario=="cancel-in-factory"; probe.CancelInHandler=scenario=="cancel-in-handler";
        if (scenario=="pre-cancel") probe.Cancellation.Cancel();
        var originalAmbient=CanonicalSyncRuntimeProbe.Ambient.Value; CanonicalSyncRuntimeProbe.Ambient.Value="caller";
        var originalCulture=CultureInfo.CurrentCulture; CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;
        try
        {
            using var provider=SyncProvider(f,probe); var scope=provider.CreateScope();
            try
            {
                var mediator=scope.ServiceProvider.GetRequiredService(f.Interface);
                Assert.Same(mediator,scope.ServiceProvider.GetRequiredService(f.Mediator));
                var request=scenario=="null-request"?null:Activator.CreateInstance(f.Request)!;
                var actual=InvokeSync(f,mediator,request,probe.Cancellation.Token);
                // Independent expected trace table, derived from D03/D04, not from generated text or the graph.
                var resolution=receiver=="direct"?"resolve-handler":"resolve-factory";
                string[] expected=scenario switch {
                    "null-request" or "pre-cancel" => [],
                    "di-error" or "di-null" => [resolution],
                    "constructor-error" => [resolution,receiver=="direct"?"handler.ctor":"factory.ctor"],
                    "factory-error" or "factory-oce" or "factory-null" => [resolution,"factory.ctor","factory"],
                    _ => receiver switch {
                        "direct" => [resolution,"handler.ctor","handler"],
                        "borrowed" => [resolution,"factory.ctor","factory","handler.ctor","handler"],
                        _ => [resolution,"factory.ctor","factory","handler.ctor","handler","handler.dispose"] }
                };
                Assert.Equal(expected,probe.Trace);
                Exception? escaping=scenario switch {
                    "di-error"=>diError,"factory-error"=>factoryError,"factory-oce"=>factoryOce,
                    "constructor-error"=>constructorError,
                    "handler-error"=>handlerError,"handler-oce"=>handlerOce,
                    "cleanup-error-success" or "cleanup-error-over-body" or "cleanup-error-over-oce"=>cleanupError,
                    "cleanup-oce-success" or "cleanup-oce-over-body" or "cleanup-oce-over-oce"=>cleanupOce,_=>null };
                if (escaping!=null) Assert.Same(escaping,actual.Error);
                else if (scenario=="null-request") Assert.Equal("request",Assert.IsType<ArgumentNullException>(actual.Error).ParamName);
                else if (scenario=="pre-cancel") Assert.Equal(probe.Cancellation.Token,Assert.IsType<OperationCanceledException>(actual.Error).CancellationToken);
                else if (scenario is "di-null" or "factory-null") Assert.IsType<InvalidOperationException>(actual.Error);
                else { Assert.Null(actual.Error); Assert.Equal(isVoid?null:(object)73,actual.Result); }
                if (escaping is OperationCanceledException oce) Assert.Equal(oce.CancellationToken,Assert.IsType<OperationCanceledException>(actual.Error).CancellationToken);
                Assert.All(probe.Calls,call=> { Assert.Same(request,call.Request); Assert.Equal(probe.Cancellation.Token,call.Token); });
                foreach (var call in probe.Calls.Where(c=>c.Phase=="handler")) Assert.Same(Assert.Single(probe.Created),call.Receiver);
                var handlerRan=expected.Contains("handler"); var factoryRan=expected.Contains("factory");
                Assert.Equal(receiver=="owned"&&handlerRan?1:0,probe.Released.Count);
                if (probe.Released.Count!=0) Assert.Same(Assert.Single(probe.Created),probe.Released[0]);
                Assert.Equal(0,probe.FactoryDisposals);
                Assert.Equal(receiver=="owned"&&handlerRan?"cleanup":handlerRan?"handler":factoryRan?"factory":"caller",CanonicalSyncRuntimeProbe.Ambient.Value);
                Assert.Equal(factoryRan ? (handlerRan ? (receiver=="owned" ? new[] { "caller","factory","handler" } : new[] { "caller","factory" }) : new[] { "caller" }) :
                    handlerRan ? new[] { "caller" } : [],probe.AmbientObserved);
                Assert.Equal(factoryRan ? (handlerRan ? (receiver=="owned" ? new[] { "","fr-FR","es-ES" } : new[] { "","fr-FR" }) : new[] { "" }) :
                    handlerRan ? new[] { "" } : [],probe.CultureObserved);
                Assert.Equal(receiver=="owned"&&handlerRan?"en-US":handlerRan?"es-ES":factoryRan?"fr-FR":"",CultureInfo.CurrentCulture.Name);
            }
            finally { scope.Dispose(); }
            // The real DI scope owns directly resolved services and factories, but never the returned session.
            Assert.Equal(receiver=="direct"?probe.Created.Count:receiver=="owned"?probe.Created.Count:0,probe.Released.Count);
            Assert.Equal(receiver=="direct"||scenario=="constructor-error"?0:probe.Trace.Contains("factory.ctor")?1:0,probe.FactoryDisposals);
        }
        finally { CanonicalSyncRuntimeProbe.Ambient.Value=originalAmbient; CultureInfo.CurrentCulture=originalCulture; probe.Cancellation.Dispose(); }
    }

    [Theory]
    [InlineData("direct",ServiceLifetime.Transient)] [InlineData("direct",ServiceLifetime.Scoped)]
    [InlineData("borrowed",ServiceLifetime.Transient)] [InlineData("borrowed",ServiceLifetime.Scoped)]
    [InlineData("owned",ServiceLifetime.Transient)] [InlineData("owned",ServiceLifetime.Scoped)]
    public void Generated_SendSync_resolves_each_operation_preserves_overrides_and_leaves_DI_cleanup_to_scope(string receiver,ServiceLifetime lifetime)
    {
        var f=SyncRuntimeFixture(receiver,false); var probe=new CanonicalSyncRuntimeProbe();
        var ambient=CanonicalSyncRuntimeProbe.Ambient.Value;
        var culture=CultureInfo.CurrentCulture;
        try
        {
            using var provider=SyncProvider(f,probe,recording:false,lifetime); using (var scope=provider.CreateScope())
            {
                var mediator=scope.ServiceProvider.GetRequiredService(f.Interface);
                for (var i=0;i<2;i++) { var actual=InvokeSync(f,mediator,Activator.CreateInstance(f.Request),default); Assert.Null(actual.Error); Assert.Equal(73,actual.Result); }
                Assert.Equal(receiver=="direct"&&lifetime==ServiceLifetime.Scoped?1:2,probe.Created.Count);
                Assert.Equal(receiver=="owned"?2:0,probe.Released.Count); Assert.Equal(0,probe.FactoryDisposals);
                if (receiver!="direct") Assert.Equal(lifetime==ServiceLifetime.Scoped?1:2,probe.Trace.Count(t=>t=="factory.ctor"));
                var receivers=probe.Calls.Where(c=>c.Phase=="handler").Select(c=>c.Receiver).ToArray();
                if (receiver=="direct"&&lifetime==ServiceLifetime.Scoped) Assert.Same(receivers[0],receivers[1]); else Assert.NotSame(receivers[0],receivers[1]);
            }
            Assert.Equal(receiver=="borrowed"?0:probe.Created.Count,probe.Released.Count);
            Assert.Equal(receiver=="direct"?0:lifetime==ServiceLifetime.Scoped?1:2,probe.FactoryDisposals);
        }
        finally { CanonicalSyncRuntimeProbe.Ambient.Value=ambient; CultureInfo.CurrentCulture=culture; probe.Cancellation.Dispose(); }
    }

    [Fact]
    public void Generated_Owned_SendSync_reentry_uses_distinct_sessions_and_cleans_inner_before_outer()
    {
        var f=SyncRuntimeFixture("owned",false); var probe=new CanonicalSyncRuntimeProbe(); var ambient=CanonicalSyncRuntimeProbe.Ambient.Value;
        var culture=CultureInfo.CurrentCulture;
        try
        {
            using var provider=SyncProvider(f,probe); using var scope=provider.CreateScope(); var mediator=scope.ServiceProvider.GetRequiredService(f.Interface);
            var outer=Activator.CreateInstance(f.Request); var inner=Activator.CreateInstance(f.Request);
            probe.DuringHandler=()=> { probe.DuringHandler=null; var actual=InvokeSync(f,mediator,inner,default); Assert.Null(actual.Error); Assert.Equal(73,actual.Result); };
            var result=InvokeSync(f,mediator,outer,default); Assert.Null(result.Error); Assert.Equal(73,result.Result);
            Assert.Equal(new[] { "resolve-factory","factory.ctor","factory","handler.ctor","handler",
                "factory","handler.ctor","handler","handler.dispose","handler.dispose" },probe.Trace);
            Assert.NotSame(probe.Created[0],probe.Created[1]); Assert.Equal(new[] { probe.Created[1],probe.Created[0] },probe.Released);
            Assert.Equal(new[] { outer,outer,inner,inner },probe.Calls.Select(c=>c.Request));
        }
        finally { CanonicalSyncRuntimeProbe.Ambient.Value=ambient; CultureInfo.CurrentCulture=culture; probe.Cancellation.Dispose(); }
    }

    [Theory]
    [InlineData(true,false,true)] [InlineData(false,true,false)] [InlineData(true,true,true)]
    public void Generated_SendSync_connects_static_or_default_registration_and_empty_pipeline(bool named,bool extension,bool emptyPipeline)
    {
        var inferred=emptyPipeline&&extension ? SyncRuntimeSource("owned",true,named,extension,emptyPipeline).Replace("c.Pipeline<Ping>(DispatchFamily.SendSync)","c.Pipeline<Ping>()") : null;
        var f=CompileSyncFixture("owned",true,named,extension,emptyPipeline,sourceOverride:inferred); var probe=new CanonicalSyncRuntimeProbe(); var ambient=CanonicalSyncRuntimeProbe.Ambient.Value;
        var culture=CultureInfo.CurrentCulture;
        try { using var provider=SyncProvider(f,probe); using var scope=provider.CreateScope(); var actual=InvokeSync(f,scope.ServiceProvider.GetRequiredService(f.Interface),Activator.CreateInstance(f.Request),default);
            Assert.Null(actual.Error); Assert.Null(actual.Result); Assert.Single(probe.Released); }
        finally { CanonicalSyncRuntimeProbe.Ambient.Value=ambient; CultureInfo.CurrentCulture=culture; probe.Cancellation.Dispose(); }
    }

    [Fact]
    public void Generated_SendSync_preserves_symbol_names_for_keyword_namespaces_and_closed_generic_response()
    {
        var source=SyncRuntimeSource("owned",false).Replace("namespace App {","namespace @event.@class { public sealed record Response<T>(int Value);")
            .Replace("global::App.Generated.Reads","global::@event.@class.Generated.Reads")
            .Replace("ISyncRequest<int>","ISyncRequest<Response<string>>").Replace("ISyncRequestHandler<Ping,int>","ISyncRequestHandler<Ping,Response<string>>")
            .Replace("int ISyncRequestHandler","Response<string> ISyncRequestHandler").Replace("return _probe.Handle(this,request,token);","return new(_probe.Handle(this,request,token));");
        var f=CompileSyncFixture("owned",false,sourceOverride:source,applicationNamespace:"event.class");
        var probe=new CanonicalSyncRuntimeProbe(); var ambient=CanonicalSyncRuntimeProbe.Ambient.Value; var culture=CultureInfo.CurrentCulture;
        try { using var provider=SyncProvider(f,probe); using var scope=provider.CreateScope(); var actual=InvokeSync(f,scope.ServiceProvider.GetRequiredService(f.Interface),Activator.CreateInstance(f.Request),default);
            Assert.Null(actual.Error); Assert.Equal(73,actual.Result!.GetType().GetProperty("Value")!.GetValue(actual.Result)); Assert.Single(probe.Released); }
        finally { CanonicalSyncRuntimeProbe.Ambient.Value=ambient; CultureInfo.CurrentCulture=culture; probe.Cancellation.Dispose(); }
    }

    [Fact]
    public void Generated_SendSync_registrar_rejects_changed_structure_before_mutating_services()
    {
        var f=SyncRuntimeFixture("owned",false); var config=new global::Zendiator.DependencyInjection.ZendiatorConfiguration();
        var method=typeof(global::Zendiator.DependencyInjection.ZendiatorConfiguration).GetMethods().Single(m=>m.Name=="Handlers"&&m.GetParameters().Length==2);
        method.MakeGenericMethod(f.Request).Invoke(config,[DispatchFamily.SendSync,new[] { f.Service }]);
        var services=new ServiceCollection(); var registrar=f.Assembly.GetType("App.Generated.Reads.ZendiatorGeneratedRegistrar")!.GetMethod("Add",BindingFlags.Static|BindingFlags.NonPublic)!;
        var error=Assert.Throws<TargetInvocationException>(()=>registrar.Invoke(null,[services,config.Snapshot()]));
        Assert.IsType<InvalidOperationException>(error.InnerException); Assert.Empty(services);
    }

    [Fact]
    public void Generated_SendSync_body_and_location_edits_cache_callable_source_but_factory_ownership_invalidates_it()
    {
        var source=SyncRuntimeSource("owned",false);
        var driver=SyncDriver().RunGenerators(SyncCompilation(source,"CanonicalSyncCache"));
        var original=driver.GetRunResult().Results.Single().GeneratedSources.Single(s=>s.HintName=="Zendiator.Canonical.SendSync.g.cs").SourceText.ToString();
        driver=driver.RunGenerators(SyncCompilation("\n"+source.Replace("return _probe.Handle(this,request,token);","return _probe.Handle(this,request,token)+1;"),"CanonicalSyncCache"));
        Assert.Equal(original,driver.GetRunResult().Results.Single().GeneratedSources.Single(s=>s.HintName=="Zendiator.Canonical.SendSync.g.cs").SourceText.ToString());
        Assert.All(driver.GetRunResult().Results.Single().TrackedSteps["CanonicalSyncSourceEmission"].SelectMany(s=>s.Outputs),o=>Assert.Equal(IncrementalStepRunReason.Cached,o.Reason));
        Assert.All(driver.GetRunResult().Results.Single().TrackedSteps["CanonicalSyncInterceptorsEmission"].SelectMany(s=>s.Outputs),o=>Assert.Equal(IncrementalStepRunReason.Modified,o.Reason));
        driver=driver.RunGenerators(SyncCompilation(source.Replace("IOwnedSessionFactory","IBorrowedSessionFactory"),"CanonicalSyncCache"));
        Assert.NotEqual(original,driver.GetRunResult().Results.Single().GeneratedSources.Single(s=>s.HintName=="Zendiator.Canonical.SendSync.g.cs").SourceText.ToString());
        Assert.All(driver.GetRunResult().Results.Single().TrackedSteps["CanonicalSyncSourceEmission"].SelectMany(s=>s.Outputs),o=>Assert.Equal(IncrementalStepRunReason.Modified,o.Reason));
    }

    [Theory]
    [InlineData("struct-request")] [InlineData("struct-handler")] [InlineData("implicit-handler")]
    [InlineData("stage-wrapper")] [InlineData("array-result")] [InlineData("namespace-collision")]
    [InlineData("nested-marker")] [InlineData("struct-factory")] [InlineData("multiple-routes")]
    [InlineData("ancestor-generated-type")] [InlineData("ancestor-marker-type")] [InlineData("default-namespace-type")]
    [InlineData("all-sync-family")] [InlineData("all-async-family")]
    public void SendSync_shape_boundary_candidates_preserve_eligibility_and_contracts(string shape)
    {
        var source=SyncRuntimeSource(shape=="struct-factory"?"borrowed":"direct",false,named:shape!="default-namespace-type",dispatch:false);
        source=shape switch {
            "struct-request"=>source.Replace("sealed record Ping","readonly record struct Ping"),
            "struct-handler"=>source.Replace("sealed partial class Handler<T>","partial struct Handler<T>"),
            "implicit-handler"=>source.Replace("c => c.Handlers<Ping>(DispatchFamily.SendSync,typeof(Handler<string>))","c => { c.AddHandler<Handler<string>>(); c.Pipeline<Ping>(DispatchFamily.SendSync); }"),
            "stage-wrapper"=>source.Replace("c => c.Handlers<Ping>(DispatchFamily.SendSync,typeof(Handler<string>))",
                "c => { c.Handlers<Ping>(DispatchFamily.SendSync,typeof(Handler<string>)); c.Pipeline<Ping>(DispatchFamily.SendSync,Stage.Hook<Hook>(),Stage.Wrapper<global::Zendiator.Generator.Tests.MetadataSyncWrapper<Ping>>()); }")+"namespace App { public sealed class Hook : ISendFinallyObserver<Ping> { public void Finally(Ping r,SendExitInfo e,CancellationToken c){} } }",
            "all-sync-family"=>source.Replace("DispatchFamily.SendSync","DispatchFamily.SendAllSync"),
            "all-async-family"=>source.Replace("DispatchFamily.SendSync","DispatchFamily.SendAllAsync").Replace("ISyncRequest","IRequest")
                .Replace("int IRequestHandler<Ping,int>.Handle(","System.Threading.Tasks.ValueTask<int> IRequestHandler<Ping,int>.HandleAsync(")
                .Replace("return _probe.Handle(this,request,token);","return new(_probe.Handle(this,request,token));"),
            "array-result"=>source.Replace("ISyncRequest<int>","ISyncRequest<int[]>").Replace("ISyncRequestHandler<Ping,int>","ISyncRequestHandler<Ping,int[]>")
                .Replace("int ISyncRequestHandler", "int[] ISyncRequestHandler").Replace("return _probe.Handle(this,request,token);","return new[] { _probe.Handle(this,request,token) };"),
            "namespace-collision"=>source+"namespace App.Generated.Reads { public sealed class Zendiator {} }",
            "ancestor-generated-type"=>source+"namespace App { public sealed class Generated {} }",
            "ancestor-marker-type"=>source+"namespace App.Generated { public sealed class Reads {} }",
            "default-namespace-type"=>source+"namespace Zendiator.Generated { public sealed class Default {} }",
            "nested-marker"=>source.Replace("[ZendiatorComposition(\"reads\")] public sealed class Reads;",
                "public static class Holder { [ZendiatorComposition(\"reads\")] public sealed class Reads; }").Replace("AddZendiator<Reads>","AddZendiator<Holder.Reads>"),
            "struct-factory"=>source.Replace("public sealed class Factory<T> : FactoryBase<T> where T:class { public Factory(Probe probe):base(probe){} }",
                "public struct Factory<T> : IBorrowedSessionFactory<Ping,Handler<T>> where T:class { private readonly Probe _probe; public Factory(Probe probe) { _probe=probe; } Handler<T> IBorrowedSessionFactory<Ping,Handler<T>>.Create(Ping r,CancellationToken c)=>new(_probe); }"),
            "multiple-routes"=>source.Replace("c => c.Handlers<Ping>(DispatchFamily.SendSync,typeof(Handler<string>))",
                "c => { c.Handlers<Ping>(DispatchFamily.SendSync,typeof(Handler<string>)); c.Handlers<Other>(DispatchFamily.SendSync,typeof(OtherHandler)); }")+
                "namespace App { public sealed record Other : ISyncRequest<int>; [ZendiatorHandler] public sealed class OtherHandler : ISyncRequestHandler<Other,int> { public int Handle(Other r,CancellationToken c)=>1; } }",_=>source };
        var driver=SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source,"CanonicalSyncUnsupported_"+shape),out var output,out var diagnostics);
        if(shape is "struct-request" or "array-result" or "stage-wrapper"){
            Assert.Empty(diagnostics);Assert.Empty(output.GetDiagnostics().Where(d=>d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));Assert.Contains(driver.GetRunResult().Results.Single().GeneratedSources,s=>s.HintName==(shape=="stage-wrapper"?"Zendiator.Canonical.Wrappers.g.cs":"Zendiator.Canonical.SendSync.g.cs"));return;
        }
        Assert.Equal(shape=="multiple-routes"?2:1,diagnostics.Length); Assert.All(diagnostics,d=>Assert.Equal("ZEN0037",d.Id));
        Assert.Empty(output.GetDiagnostics().Where(d=>d.Severity==DiagnosticSeverity.Error));
        Assert.DoesNotContain(driver.GetRunResult().Results.Single().GeneratedSources,s=>s.HintName.StartsWith("Zendiator.Canonical",StringComparison.Ordinal));
    }

    private sealed class CustomSyncCollection : System.Collections.ObjectModel.Collection<ServiceDescriptor>,IServiceCollection;

    [Theory]
    [InlineData("collection")] [InlineData("provider")]
    public void Enabled_SendSync_rejects_custom_container_or_candidate_only_provider_decorator(string boundary)
    {
        var f=SyncRuntimeFixture("owned",false); var probe=new CanonicalSyncRuntimeProbe();
        if (boundary=="collection")
        {
            var collection=new CustomSyncCollection();
            var error=Assert.Throws<TargetInvocationException>(()=>f.Register.Invoke(null,[collection]));
            Assert.IsType<NotSupportedException>(error.InnerException); Assert.Empty(collection);
        }
        else
        {
            using var provider=new ServiceCollection().BuildServiceProvider();
            var error=Assert.Throws<TargetInvocationException>(()=>Activator.CreateInstance(f.Mediator,new RecordingSyncProvider(provider,probe,f.Service)));
            Assert.IsType<NotSupportedException>(error.InnerException);
        }
        Assert.Empty(probe.Trace); probe.Cancellation.Dispose();
    }

    [Fact]
    public void Enabled_SendSync_keeps_diagnostic_for_unsupported_ServiceLifetime_configuration_syntax()
    {
        var source=SyncRuntimeSource("direct",false,dispatch:false).Replace(
            "c => c.Handlers<Ping>(DispatchFamily.SendSync,typeof(Handler<string>))",
            "c => { c.ServiceLifetime=ServiceLifetime.Singleton; c.Handlers<Ping>(DispatchFamily.SendSync,typeof(Handler<string>)); }");
        var driver=SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source,"UnsupportedSingletonConfig"),out var output,out var diagnostics);
        Assert.Equal("ZEN0035",Assert.Single(diagnostics).Id);
        Assert.Empty(output.GetDiagnostics().Where(d=>d.Severity==DiagnosticSeverity.Error));
        Assert.DoesNotContain(driver.GetRunResult().Results.Single().GeneratedSources,s=>s.HintName.StartsWith("Zendiator.Canonical",StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Enabled_SendSync_missing_or_source_homonym_Microsoft_DI_implementation_keeps_ZEN0037(bool homonym)
    {
        var source=SyncRuntimeSource("direct",false,dispatch:false);
        if (homonym) source+="namespace Microsoft.Extensions.DependencyInjection { public sealed class ServiceProvider {} }";
        var compilation=SyncCompilation(source,"UnsupportedProviderReference");
        if (!homonym) compilation=compilation.WithReferences(compilation.References.Where(r=>!string.Equals(Path.GetFileName(r.Display),"Microsoft.Extensions.DependencyInjection.dll",StringComparison.OrdinalIgnoreCase)));
        var driver=SyncDriver().RunGeneratorsAndUpdateCompilation(compilation,out var output,out var diagnostics);
        Assert.Equal("ZEN0037",Assert.Single(diagnostics).Id);
        Assert.Empty(output.GetDiagnostics().Where(d=>d.Severity==DiagnosticSeverity.Error));
        Assert.DoesNotContain(driver.GetRunResult().Results.Single().GeneratedSources,s=>s.HintName.StartsWith("Zendiator.Canonical",StringComparison.Ordinal));
    }
}
