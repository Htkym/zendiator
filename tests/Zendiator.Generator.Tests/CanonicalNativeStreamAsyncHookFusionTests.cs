using System.Threading.Tasks.Sources;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed class StreamHookRaceSource<T>(T value, string completion) : IValueTaskSource<T>
{
    private Action<object?>? _continuation;
    private object? _state;
    private bool _completed;
    private readonly TaskCompletionSource<bool> _registered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Registrations, Consumptions, InlineCallbacks;
    public bool Completing { get; private set; }
    public ValueTask<T> Task => new(this, 0);
    public Task Registered => _registered.Task;
    public ValueTaskSourceStatus GetStatus(short token)
    {
        if (_completed) return ValueTaskSourceStatus.Succeeded;
        if (completion == "before-registration") _completed = true;
        return ValueTaskSourceStatus.Pending;
    }
    public T GetResult(short token)
    {
        if (!_completed || ++Consumptions != 1) throw new InvalidOperationException("Raw result must settle and be consumed once.");
        return value;
    }
    public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
    {
        if (++Registrations != 1 || _continuation is not null) throw new InvalidOperationException("One continuation per raw operation.");
        if (flags.HasFlag(ValueTaskSourceOnCompletedFlags.UseSchedulingContext)) throw new InvalidOperationException("ConfigureAwait(false) expected.");
        if (_completed)
        {
            _registered.SetResult(true);
            InlineCallbacks++;
            continuation(state);
            return;
        }
        _continuation = continuation;
        _state = state;
        _registered.SetResult(true);
        if (completion == "inside-registration") Complete();
    }
    public void Complete()
    {
        if (_completed || _continuation is null) throw new InvalidOperationException("Complete one registered pending operation.");
        _completed = true;
        var continuation = _continuation;
        var state = _state;
        _continuation = null;
        _state = null;
        Completing = true;
        try { InlineCallbacks++; continuation(state); }
        finally { Completing = false; }
    }
}

public sealed partial class GeneratorTests
{
    [Fact]
    public async Task Native_stream_async_hooks_settle_registration_races_before_reentrant_next_generation()
    {
        var source = NativeStreamFixture("""
            c.Handlers<Items>(DispatchFamily.Stream,typeof(Handler));
            c.StreamPipeline<Items,string>(Stage.Hook<Filter>(),Stage.Hook<Map>());
            """, """
            public sealed record Items:IStreamRequest<int>;
            public sealed class Probe{
                public readonly global::Zendiator.Generator.Tests.StreamHookRaceSource<bool> FirstSource=new(true,"inside-registration"),RejectedFilter=new(false,"before-registration");
                public readonly global::Zendiator.Generator.Tests.StreamHookRaceSource<string> FirstMap=new("v2","manual"),LaterMap=new("v3","manual");
                public int Opened,Pulls,Reads,Filters,Maps,Disposals;
            }
            [ZendiatorHandler]public sealed class Handler(Probe p):IStreamSourceHandler<Items,int,Cursor>{
                public ValueTask<Cursor> OpenAsync(Items r,CancellationToken t){p.Opened++;return new(new Cursor(p));}
            }
            public sealed class Cursor(Probe p):IAsyncEnumerator<int>{
                public int Current{get{p.Reads++;return p.Pulls;}}
                public ValueTask<bool> MoveNextAsync(){p.Pulls++;return p.Pulls==1?p.FirstSource.Task:new(true);}
                public ValueTask DisposeAsync(){p.Disposals++;return default;}
            }
            public sealed class Filter(Probe p):IAsyncStreamItemFilter<Items,int>{
                public ValueTask<bool> AcceptAsync(Items r,int item,CancellationToken t){p.Filters++;return item==1?p.RejectedFilter.Task:new(true);}
            }
            public sealed class Map(Probe p):IAsyncStreamItemMap<Items,int,string>{
                public ValueTask<string> MapAsync(Items r,int item,CancellationToken t){p.Maps++;return item==2?p.FirstMap.Task:p.LaterMap.Task;}
            }
            """, """
            var p=new Probe();var services=new ServiceCollection();services.AddSingleton(p);Register(services);
            await using var provider=services.BuildServiceProvider();await using var scope=provider.CreateAsyncScope();
            var mediator=scope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
            IAsyncEnumerable<string> feed=mediator.StreamAsync(new Items());var cursor=feed.GetAsyncEnumerator();
            var old=cursor.MoveNextAsync().AsTask();
            await p.FirstMap.Registered.ConfigureAwait(false);
            Check.That(!old.IsCompleted&&p.Pulls==2&&p.Reads==2&&p.Filters==2&&p.Maps==1,"registration races seek exactly one accepted item and then wait in map");
            Check.That(p.FirstSource.Registrations==1&&p.FirstSource.Consumptions==1&&p.FirstSource.InlineCallbacks==1&&p.RejectedFilter.Registrations==1&&p.RejectedFilter.Consumptions==1&&p.RejectedFilter.InlineCallbacks==1,"source inline registration and filter completion before registration each settle once");
            Check.Invalid(()=>{_=cursor.MoveNextAsync();});Check.Invalid(()=>{_=cursor.DisposeAsync();});
            var reentered=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<bool>? later=null;var inlinePublicReentry=false;
            var published=old.ContinueWith(_=>{
                try{
                    Check.That(cursor.Current=="v2","public completion publishes Current before reentry");
                    inlinePublicReentry=p.FirstMap.Completing;
                    later=cursor.MoveNextAsync().AsTask();
                    Check.That(!later.IsCompleted&&p.Pulls==3&&p.Reads==3&&p.Filters==3&&p.Maps==2,"consumer continuation starts a separate pending generation without prefetch");
                    reentered.SetResult(true);
                }catch(Exception error){reentered.SetException(error);}
            },CancellationToken.None,TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
            await Task.Run(()=>{
                Check.That(SynchronizationContext.Current==null&&TaskScheduler.Current==TaskScheduler.Default,"raw completion uses the default scheduling context for forced public inline reentry");
                p.FirstMap.Complete();
            }).ConfigureAwait(false);
            await reentered.Task.ConfigureAwait(false);
            await published.ConfigureAwait(false);
            await p.LaterMap.Registered.ConfigureAwait(false);
            Check.That(inlinePublicReentry,"the continuation must exercise public continuation reentry before raw completion returns");
            Check.That(p.FirstMap.Consumptions==1&&p.FirstMap.InlineCallbacks==1&&p.LaterMap.Consumptions==0,"old raw callback is consumed before later pending work");
            Check.Invalid(()=>{_=cursor.MoveNextAsync();});Check.Invalid(()=>{_=cursor.DisposeAsync();});
            await Task.Run(p.LaterMap.Complete).ConfigureAwait(false);Check.That(await later!.ConfigureAwait(false)&&cursor.Current=="v3","later generation publishes its own mapped result");
            await cursor.DisposeAsync();await cursor.DisposeAsync();
            Check.That(await old.ConfigureAwait(false)&&!await cursor.MoveNextAsync(),"delayed old public Task remains true after newer generation and disposal");
            Check.That(p.Opened==1&&p.Pulls==3&&p.Reads==3&&p.Filters==3&&p.Maps==2&&p.Disposals==1&&p.LaterMap.Consumptions==1&&p.LaterMap.InlineCallbacks==1,"early disposal does not pull or consume twice");
            """);
        await RunNativeStreamFixture("async-hook-registration-reentry", source);
    }

