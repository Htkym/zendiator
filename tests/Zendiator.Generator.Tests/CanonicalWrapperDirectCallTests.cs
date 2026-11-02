using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private const string WrapperDirectCallSource = """
        using System;using System.Collections.Generic;using System.Threading;using System.Threading.Tasks;
        using Zendiator;using Zendiator.DependencyInjection;using Microsoft.Extensions.DependencyInjection;
        namespace App {
            public sealed class Trace { public readonly List<string> Calls = new(); }
            public sealed record SealedPing(int Value):IRequest<int>;
            public sealed record InheritedPing(int Value):IRequest<int>;
            public sealed record VirtualPing(int Value):IRequest<int>;
            public sealed record ReimplementedPing(int Value):IRequest<int>;
            public sealed record ExplicitPing(int Value):IRequest<int>;
            public sealed record OverloadPing(int Value):IRequest<int>;
            public sealed record SyncPing(int Value):ISyncRequest<int>;
            public sealed record SyncNote(int Value):ISyncRequest;
            public sealed record SealedNote(int Value):ISyncRequest;
            [ZendiatorHandler]public sealed class SealedHandler(Trace t):IRequestHandler<SealedPing,int>{public ValueTask<int> HandleAsync(SealedPing r,CancellationToken c){t.Calls.Add("handler");return new(r.Value);}}
            [ZendiatorHandler]public sealed class InheritedHandler(Trace t):IRequestHandler<InheritedPing,int>{public ValueTask<int> HandleAsync(InheritedPing r,CancellationToken c){t.Calls.Add("handler");return new(r.Value);}}
            [ZendiatorHandler]public sealed class VirtualHandler(Trace t):IRequestHandler<VirtualPing,int>{public ValueTask<int> HandleAsync(VirtualPing r,CancellationToken c){t.Calls.Add("handler");return new(r.Value);}}
            [ZendiatorHandler]public sealed class ReimplementedHandler(Trace t):IRequestHandler<ReimplementedPing,int>{public ValueTask<int> HandleAsync(ReimplementedPing r,CancellationToken c){t.Calls.Add("handler");return new(r.Value);}}
            [ZendiatorHandler]public sealed class ExplicitHandler(Trace t):IRequestHandler<ExplicitPing,int>{public ValueTask<int> HandleAsync(ExplicitPing r,CancellationToken c){t.Calls.Add("handler");return new(r.Value);}}
            [ZendiatorHandler]public sealed class OverloadHandler(Trace t):IRequestHandler<OverloadPing,int>{public ValueTask<int> HandleAsync(OverloadPing r,CancellationToken c){t.Calls.Add("handler");return new(r.Value);}}
            [ZendiatorHandler]public sealed class SyncHandler(Trace t):ISyncRequestHandler<SyncPing,int>{public int Handle(SyncPing r,CancellationToken c){t.Calls.Add("handler");return r.Value;}}
            [ZendiatorHandler]public sealed class NoteHandler(Trace t):ISyncRequestHandler<SyncNote>{public void Handle(SyncNote r,CancellationToken c)=>t.Calls.Add("handler:"+r.Value);}
            [ZendiatorHandler]public sealed class SealedNoteHandler(Trace t):ISyncRequestHandler<SealedNote>{public void Handle(SealedNote r,CancellationToken c)=>t.Calls.Add("handler:"+r.Value);}

            public sealed class SealedWrapper(Trace t):IAsyncSendWrapper<SealedPing,int>{
                public ValueTask<int> InvokeAsync<TNext>(SealedPing r,TNext next,CancellationToken c) where TNext:IAsyncSendContinuation<SealedPing,int>{t.Calls.Add("sealed");return next.InvokeAsync(r with{Value=r.Value+1},c);}
            }
            public class InheritedBase(Trace t):IAsyncSendWrapper<InheritedPing,int>{
                public ValueTask<int> InvokeAsync<TNext>(InheritedPing r,TNext next,CancellationToken c) where TNext:IAsyncSendContinuation<InheritedPing,int>{t.Calls.Add("inherited");return next.InvokeAsync(r with{Value=r.Value+2},c);}
            }
            public sealed class InheritedWrapper(Trace t):InheritedBase(t);
            public class VirtualWrapper(Trace t):IAsyncSendWrapper<VirtualPing,int>{
                protected Trace Trace{get;}=t;
                public virtual ValueTask<int> InvokeAsync<TNext>(VirtualPing r,TNext next,CancellationToken c) where TNext:IAsyncSendContinuation<VirtualPing,int>{Trace.Calls.Add("virtual-base");return next.InvokeAsync(r,c);}
            }
            public sealed class VirtualOverride(Trace t):VirtualWrapper(t){
                public override ValueTask<int> InvokeAsync<TNext>(VirtualPing r,TNext next,CancellationToken c){Trace.Calls.Add("virtual-override");return next.InvokeAsync(r with{Value=r.Value+3},c);}
            }
            public class ReimplementedBase(Trace t):IAsyncSendWrapper<ReimplementedPing,int>{
                protected Trace Trace{get;}=t;
                public ValueTask<int> InvokeAsync<TNext>(ReimplementedPing r,TNext next,CancellationToken c) where TNext:IAsyncSendContinuation<ReimplementedPing,int> =>throw new InvalidOperationException("Base method is not the runtime contract");
            }
            public sealed class Reimplemented(Trace t):ReimplementedBase(t),IAsyncSendWrapper<ReimplementedPing,int>{
                public new ValueTask<int> InvokeAsync<TNext>(ReimplementedPing r,TNext next,CancellationToken c) where TNext:IAsyncSendContinuation<ReimplementedPing,int>{Trace.Calls.Add("reimplemented");return next.InvokeAsync(r with{Value=r.Value+4},c);}
            }
            public sealed class ExplicitWrapper(Trace t):IAsyncSendWrapper<ExplicitPing,int>{
                public ValueTask<int> InvokeAsync<TNext>(ExplicitPing r,TNext next,CancellationToken c) where TNext:IAsyncSendContinuation<ExplicitPing,int> =>throw new InvalidOperationException("Public method is not the contract");
                ValueTask<int> IAsyncSendWrapper<ExplicitPing,int>.InvokeAsync<TNext>(ExplicitPing r,TNext next,CancellationToken c){t.Calls.Add("explicit");return next.InvokeAsync(r with{Value=r.Value+5},c);}
            }
            public sealed class OverloadWrapper(Trace t):IAsyncSendWrapper<OverloadPing,int>{
                public ValueTask<int> InvokeAsync<TNext>(OverloadPing r,TNext next,CancellationToken c) where TNext:IAsyncSendContinuation<OverloadPing,int>{t.Calls.Add("overload");return next.InvokeAsync(r with{Value=r.Value+6},c);}
                public ValueTask<int> InvokeAsync(object r,CancellationToken c)=>throw new InvalidOperationException("Wrong overload");
            }
            public class SyncWrapper(Trace t):ISendWrapper<SyncPing,int>{
                public int Invoke<TNext>(SyncPing r,TNext next,CancellationToken c) where TNext:ISendContinuation<SyncPing,int>{t.Calls.Add("sync");return next.Invoke(r with{Value=r.Value+7},c);}
            }
            public class NoteWrapper(Trace t):ISendWrapper<SyncNote>{
                public void Invoke<TNext>(SyncNote r,TNext next,CancellationToken c) where TNext:ISendContinuation<SyncNote>{t.Calls.Add("note");next.Invoke(r with{Value=r.Value+8},c);}
            }
            public sealed class SealedNoteWrapper(Trace t):ISendWrapper<SealedNote>{
                public void Invoke<TNext>(SealedNote r,TNext next,CancellationToken c) where TNext:ISendContinuation<SealedNote>{t.Calls.Add("sealed-note");next.Invoke(r with{Value=r.Value+9},c);}
            }
            [ZendiatorComposition("wrapperDirectCalls")]public sealed class Reads;
            public static class Bridge {
                public static IServiceCollection Register(IServiceCollection s)=>s.AddSingleton<Trace>()
                    .AddTransient<VirtualWrapper,VirtualOverride>().AddTransient<ReimplementedBase,Reimplemented>()
                    .AddZendiator<Reads>(c=>{
                        c.Handlers<SealedPing>(DispatchFamily.SendAsync,typeof(SealedHandler));c.Pipeline<SealedPing>(DispatchFamily.SendAsync,Stage.Wrapper<SealedWrapper>());
                        c.Handlers<InheritedPing>(DispatchFamily.SendAsync,typeof(InheritedHandler));c.Pipeline<InheritedPing>(DispatchFamily.SendAsync,Stage.Wrapper<InheritedWrapper>());
                        c.Handlers<VirtualPing>(DispatchFamily.SendAsync,typeof(VirtualHandler));c.Pipeline<VirtualPing>(DispatchFamily.SendAsync,Stage.Wrapper<VirtualWrapper>());
                        c.Handlers<ReimplementedPing>(DispatchFamily.SendAsync,typeof(ReimplementedHandler));c.Pipeline<ReimplementedPing>(DispatchFamily.SendAsync,Stage.Wrapper<ReimplementedBase>());
                        c.Handlers<ExplicitPing>(DispatchFamily.SendAsync,typeof(ExplicitHandler));c.Pipeline<ExplicitPing>(DispatchFamily.SendAsync,Stage.Wrapper<ExplicitWrapper>());
                        c.Handlers<OverloadPing>(DispatchFamily.SendAsync,typeof(OverloadHandler));c.Pipeline<OverloadPing>(DispatchFamily.SendAsync,Stage.Wrapper<OverloadWrapper>());
                        c.Handlers<SyncPing>(DispatchFamily.SendSync,typeof(SyncHandler));c.Pipeline<SyncPing>(DispatchFamily.SendSync,Stage.Wrapper<SyncWrapper>());
                        c.Handlers<SyncNote>(DispatchFamily.SendSync,typeof(NoteHandler));c.Pipeline<SyncNote>(DispatchFamily.SendSync,Stage.Wrapper<NoteWrapper>());
                        c.Handlers<SealedNote>(DispatchFamily.SendSync,typeof(SealedNoteHandler));c.Pipeline<SealedNote>(DispatchFamily.SendSync,Stage.Wrapper<SealedNoteWrapper>());
                    });
                public static async Task<int[]> RunAll(object m){
                    var z=(global::App.Generated.Reads.IZendiator)m;var t=CancellationToken.None;
                    var results=new List<int>{
                        await z.SendAsync(new SealedPing(10),t),await z.SendAsync(new InheritedPing(10),t),await z.SendAsync(new VirtualPing(10),t),
                        await z.SendAsync(new ReimplementedPing(10),t),await z.SendAsync(new ExplicitPing(10),t),await z.SendAsync(new OverloadPing(10),t),
                        z.Send(new SyncPing(10),t)};
                    z.Send(new SyncNote(10),t);z.Send(new SealedNote(10),t);
                    return results.ToArray();
                }
            }
        }
        """;

    [Fact]
    public async Task Send_wrapper_direct_call_binds_only_the_runtime_contract_implementation()
    {
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(WrapperDirectCallSource, "WrapperDirectCalls"), out var output, out var diagnostics);
        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        var routes = driver.GetRunResult().Results.Single().GeneratedSources
            .Where(s => s.HintName.StartsWith("Zendiator.Route.", StringComparison.Ordinal)).Select(s => s.SourceText.ToString()).ToArray();
        string Route(string request) => Assert.Single(routes, r => r.Contains("global::App." + request + " request", StringComparison.Ordinal));

        // Sealed receivers call the implementation directly; other classes keep interface dispatch for derived runtime types.
        Assert.Contains("((global::App.SealedWrapper)wrapper).InvokeAsync(", Route("SealedPing"));
        Assert.Contains("((global::App.InheritedBase)wrapper).InvokeAsync(", Route("InheritedPing"));
        Assert.Contains("((global::App.SealedNoteWrapper)wrapper).Invoke(", Route("SealedNote"));
        foreach (var request in new[] { "SealedPing", "InheritedPing", "SealedNote" })
            Assert.DoesNotContain("GetType()", Route(request));
        foreach (var (request, receiver) in new[] { ("VirtualPing", "VirtualWrapper"), ("ReimplementedPing", "ReimplementedBase"), ("SyncPing", "SyncWrapper") })
            Assert.Contains("(((global::System.Object)wrapper).GetType() == typeof(global::App." + receiver + ") ? ((global::App." + receiver + ")wrapper).", Route(request));
        foreach (var (request, receiver) in new[] { ("ExplicitPing", "ExplicitWrapper"), ("OverloadPing", "OverloadWrapper"), ("SyncNote", "NoteWrapper") })
        {
            Assert.DoesNotContain("((global::App." + receiver + ")wrapper)", Route(request));
            Assert.DoesNotContain("GetType()", Route(request));
        }

        using var bytes = new MemoryStream();
        var emitted = output.Emit(bytes);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        bytes.Position = 0;
        var assembly = new AssemblyLoadContext("WrapperDirectCalls", isCollectible: true).LoadFromStream(bytes);
        var bridge = assembly.GetType("App.Bridge")!;
        var services = (IServiceCollection)bridge.GetMethod("Register")!.Invoke(null, [new ServiceCollection()])!;
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService(assembly.GetType("App.Generated.Reads.IZendiator")!);
        var results = await (Task<int[]>)bridge.GetMethod("RunAll")!.Invoke(null, [mediator])!;
        Assert.Equal(new[] { 11, 12, 13, 14, 15, 16, 17 }, results);
        var trace = provider.GetRequiredService(assembly.GetType("App.Trace")!);
        var recorded = (List<string>)trace.GetType().GetField("Calls")!.GetValue(trace)!;
        Assert.Equal(new[]
        {
            "sealed", "handler", "inherited", "handler", "virtual-override", "handler", "reimplemented", "handler",
            "explicit", "handler", "overload", "handler", "sync", "handler", "note", "handler:18", "sealed-note", "handler:19"
        }, recorded);
    }
}
