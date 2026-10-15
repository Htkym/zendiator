using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using Zendiator.SourceGenerator;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private const string TypedSendProposalContracts = """
        namespace Zendiator.SendTypeConnectionPrototype {
        // Proposed experiment protocol; not shipped in Zendiator.Abstractions or registered by AddZendiator.
        public interface IRoute<O,I,R,P,H,Q,S>{}
        public interface IRequestMap<O,I>{I Map(O request,CancellationToken token);}
        public interface IResponseMap<I,R,P>{P Map(I request,R response,CancellationToken token);}
        }
        """;

    private const string TypedSendConsumer = """
        namespace TypedFixture {
        public sealed record Outer(int Value):ISyncRequest<string>;
        public sealed record Inner(int Value):ISyncRequest<int>;
        public sealed record OtherInner:ISyncRequest<int>;
        public sealed class Probe {
            public string Mode="success";public readonly Exception Failure=new FormatException("sentinel");public CancellationToken Token;
            public Outer Outer=null!;public Inner? Inner;public int Calls;public readonly List<string> Created=new(),Disposed=new(),Trace=new();
            public static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
            public void Resolve(string role){Created.Add(role);Trace.Add("resolve:"+role);}
            public void Record(string phase,object request,bool outer,CancellationToken token){Check(token==Token&&ReferenceEquals(request,outer?Outer:Inner),"saved request and token identity");Trace.Add(phase+":"+(outer?"outer":"inner"));}
            public void Exit(string phase,object request,bool outer,SendExitInfo exit,CancellationToken token){Check(exit.Success==(Mode=="success")&&ReferenceEquals(exit.Error,Mode=="success"?null:Failure),"entered exit and exception identity");Record(phase+":"+exit.Success,request,outer,token);}
            public void Error(string phase,object request,bool outer,Exception e,CancellationToken token){Check(ReferenceEquals(e,Failure),"original exception identity");Record(phase,request,outer,token);}
        }
        [ZendiatorHandler]public sealed class Handler:ISyncRequestHandler<Inner,int>,IDisposable {
            private readonly Probe p;public Handler(Probe p){this.p=p;p.Resolve("H");}
            public int Handle(Inner r,CancellationToken t){p.Record("handler",r,false,t);p.Calls++;return r.Value*2;}
            public void Dispose(){p.Disposed.Add("H");}
        }
        public sealed class RequestMap:Zendiator.SendTypeConnectionPrototype.IRequestMap<Outer,Inner>,ISendExceptionObserver<Outer>,ISendFinallyObserver<Outer>,IDisposable {
            private readonly Probe p;public RequestMap(Probe p){this.p=p;p.Resolve("Q");}
            public Inner Map(Outer r,CancellationToken t){p.Record("request-map",r,true,t);if(p.Mode=="request")throw p.Failure;return p.Inner=new Inner(r.Value+1);}
            public void OnException(Outer r,Exception e,CancellationToken t)=>p.Error("exception-request",r,true,e,t);
            public void Finally(Outer r,SendExitInfo exit,CancellationToken t)=>p.Exit("finally-request",r,true,exit,t);
            public void Dispose(){p.Disposed.Add("Q");}
        }
        public sealed class ResponseMap:Zendiator.SendTypeConnectionPrototype.IResponseMap<Inner,int,string>,ISendExceptionObserver<Inner>,ISendFinallyObserver<Inner>,IDisposable {
            private readonly Probe p;public ResponseMap(Probe p){this.p=p;p.Resolve("S");}
            public string Map(Inner r,int value,CancellationToken t){p.Record("response-map",r,false,t);Probe.Check(value==16,"handler response");if(p.Mode=="response")throw p.Failure;return "r:"+value.ToString(System.Globalization.CultureInfo.InvariantCulture);}
            public void OnException(Inner r,Exception e,CancellationToken t)=>p.Error("exception-response",r,false,e,t);
            public void Finally(Inner r,SendExitInfo exit,CancellationToken t)=>p.Exit("finally-response",r,false,exit,t);
            public void Dispose(){p.Disposed.Add("S");}
        }
        public sealed class Good:Zendiator.SendTypeConnectionPrototype.IRoute<Outer,Inner,int,string,Handler,RequestMap,ResponseMap>;
        public static class Entry {
            // Independent, concrete reference for the declared two nested stage boundaries.
            private static string Reference(IServiceProvider sp,Outer outer,CancellationToken token){
                ArgumentNullException.ThrowIfNull(outer);token.ThrowIfCancellationRequested();var q=sp.GetRequiredService<RequestMap>();var successQ=false;Exception? errorQ=null;
                try{
                    var inner=q.Map(outer,token);ArgumentNullException.ThrowIfNull(inner);token.ThrowIfCancellationRequested();var s=sp.GetRequiredService<ResponseMap>();var successS=false;Exception? errorS=null;string result;
                    try{token.ThrowIfCancellationRequested();var h=sp.GetRequiredService<Handler>();var value=h.Handle(inner,token);result=s.Map(inner,value,token);ArgumentNullException.ThrowIfNull(result);successS=true;}
                    catch(Exception failure){errorS=failure;try{s.OnException(inner,failure,token);}catch(Exception replacement){errorS=replacement;throw;}throw;}
                    finally{s.Finally(inner,new(successS,errorS),token);}
                    successQ=true;return result;
                }catch(Exception failure){errorQ=failure;try{q.OnException(outer,failure,token);}catch(Exception replacement){errorQ=replacement;throw;}throw;}
                finally{q.Finally(outer,new(successQ,errorQ),token);}
            }
            private static string Observe(bool prototype,string mode){
                using var token=new CancellationTokenSource();var p=new Probe{Mode=mode,Token=token.Token,Outer=new Outer(7)};IServiceCollection services=new ServiceCollection();services.AddSingleton(p);services.AddScoped<RequestMap>();services.AddScoped<ResponseMap>();services.AddScoped<Handler>();
                using var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});using var scope=provider.CreateScope();var sp=scope.ServiceProvider;string? value=null;var error="none";
                try{value=prototype?global::Zendiator.Generated.SendTypeConnectionPrototype.__ZendiatorTypedSend_Good.Send(sp,p.Outer,p.Token):Reference(sp,p.Outer,p.Token);}catch(Exception e)when(ReferenceEquals(e,p.Failure)){error="sentinel";}
                var expected=mode=="request"?new[]{"resolve:Q","request-map:outer","exception-request:outer","finally-request:False:outer"}:mode=="response"?new[]{"resolve:Q","request-map:outer","resolve:S","resolve:H","handler:inner","response-map:inner","exception-response:inner","finally-response:False:inner","exception-request:outer","finally-request:False:outer"}:new[]{"resolve:Q","request-map:outer","resolve:S","resolve:H","handler:inner","response-map:inner","finally-response:True:inner","finally-request:True:outer"};
                Probe.Check(p.Trace.SequenceEqual(expected),"independent entered trace");Probe.Check(p.Calls==(mode=="request"?0:1),"single handler or not reached");
                Probe.Check(p.Created.SequenceEqual(mode=="request"?new[]{"Q"}:new[]{"Q","S","H"}),"request failure does not resolve inner stage/handler");
                Probe.Check(p.Outer.Value==7&&(mode=="request"?p.Inner==null:p.Inner?.Value==8)&&!ReferenceEquals(p.Outer,p.Inner),"outer identity is never overwritten by mapped request");
                Probe.Check(value==(mode=="success"?"r:16":null)&&error==(mode=="success"?"none":"sentinel"),"public response and failure identity");
                Probe.Check(p.Disposed.Count==0,"borrowed receivers not disposed by dispatch");scope.Dispose();Probe.Check(p.Disposed.SequenceEqual(p.Created.AsEnumerable().Reverse()),"scope owns each resolved receiver");
                return JsonSerializer.Serialize(new{mode,value,error,p.Calls,p.Created,p.Disposed,p.Trace,outer=p.Outer.Value,inner=p.Inner?.Value});
            }
            public static string Run(){var rows=new List<object>();foreach(var mode in new[]{"success","response","request"}){var reference=Observe(false,mode);var prototype=Observe(true,mode);Probe.Check(reference==prototype,"reference/prototype mismatch\n"+reference+"\n"+prototype);rows.Add(new{reference=JsonDocument.Parse(reference).RootElement.Clone(),prototype=JsonDocument.Parse(prototype).RootElement.Clone()});}return JsonSerializer.Serialize(new{pairs=3,dispatches=6,rows});}
        } }
        """;

    private const string TypedSendRejected = """
        namespace TypedFixture {
        [ZendiatorHandler]public sealed class WrongHandler:ISyncRequestHandler<OtherInner,int>{public int Handle(OtherInner r,CancellationToken t)=>0;}
        [ZendiatorHandler]public struct StructHandler:ISyncRequestHandler<Inner,int>{public int Handle(Inner r,CancellationToken t)=>0;}
        public sealed class WrongResponseMap:Zendiator.SendTypeConnectionPrototype.IResponseMap<Inner,int,long>,ISendExceptionObserver<Inner>,ISendFinallyObserver<Inner>{public long Map(Inner r,int value,CancellationToken t)=>value;public void OnException(Inner r,Exception e,CancellationToken t){}public void Finally(Inner r,SendExitInfo exit,CancellationToken t){}}
        public sealed class NullableResponseMap:Zendiator.SendTypeConnectionPrototype.IResponseMap<Inner,int,string?>,ISendExceptionObserver<Inner>,ISendFinallyObserver<Inner>{public string? Map(Inner r,int value,CancellationToken t)=>null;public void OnException(Inner r,Exception e,CancellationToken t){}public void Finally(Inner r,SendExitInfo exit,CancellationToken t){}}
        public sealed class RequestMismatch:Zendiator.SendTypeConnectionPrototype.IRoute<Outer,Inner,int,string,WrongHandler,RequestMap,ResponseMap>;
        public sealed class PublicMismatch:Zendiator.SendTypeConnectionPrototype.IRoute<Outer,Inner,int,string,Handler,RequestMap,WrongResponseMap>;
        public sealed class Unsupported:Zendiator.SendTypeConnectionPrototype.IRoute<Outer,Inner,int,string,StructHandler,RequestMap,ResponseMap>;
        public sealed class NullableMismatch:Zendiator.SendTypeConnectionPrototype.IRoute<Outer,Inner,int,string,Handler,RequestMap,NullableResponseMap>;
        public sealed class CompilerOwned:Zendiator.SendTypeConnectionPrototype.IRoute<MissingRequest,Inner,int,string,Handler,RequestMap,ResponseMap>;
        }
        """;

    private static (Compilation Output, GeneratorDriver Driver, string? Folder) CompileTypedSendPrototype(bool negative)
    {
        var source = OpenSendSyncSource() + TypedSendProposalContracts + TypedSendConsumer + (negative ? TypedSendRejected : "");
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "TypedSend_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        Assert.Empty(diagnostics);
        var root = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        var folder = string.IsNullOrEmpty(root) ? null : Path.Combine(root, negative ? "typed-send-negative" : "typed-send-runtime"); if (folder != null) Directory.CreateDirectory(folder);
        var names = negative ? new[] { "Good", "RequestMismatch", "PublicMismatch", "Unsupported", "NullableMismatch", "CompilerOwned" } : new[] { "Good" };
        var bindings = new Dictionary<string, TypedSendPrototypeBinding.Result>();
        foreach (var name in names)
        {
            var declaration = output.GetTypeByMetadataName("TypedFixture." + name)!;
            var bound = TypedSendPrototypeBinding.Bind(output, declaration); bindings.Add(name, bound);
            if (bound.Model != null)
            {
                var text = TypedSendPrototypeEmitter.Emit(bound.Model);
                output = output.AddSyntaxTrees(CSharpSyntaxTree.ParseText(text, SyncParseOptions, name + ".prototype.g.cs"));
                if (folder != null) File.WriteAllText(Path.Combine(folder, name + ".prototype.g.cs"), text);
            }
        }
        if (folder != null)
        {
            SaveSendAllSync(folder, source, driver, output, diagnostics);
            File.WriteAllText(Path.Combine(folder, "binding.json"), JsonSerializer.Serialize(bindings.Select(p => new { name = p.Key, p.Value.Model, p.Value.CompilerOwnsError, diagnostic = p.Value.Diagnostic == null ? null : new { p.Value.Diagnostic.Id, message = p.Value.Diagnostic.GetMessage(), span = p.Value.Diagnostic.Location.SourceSpan.ToString(), anchor = source.Substring(p.Value.Diagnostic.Location.SourceSpan.Start, p.Value.Diagnostic.Location.SourceSpan.Length), severity = p.Value.Diagnostic.Severity.ToString() } })));
        }
        var good = bindings["Good"]; Assert.Null(good.Diagnostic); Assert.False(good.CompilerOwnsError); var model = Assert.IsType<TypedSendPrototypeModel>(good.Model);
        Assert.Equal("global::TypedFixture.Outer", model.ExternalRequest.Name); Assert.Equal("global::TypedFixture.Inner", model.HandlerRequest.Name); Assert.Equal("int", model.HandlerResponse.Name); Assert.Equal("string", model.PublicResponse.Name);
        Assert.Equal(model.ExternalRequest, model.RequestMap.SavedRequest); Assert.Equal(model.HandlerRequest, model.ResponseMap.SavedRequest); Assert.NotEqual(model.RequestMap.SavedRequest.Identity, model.ResponseMap.SavedRequest.Identity);
        Assert.Equal(model.HandlerResponse, model.ResponseMap.Input); Assert.Equal(model.PublicResponse, model.ResponseMap.Output); Assert.Contains("nullable=NotAnnotated", model.PublicResponse.Identity);
        // Existing normal generated entry points are compile sentinels only. Their runtime matrices are not replayed.
        foreach (var ns in new[] { "OpenResponse.Generated.Composition", "OpenVoid.Generated.Composition", "OpenSentinel.Generated.ClosedComposition", "OpenSentinel.Generated.ScopedComposition" }) Assert.NotNull(output.GetTypeByMetadataName(ns + ".IZendiator"));
        Assert.Null(output.GetTypeByMetadataName("TypedFixture.Generated.Good.IZendiator"));
        if (negative)
        {
            foreach (var (name, id) in new[] { ("RequestMismatch", "ZEN0026"), ("PublicMismatch", "ZEN0026"), ("Unsupported", "ZEN0035"), ("NullableMismatch", "ZEN0026") })
            {
                var result = bindings[name]; Assert.Null(result.Model); Assert.False(result.CompilerOwnsError); var diagnostic = Assert.IsAssignableFrom<Diagnostic>(result.Diagnostic);
                Assert.Equal(id, diagnostic.Id); Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity); Assert.Equal(name, source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length)); Assert.Empty(diagnostic.AdditionalLocations);
                Assert.Null(output.GetTypeByMetadataName("Zendiator.Generated.SendTypeConnectionPrototype.__ZendiatorTypedSend_" + name));
            }
            Assert.Contains("inner handler request", bindings["RequestMismatch"].Diagnostic!.GetMessage()); Assert.Contains("public response", bindings["PublicMismatch"].Diagnostic!.GetMessage()); Assert.Contains("nullability", bindings["NullableMismatch"].Diagnostic!.GetMessage());
            Assert.True(bindings["CompilerOwned"].CompilerOwnsError); Assert.Null(bindings["CompilerOwned"].Model); Assert.Null(bindings["CompilerOwned"].Diagnostic);
            var errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray(); Assert.NotEmpty(errors); Assert.All(errors, d => { Assert.Equal("CS0246", d.Id); Assert.Equal("MissingRequest", source.Substring(d.Location.SourceSpan.Start, d.Location.SourceSpan.Length)); });
            if (folder != null) File.WriteAllText(Path.Combine(folder, "negative-result.json"), JsonSerializer.Serialize(new { logicalInputs = 1, drivers = 1, rejectedRouteOwned = 4, compilerOwned = 1, generatedPrototype = 1, normalGeneratedCompositions = 4, dispatches = 0, compiler = errors.Select(d => new { d.Id, message = d.GetMessage(), span = d.Location.SourceSpan.ToString() }) }));
        }
        else Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        return (output, driver, folder);
    }

    [Fact]
    public void TypedSendPrototype_distinct_request_ports_entered_failures_and_DI_scope_match_reference()
    {
        var compiled = CompileTypedSendPrototype(false); using var bytes = new MemoryStream(); var emitted = compiled.Output.Emit(bytes); Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        if (compiled.Folder != null) File.WriteAllBytes(Path.Combine(compiled.Folder, "generated.dll"), bytes.ToArray()); bytes.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes); var result = assembly.GetType("TypedFixture.Entry")!.GetMethod("Run")!.CreateDelegate<Func<string>>()();
        if (compiled.Folder != null) File.WriteAllText(Path.Combine(compiled.Folder, "runtime.json"), result);
        using var json = JsonDocument.Parse(result); Assert.Equal(6, json.RootElement.GetProperty("dispatches").GetInt32());
    }

    [Fact]
    public void TypedSendPrototype_rejects_independent_port_nullable_and_shape_errors_without_hiding_compiler_owner()
    {
        CompileTypedSendPrototype(true);
    }
}
