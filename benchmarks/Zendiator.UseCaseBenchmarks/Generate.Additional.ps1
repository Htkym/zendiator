function Add-ExtraDefinitions {
    foreach($b in 0,1,3,5) {
        $marker=if($b){"ILevel$b"}else{'IWork'}
        $lines.Add(@"
public sealed record Void$b(Counter Counter, Probe? Probe = null) : $marker, Z.IRequest, M.IRequest, G.IRequest, D.IRequest<Void$b,ValueTask<ValueTuple>>;
public sealed class ZVoid$b : Z.IRequestHandler<Void$b> { public ValueTask HandleAsync(Void$b r,CancellationToken t) { Work.Void(r.Counter,r.Probe,t); return default; } }
public sealed class MVoid$b : M.IRequestHandler<Void$b> { public Task Handle(Void$b r,CancellationToken t) { Work.Void(r.Counter,r.Probe,t); return Task.CompletedTask; } }
public sealed class GVoid$b : G.IRequestHandler<Void$b> { public ValueTask<G.Unit> Handle(Void$b r,CancellationToken t) { Work.Void(r.Counter,r.Probe,t); return default; } }
public sealed class DVoid$b : D.IRequestHandler<Void$b,ValueTask<ValueTuple>> { public ValueTask<ValueTuple> Handle(Void$b r,CancellationToken t) { Work.Void(r.Counter,r.Probe,t); return default; } }
"@)
        $attrs=if($b){'[I.Behaviors(' + ((1..$b | ForEach-Object {"typeof(IBehavior$_<,>)"}) -join ',') + ')]'}else{'[I.Behaviors()]'}
        $lines.Add("[I.Handler] $attrs public static partial class IVoid$b { private static ValueTask HandleAsync(Void$b r,CancellationToken t) { Work.Void(r.Counter,r.Probe,t); return default; } }")
        if($b) { foreach($i in 1..$b) {
            $lines.Add(@"
public sealed class DVBehavior${b}_$i : D.IPipelineBehavior<Void$b,ValueTask<ValueTuple>>
{ public required D.IRequestHandler<Void$b,ValueTask<ValueTuple>> NextPipeline {get;set;} public ValueTask<ValueTuple> Handle(Void$b r,CancellationToken t) { Work.Enter(r,$i); return NextPipeline.Handle(r,t); } }
"@)
        } }
    }
    foreach($i in 1..5) {
        $lines.Add(@"
public sealed class ZVBehavior$i<T> : Z.IPipelineBehavior<T> where T:Z.IRequest,ILevel$i
{ public ValueTask HandleAsync<TNext>(T r,TNext next,CancellationToken t) where TNext:struct,Z.IRequestContinuation<T> { Work.Enter(r,$i); return next.InvokeAsync(r,t); } }
"@)
    }
    $lines.Add(@'
public sealed record ClosedGeneric<T>(int Value=41, Probe? Probe=null) : IWork,Z.IRequest<int>,M.IRequest<int>,D.IRequest<ClosedGeneric<T>,ValueTask<int>>;
public sealed record OpenGeneric<T>(int Value=41, Probe? Probe=null) : IWork,Z.IRequest<int>,M.IRequest<int>,D.IRequest<OpenGeneric<T>,ValueTask<int>>;
public sealed class ZClosed : Z.IRequestHandler<ClosedGeneric<int>,int> { public ValueTask<int> HandleAsync(ClosedGeneric<int> r,CancellationToken t)=>new(Work.Handle(r.Value,r.Probe,t)); }
public sealed class MClosed : M.IRequestHandler<ClosedGeneric<int>,int> { public Task<int> Handle(ClosedGeneric<int> r,CancellationToken t)=>Task.FromResult(Work.Handle(r.Value,r.Probe,t)); }
public sealed class DClosed : D.IRequestHandler<ClosedGeneric<int>,ValueTask<int>> { public ValueTask<int> Handle(ClosedGeneric<int> r,CancellationToken t)=>new(Work.Handle(r.Value,r.Probe,t)); }
[I.Handler][I.Behaviors()] public static partial class IClosed { private static ValueTask<int> HandleAsync(ClosedGeneric<int> r,CancellationToken t)=>new(Work.Handle(r.Value,r.Probe,t)); }
public sealed class ZOpen<T> : Z.IRequestHandler<OpenGeneric<T>,int> { public ValueTask<int> HandleAsync(OpenGeneric<T> r,CancellationToken t)=>new(Work.Handle(r.Value,r.Probe,t)); }
public sealed class MOpen<T> : M.IRequestHandler<OpenGeneric<T>,int> { public Task<int> Handle(OpenGeneric<T> r,CancellationToken t)=>Task.FromResult(Work.Handle(r.Value,r.Probe,t)); }
public sealed class DOpen<T> : D.IRequestHandler<OpenGeneric<T>,ValueTask<int>> { public ValueTask<int> Handle(OpenGeneric<T> r,CancellationToken t)=>new(Work.Handle(r.Value,r.Probe,t)); }
#if PROBE_IMMEDIATE_OPEN
[I.Handler][I.Behaviors()] public static partial class IOpen<T> { private static ValueTask<int> HandleAsync(OpenGeneric<T> r,CancellationToken t)=>new(Work.Handle(r.Value,r.Probe,t)); }
#endif
'@)
    foreach($n in 0,1,4,16) {
        $lines.Add("public sealed record Notice$n(Counter Counter, Probe? Probe=null, bool Asynchronous=false) : Z.INotification,M.INotification,G.INotification,DispatchR.Abstractions.Notification.INotification;")
        if($n) { foreach($i in 1..$n) {
            $lines.Add(@"
public sealed class ZNotice${n}_$i : Z.INotificationHandler<Notice$n> { public ValueTask HandleAsync(Notice$n r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,$i,t); }
public sealed class MNotice${n}_$i : M.INotificationHandler<Notice$n> { public Task Handle(Notice$n r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,$i,t).AsTask(); }
public sealed class GNotice${n}_$i : G.INotificationHandler<Notice$n> { public ValueTask Handle(Notice$n r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,$i,t); }
public sealed class DNotice${n}_$i : DispatchR.Abstractions.Notification.INotificationHandler<Notice$n> { public ValueTask Handle(Notice$n r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,$i,t); }
"@)
        } }
    }
}

function Add-ExtraRegistrations {
    foreach($b in 0,1,3,5) {
        if($b) { foreach($i in 1..$b) {
            $lines.Add("if(library == `"MediatRHistorical`") s.AddTransient<M.IPipelineBehavior<Void$b,M.Unit>,MBehavior$i<Void$b,M.Unit>>();")
            $lines.Add("if(library == `"Mediator`") s.AddSingleton<G.IPipelineBehavior<Void$b,G.Unit>,GBehavior$i<Void$b,G.Unit>>();")
        } }
    }
    $lines.Add(@'
if(library == "MediatRHistorical") {
    s.AddTransient<M.IRequestHandler<OpenGeneric<int>,int>,MOpen<int>>();
    s.AddTransient<M.IRequestHandler<OpenGeneric<string>,int>,MOpen<string>>();
}
if(library == "DispatchR") {
    s.AddScoped<D.IRequestHandler<OpenGeneric<int>,ValueTask<int>>,DOpen<int>>();
    s.AddScoped<D.IRequestHandler<OpenGeneric<string>,ValueTask<int>>,DOpen<string>>();
}
'@)
}

function Add-ExtraBenchmarks {
    foreach($lib in 'Direct','Zendiator','MediatRHistorical','Mediator','DispatchR','Immediate') {
        $entry=@{Direct='ZPing0';Zendiator='Competitive.Generated.IZendiator';MediatRHistorical='M.IMediator';Mediator='G.Mediator';DispatchR='DispatchR.IMediator';Immediate='IClosed.Handler'}[$lib]
        $cases=@()
        if($lib -ne 'Mediator') {$cases+='Closed'}
        if($lib -notin 'Mediator','Immediate') {$cases+='Open'}
        foreach($b in 0,1,3,5) {if($lib -ne 'Direct' -or $b -eq 0){$cases+="Void$b"}}
        if($lib -ne 'Immediate') {foreach($n in 0,1,4,16){$cases+="Notification$n"}}
        foreach($case in $cases) {
            $isvoid=$case.StartsWith('Void'); $isnotice=$case.StartsWith('Notification')
            $type=if($isvoid){$case}elseif($isnotice){$case.Replace('Notification','Notice')}else{"${case}Generic<int>"}
            $result=if($isvoid -or $isnotice){'ValueTask'}else{'ValueTask<int>'}
            $e=if($lib -eq 'Immediate'){if($isvoid){"I$case.Handler"}else{'IClosed.Handler'}}else{$entry}
            $call=if($lib -eq 'Direct'){
                if($isvoid){'Work.Void(r.Counter,r.Probe,t); await ValueTask.CompletedTask;'}
                elseif($isnotice){$n=[int]$case.Replace('Notification','');"for(int i=1;i<=$n;i++) await Work.Notify(r.Counter,r.Probe,r.Asynchronous,i,t);"}
                else{'return Work.Handle(r.Value,r.Probe,t);'}
            } else {
                $method=if($isnotice){if($lib -in 'Zendiator'){'PublishAsync'}else{'Publish'}}elseif($lib -in 'Zendiator'){'SendAsync'}elseif($lib -eq 'Immediate'){'HandleAsync'}else{'Send'}
                $(if(!$isvoid -and !$isnotice){'return '}else{''}) + "await entry.$method(r,t);"
            }
            $resolve=if($lib -eq 'Direct'){'new ZPing0()'}else{"scope.ServiceProvider.GetRequiredService<$e>()"}
            $request=if($isvoid -or $isnotice){'new(counter)'}else{'new()'}
            $asyncField=if($isnotice -and $case -ne 'Notification0' -and $lib -ne 'Direct'){"    private readonly $type asyncRequest = new(new Counter(), Asynchronous: true);`n"}else{''}
            $asyncBenchmark=if($asyncField){"    [Benchmark] public ValueTask DispatchAsync() => Invoke(asyncRequest);`n"}else{''}
            $lines.Add(@"
[MemoryDiagnoser] public class ${lib}$case
{
$asyncField    private ServiceProvider provider=null!; private IServiceScope scope=null!; private $e entry=null!;
    private readonly Counter counter=new(); private $type request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("$lib"); scope=provider.CreateScope(); entry=$resolve; request=$request; ChildEvidence.Record("${lib}$case", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async $result Invoke($type r,CancellationToken t=default) { $call }
    [Benchmark] public $result Dispatch() => Invoke(request);
$asyncBenchmark}
"@)
        }
    }
}

function Add-ExtraGate {
    foreach($lib in 'Direct','Zendiator','MediatRHistorical','Mediator','DispatchR','Immediate') {
        foreach($b in 0,1,3,5) {
            if($lib -eq 'Direct' -and $b){continue}
            $lines.Add("{ var x=new ${lib}Void$b(); x.Setup(); try { await Correctness.Void(`"$lib`",$b,(c,p,t)=>x.Invoke(new Void$b(c,p),t)); } finally {x.Cleanup();} }")
        }
        foreach($case in 'Closed','Open') {
            if($lib -eq 'Mediator'){continue}
            if($lib -eq 'Immediate' -and $case -eq 'Open'){continue}
            $lines.Add("{ var x=new ${lib}$case(); x.Setup(); try { await Correctness.Send(`"$lib-$case`",0,async(p,t)=>await x.Invoke(new ${case}Generic<int>(41,p),t)); } finally {x.Cleanup();} }")
        }
        if($lib -ne 'Immediate') {
            foreach($n in 0,1,4,16) {
                $lines.Add("{ var x=new ${lib}Notification$n(); x.Setup(); try { await Correctness.Notification(`"$lib`",$n,(c,p,a,t)=>x.Invoke(new Notice$n(c,p,a),t)); } finally {x.Cleanup();} }")
            }
        }
        foreach($life in 'Scoped','Singleton') {
            if($lib -eq 'Direct'){continue}
            if($lib -eq 'DispatchR' -and $life -eq 'Singleton'){continue}
            if($lib -eq 'Mediator') {$lines.Add("if(Registration.MediatorLifetime == `"$life`") {")}
            foreach($b in 0,1,3,5) {
                $lines.Add("{ var x=new ${lib}Send$b {Lifetime=`"$life`"}; x.Setup(); try { await Correctness.Send(`"$lib-Matched$life`",$b,async(p,t)=>await x.Invoke(new Ping$b(41,p),t)); if(await x.ScopeK1()!=42 || await x.ResolveSend()!=42) throw new InvalidOperationException(`"Scope result`"); } finally {x.Cleanup();} }")
            }
            if($lib -eq 'Mediator') {$lines.Add("}")}
        }
        if($lib -ne 'Direct') {
            $handler=@{Zendiator='ZPing0';MediatRHistorical='M.IRequestHandler<Ping0,int>';Mediator='G.IRequestHandler<Ping0,int>';DispatchR='D.IRequestHandler<Ping0,ValueTask<int>>';Immediate='IPing0.Handler'}[$lib]
            foreach($life in 'Default','Scoped','Singleton') {
                if($lib -eq 'DispatchR' -and $life -eq 'Singleton'){continue}
                if($lib -eq 'Mediator' -and $life -ne 'Default') {$lines.Add("if(Registration.MediatorLifetime == `"$life`")")}
                $lines.Add("Correctness.Lifetime(`"$lib`",`"$life`",typeof($handler));")
            }
        }
    }
}
