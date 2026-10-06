$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Generate.Additional.ps1')
$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add(@'
#nullable enable
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zendiator.DependencyInjection;

using Zendiator.CompetitiveBenchmarks;
using DispatchR.Extensions;
using BenchmarkDotNet.Attributes;
using Z = Zendiator;
using M = MediatR;
using G = Mediator;
using D = DispatchR.Abstractions.Send;
using DS = DispatchR.Abstractions.Stream;
using I = Immediate.Handlers.Shared;
[assembly: I.ImmediateAssemblyIdentifier("Competitive")]
namespace Competitive;
'@)
foreach ($b in 0,1,3,5) {
    $marker = if ($b -eq 0) {'IWork'} else {"ILevel$b"}
    $lines.Add(@"
public sealed record Ping$b(int Value = 41, Probe? Probe = null) : $marker, Z.IRequest<int>, M.IRequest<int>, G.IRequest<int>, D.IRequest<Ping$b, ValueTask<int>>;
public sealed record Stream$b(int Count, bool Asynchronous, Probe? Probe = null) : $marker, Z.IStreamRequest<int>, M.IStreamRequest<int>, G.IStreamRequest<int>, DS.IStreamRequest<Stream$b, int>;
public sealed class ZPing$b : Z.IRequestHandler<Ping$b,int> { public ValueTask<int> HandleAsync(Ping$b r,CancellationToken t) => new(Work.Handle(r.Value,r.Probe,t)); }
public sealed class MPing$b : M.IRequestHandler<Ping$b,int> { public Task<int> Handle(Ping$b r,CancellationToken t) => Task.FromResult(Work.Handle(r.Value,r.Probe,t)); }
public sealed class GPing$b : G.IRequestHandler<Ping$b,int> { public ValueTask<int> Handle(Ping$b r,CancellationToken t) => new(Work.Handle(r.Value,r.Probe,t)); }
public sealed class DPing$b : D.IRequestHandler<Ping$b,ValueTask<int>> { public ValueTask<int> Handle(Ping$b r,CancellationToken t) => new(Work.Handle(r.Value,r.Probe,t)); }
public sealed class ZStream$b : Z.IStreamRequestHandler<Stream$b,int> { public IAsyncEnumerable<int> HandleAsync(Stream$b r,CancellationToken t) => Work.Stream(r.Count,r.Asynchronous,r.Probe,t); }
public sealed class MStream$b : M.IStreamRequestHandler<Stream$b,int> { public IAsyncEnumerable<int> Handle(Stream$b r,CancellationToken t) => Work.Stream(r.Count,r.Asynchronous,r.Probe,t); }
public sealed class GStream$b : G.IStreamRequestHandler<Stream$b,int> { public IAsyncEnumerable<int> Handle(Stream$b r,CancellationToken t) => Work.Stream(r.Count,r.Asynchronous,r.Probe,t); }
public sealed class DStream$b : DS.IStreamRequestHandler<Stream$b,int> { public IAsyncEnumerable<int> Handle(Stream$b r,CancellationToken t) => Work.Stream(r.Count,r.Asynchronous,r.Probe,t); }
"@)
    if ($b) { foreach ($i in 1..$b) {
        $lines.Add(@"
public sealed class DBehavior${b}_$i : D.IPipelineBehavior<Ping$b,ValueTask<int>>
{ public required D.IRequestHandler<Ping$b,ValueTask<int>> NextPipeline {get;set;} public ValueTask<int> Handle(Ping$b r,CancellationToken t) { Work.Enter(r,$i); return NextPipeline.Handle(r,t); } }
public sealed class DSBehavior${b}_$i : DS.IStreamPipelineBehavior<Stream$b,int>
{ public required DS.IStreamRequestHandler<Stream$b,int> NextPipeline {get;set;} public async IAsyncEnumerable<int> Handle(Stream$b r,[EnumeratorCancellation] CancellationToken t) { Work.Enter(r,$i); await foreach(var item in NextPipeline.Handle(r,t).WithCancellation(t).ConfigureAwait(false)) yield return item; } }
"@)
    } }
    $attrs = if($b) { '[I.Behaviors(' + ((1..$b | ForEach-Object {"typeof(IBehavior$_<,>)"}) -join ',') + ')]' } else {'[I.Behaviors()]'}
    $sattrs = if($b) { '[I.Behaviors(' + ((1..$b | ForEach-Object {"typeof(ISBehavior$_<,>)"}) -join ',') + ')]' } else {'[I.Behaviors()]'}
    $lines.Add(@"
[I.Handler] $attrs
public static partial class IPing$b { private static ValueTask<int> HandleAsync(Ping$b r,CancellationToken t) => new(Work.Handle(r.Value,r.Probe,t)); }
[I.Handler] $sattrs
public static partial class IStream$b { private static IAsyncEnumerable<int> HandleAsync(Stream$b r,CancellationToken t) => Work.Stream(r.Count,r.Asynchronous,r.Probe,t); }
"@)
}
foreach ($i in 1..5) {
    $lines.Add(@"
public sealed class ZBehavior$i<T,R> : Z.IPipelineBehavior<T,R> where T : Z.IRequest<R>, ILevel$i
{ public ValueTask<R> HandleAsync<TNext>(T r,TNext next,CancellationToken t) where TNext:struct,Z.IRequestContinuation<T,R> { Work.Enter(r,$i); return next.InvokeAsync(r,t); } }
public sealed class ZSBehavior$i<T,R> : Z.IStreamPipelineBehavior<T,R> where T : Z.IStreamRequest<R>, ILevel$i
{ public async IAsyncEnumerable<R> HandleAsync<TNext>(T r,TNext next,[EnumeratorCancellation] CancellationToken t) where TNext:struct,Z.IStreamContinuation<T,R> { Work.Enter(r,$i); await foreach(var item in next.InvokeAsync(r,t).WithCancellation(t).ConfigureAwait(false)) yield return item; } }
public sealed class MBehavior$i<T,R> : M.IPipelineBehavior<T,R> where T : notnull,IWork
{ public Task<R> Handle(T r,M.RequestHandlerDelegate<R> next,CancellationToken t) { Work.Enter(r,$i); return next(t); } }
public sealed class MSBehavior$i<T,R> : M.IStreamPipelineBehavior<T,R> where T : notnull,IWork
{ public async IAsyncEnumerable<R> Handle(T r,M.StreamHandlerDelegate<R> next,[EnumeratorCancellation] CancellationToken t) { Work.Enter(r,$i); await foreach(var item in next().WithCancellation(t).ConfigureAwait(false)) yield return item; } }
public sealed class GBehavior$i<T,R> : G.IPipelineBehavior<T,R> where T : G.IMessage,IWork
{ public ValueTask<R> Handle(T r,G.MessageHandlerDelegate<T,R> next,CancellationToken t) { Work.Enter(r,$i); return next(r,t); } }
public sealed class GSBehavior$i<T,R> : G.IStreamPipelineBehavior<T,R> where T : G.IStreamMessage,IWork
{ public async IAsyncEnumerable<R> Handle(T r,G.StreamHandlerDelegate<T,R> next,[EnumeratorCancellation] CancellationToken t) { Work.Enter(r,$i); await foreach(var item in next(r,t).WithCancellation(t).ConfigureAwait(false)) yield return item; } }
public sealed class IBehavior$i<T,R> : I.Behavior<T,R> where T:IWork
{ public override ValueTask<R> HandleAsync(T r,CancellationToken t) { Work.Enter(r,$i); return Next(r,t); } }
public sealed class ISBehavior$i<T,R> : I.StreamingBehavior<T,R> where T:IWork
{ public override async IAsyncEnumerable<R> HandleAsync(T r,[EnumeratorCancellation] CancellationToken t) { Work.Enter(r,$i); await foreach(var item in Next(r,t).WithCancellation(t).ConfigureAwait(false)) yield return item; } }
"@)
}
Add-ExtraDefinitions
$lines.Add(@'
public static class Registration
{
#if MATCHED_SCOPED
    public static string MediatorLifetime => "Scoped";
    public static IEnumerable<string> MediatorLifetimes => ["Scoped"];
#else
    public static string MediatorLifetime => "Singleton";
    public static IEnumerable<string> MediatorLifetimes => ["Default","Singleton"];
#endif
    public static ServiceProvider Create(string library, string lifetime = "Default")
    {
        var s = new ServiceCollection();
        s.AddLogging();
        switch(library)
        {
            case "Zendiator":
                s.AddZendiator(c => {
                    c.Namespace = "Competitive.Generated";
                    c.ServiceLifetime = lifetime == "Singleton" ? ServiceLifetime.Singleton : lifetime == "Transient" ? ServiceLifetime.Transient : ServiceLifetime.Scoped;
                    c.RegisterServicesFromAssemblyContaining<Ping0>();
'@)
foreach($i in 1..5) {
    $lines.Add("c.AddOpenBehavior(typeof(ZBehavior$i<,>), order: $i); c.AddOpenStreamBehavior(typeof(ZSBehavior$i<,>), order: $(10+$i));")
    $lines.Add("c.AddOpenBehavior(typeof(ZVBehavior$i<>), order: $(20+$i));")
}
$lines.Add(@'
                });
                break;
            case "MediatRHistorical":
                s.AddMediatR(c => { c.RegisterServicesFromAssemblyContaining<Ping0>(); if (lifetime != "Default") c.Lifetime = lifetime == "Singleton" ? ServiceLifetime.Singleton : lifetime == "Scoped" ? ServiceLifetime.Scoped : ServiceLifetime.Transient; });
                break;
            case "Mediator":
#if MATCHED_SCOPED
                s.AddMediator(c => { c.ServiceLifetime = ServiceLifetime.Scoped; });
#else
                s.AddMediator(c => { c.ServiceLifetime = ServiceLifetime.Singleton; });
#endif
                break;
            case "DispatchR":
                if (lifetime is not ("Default" or "Scoped")) throw new NotSupportedException("DispatchR entry lifetime is fixed by AddDispatchR.");
                break;
            case "Immediate":
                if (lifetime == "Default") s.AddCompetitiveHandlers();
                else s.AddCompetitiveHandlers(lifetime == "Singleton" ? ServiceLifetime.Singleton : lifetime == "Transient" ? ServiceLifetime.Transient : ServiceLifetime.Scoped);
                break;
            case "Direct": break;
            default: throw new ArgumentOutOfRangeException(nameof(library));
        }
        RegistrationAudit.Record(s, library, lifetime, "after-library");
'@)
foreach($b in 1,3,5) {
    foreach($i in 1..$b) {
        foreach($l in 'M','G') {
            $lib=@{M='MediatRHistorical';G='Mediator';D='DispatchR'}[$l]
            $prefix=$l
            $sprefix=if($l -eq 'D'){'DS'}else{$prefix}
            $response=if($l -eq 'D'){'ValueTask<int>'}else{'int'}
            $life=if($l -eq 'G'){'Singleton'}elseif($l -eq 'M'){'Transient'}else{'Scoped'}
            $lines.Add("if(library == `"$lib`") { s.Add$life<$prefix.IPipelineBehavior<Ping$b,$response>,${l}Behavior$i<Ping$b,int>>(); s.Add$life<$sprefix.IStreamPipelineBehavior<Stream$b,int>,${l}SBehavior$i<Stream$b,int>>(); }")
        }
    }
}
Add-ExtraRegistrations
$order = foreach($b in 1,3,5) { foreach($i in 1..$b) { "typeof(DBehavior${b}_$i)"; "typeof(DSBehavior${b}_$i)"; "typeof(DVBehavior${b}_$i)" } }
$lines.Add('if(library == "DispatchR") s.AddDispatchR(c => { c.Assemblies.Add(typeof(Ping0).Assembly); c.ExcludeHandlers=[typeof(DOpen<>)]; c.PipelineOrder=[' + ($order -join ',') + ']; });')
$lines.Add(@'
if (library == "Mediator" && lifetime != "Default" && lifetime != MediatorLifetime)
    throw new NotSupportedException("Mediator lifetime must match its compile-time generation configuration.");
RegistrationAudit.Record(s, library, lifetime, "before-provider");
return s.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }); } }
'@)
foreach($lib in 'Direct','Zendiator','MediatRHistorical','Mediator','DispatchR','Immediate') {
    foreach($b in 0,1,3,5) {
        if($lib -eq 'Direct' -and $b -ne 0) {continue}
        $entry=@{
            Direct='ZPing0'; Zendiator='Competitive.Generated.IZendiator'; MediatRHistorical='M.IMediator';
            Mediator='G.Mediator'; DispatchR='DispatchR.IMediator'; Immediate="IPing$b.Handler"
        }[$lib]
        $streamEntry=if($lib -eq 'Immediate'){"IStream$b.Handler"}else{$entry}
        $resolve=if($lib -eq 'Direct'){'new ZPing0()'}else{"scope.ServiceProvider.GetRequiredService<$entry>()"}
        $sresolve=if($lib -eq 'Direct'){'new ZPing0()'}else{"scope.ServiceProvider.GetRequiredService<$streamEntry>()"}
        $send=@{
            Direct='new ValueTask<int>(Work.Handle(r.Value,r.Probe,t))'; Zendiator='entry.SendAsync(r,t)';
            MediatRHistorical='entry.Send(r,t)'; Mediator='entry.Send(r,t)'; DispatchR='entry.Send(r,t)';
            Immediate='entry.HandleAsync(r,t)'
        }[$lib]
        $stream=@{
            Direct='Work.Stream(r.Count,r.Asynchronous,r.Probe,t)'; Zendiator='entry.StreamAsync(r,t)';
            MediatRHistorical='entry.CreateStream(r,t)'; Mediator='entry.CreateStream(r,t)'; DispatchR='entry.CreateStream(r,t)';
            Immediate='entry.HandleAsync(r,t)'
        }[$lib]
        $return=if($lib -eq 'MediatRHistorical'){'Task<int>'}else{'ValueTask<int>'}
        $lines.Add(@"
[MemoryDiagnoser]
public class ${lib}Send$b
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private $entry entry = null!;
    private readonly Ping$b request = new();
    [ParamsSource(nameof(Lifetimes))] public string Lifetime {get;set;} = "Default";
    public IEnumerable<string> Lifetimes => $(if($lib -eq 'Direct'){'["Default"]'}elseif($lib -eq 'Mediator'){'Registration.MediatorLifetimes'}elseif($lib -eq 'DispatchR'){'["Default","Scoped"]'}else{'["Default","Scoped","Singleton"]'});
    [GlobalSetup] public void Setup() { provider=Registration.Create("$lib",Lifetime); scope=provider.CreateScope(); entry=$resolve; ChildEvidence.Record("${lib}Send$b", Lifetime); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public $return Invoke(Ping$b r,CancellationToken t=default) => $send;
    [Benchmark] public $return Typed() => Invoke(request);
    [Benchmark] public $return ResolveSend() { entry=$resolve; return Invoke(request); }
    [Benchmark] public async ValueTask<int> ScopeK1()
    {
        using var local=provider.CreateScope();
        var entry=$(if($lib -eq 'Direct'){'new ZPing0()'}else{"local.ServiceProvider.GetRequiredService<$entry>()"});
        var r=request; var t=CancellationToken.None;
        return await $send;
    }
    public async ValueTask<int> ScopeMany(int count)
    {
        using var local=provider.CreateScope();
        var entry=$(if($lib -eq 'Direct'){'new ZPing0()'}else{"local.ServiceProvider.GetRequiredService<$entry>()"});
        var r=request; var t=CancellationToken.None; int sum=0;
        for(int i=0;i<count;i++) sum+=await $send;
        return sum;
    }
    [Benchmark] public ValueTask<int> ScopeK10() => ScopeMany(10);
    [Benchmark] public ValueTask<int> ScopeK100() => ScopeMany(100);
}
[MemoryDiagnoser]
public class ${lib}Stream$b
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private $streamEntry entry = null!;
    private Stream$b request = null!;
    [Params(0,1,16,1024)] public int Count {get;set;}
    [Params(false,true)] public bool Asynchronous {get;set;}
    [GlobalSetup] public void Setup() { provider=Registration.Create("$lib"); scope=provider.CreateScope(); entry=$sresolve; request=new(Count,Asynchronous); ChildEvidence.Record("${lib}Stream$b", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public IAsyncEnumerable<int> Invoke(Stream$b r,CancellationToken t=default) => $stream;
    [Benchmark] public IAsyncEnumerable<int> Creation() => Invoke(request);
    [Benchmark] public async ValueTask<int> Full() { int sum=0; await foreach(var item in Invoke(request).ConfigureAwait(false)) sum+=item; return sum; }
    [Benchmark] public async ValueTask<bool> First() { await using var e=Invoke(request).GetAsyncEnumerator(); return await e.MoveNextAsync(); }
    [Benchmark] public async ValueTask<int> EarlyBreak() { await foreach(var item in Invoke(request).ConfigureAwait(false)) return item; return -1; }
    [Benchmark] public async ValueTask<bool> Cancellation()
    {
        using var cts=new CancellationTokenSource();
        await using var e=Invoke(request,cts.Token).GetAsyncEnumerator();
        await e.MoveNextAsync();
        cts.Cancel();
        try { await e.MoveNextAsync(); return false; } catch(OperationCanceledException) { return true; }
    }
}
"@)
    }
}
Add-ExtraBenchmarks
$lines.Add('public static class GeneratedGate { public static async Task Run() {')
foreach($lib in 'Direct','Zendiator','MediatRHistorical','Mediator','DispatchR','Immediate') {
    foreach($b in 0,1,3,5) {
        if($lib -eq 'Direct' -and $b -ne 0) {continue}
        $lines.Add(@"
{
    var send=new ${lib}Send$b(); send.Setup();
    try {
        await Correctness.Send("$lib",$b, async (p,t)=>await send.Invoke(new Ping$b(41,p),t));
        if(await send.ResolveSend()!=42 || await send.ScopeK1()!=42 || await send.ScopeK10()!=420 || await send.ScopeK100()!=4200) throw new InvalidOperationException("Scope result");
    }
    finally { send.Cleanup(); }
    var stream=new ${lib}Stream$b(); stream.Setup();
    try { await Correctness.Stream("$lib",$b,(n,a,p,t)=>stream.Invoke(new Stream$b(n,a,p),t)); }
    finally { stream.Cleanup(); }
}
"@)
    }
}
Add-ExtraGate
$lines.Add('} }')
[IO.File]::WriteAllLines((Join-Path $PSScriptRoot 'GeneratedCases.cs'),$lines)
