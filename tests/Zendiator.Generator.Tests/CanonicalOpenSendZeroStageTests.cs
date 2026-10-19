using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Xunit;
using Zendiator.SourceGenerator;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private const string OpenSendZeroCommon = """
        using System;using System.Collections.Generic;using System.Linq;using System.Threading;using System.Threading.Tasks;using System.Threading.Tasks.Sources;using System.Text.Json;
        using Microsoft.Extensions.DependencyInjection;using Zendiator;using Zendiator.DependencyInjection;
        namespace OpenZero {
        public interface ILeft;public interface IRight;
        public sealed class Left:ILeft;public sealed class Right:IRight;public sealed class OtherLeft:ILeft;
        public sealed class Shared {public int Value=3;}
        public sealed class Once:IValueTaskSource<int>,IValueTaskSource {
            private ManualResetValueTaskSourceCore<int> core;public int Consumptions;
            public Once(){core.RunContinuationsAsynchronously=true;}
            public ValueTask<int> Result=>new(this,core.Version);public ValueTask Void=>new(this,core.Version);
            public void Complete()=>core.SetResult(42);public void Fail(Exception error)=>core.SetException(error);
            public int GetResult(short token){Probe.Check(++Consumptions==1,"single source consumption");return core.GetResult(token);}
            void IValueTaskSource.GetResult(short token){_=GetResult(token);}
            public ValueTaskSourceStatus GetStatus(short token)=>core.GetStatus(token);
            public void OnCompleted(Action<object?> continuation,object? state,short token,ValueTaskSourceOnCompletedFlags flags)=>core.OnCompleted(continuation,state,token,flags);
        }
        public sealed class Probe {
            public static readonly AsyncLocal<string?> Ambient=new();
            public readonly CancellationTokenSource Cancellation=new();public readonly string Mode;public readonly Exception Failure;
            public object Original=null!;public Shared Data=null!;public Once? Source;public int Calls;
            public readonly List<object> Created=new(),Disposed=new();public readonly List<string> Trace=new();
            public Probe(string mode){Mode=mode;Failure=mode=="pending-cancel"?new OperationCanceledException("handler cancellation",Cancellation.Token):new FormatException("handler failure");}
            public static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
            public void Resolve(object receiver){Created.Add(receiver);Trace.Add("resolve:"+Ambient.Value);Ambient.Value="resolve";}
            public void Hit(object request,Shared data,CancellationToken token){
                Check(ReferenceEquals(request,Original)&&ReferenceEquals(data,Data)&&token==Cancellation.Token,"original request/shared/token");
                Check(++Calls==1,"single handler invocation");Trace.Add("handle:"+Ambient.Value);Ambient.Value="handler";data.Value++;
                if(Mode=="throw")throw Failure;
            }
            public Once Start(){var source=new Once();Source=source;if(Mode=="immediate")source.Complete();return source;}
            public void Dispose(object receiver){Disposed.Add(receiver);Trace.Add("dispose");}
        }
        }
        """;

    private static string OpenSendZeroDefinition(string profile)
    {
        var async = profile.StartsWith('A'); var isVoid = profile.EndsWith('V');
        var family = async ? "SendAsync" : "SendSync";
        var method = async ? "HandleAsync" : "Handle";
        var request = async ? isVoid ? "IRequest" : "IRequest<int>" : isVoid ? "ISyncRequest" : "ISyncRequest<int>";
        var contract = (async ? "IRequestHandler" : "ISyncRequestHandler") + "<Query<Y,X>" + (isVoid ? ">" : ",int>");
        var result = async ? isVoid ? "ValueTask" : "ValueTask<int>" : isVoid ? "void" : "int";
        var body = async ? "return p.Start()." + (isVoid ? "Void;" : "Result;") : isVoid ? "" : "return r.Value+35;";
        return $$"""
            namespace OpenZero.{{profile}} {
            public sealed record Query<L,R>(int Value,Shared Shared):{{request}};
            [ZendiatorHandler]public sealed class Handler<X,Y>:{{contract}},IDisposable where X:class,IRight,new() where Y:class,ILeft,new(){
                private readonly Probe p;public Handler(Probe p){this.p=p;p.Resolve(this);}
                public {{result}} {{method}}(Query<Y,X> r,CancellationToken token){p.Hit(r,r.Shared,token);{{body}}}
                public void Dispose()=>p.Dispose(this);
            }
            [ZendiatorComposition("openZero-{{profile}}-omitted")]public sealed class Omitted;
            [ZendiatorComposition("openZero-{{profile}}-empty")]public sealed class Empty;
            public static class Register {
                public static IServiceCollection Omitted(IServiceCollection s)=>s.AddZendiator<Omitted>(c=>{
                    c.Handlers(typeof(Query<,>),DispatchFamily.{{family}},typeof(Handler<,>));
                });
                public static IServiceCollection Empty(IServiceCollection s)=>s.AddZendiator<Empty>(c=>{
                    c.Handlers(typeof(Query<,>),DispatchFamily.{{family}},typeof(Handler<,>));
                    c.Pipeline(typeof(Query<,>),DispatchFamily.{{family}});
                });
            }
            }
            """;
    }

    private static string OpenSendZeroConsumer(string profile)
    {
        var async = profile.StartsWith('A'); var isVoid = profile.EndsWith('V');
        var family = async ? "SendAsync" : "SendSync";
        var methodName = async ? "SendAsync" : "Send";
        var result = async ? isVoid ? "ValueTask" : "ValueTask<int>" : isVoid ? "void" : "int";
        var invocation = "p.GetRequiredService<Handler<R,L>>()." + (async ? "HandleAsync" : "Handle") + "(r,token)";
        var call = async ? $$"""
            {{result}} operation=default;var returned=false;var pending=false;var initialFaulted=false;var initialCanceled=false;
            try{operation=generated?Dispatch(sp,empty,request,token):Reference(sp,request,token);returned=true;pending=!operation.IsCompleted;initialFaulted=operation.IsFaulted;initialCanceled=operation.IsCanceled;}
            catch(Exception error){caught=error;phase="call";}
            Probe.Check(Probe.Ambient.Value=="caller","async call context isolation");
            if(returned){
                if(mode.StartsWith("pending",StringComparison.Ordinal)){
                    Probe.Check(pending&&p.Source!=null&&p.Source.Consumptions==0,"genuine pending single-use source");
                    if(mode=="pending")p.Source!.Complete();else{if(mode=="pending-cancel")p.Cancellation.Cancel();p.Source!.Fail(p.Failure);}
                }
                try{ {{(isVoid ? "" : "value=")}}await operation.ConfigureAwait(false); }catch(Exception error){caught=error;phase="await";}
                Probe.Check(operation.IsFaulted==(mode is "throw" or "pending-fault")&&operation.IsCanceled==(mode is "entry-cancel" or "pending-cancel"),"returned terminal status");
            }
            Probe.Check(returned==(mode!="null")&&pending==mode.StartsWith("pending",StringComparison.Ordinal),"call completion boundary");
            Probe.Check(initialFaulted==(mode=="throw")&&initialCanceled==(mode=="entry-cancel"),"initial returned status");
            Probe.Check(Probe.Ambient.Value=="caller","async await context isolation");
            Probe.Check((p.Source?.Consumptions??0)==(mode is "immediate" or "pending" or "pending-fault" or "pending-cancel"?1:0),"exact ValueTask source consumption");
            """ : $$"""
            var returned=false;var pending=false;var initialFaulted=false;var initialCanceled=false;
            try{ {{(isVoid ? "" : "value=")}} {{(isVoid ? "DispatchOrReference(generated,sp,empty,request,token)" : "(generated?Dispatch(sp,empty,request,token):Reference(sp,request,token))")}};returned=true;}
            catch(Exception error){caught=error;phase="call";}
            Probe.Check(Probe.Ambient.Value==(mode is "entry-cancel" or "null"?"caller":"handler"),"native synchronous caller context");
            """;
        var reference = async ? $$"""
            private static {{result}} Reference<L,R>(IServiceProvider p,Query<L,R> r,CancellationToken token) where L:class,ILeft,new() where R:class,IRight,new(){
                ArgumentNullException.ThrowIfNull(r);return Core(p,r,token);
            }
            private static async {{result}} Core<L,R>(IServiceProvider p,Query<L,R> r,CancellationToken token) where L:class,ILeft,new() where R:class,IRight,new(){
                token.ThrowIfCancellationRequested();{{(isVoid ? "" : "return ")}}await {{invocation}}.ConfigureAwait(false);
            }
            """ : $$"""
            private static {{result}} Reference<L,R>(IServiceProvider p,Query<L,R> r,CancellationToken token) where L:class,ILeft,new() where R:class,IRight,new(){
                ArgumentNullException.ThrowIfNull(r);token.ThrowIfCancellationRequested();{{(isVoid ? "" : "return ")}}{{invocation}};
            }
            """;
        return $$"""
            namespace OpenZero.{{profile}} {
            public static class Entry {
                {{reference}}
                private static {{result}} Dispatch<L,R>(IServiceProvider p,bool empty,Query<L,R> r,CancellationToken token) where L:class,ILeft,new() where R:class,IRight,new(){
                    if(empty){ {{(isVoid && !async ? "" : "return ")}}p.GetRequiredService<Generated.Empty.IZendiator>().{{methodName}}(r,token); }
                    else{ {{(isVoid && !async ? "" : "return ")}}p.GetRequiredService<Generated.Omitted.IZendiator>().{{methodName}}(r,token); }
                }
                {{(isVoid && !async ? "private static void DispatchOrReference(bool generated,IServiceProvider p,bool empty,Query<Left,Right> r,CancellationToken token){if(generated)Dispatch(p,empty,r,token);else Reference(p,r,token);}" : "")}}
                public static {{result}} OtherTypeCompileSentinel(Generated.Omitted.IZendiator m){{(isVoid && !async ? "{m.Send(new Query<OtherLeft,Right>(7,new Shared()));}" : "=>m." + methodName + "(new Query<OtherLeft,Right>(7,new Shared()));")}}
                public static {{(async ? "async Task<string>" : "string")}} Observe(bool generated,bool empty,string mode){
                    var p=new Probe(mode);using var cancellation=p.Cancellation;IServiceCollection services=new ServiceCollection();services.AddSingleton(p);
                    services.AddScoped(typeof(Handler<,>));var existing=services.Last();
                    if(empty)Register.Empty(services);else Register.Omitted(services);var count=services.Count;
                    if(empty)Register.Empty(services);else Register.Omitted(services);
                    Probe.Check(count==4&&services.Count==count&&services.Contains(existing),"TryAdd/repeat registration");
                    var facadeType=empty?typeof(Generated.Empty.IZendiator):typeof(Generated.Omitted.IZendiator);
                    var concreteType=empty?typeof(Generated.Empty.Zendiator):typeof(Generated.Omitted.Zendiator);
                    foreach(var type in new[]{typeof(Handler<,>),facadeType,concreteType})Probe.Check(services.Single(d=>d.ServiceType==type).Lifetime==ServiceLifetime.Scoped,"scoped services");
                    var opposite=new ZendiatorConfiguration();opposite.Handlers(typeof(Query<,>),DispatchFamily.{{family}},typeof(Handler<,>));
                    if(!empty)opposite.Pipeline(typeof(Query<,>),DispatchFamily.{{family}});
                    IServiceCollection rejected=new ServiceCollection();var mismatchRejected=false;
                    try{if(empty)Generated.Empty.ZendiatorGeneratedRegistrar.Add(rejected,opposite.Snapshot());else Generated.Omitted.ZendiatorGeneratedRegistrar.Add(rejected,opposite.Snapshot());}catch(InvalidOperationException){mismatchRejected=true;}
                    Probe.Check(mismatchRejected&&rejected.Count==0,"omitted/empty snapshot before descriptor addition");
                    using var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});using var scope=provider.CreateScope();var sp=scope.ServiceProvider;
                    Probe.Check(ReferenceEquals(sp.GetRequiredService(facadeType),sp.GetRequiredService(concreteType)),"one facade");
                    var data=new Shared();var request=new Query<Left,Right>(7,data);p.Original=request;p.Data=data;
                    if(mode is "entry-cancel" or "null")cancellation.Cancel();if(mode=="null")request=null!;
                    var token=cancellation.Token;Probe.Ambient.Value="caller";Exception? caught=null;string? phase=null;int? value=null;
                    {{call}}
                    var entered=mode is not ("entry-cancel" or "null");var failed=mode is "entry-cancel" or "null" or "throw" or "pending-fault" or "pending-cancel";
                    Probe.Check(p.Calls==(entered?1:0)&&data.Value==(entered?4:3),"entry guard/single call/shared effect");
                    Probe.Check(failed?caught!=null:caught==null,"completion outcome");
                    if(mode=="null")Probe.Check(caught is ArgumentNullException&&phase=="call","public null guard precedes cancellation");
                    else if(mode=="entry-cancel")Probe.Check(caught is OperationCanceledException canceled&&canceled.CancellationToken==token&&phase=="{{(async ? "await" : "call")}}","entry cancellation boundary/token");
                    else if(failed)Probe.Check(ReferenceEquals(caught,p.Failure)&&phase=="{{(async ? "await" : "call")}}","handler failure identity/boundary");
                    if(mode=="pending-cancel")Probe.Check(caught is OperationCanceledException canceled&&canceled.CancellationToken==token&&cancellation.IsCancellationRequested,"handler cancellation identity/token");
                    {{(isVoid ? "Probe.Check(value==null,\"native void has no response\");" : "if(!failed)Probe.Check(value==42,\"response value\");")}}
                    Probe.Check(p.Created.Count==(entered?1:0)&&p.Trace.SequenceEqual(entered?new[]{"resolve:caller","handle:resolve"}:Array.Empty<string>()),"receiver ordering/no entry resolution");
                    if(entered)Probe.Check(p.Created[0].GetType()==typeof(Handler<Right,Left>)&&ReferenceEquals(p.Created[0],sp.GetRequiredService<Handler<Right,Left>>()),"request-driven inverted type arguments/scoped receiver");
                    Probe.Check(p.Disposed.Count==0,"borrowed receiver survives dispatch");scope.Dispose();
                    Probe.Check(p.Disposed.SequenceEqual(p.Created)&&p.Disposed.Distinct(ReferenceEqualityComparer.Instance).Count()==p.Created.Count,"exactly one scope cleanup");
                    return JsonSerializer.Serialize(new{profile="{{profile}}",empty,mode,p.Calls,p.Trace,shared=data.Value,value,phase,returned,pending,initialFaulted,initialCanceled,consumptions=p.Source?.Consumptions??0,callerContext=Probe.Ambient.Value,error=caught?.GetType().Name,created=p.Created.Select(r=>r.GetType().ToString()),disposed=p.Disposed.Select(r=>r.GetType().ToString()),registrations=count,mismatchRejected});
                }
            }
            }
            """;
    }

    private const string OpenSendZeroRunner = """
        namespace OpenZero {
        public static class Entry {
            private static void Add(List<object> rows,string reference,string generated){Probe.Check(reference==generated,"reference/generated mismatch\n"+reference+"\n"+generated);rows.Add(new{equal=true,reference=JsonDocument.Parse(reference).RootElement.Clone(),generated=JsonDocument.Parse(generated).RootElement.Clone()});}
            private static void Sync(List<object> rows,Func<bool,bool,string,string> observe,bool empty,string mode)=>Add(rows,observe(false,empty,mode),observe(true,empty,mode));
            private static async Task Async(List<object> rows,Func<bool,bool,string,Task<string>> observe,bool empty,string mode){var reference=await observe(false,empty,mode);var generated=await observe(true,empty,mode);Add(rows,reference,generated);}
            public static async Task<string> Run(){var rows=new List<object>();
                foreach(var empty in new[]{false,true}){Sync(rows,SR.Entry.Observe,empty,"success");Sync(rows,SV.Entry.Observe,empty,"success");await Async(rows,AR.Entry.Observe,empty,"immediate");await Async(rows,AV.Entry.Observe,empty,"immediate");}
                Sync(rows,SR.Entry.Observe,false,"entry-cancel");Sync(rows,SV.Entry.Observe,true,"throw");
                await Async(rows,AR.Entry.Observe,false,"pending");await Async(rows,AR.Entry.Observe,false,"throw");await Async(rows,AR.Entry.Observe,false,"pending-fault");
                await Async(rows,AV.Entry.Observe,true,"pending");await Async(rows,AV.Entry.Observe,true,"pending-cancel");await Async(rows,AV.Entry.Observe,true,"entry-cancel");
                Sync(rows,SR.Entry.Observe,false,"null");await Async(rows,AV.Entry.Observe,true,"null");
                return JsonSerializer.Serialize(new{pairs=rows.Count,dispatches=rows.Count*2,rows});
            }
        }
        }
        """;

    private static string? OpenSendZeroFolder(string name)
    {
        var root = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        if (string.IsNullOrEmpty(root)) return null;
        var path = Path.Combine(root, "open-send-zero-" + name); Directory.CreateDirectory(path); return path;
    }

    [Fact]
    public async Task OpenSend_zero_stage_preserves_public_registration_and_native_handler_boundaries()
    {
        var profiles = new[] { "SR", "SV", "AR", "AV" };
        var source = OpenSendZeroCommon + string.Concat(profiles.Select(p => OpenSendZeroDefinition(p) + OpenSendZeroConsumer(p))) + OpenSendZeroRunner;
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "OpenSendZero_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        var folder = OpenSendZeroFolder("runtime"); SaveSendAllSync(folder, source, driver, output, diagnostics);
        Assert.Empty(diagnostics); Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        var catalog = Assert.IsType<CompositionSelectionCatalog>(Assert.Single(driver.GetRunResult().Results.Single().TrackedSteps["CompositionSelectionModel"].SelectMany(s => s.Outputs)).Value);
        var execution = CanonicalExecutionGraphNormalizer.Create(catalog);
        var sync = CanonicalSyncEmissionNormalizer.Create(catalog, execution); var async = CanonicalAsyncEmissionNormalizer.Create(catalog, execution);
        Assert.Equal(4, sync.Compositions.Count); Assert.Equal(4, async.Compositions.Count);
        foreach (var profile in profiles)
        {
            var isAsync = profile.StartsWith('A'); var isVoid = profile.EndsWith('V'); var methodName = isAsync ? "SendAsync" : "Send";
            foreach (var form in new[] { "Omitted", "Empty" })
            {
                var ns = "OpenZero." + profile + ".Generated." + form;
                var composition = Assert.Single(catalog.Compositions, c => c.GeneratedNamespace == ns); var route = Assert.Single(composition.Pipelines);
                Assert.Equal(form == "Empty", route.HasEmptyPipeline); Assert.Empty(route.Stages); Assert.Single(route.Handlers);
                Assert.Empty(route.Handlers[0].Sessions); Assert.Equal(FusionOwnership.BorrowedService, route.Handlers[0].ServiceOwnership);
                Assert.Equal(isAsync ? BoundRequestShape.OpenAsyncReference : BoundRequestShape.OpenSyncReference, route.RequestShape);
                Assert.Equal(isVoid ? FusionResultKind.Void : FusionResultKind.Response, route.ResultKind);
                Assert.NotNull(route.SendGeneric); Assert.NotNull(route.SendOpenRegistration); Assert.Empty(route.SendOpenRegistration.Stages);
                CanonicalSyncCompositionEmission normalized;
                if (isAsync)
                {
                    var value = Assert.Single(async.Compositions, c => c.Namespace == ns); Assert.Null(value.Factory); Assert.Empty(value.Hooks);
                    normalized = CanonicalAsyncEmissionNormalizer.Registration(value);
                }
                else normalized = Assert.Single(sync.Compositions, c => c.Namespace == ns);
                Assert.Empty(normalized.Hooks); Assert.Null(normalized.Factory); Assert.Equal(route.SendGeneric, normalized.Generic); Assert.Equal(route.SendOpenRegistration, normalized.OpenRegistration);
                var api = output.GetTypeByMetadataName(ns + ".IZendiator")!; var mediator = output.GetTypeByMetadataName(ns + ".Zendiator")!;
                Assert.NotNull(api); Assert.False(api.IsGenericType); Assert.NotNull(mediator); Assert.False(mediator.IsGenericType);
                var publicMethod = Assert.Single(mediator.GetMembers(methodName).OfType<IMethodSymbol>()); Assert.False(publicMethod.IsAsync);
                var execute = Assert.Single(mediator.GetMembers().OfType<IMethodSymbol>(), m => m.Name.StartsWith(isAsync ? "ExecuteAsync__" : "Execute__", StringComparison.Ordinal)); Assert.Equal(isAsync, execute.IsAsync);
                foreach (var method in new[] { Assert.Single(api.GetMembers(methodName).OfType<IMethodSymbol>()), publicMethod, execute })
                {
                    Assert.Equal(2, method.Arity); Assert.Equal(2, method.Parameters.Length);
                    Assert.All(method.TypeParameters, p => { Assert.True(p.HasReferenceTypeConstraint); Assert.True(p.HasConstructorConstraint); });
                    Assert.Contains(method.TypeParameters[0].ConstraintTypes, t => t.Name == "IRight"); Assert.Contains(method.TypeParameters[1].ConstraintTypes, t => t.Name == "ILeft");
                    if (isAsync) { var result = Assert.IsAssignableFrom<INamedTypeSymbol>(method.ReturnType); Assert.Equal("ValueTask", result.Name); Assert.Equal(isVoid ? 0 : 1, result.Arity); }
                    else if (isVoid) Assert.True(method.ReturnsVoid); else Assert.Equal(SpecialType.System_Int32, method.ReturnType.SpecialType);
                }
            }
        }
        using var bytes = new MemoryStream(); var emit = output.Emit(bytes); Assert.True(emit.Success, string.Join("\n", emit.Diagnostics));
        if (folder != null) File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray()); bytes.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var runtime = await assembly.GetType("OpenZero.Entry")!.GetMethod("Run")!.CreateDelegate<Func<Task<string>>>()().WaitAsync(TimeSpan.FromSeconds(20));
        if (folder != null) File.WriteAllText(Path.Combine(folder, "runtime.json"), runtime);
        using var json = JsonDocument.Parse(runtime); Assert.Equal(18, json.RootElement.GetProperty("pairs").GetInt32()); Assert.Equal(36, json.RootElement.GetProperty("dispatches").GetInt32());
    }

    [Fact]
    public void OpenSend_zero_stage_rejects_unproved_scope_and_preserves_compiler_constraints()
    {
        var source = OpenSendZeroCommon + OpenSendZeroDefinition("SR") + """
            namespace OpenZero.SR {
            [ZendiatorHandler]public sealed class Missing<X,Y,Unused>:ISyncRequestHandler<Query<Y,X>,int>{public int Handle(Query<Y,X> r,CancellationToken token)=>0;}
            public sealed class Partial<X,Y>:ISendRequestTransform<Query<X,Y>>{public Query<X,Y> Before(Query<X,Y> r,CancellationToken token)=>r;}
            public sealed record OtherQuery<L,R>:ISyncRequest<int>;
            [ZendiatorHandler]public sealed class OtherHandler<L,R>:ISyncRequestHandler<OtherQuery<L,R>,int>{public int Handle(OtherQuery<L,R> r,CancellationToken token)=>0;}
            }
            """;
        const string handlers = "c.Handlers(typeof(Query<,>),DispatchFamily.SendSync,typeof(Handler<,>));";
        const string pipeline = "c.Pipeline(typeof(Query<,>),DispatchFamily.SendSync);";
        const string factory = "namespace OpenZero.SR {public sealed class Factory<L,R>:IBorrowedSessionFactory<Query<L,R>,Handler<R,L>> where L:class,ILeft,new() where R:class,IRight,new(){public Handler<R,L> Create(Query<L,R> r,CancellationToken token)=>throw new NotSupportedException();}}";
        foreach (var (name, invalid) in new[]
        {
            ("unbound-handler", source.Replace("typeof(Handler<,>)", "typeof(Missing<,,>)")),
            ("value-handler", source.Replace("public sealed class Handler<X,Y>", "public readonly struct Handler<X,Y>")),
            ("value-request", source.Replace("public sealed record Query<L,R>", "public readonly record struct Query<L,R>")),
            ("handler-session", source.Replace("typeof(Handler<,>)", "HandlerEntry.Session(typeof(Handler<,>),typeof(Factory<,>))") + factory),
            ("wrong-family", source.Replace("DispatchFamily.SendSync", "DispatchFamily.SendAsync")),
            ("partial-hook", source.Replace(pipeline, "").Replace(handlers, handlers + "c.Pipeline(typeof(Query<,>),DispatchFamily.SendSync,Stage.Hook(typeof(Partial<,>)));")),
            ("multiple-routes", source.Replace(handlers, handlers + "c.Handlers(typeof(OtherQuery<,>),DispatchFamily.SendSync,typeof(OtherHandler<,>));")),
        })
        {
            var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(invalid, "OpenSendZeroRejected_" + name + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
            SaveSendAllSync(OpenSendZeroFolder(name), invalid, driver, output, diagnostics);
            Assert.NotEmpty(diagnostics); Assert.All(diagnostics, d => Assert.Equal(name == "multiple-routes" ? "ZEN0037" : "ZEN0035", d.Id));
            Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
            foreach (var form in new[] { "Omitted", "Empty" }) { Assert.Null(output.GetTypeByMetadataName("OpenZero.SR.Generated." + form + ".IZendiator")); Assert.Null(output.GetTypeByMetadataName("OpenZero.SR.Generated." + form + ".ZendiatorGeneratedRegistrar")); }
        }
        var constraints = OpenSendZeroCommon + string.Concat(new[] { "SR", "SV", "AR", "AV" }.Select(OpenSendZeroDefinition)) + """
            namespace OpenZero {
            public sealed class NoDefault:ILeft {public NoDefault(int value){}}
            public static class Consumer {
                public static int SR(SR.Generated.Omitted.IZendiator m)=>m.Send(new SR.Query<NoDefault,Right>(7,new Shared()));
                public static void SV(SV.Generated.Empty.IZendiator m)=>m.Send(new SV.Query<NoDefault,Right>(7,new Shared()));
                public static ValueTask<int> AR(AR.Generated.Omitted.IZendiator m)=>m.SendAsync(new AR.Query<NoDefault,Right>(7,new Shared()));
                public static ValueTask AV(AV.Generated.Empty.IZendiator m)=>m.SendAsync(new AV.Query<NoDefault,Right>(7,new Shared()));
            }
            }
            """;
        var accepted = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(constraints, "OpenSendZeroCompiler_" + Guid.NewGuid().ToString("N")), out var compilerOutput, out var generatorDiagnostics);
        SaveSendAllSync(OpenSendZeroFolder("compiler-constraints"), constraints, accepted, compilerOutput, generatorDiagnostics); Assert.Empty(generatorDiagnostics);
        var errors = compilerOutput.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray(); Assert.Equal(4, errors.Length);
        Assert.All(errors, d => { Assert.Equal("CS0310", d.Id); Assert.Contains("NoDefault", d.GetMessage()); Assert.True(d.Location.IsInSource); Assert.Contains(constraints.Substring(d.Location.SourceSpan.Start, d.Location.SourceSpan.Length), new[] { "Send", "SendAsync" }); });
        foreach (var profile in new[] { "SR", "SV", "AR", "AV" }) foreach (var form in new[] { "Omitted", "Empty" }) Assert.NotNull(compilerOutput.GetTypeByMetadataName("OpenZero." + profile + ".Generated." + form + ".IZendiator"));
    }
}
