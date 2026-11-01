using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    [Fact]
    public async Task Native_stream_terminal_operations_on_one_plan_are_independent_during_pending_sink_and_reentry()
    {
        var source = NativeStreamFixture("""
            c.Handlers<Items>(DispatchFamily.Stream,typeof(Handler));c.Pipeline<Items>(DispatchFamily.Stream);
            """, """
            public sealed record Items:IStreamRequest<int>;
            public sealed class Probe{
                public int Opened,HandlerConstructions,HandlerDisposals,SinkDisposals;
                public readonly HashSet<int> Active=new();public readonly List<int> Disposed=new();
                public readonly global::Zendiator.Generator.Tests.AsyncOnceGate<bool> FirstSinkGate=new(true);
                public Func<IStreamSink<int>,ValueTask>? Consume;
            }
            [ZendiatorHandler]public sealed class Handler:IStreamSourceHandler<Items,int,Cursor>,IDisposable{
                private readonly Probe p;public Handler(Probe p){this.p=p;p.HandlerConstructions++;}
                public ValueTask<Cursor> OpenAsync(Items r,CancellationToken t){
                    var id=++p.Opened;Check.That(p.Active.Add(id),"each operation starts a fresh source cursor");return new(new Cursor(p,id));
                }
                public void Dispose()=>p.HandlerDisposals++;
            }
            public sealed class Cursor(Probe p,int id):IAsyncEnumerator<int>{
                private int index=-1;private bool disposed;public int Current=>id*100+index;
                public ValueTask<bool> MoveNextAsync(){Check.That(!disposed,"another operation must not dispose this cursor");return new(++index<2);}
                public ValueTask DisposeAsync(){
                    Check.That(!disposed&&p.Active.Remove(id),"each acquired cursor is disposed exactly once");disposed=true;p.Disposed.Add(id);return default;
                }
            }
            public sealed class FirstSink(Probe p):IStreamSink<int>,IDisposable{
                public readonly List<int> Values=new();
                public ValueTask<bool> OnNextAsync(int item,CancellationToken t){Values.Add(item);return Values.Count==1?p.FirstSinkGate.Task:new(true);}
                public void Dispose()=>p.SinkDisposals++;
            }
            public sealed class RecordingSink(Probe p,List<int> values):IStreamSink<int>,IDisposable{
                public ValueTask<bool> OnNextAsync(int item,CancellationToken t){values.Add(item);return new(true);}
                public void Dispose()=>p.SinkDisposals++;
            }
            public sealed class ReenteringSink(Probe p):IStreamSink<int>,IDisposable{
                public readonly List<int> Values=new(),NestedValues=new();
                public async ValueTask<bool> OnNextAsync(int item,CancellationToken t){
                    Values.Add(item);
                    if(Values.Count==1){
                        var consume=p.Consume??throw new InvalidOperationException("reentry delegate is not configured");
                        await consume(new RecordingSink(p,NestedValues)).ConfigureAwait(false);
                    }
                    return true;
                }
                public void Dispose()=>p.SinkDisposals++;
            }
            """, """
            var p=new Probe();var services=new ServiceCollection();services.AddSingleton(p);Register(services);
            await using var provider=services.BuildServiceProvider();await using var scope=provider.CreateAsyncScope();
            var mediator=scope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
            var plan=mediator.StreamSource(new Items());p.Consume=sink=>plan.ConsumeAsync(sink);
            Check.That((object)plan is not IAsyncEnumerable<int>,"the typed plan cannot silently enter a boxed standard enumeration path");
            Check.That(p.Opened==0&&p.HandlerConstructions==0,"creating the reusable plan does not acquire its handler or cursor");
            var firstSink=new FirstSink(p);var first=plan.ConsumeAsync(firstSink).AsTask();
            var secondSink=new ReenteringSink(p);
            try{
                Check.That(!first.IsCompleted&&firstSink.Values.SequenceEqual(new[]{100})&&p.Active.SetEquals(new[]{1}),"first operation is suspended only in its own sink");
                await plan.ConsumeAsync(secondSink).ConfigureAwait(false);
                Check.That(secondSink.Values.SequenceEqual(new[]{200,201})&&secondSink.NestedValues.SequenceEqual(new[]{300,301}),"sink reentry consumes a fresh cursor without replacing its caller's progress");
                Check.That(!first.IsCompleted&&firstSink.Values.SequenceEqual(new[]{100})&&p.Active.SetEquals(new[]{1}),"other terminal completion leaves the pending operation untouched");
                Check.That(p.Disposed.SequenceEqual(new[]{3,2}),"nested and second operations close only their own source cursors");
            }finally{
                p.FirstSinkGate.Complete();await first.ConfigureAwait(false);
            }
            Check.That(firstSink.Values.SequenceEqual(new[]{100,101})&&p.FirstSinkGate.Consumptions==1,"pending sink resumes once with the original cursor's remaining item");
            Check.That(p.Opened==3&&p.Active.Count==0&&p.Disposed.SequenceEqual(new[]{3,2,1}),"all independent operations finish and release their own cursor once");
            Check.That(p.HandlerConstructions==1&&p.HandlerDisposals==0&&p.SinkDisposals==0,"the shared DI receiver and caller sinks remain borrowed");
            await scope.DisposeAsync();Check.That(p.HandlerDisposals==1&&p.SinkDisposals==0,"the DI scope releases the one borrowed handler and leaves caller sinks alone");
            """);
        await RunNativeStreamFixture("terminal-independent-ownership", source);
    }

    [Fact]
    public async Task Native_stream_terminal_matches_public_pull_with_ordinary_source_and_completed_hook_context_flow()
    {
        var source = NativeStreamFixture("""
            c.Handlers<Plain>(DispatchFamily.Stream,typeof(Handler));c.Pipeline<Plain>(DispatchFamily.Stream);
            c.Handlers<Mapped>(DispatchFamily.Stream,typeof(Handler));
            c.Pipeline<Mapped>(DispatchFamily.Stream,Stage.Hook<Filter>(),Stage.Hook<Map>());
            """, """
            public sealed record Plain:IStreamRequest<int>;
            public sealed record Mapped:IStreamRequest<int>;
            public sealed class Probe{
                public static readonly AsyncLocal<string?> Ambient=new();public readonly List<string> Trace=new();
                public void Observe(string route,string phase)=>Trace.Add(route+"/"+phase+"="+Ambient.Value);
            }
            [ZendiatorHandler]public sealed class Handler(Probe p):IStreamSourceHandler<Plain,int,Cursor>,IStreamSourceHandler<Mapped,int,Cursor>{
                public ValueTask<Cursor> OpenAsync(Plain r,CancellationToken t)=>Open("plain");
                public ValueTask<Cursor> OpenAsync(Mapped r,CancellationToken t)=>Open("mapped");
                private ValueTask<Cursor> Open(string route){p.Observe(route,"open");Probe.Ambient.Value=route+":open-change";return new(new Cursor(p,route));}
            }
            public sealed class Cursor(Probe p,string route):IAsyncEnumerator<int>{
                private int index;public int Current=>index;
                public ValueTask<bool> MoveNextAsync(){index++;p.Observe(route,"move"+index);Probe.Ambient.Value=route+":move"+index;return new(index<=2);}
                public ValueTask DisposeAsync(){p.Observe(route,"dispose");Probe.Ambient.Value=route+":dispose-change";return default;}
            }
            public sealed class Filter(Probe p):IAsyncStreamItemFilter<Mapped,int>{
                public ValueTask<bool> AcceptAsync(Mapped r,int item,CancellationToken t){
                    p.Observe("mapped","filter"+item);Probe.Ambient.Value="mapped:filter"+item;return new(true);
                }
            }
            public sealed class Map(Probe p):IAsyncStreamItemMap<Mapped,int>{
                public ValueTask<int> MapAsync(Mapped r,int item,CancellationToken t){
                    p.Observe("mapped","map"+item);Probe.Ambient.Value="mapped:map"+item;return new(item*10);
                }
            }
            public sealed class Sink(Probe p,string route):IStreamSink<int>{
                public readonly List<int> Values=new();
                public ValueTask<bool> OnNextAsync(int item,CancellationToken t){Values.Add(item);p.Observe(route,"sink"+Values.Count);return new(true);}
            }
            public static class PublicPull{
                public static async ValueTask Consume(IAsyncEnumerable<int> source,Sink sink){
                    await foreach(var item in source.ConfigureAwait(false))await sink.OnNextAsync(item,default).ConfigureAwait(false);
                }
            }
            """, """
            var p=new Probe();var services=new ServiceCollection();services.AddSingleton(p);Register(services);
            await using var provider=services.BuildServiceProvider();await using var scope=provider.CreateAsyncScope();
            var mediator=scope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
            var plain=mediator.StreamAsync(new Plain());var mapped=mediator.StreamAsync(new Mapped());
            var plainPlan=mediator.StreamSource(new Plain());var mappedPlan=mediator.StreamSource(new Mapped());
            try{
                await Verify("plain",plain,sink=>plainPlan.ConsumeAsync(sink));
                await Verify("mapped",mapped,sink=>mappedPlan.ConsumeAsync(sink));
            }finally{Probe.Ambient.Value=null;}

            async ValueTask Verify(string route,IAsyncEnumerable<int> feed,Func<Sink,ValueTask> consume){
                Probe.Ambient.Value="request";p.Trace.Clear();var publicSink=new Sink(p,route);
                await PublicPull.Consume(feed,publicSink).ConfigureAwait(false);var expected=p.Trace.ToArray();
                Check.That(Probe.Ambient.Value=="request","public consumption preserves its caller's context");
                p.Trace.Clear();var terminalSink=new Sink(p,route);await consume(terminalSink).ConfigureAwait(false);
                Check.That(Probe.Ambient.Value=="request","terminal source and cleanup changes do not escape into its caller");
                Check.That(p.Trace.SequenceEqual(expected)&&terminalSink.Values.SequenceEqual(publicSink.Values),"terminal and public pull preserve the same callback context and results");
                Check.That(p.Trace.Contains(route+"/open=request")&&p.Trace.Contains(route+"/move1="+route+":open-change"),"synchronous Open changes reach the first Move within the same activation flow");
                Check.That(p.Trace.Contains(route+"/sink1=request")&&p.Trace.Contains(route+"/move2=request"),"first Move changes stay inside the first-Move async boundary");
                if(route=="mapped"){
                    Check.That(terminalSink.Values.SequenceEqual(new[]{10,20}),"the mapped route actually executes both async-contract callbacks");
                    Check.That(p.Trace.Contains("mapped/filter1=mapped:move1")&&p.Trace.Contains("mapped/map1=mapped:filter1")&&p.Trace.Contains("mapped/filter2=mapped:move2")&&p.Trace.Contains("mapped/map2=mapped:filter2"),"filter changes reach the following map inside the item loop without leaking to the outer sink");
                    Check.That(p.Trace.Contains("mapped/sink2=request")&&p.Trace.Contains("mapped/dispose=request"),"the async item loop restores the caller context before its sink and outer cleanup");
                }else{
                    Check.That(terminalSink.Values.SequenceEqual(new[]{1,2}),"plain route values");
                    Check.That(p.Trace.Contains("plain/sink2=plain:move2")&&p.Trace.Contains("plain/dispose=plain:move3"),"later plain synchronous Move context flows to its sink and cleanup");
                }
            }
            """);
        await RunNativeStreamFixture("terminal-context-boundaries", source);
    }

    [Fact]
    public async Task Native_stream_active_upstream_cleanup_preserves_GetResult_context_and_error_state()
    {
        var source = NativeStreamFixture("""
            c.Handlers<Items>(DispatchFamily.Stream,typeof(Handler));
            c.Pipeline<Items>(DispatchFamily.Stream,Stage.Wrapper<Opaque>());
            """, """
            public sealed record Items:IStreamRequest<int>;
            public sealed class Probe(string mode){
                public static readonly AsyncLocal<string?> Ambient=new();
                public readonly SynchronizationContext ChangedContext=new();
                public readonly bool Pending=mode=="pending";
                public readonly Exception? Error=mode=="fault"?new InvalidOperationException("source cleanup"):
                    mode=="cancel"?new OperationCanceledException("source cleanup",null,new CancellationToken(true)):null;
                public readonly List<string> Trace=new();
                public IAsyncEnumerator<int>? Child;
                public DisposeResult? Cleanup;
                public int Opened,RawMoves,RawDisposals,Consumptions,OpaqueDisposals,Registrations;
            }
            [ZendiatorHandler]public sealed class Handler(Probe p):IStreamSourceHandler<Items,int,RawCursor>{
                public ValueTask<RawCursor> OpenAsync(Items r,CancellationToken token){p.Opened++;return new(new RawCursor(p));}
            }
            public sealed class RawCursor(Probe p):IAsyncEnumerator<int>{
                public int Current=>throw new InvalidOperationException("the opaque cursor supplies Current");
                public ValueTask<bool> MoveNextAsync(){p.RawMoves++;return new(true);}
                public ValueTask DisposeAsync(){
                    Check.That(++p.RawDisposals==1,"the retained active child is disposed once");
                    p.Trace.Add("source.Dispose");p.Cleanup=new DisposeResult(p);return new(p.Cleanup,0);
                }
            }
            public sealed class DisposeResult:global::System.Threading.Tasks.Sources.IValueTaskSource{
                private readonly Probe p;
                private global::System.Threading.Tasks.Sources.ManualResetValueTaskSourceCore<bool> core;
                public DisposeResult(Probe p){
                    this.p=p;if(!p.Pending){if(p.Error is null)core.SetResult(true);else core.SetException(p.Error);}
                }
                public global::System.Threading.Tasks.Sources.ValueTaskSourceStatus GetStatus(short token)=>core.GetStatus(token);
                public void GetResult(short token){
                    Check.That(token==0&&++p.Consumptions==1,"completed raw cleanup is consumed once");
                    p.Trace.Add("source.GetResult");Probe.Ambient.Value="source cleanup result";
                    SynchronizationContext.SetSynchronizationContext(p.ChangedContext);
                    core.GetResult(token);
                }
                public void Complete()=>core.SetResult(true);
                public void OnCompleted(Action<object?> continuation,object? state,short token,
                    global::System.Threading.Tasks.Sources.ValueTaskSourceOnCompletedFlags flags){
                    Check.That(p.Pending&&++p.Registrations==1,"only pending cleanup registers its single continuation");
                    core.OnCompleted(continuation,state,token,flags);
                }
            }
            public sealed class Opaque(Probe p):IStreamMiddleware<Items,int>{
                public async ValueTask<IAsyncEnumerator<int>> OpenAsync(Items r,IStreamUpstream<Items,int> next,CancellationToken token){
                    p.Child=await next.OpenAsync(r,token).ConfigureAwait(false);return new OpaqueCursor(p);
                }
            }
            public sealed class OpaqueCursor(Probe p):IAsyncEnumerator<int>{
                public int Current=>42;
                public ValueTask<bool> MoveNextAsync()=>new(true);
                public ValueTask DisposeAsync(){
                    Check.That(++p.OpaqueDisposals==1,"the returned opaque cursor is disposed once");
                    p.Trace.Add("opaque.Dispose");return default;
                }
            }
            """, """
            foreach(var mode in new[]{"success","fault","cancel","pending"}){
                var p=new Probe(mode);var services=new ServiceCollection();services.AddSingleton(p);Register(services);
                await using var provider=services.BuildServiceProvider();await using var scope=provider.CreateAsyncScope();
                var mediator=scope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
                var cursor=mediator.StreamAsync(new Items()).GetAsyncEnumerator();
                var originalContext=SynchronizationContext.Current;
                SynchronizationContext? callerContext=p.Pending?null:new SynchronizationContext();
                try{
                    Probe.Ambient.Value="cleanup caller";SynchronizationContext.SetSynchronizationContext(callerContext);
                    Check.That(await cursor.MoveNextAsync()&&cursor.Current==42,"the real public opaque pipeline supplies its item");
                    Check.That(p.Opened==1&&p.RawMoves==0&&p.RawDisposals==0&&
                        p.Child is global::Zendiator.Streaming.StreamCursorState {IsClosed:false},
                        "opaque return leaves an initialized active upstream child for parent cleanup");
                    var disposal=cursor.DisposeAsync();
                    Task? shared=null;
                    Check.That(Probe.Ambient.Value=="cleanup caller"&&ReferenceEquals(SynchronizationContext.Current,callerContext),
                        "synchronous cleanup GetResult mutations are isolated before Dispose returns");
                    if(p.Pending){
                        shared=disposal.AsTask();var repeated=cursor.DisposeAsync().AsTask();
                        Check.That(!disposal.IsCompleted&&ReferenceEquals(shared,repeated)&&p.Registrations==1&&p.Consumptions==0,
                            "the retained active child truly suspends cleanup and repeated Dispose joins the same Task");
                        p.Cleanup!.Complete();
                        Check.That(Probe.Ambient.Value=="cleanup caller"&&SynchronizationContext.Current is null,
                            "inline cleanup continuation does not leak ambient changes into its completing caller");
                    }
                    else
                    Check.That(disposal.IsCompleted&&disposal.IsCanceled==(p.Error is OperationCanceledException)&&
                        disposal.IsFaulted==(p.Error is not null&&p.Error is not OperationCanceledException),
                        "the completed public cleanup preserves success, fault and cancellation state");
                    Exception? observed=null;
                    try{if(shared is null)await disposal.ConfigureAwait(false);else await shared.ConfigureAwait(false);}
                    catch(Exception error){observed=error;}
                    Check.That(ReferenceEquals(observed,p.Error),"cleanup preserves the original exception instance");
                    Check.That(Probe.Ambient.Value=="cleanup caller"&&ReferenceEquals(SynchronizationContext.Current,callerContext),
                        "awaiting cleanup does not leak source GetResult context changes");
                    Check.That(p.RawDisposals==1&&p.Consumptions==1&&p.OpaqueDisposals==1&&
                        p.Trace.SequenceEqual(new[]{"opaque.Dispose","source.Dispose","source.GetResult"}),
                        "the nonempty upstream cleanup follows opaque cleanup and consumes the source once");
                    Check.That(p.Child is global::Zendiator.Streaming.StreamCursorState {IsClosed:true},
                        "parent finally closes the retained upstream even when its cleanup fails");
                }finally{Probe.Ambient.Value=null;SynchronizationContext.SetSynchronizationContext(originalContext);}
            }
            """);
        await RunNativeStreamFixture("active-upstream-cleanup-context", source);
    }
}
