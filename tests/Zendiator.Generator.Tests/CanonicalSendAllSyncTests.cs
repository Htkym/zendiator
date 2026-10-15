using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private static string SendAllSyncModule(bool isVoid)
    {
        var handlers = string.Join("\n", Enumerable.Range(1, 3).Select(id =>
            "[ZendiatorHandler]public sealed class H" + id + ":ISyncRequestHandler<Ping" + (isVoid ? "" : ",int") + ">,IDisposable{" +
            "private readonly Probe p;public H" + id + "(Probe p){this.p=p;p.Resolved.Add(\"H" + id + "\");}" +
            "public " + (isVoid ? "void" : "int") + " Handle(Ping r,CancellationToken t){" + (isVoid ? "" : "return ") +
            "p.Body(r," + id + ",r.Value,r.Shared,t);}public void Dispose()=>p.Ended.Add(\"H" + id + "\");}"));
        return """
            namespace __NS__ {
            public sealed record Ping(int Value,SharedData Shared):__MARKER__;
            public sealed class Hook:ISendRequestTransform<Ping>,__SUCCESS__,ISendExceptionObserver<Ping>,ISendFinallyObserver<Ping>,IDisposable{
                private readonly Probe p;public Hook(Probe p){this.p=p;p.Resolved.Add("K");}
                public Ping Before(Ping r,CancellationToken t){p.Before(r,r.Value,r.Shared,t);var next=new Ping(r.Value+10,r.Shared);p.Saved=next;return next;}
                public __RET__ After(Ping r,__AFTER_ARG__CancellationToken t){__AFTER_CALL__}
                public void OnException(Ping r,Exception e,CancellationToken t)=>p.Observe(r,r.Value,r.Shared,e,t);
                public void Finally(Ping r,SendExitInfo e,CancellationToken t)=>p.Finally(r,r.Value,r.Shared,e,t);
                public void Dispose()=>p.Ended.Add("K");
            }
            __HANDLERS__
            public static class Reference {
                private static __RET__ Branch(IServiceProvider p,Ping request,int id,CancellationToken t){
                    ArgumentNullException.ThrowIfNull(request);t.ThrowIfCancellationRequested();var hook=p.GetRequiredService<Hook>();
                    var saved=request;bool success=false;Exception? error=null;
                    try{saved=hook.Before(saved,t);ArgumentNullException.ThrowIfNull(saved);t.ThrowIfCancellationRequested();
                        __BODY__
                        __AFTER__
                        success=true;__RETURN__
                    }catch(Exception failure){error=failure;try{hook.OnException(saved,failure,t);}catch(Exception replacement){error=replacement;throw;}throw;}
                    finally{hook.Finally(saved,new SendExitInfo(success,error),t);}
                }
                public static __ALL_RET__ Run(IServiceProvider p,Ping request,CancellationToken t){
                    ArgumentNullException.ThrowIfNull(request);t.ThrowIfCancellationRequested();__ALL_BODY__
                }
                __SPAN__
            }
            __WRAPPER__
            }
            """
            .Replace("__NS__", isVoid ? "AllSync.Command" : "AllSync.Response")
            .Replace("__MARKER__", isVoid ? "ISyncRequest" : "ISyncRequest<int>")
            .Replace("__SUCCESS__", isVoid ? "ISendSuccessObserver<Ping>" : "ISendResponseTransform<Ping,int>")
            .Replace("__RET__", isVoid ? "void" : "int")
            .Replace("__AFTER_ARG__", isVoid ? "" : "int value,")
            .Replace("__AFTER_CALL__", (isVoid ? "" : "return ") + "p.After(r,r.Value,r.Shared," + (isVoid ? "0" : "value") + ",t);")
            .Replace("__HANDLERS__", handlers)
            .Replace("__BODY__", isVoid
                ? "switch(id){case 2:p.GetRequiredService<H2>().Handle(saved,t);break;case 1:p.GetRequiredService<H1>().Handle(saved,t);break;default:p.GetRequiredService<H3>().Handle(saved,t);break;}"
                : "var value=id switch{2=>p.GetRequiredService<H2>().Handle(saved,t),1=>p.GetRequiredService<H1>().Handle(saved,t),_=>p.GetRequiredService<H3>().Handle(saved,t)};")
            .Replace("__AFTER__", isVoid ? "hook.After(saved,t);" : "value=hook.After(saved,value,t);")
            .Replace("__RETURN__", isVoid ? "" : "return value;")
            .Replace("__ALL_RET__", isVoid ? "void" : "int[]")
            .Replace("__ALL_BODY__", isVoid ? "Branch(p,request,2,t);Branch(p,request,1,t);Branch(p,request,3,t);" : "return new[]{Branch(p,request,2,t),Branch(p,request,1,t),Branch(p,request,3,t)};")
            .Replace("__SPAN__", isVoid ? "" : "public static int Span(IServiceProvider p,Ping request,Span<int> destination,CancellationToken t){ArgumentNullException.ThrowIfNull(request);if(destination.Length<3)throw new ArgumentException(\"Destination is too short.\",nameof(destination));var results=Run(p,request,t);results.AsSpan().CopyTo(destination);return results.Length;}")
            .Replace("__WRAPPER__", isVoid ? "" : "public sealed class Opaque:ISendWrapper<Ping,int>{public int Invoke<TNext>(Ping request,TNext next,CancellationToken t)where TNext:ISendContinuation<Ping,int> =>next.Invoke(request,t);}");
    }

    private static string SendAllSyncSource(bool gate) => """
        using System;using System.Collections.Generic;using System.Linq;using System.Text.Json;using System.Threading;using System.Threading.Tasks;
        using Zendiator;using Zendiator.DependencyInjection;using Microsoft.Extensions.DependencyInjection;
        namespace AllSync {
        public sealed class SharedData{public int Version;public int[] Values=new[]{3,4,5,-9};}
        public sealed class Probe {
            public readonly List<string> Trace=new(),Resolved=new(),Ended=new();public readonly List<int> Handlers=new();
            public readonly CancellationTokenSource Cancellation=new();public readonly Exception Failure=new FormatException("planned");
            public object? Original,Saved;public string Case="";public int Branch;
            public static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
            public string Error(Exception? e)=>e==null?"none":ReferenceEquals(e,Failure)?"sentinel":e is OperationCanceledException o&&o.CancellationToken==Cancellation.Token?"cancel":e.GetType().Name;
            private void SavedRequest(object r,int value,SharedData s,CancellationToken t){Check(ReferenceEquals(r,Saved)&&value==11,"branch saved request");Check(t==Cancellation.Token,"token identity");}
            public void Before(object r,int value,SharedData s,CancellationToken t){
                Check(ReferenceEquals(r,Original)&&value==1&&t==Cancellation.Token,"each branch starts from original");
                Branch++;Trace.Add("before:"+Branch+":"+value+":"+s.Version);if(Case=="cancel"&&Branch==2)Cancellation.Cancel();
            }
            public int Body(object r,int id,int value,SharedData s,CancellationToken t){
                SavedRequest(r,value,s,t);Handlers.Add(id);Trace.Add("body:H"+id+":"+value+":"+s.Version);
                if((Case=="body"||Case=="voidbody")&&Branch==2)throw Failure;
                var result=value+s.Values[id-1];s.Version++;return result;
            }
            public int After(object r,int value,SharedData s,int response,CancellationToken t){
                SavedRequest(r,value,s,t);Trace.Add("after:"+Branch+":"+value+":"+s.Version);if(Case=="after"&&Branch==2)throw Failure;return response+100;
            }
            public void Observe(object r,int value,SharedData s,Exception e,CancellationToken t){SavedRequest(r,value,s,t);Trace.Add("exception:"+Branch+":"+Error(e));}
            public void Finally(object r,int value,SharedData s,SendExitInfo e,CancellationToken t){
                SavedRequest(r,value,s,t);Trace.Add("finally:"+Branch+":"+e.Success+":"+Error(e.Error));if(Case=="finally"&&Branch==2)throw Failure;
            }
        }}
        """ + "\n" + SendAllSyncModule(false) + "\n" + SendAllSyncModule(true) + """
        namespace AllSync {
        [ZendiatorComposition("send-all-response")]public sealed class Responses;
        [ZendiatorComposition("send-all-void")]public sealed class Commands;
        public static class Bridge {
            public static IServiceCollection RegisterResponse(IServiceCollection s)=>s.AddZendiator<Responses>(c=>{
                c.Handlers<Response.Ping>(DispatchFamily.SendAllSync,typeof(Response.H2),typeof(Response.H1),typeof(Response.H3));
                c.Pipeline<Response.Ping>(DispatchFamily.SendAllSync,Stage.Hook<Response.Hook>());
            });
            public static IServiceCollection RegisterVoid(IServiceCollection s)=>s.AddZendiator<Commands>(c=>{
                c.Handlers<Command.Ping>(DispatchFamily.SendAllSync,typeof(Command.H2),typeof(Command.H1),typeof(Command.H3));
                c.Pipeline<Command.Ping>(DispatchFamily.SendAllSync,Stage.Hook<Command.Hook>());
            });
            __CONSUMER__
        }
        __NEGATIVE__
        }
        """.Replace("__CONSUMER__", gate ? "" : SendAllSyncConsumer).Replace("__NEGATIVE__", gate ? """
        [ZendiatorComposition("bad-wrapper")]public sealed class BadWrapper;
        [ZendiatorComposition("bad-async")]public sealed class BadAsync;
        public static class Rejected {
            public static IServiceCollection Wrapper(IServiceCollection s)=>s.AddZendiator<BadWrapper>(c=>{
                c.Handlers<Response.Ping>(DispatchFamily.SendAllSync,typeof(Response.H1));
                c.Pipeline<Response.Ping>(DispatchFamily.SendAllSync,Stage.Hook<Response.Hook>(),Stage.Wrapper<Response.Opaque>());
            });
            public static IServiceCollection Async(IServiceCollection s)=>s.AddZendiator<BadAsync>(c=>{
                c.Handlers<global::Closed.AllAsync.Ping>(DispatchFamily.SendAllAsync,typeof(global::Closed.AllAsync.Handler));
                c.Pipeline<global::Closed.AllAsync.Ping>(DispatchFamily.SendAllAsync,Stage.Hook<global::Closed.AllAsync.Hook0>());
            });
        }
        """ : "") + (gate ? "\n" + ClosedModule(CapabilitySource(true, false, "ssss", false), "AllAsync", "Ping", "Hook0", "Handler") : "");

    private const string SendAllSyncConsumer = """
        private static object Observe(string name,bool generated){
            var p=new Probe{Case=name};var shared=new SharedData();IServiceCollection services=new ServiceCollection();services.AddSingleton(p);
            var isVoid=name.StartsWith("void",StringComparison.Ordinal);
            if(isVoid)RegisterVoid(services);else RegisterResponse(services);
            Probe.Check(services.GroupBy(d=>d.ServiceType).All(g=>g.Count()==1),"unique DI descriptors");
            var required=isVoid?new[]{typeof(Command.H1),typeof(Command.H2),typeof(Command.H3),typeof(Command.Hook)}
                :new[]{typeof(Response.H1),typeof(Response.H2),typeof(Response.H3),typeof(Response.Hook)};
            foreach(var type in required)Probe.Check(services.Single(d=>d.ServiceType==type).Lifetime==ServiceLifetime.Scoped,"all borrowed services registered");
            using var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});var scope=provider.CreateScope();
            int[]? response=null;var copied=-1;Exception? error=null;
            try{
                if(isVoid){
                    var request=new Command.Ping(1,shared);p.Original=request;
                    var mediator=scope.ServiceProvider.GetRequiredService<global::AllSync.Generated.Commands.IZendiator>();
                    Probe.Check(ReferenceEquals(mediator,scope.ServiceProvider.GetRequiredService<global::AllSync.Generated.Commands.Zendiator>()),"void mediator identity");
                    try{if(generated)mediator.SendAll(request,p.Cancellation.Token);else Command.Reference.Run(scope.ServiceProvider,request,p.Cancellation.Token);}catch(Exception e){error=e;}
                }else{
                    var request=new Response.Ping(1,shared);p.Original=request;
                    var mediator=scope.ServiceProvider.GetRequiredService<global::AllSync.Generated.Responses.IZendiator>();
                    Probe.Check(ReferenceEquals(mediator,scope.ServiceProvider.GetRequiredService<global::AllSync.Generated.Responses.Zendiator>()),"response mediator identity");
                    try{
                        if(name=="array")response=generated?mediator.SendAll(request,p.Cancellation.Token):Response.Reference.Run(scope.ServiceProvider,request,p.Cancellation.Token);
                        else{
                            var destination=name=="short"?shared.Values.AsSpan(0,2):shared.Values.AsSpan();
                            copied=generated?mediator.SendAll(request,destination,p.Cancellation.Token):Response.Reference.Span(scope.ServiceProvider,request,destination,p.Cancellation.Token);
                            if(error==null)Probe.Check(name!="short","short Span must fail before any branch");
                        }
                    }catch(Exception e){error=e;}
                }
                var success=name is "array" or "span" or "void";
                Probe.Check(success?error==null:name=="short"?error is ArgumentException:name=="cancel"?error is OperationCanceledException o&&o.CancellationToken==p.Cancellation.Token:ReferenceEquals(error,p.Failure),"exception/token identity");
                Probe.Check(p.Branch==(name=="short"?0:success?3:2),"branch count and later stop");
                var expectedHandlers=name=="short"?Array.Empty<int>():name=="cancel"?new[]{2}:success?new[]{2,1,3}:new[]{2,1};
                Probe.Check(p.Handlers.SequenceEqual(expectedHandlers),"declared handler order and stop");
                Probe.Check(shared.Version==(name=="short"?0:success?3:name is "after" or "finally"?2:1),"shared changes preserved");
                if(name=="array")Probe.Check(response!.SequenceEqual(new[]{115,114,116}),"ordered result array");
                else if(name=="span")Probe.Check(copied==3&&shared.Values.SequenceEqual(new[]{115,114,116,-9}),"Span overlap commit and untouched tail");
                else Probe.Check(shared.Values.SequenceEqual(new[]{3,4,5,-9}),"Span destination unchanged on body/After/Finally failure/cancellation/short input");
                Probe.Check(p.Ended.Count==0,"dispatch does not dispose borrowed components");
                scope.Dispose();Probe.Check(p.Ended.SequenceEqual(p.Resolved.AsEnumerable().Reverse()),"DI owns each resolved instance exactly once");
                return new{name,response,copied,destination=shared.Values,shared.Version,p.Branch,handlers=p.Handlers.ToArray(),trace=p.Trace.ToArray(),resolved=p.Resolved.ToArray(),ended=p.Ended.ToArray(),error=p.Error(error)};
            }finally{scope.Dispose();p.Cancellation.Dispose();}
        }
        private sealed class GuardProvider(Probe p):IServiceProvider{
            public int Calls;
            public object? GetService(Type type){
                Calls++;
                if(type==typeof(Response.Hook))return new Response.Hook(p);
                if(type==typeof(Response.H1))return new Response.H1(p);
                if(type==typeof(Response.H2))return new Response.H2(p);
                if(type==typeof(Response.H3))return new Response.H3(p);
                throw new InvalidOperationException(type.FullName);
            }
        }
        public static string GuardCompetition(){
            var rows=new List<object>();
            foreach(var generated in new[]{true,false}){
                var p=new Probe();var provider=new GuardProvider(p);var destination=new[]{41,42,43,44};Exception? error=null;
                try{
                    try{
                        if(generated)new global::AllSync.Generated.Responses.Zendiator(provider).SendAll(null!,destination.AsSpan(0,2),p.Cancellation.Token);
                        else Response.Reference.Span(provider,null!,destination.AsSpan(0,2),p.Cancellation.Token);
                    }catch(Exception e){error=e;}
                    rows.Add(new{engine=generated?"generated":"reference",error=error?.GetType().Name,
                        parameter=(error as ArgumentException)?.ParamName,destination,providerCalls=provider.Calls,
                        handlerCalls=p.Handlers.Count,resolved=p.Resolved.Count,trace=p.Trace.Count});
                }finally{p.Cancellation.Dispose();}
            }
            return JsonSerializer.Serialize(new{condition="null request and short Span",apiCalls=2,rows});
        }
        public static string Run(){
            var rows=new List<object>();
            foreach(var name in new[]{"array","span","short","body","after","finally","cancel","void","voidbody"}){
                var generated=Observe(name,true);var reference=Observe(name,false);
                Probe.Check(JsonSerializer.Serialize(generated)==JsonSerializer.Serialize(reference),"generated/reference mismatch: "+name);
                rows.Add(new{name,generated,reference});
            }
            return JsonSerializer.Serialize(new{generatorInputs=1,scenarios=9,dispatchCalls=18,rows});
        }
        """;

    private static void SaveSendAllSync(string? folder, string source, GeneratorDriver driver, Compilation output, IEnumerable<Diagnostic> diagnostics)
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

    private static void AssertSendAllSyncContract(Compilation output)
    {
        var response = output.GetTypeByMetadataName("AllSync.Generated.Responses.IZendiator"); Assert.NotNull(response);
        var methods = response.GetMembers("SendAll").OfType<IMethodSymbol>().ToArray(); Assert.Equal(2, methods.Length);
        Assert.Contains(methods, m => m.ReturnType is IArrayTypeSymbol a && a.ElementType.SpecialType == SpecialType.System_Int32);
        Assert.Contains(methods, m => m.ReturnType.SpecialType == SpecialType.System_Int32 && m.Parameters[1].Type.Name == "Span");
        var command = output.GetTypeByMetadataName("AllSync.Generated.Commands.IZendiator"); Assert.NotNull(command);
        Assert.True(Assert.Single(command.GetMembers("SendAll").OfType<IMethodSymbol>()).ReturnsVoid);
    }

    [Fact]
    public void SendAllSync_public_gate_accepts_borrowed_sync_response_void_and_rejects_wrapper_async()
    {
        var source = SendAllSyncSource(true);
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "SendAllSyncGate_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        var folder = string.IsNullOrEmpty(evidence) ? null : Path.Combine(evidence, "sendall-sync-gate");
        SaveSendAllSync(folder, source, driver, output, diagnostics);
        Assert.Equal(2, diagnostics.Length); Assert.All(diagnostics, d => Assert.Equal("ZEN0037", d.Id));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        AssertSendAllSyncContract(output);
        var files = driver.GetRunResult().Results.Single().GeneratedSources;
        var routes = files.Where(f => IsCanonicalRoute(f)).ToArray(); Assert.Equal(2, routes.Length);
        Assert.All(routes, f => { Assert.DoesNotContain("ValueTask", f.SourceText.ToString()); Assert.DoesNotContain("global::System.Threading.Tasks.Task", f.SourceText.ToString()); Assert.DoesNotContain("Unit", f.SourceText.ToString()); });
        foreach (var name in new[] { "Responses", "Commands" })
        {
            var ns = "AllSync.Generated." + name;
            Assert.NotNull(output.GetTypeByMetadataName(ns + ".ZendiatorGeneratedRegistrar"));
            Assert.Contains(files, f => f.HintName.EndsWith("Interceptors.g.cs", StringComparison.Ordinal) && f.SourceText.ToString().Contains(ns + ".ZendiatorGeneratedRegistrar", StringComparison.Ordinal));
        }
        foreach (var name in new[] { "BadWrapper", "BadAsync" })
        {
            var ns = "AllSync.Generated." + name;
            Assert.Null(output.GetTypeByMetadataName(ns + ".IZendiator")); Assert.Null(output.GetTypeByMetadataName(ns + ".ZendiatorGeneratedRegistrar"));
            Assert.DoesNotContain(files, f => f.HintName.EndsWith("Interceptors.g.cs", StringComparison.Ordinal) && f.SourceText.ToString().Contains(ns + ".ZendiatorGeneratedRegistrar", StringComparison.Ordinal));
        }
        if (folder != null) File.WriteAllText(Path.Combine(folder, "gate-result.json"), JsonSerializer.Serialize(new { generatorInputs = 1, runtimeOperations = 0, accepted = new[] { "Responses", "Commands" }, rejected = new[] { "BadWrapper", "BadAsync" } }));
    }

    [Fact]
    public void SendAllSync_nine_scenarios_preserve_branch_state_Span_commit_stop_and_borrowed_ownership()
    {
        var source = SendAllSyncSource(false);
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "SendAllSyncRuntime_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        var folder = string.IsNullOrEmpty(evidence) ? null : Path.Combine(evidence, "sendall-sync-runtime");
        SaveSendAllSync(folder, source, driver, output, diagnostics);
        Assert.Empty(diagnostics); Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        AssertSendAllSyncContract(output);
        using var bytes = new MemoryStream(); var emitted = output.Emit(bytes); Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        if (folder != null) File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
        bytes.Position = 0; var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var run = assembly.GetType("AllSync.Bridge")!.GetMethod("Run")!.CreateDelegate<Func<string>>();
        var result = run(); if (folder != null) File.WriteAllText(Path.Combine(folder, "runtime.json"), result);
        using var observed = JsonDocument.Parse(result); Assert.Equal(9, observed.RootElement.GetProperty("scenarios").GetInt32()); Assert.Equal(18, observed.RootElement.GetProperty("dispatchCalls").GetInt32());
    }

    [Fact]
    public void SendAllSync_Span_argument_guard_precedes_short_destination_without_DI_or_handler_entry()
    {
        var source = SendAllSyncSource(false);
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "SendAllSyncGuard_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        var folder = string.IsNullOrEmpty(evidence) ? null : Path.Combine(evidence, "sendall-sync-guard");
        SaveSendAllSync(folder, source, driver, output, diagnostics);
        Assert.Empty(diagnostics); Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        using var bytes = new MemoryStream(); var emitted = output.Emit(bytes); Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        if (folder != null) File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
        bytes.Position = 0; var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var run = assembly.GetType("AllSync.Bridge")!.GetMethod("GuardCompetition")!.CreateDelegate<Func<string>>();
        var result = run(); if (folder != null) File.WriteAllText(Path.Combine(folder, "runtime.json"), result);
        using var observed = JsonDocument.Parse(result); var rows = observed.RootElement.GetProperty("rows").EnumerateArray().ToArray();
        Assert.Equal(2, rows.Length);
        Assert.All(rows, row =>
        {
            Assert.Equal("ArgumentNullException", row.GetProperty("error").GetString()); Assert.Equal("request", row.GetProperty("parameter").GetString());
            Assert.Equal(new[] { 41, 42, 43, 44 }, row.GetProperty("destination").EnumerateArray().Select(x => x.GetInt32()));
            foreach (var name in new[] { "providerCalls", "handlerCalls", "resolved", "trace" }) Assert.Equal(0, row.GetProperty(name).GetInt32());
        });
    }
}

