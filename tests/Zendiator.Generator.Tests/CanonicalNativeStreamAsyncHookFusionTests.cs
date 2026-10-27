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
}