    [Fact]
    public async Task Native_stream_async_map_releases_closed_output_without_changing_Current_reenumeration_or_cleanup()
    {
        var source = NativeStreamFixture("""
            c.Handlers<Items>(DispatchFamily.Stream,typeof(Handler));
            c.StreamPipeline<Items,Payload>(Stage.Hook<Map>());
            """, """
            public sealed record Items(string Mode):IStreamRequest<int>;
            public sealed class Payload(int value){public int Value{get;}=value;}
            public sealed class Probe{
                public int Opened,Maps,Reads,Disposals;public WeakReference<Payload>? Last;
                public readonly HashSet<int> Closed=new();
                public readonly Exception MoveError=new FormatException("move"),CleanupError=new ArithmeticException("cleanup");
            }
            [ZendiatorHandler]public sealed class Handler(Probe p):IStreamSourceHandler<Items,int,Cursor>{
                public ValueTask<Cursor> OpenAsync(Items r,CancellationToken t)=>new(new Cursor(p,r.Mode,++p.Opened));
            }
            public sealed class Cursor(Probe p,string mode,int id):IAsyncEnumerator<int>{
                private bool moved;
                public int Current{get{p.Reads++;return id;}}
                public ValueTask<bool> MoveNextAsync(){
                    if(!moved){moved=true;return new(true);}
                    if(mode=="fault")throw p.MoveError;
                    return new(false);
                }
                public ValueTask DisposeAsync(){
                    p.Disposals++;Check.That(p.Closed.Add(id),"raw cursor cleanup occurs once");
                    if(mode=="fault")throw p.CleanupError;
                    return default;
                }
            }
            public sealed class Map(Probe p):IAsyncStreamItemMap<Items,int,Payload>{
                public ValueTask<Payload> MapAsync(Items r,int item,CancellationToken t){
                    var output=new Payload(item);p.Last=new(output);p.Maps++;return new(output);
                }
            }
            public sealed class Held(IAsyncEnumerable<Payload> feed,WeakReference<Payload> weak,string mode,int id){
                public readonly IAsyncEnumerable<Payload> Feed=feed;public readonly WeakReference<Payload> Weak=weak;
                public readonly string Mode=mode;public readonly int Id=id;
            }
            public static class Memory{
                [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
                public static Held Open(global::NativeStream.Generated.Reads.IZendiator mediator,Probe p,string mode)
                    =>OpenCore(mediator,p,mode).GetAwaiter().GetResult();
                private static async Task<Held> OpenCore(global::NativeStream.Generated.Reads.IZendiator mediator,Probe p,string mode){
                    IAsyncEnumerable<Payload> feed=mediator.StreamAsync(new Items(mode));var cursor=feed.GetAsyncEnumerator();
                    Check.That(ReferenceEquals(feed,cursor),"the first public enumerable is the owner retained by this test");
                    Check.Invalid(()=>{_=cursor.Current;});
                    Check.That(await cursor.MoveNextAsync(),"mapped first item");ReadCurrent(cursor,p.Opened);
                    return new(feed,p.Last!,mode,p.Opened);
                }
                [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
                public static void ReadCurrent(IAsyncEnumerator<Payload> cursor,int expected)
                    =>Check.That(cursor.Current.Value==expected,"Current exposes the mapped value before close");
                [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
                public static void Close(Held state,Probe p)=>CloseCore(state,p).GetAwaiter().GetResult();
                private static async Task CloseCore(Held state,Probe p){
                    var cursor=(IAsyncEnumerator<Payload>)state.Feed;
                    if(state.Mode=="fault"){
                        Exception? caught=null;try{await cursor.MoveNextAsync();}catch(Exception error){caught=error;}
                        Check.That(ReferenceEquals(caught,p.CleanupError),"the original cleanup failure has priority over the source Move failure");
                        await cursor.DisposeAsync();await cursor.DisposeAsync();
                    }else{
                        if(state.Mode=="eof")Check.That(!await cursor.MoveNextAsync(),"normal EOF");
                        await cursor.DisposeAsync();await cursor.DisposeAsync();
                    }
                    Check.Invalid(()=>{_=cursor.Current;});
                }
                [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
                public static bool Alive(WeakReference<Payload> weak)=>weak.TryGetTarget(out _);
                public static void Collect(){
                    GC.Collect(GC.MaxGeneration,GCCollectionMode.Forced,true,true);GC.WaitForPendingFinalizers();
                    GC.Collect(GC.MaxGeneration,GCCollectionMode.Forced,true,true);
                }
                [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
                public static void CheckActive(Held[] states){
                    Collect();Check.That(states.All(state=>Alive(state.Weak)),"valid Current retains every live mapped output");
                    foreach(var state in states)GC.KeepAlive(state.Feed);
                }
                [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
                public static void CheckClosed(Held[] states){
                    for(var attempt=0;attempt<3;attempt++){Collect();if(states.All(state=>!Alive(state.Weak)))break;}
                    var retained=states.Where(state=>Alive(state.Weak)).Select(state=>state.Mode).ToArray();
                    foreach(var state in states)GC.KeepAlive(state.Feed);
                    Check.That(retained.Length==0,"closed enumerable retained mapped output: "+string.Join(",",retained));
                }
                [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
                public static void Reopen(Held state,Probe p)=>ReopenCore(state,p).GetAwaiter().GetResult();
                private static async Task ReopenCore(Held state,Probe p){
                    var cursor=state.Feed.GetAsyncEnumerator();
                    Check.That(!ReferenceEquals(state.Feed,cursor),"re-enumeration owns an independent cursor");
                    Check.That(await cursor.MoveNextAsync(),"closed source can start a fresh enumeration");ReadCurrent(cursor,p.Opened);
                    Check.Invalid(()=>{_=((IAsyncEnumerator<Payload>)state.Feed).Current;});
                    Check.That(!await cursor.MoveNextAsync(),"independent enumeration reaches EOF");
                    await cursor.DisposeAsync();Check.Invalid(()=>{_=cursor.Current;});
                }
            }
            """, """
            var p=new Probe();var services=new ServiceCollection();services.AddSingleton(p);Register(services);
            await using var provider=services.BuildServiceProvider();await using var scope=provider.CreateAsyncScope();
            var mediator=scope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
            var states=new[]{Memory.Open(mediator,p,"eof"),Memory.Open(mediator,p,"early"),Memory.Open(mediator,p,"fault")};
            Memory.CheckActive(states);
            foreach(var state in states)Memory.Close(state,p);
            Check.That(p.Opened==3&&p.Maps==3&&p.Reads==3&&p.Disposals==3&&p.Closed.Count==3,"all first owners close once without another map or Current read");
            Memory.CheckClosed(states);
            Memory.Reopen(states[0],p);
            Check.That(p.Opened==4&&p.Maps==4&&p.Reads==4&&p.Disposals==4&&p.Closed.Count==4,"re-enumeration preserves fresh acquisition and single cleanup");
            GC.KeepAlive(states);
            """);
        await RunNativeStreamFixture("async-hook-closed-output", source);
    }
}
