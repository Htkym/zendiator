using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    [Fact]
    public async Task Native_stream_middleware_sessions_share_pull_and_terminal_stop_and_reverse_owned_cleanup()
    {
        var source = NativeStreamFixture("""
            c.Handlers<Items>(DispatchFamily.Stream,HandlerEntry.Session<Handler,HandlerFactory>());
            c.Pipeline<Items>(DispatchFamily.Stream,Stage.Wrapper<Repeat,RepeatFactory>(),Stage.Hook<Map,MapFactory>(),Stage.Hook<BorrowedFilter>());
            """, """
            public sealed record Items:IStreamRequest<int>;
            public sealed class Probe(string mode){
                public readonly string Mode=mode;public readonly CancellationTokenSource Cancellation=new();
                public readonly List<string> Created=new(),Released=new(),Live=new();public readonly List<int> Values=new();
                public int Id,Opened,Produced,CursorDisposals,MiddlewareCursorDisposals,Maps,SinkCalls,DiCreated,DiReleased,HandlerFactoryDiCreated,BorrowedFilterDiCreated,ReopenDenied;
                public readonly Exception FactoryError=new InvalidOperationException("factory");
                public readonly Exception SourceError=new FormatException("source");public readonly Exception CleanupError=new ArithmeticException("cleanup");
                public global::Zendiator.Generator.Tests.AsyncOnceGate<Handler>? FactoryGate;
                public readonly global::Zendiator.Generator.Tests.AsyncOnceGate<int> MapGate=new(10);
                public readonly global::Zendiator.Generator.Tests.AsyncOnceGate<bool> SinkGate=new(false);
                public string Create(string role){var name=role+(++Id);Created.Add(name);Live.Add(name);return name;}
                public void Release(string name){Check.That(Live.Count!=0&&Live[^1]==name,"owned resources release in reverse acquisition order");Live.RemoveAt(Live.Count-1);Released.Add(name);}
            }
            [ZendiatorHandler]public sealed class Handler(Probe p,string name):IStreamSourceHandler<Items,int,Cursor>,IAsyncDisposable{
                public ValueTask<Cursor> OpenAsync(Items r,CancellationToken t){p.Opened++;return new(new Cursor(p,t));}
                public ValueTask DisposeAsync(){p.Release(name);return default;}
            }
            public sealed class Cursor(Probe p,CancellationToken token):IAsyncEnumerator<int>{
                private int index=-1;public int Current=>index;
                public ValueTask<bool> MoveNextAsync(){token.ThrowIfCancellationRequested();index++;if(index==1&&p.Mode is "source-failure" or "cleanup-failure")throw p.SourceError;if(index==2)return new(false);p.Produced++;return new(true);}
                public ValueTask DisposeAsync(){p.CursorDisposals++;return default;}
            }
            public sealed class HandlerFactory:IAsyncOwnedSessionFactory<Items,Handler>,IDisposable{
                private readonly Probe p;public HandlerFactory(Probe p){this.p=p;p.DiCreated++;p.HandlerFactoryDiCreated++;}
                public ValueTask<Handler> CreateAsync(Items r,CancellationToken t){var value=new Handler(p,p.Create("H"));if(p.Mode!="pending-factory")return new(value);p.FactoryGate=new(value);return p.FactoryGate.Task;}
                public void Dispose()=>p.DiReleased++;
            }
            public sealed class Map(Probe p,string name):IAsyncStreamItemMap<Items,int>,IAsyncDisposable{
                public ValueTask<int> MapAsync(Items r,int item,CancellationToken t){p.Maps++;return p.Mode=="pending-map"&&p.Maps==1?p.MapGate.Task:new(item+10);}
                public ValueTask DisposeAsync(){p.Release(name);if(p.Mode=="cleanup-failure")throw p.CleanupError;return default;}
            }
            public sealed class MapFactory:IOwnedSessionFactory<Items,Map>,IDisposable{
                private readonly Probe p;public MapFactory(Probe p){this.p=p;p.DiCreated++;}
                public Map Create(Items r,CancellationToken t){if(p.Mode=="partial-startup")throw p.FactoryError;return new(p,p.Create("M"));}
                public void Dispose()=>p.DiReleased++;
            }
            public sealed class BorrowedFilter:IStreamItemFilter<Items,int>,IDisposable{
                private readonly Probe p;public BorrowedFilter(Probe p){this.p=p;p.DiCreated++;p.BorrowedFilterDiCreated++;}
                public bool Accept(Items r,int item,CancellationToken t)=>item>=0;
                public void Dispose()=>p.DiReleased++;
            }
            public sealed class Repeat(Probe p,string name):IStreamMiddleware<Items,int>,IAsyncDisposable{
                public ValueTask<IAsyncEnumerator<int>> OpenAsync(Items r,IStreamUpstream<Items,int> next,CancellationToken t){
                    if(p.Mode=="short-circuit")return new(new Empty(p));
                    return new(Run(r,next,t).GetAsyncEnumerator(t));
                }
                private sealed class Empty(Probe p):IAsyncEnumerator<int>{
                    public int Current=>throw new InvalidOperationException("Current at EOF");
                    public ValueTask<bool> MoveNextAsync()=>new(false);
                    public ValueTask DisposeAsync(){p.MiddlewareCursorDisposals++;return default;}
                }
                private async IAsyncEnumerable<int> Run(Items r,IStreamUpstream<Items,int> next,[System.Runtime.CompilerServices.EnumeratorCancellation]CancellationToken t){
                    try{
                        for(var branch=0;branch<(p.Mode=="repeat"?2:1);branch++){
                            await using var cursor=await next.OpenAsync(r,t).ConfigureAwait(false);
                            while(await cursor.MoveNextAsync().ConfigureAwait(false))yield return cursor.Current;
                        }
                    }finally{
                        if(p.Mode=="stop-cancel"){
                            try{var forbidden=await next.OpenAsync(r,t).ConfigureAwait(false);await forbidden.DisposeAsync();throw new Exception("upstream reopened after terminal stop");}
                            catch(InvalidOperationException){p.ReopenDenied++;}
                        }
                    }
                }
                public ValueTask DisposeAsync(){p.Release(name);return default;}
            }
            public sealed class RepeatFactory:IOwnedSessionFactory<Items,Repeat>,IDisposable{
                private readonly Probe p;public RepeatFactory(Probe p){this.p=p;p.DiCreated++;}
                public Repeat Create(Items r,CancellationToken t)=>new(p,p.Create("W"));
                public void Dispose()=>p.DiReleased++;
            }
            public readonly struct Sink(Probe p):IStreamSink<int>{
                public ValueTask<bool> OnNextAsync(int item,CancellationToken t){
                    Check.That(t==p.Cancellation.Token,"sink receives the operation token");p.SinkCalls++;p.Values.Add(item);
                    if(p.Mode=="pending-sink")return p.SinkGate.Task;
                    if(p.Mode is "stop-cancel" or "cancel-continue")p.Cancellation.Cancel();
                    return new(p.Mode!="stop-cancel");
                }
            }
            """, """
            foreach(var mode in new[]{"short-circuit","repeat","pending-factory","pending-map","pending-sink","stop-cancel","cancel-continue","partial-startup","source-failure","cleanup-failure"}){
                var p=new Probe(mode);var services=new ServiceCollection();services.AddSingleton(p);Register(services);
                await using var provider=services.BuildServiceProvider();var scope=provider.CreateAsyncScope();
                var mediator=scope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
                var task=mediator.StreamAsync(new Items(),p.Cancellation.Token).ConsumeAsync(new Sink(p)).AsTask();
                if(mode=="pending-factory"){
                    Check.That(!task.IsCompleted&&p.FactoryGate!=null&&p.FactoryGate.Consumptions==0&&p.Opened==0&&p.Released.Count==0,"pending factory ownership is not released early");p.FactoryGate!.Complete();
                }else if(mode=="pending-map"){
                    Check.That(!task.IsCompleted&&p.Produced==1&&p.SinkCalls==0&&p.Released.Count==0,"async map preserves backpressure");p.MapGate.Complete();
                }else if(mode=="pending-sink"){
                    Check.That(!task.IsCompleted&&p.Produced==1&&p.CursorDisposals==0&&p.Released.Count==0,"pending sink is drained before cleanup");p.Cancellation.Cancel();p.SinkGate.Complete();
                }
                Exception? error=null;try{await task.ConfigureAwait(false);}catch(Exception caught){error=caught;}
                if(mode=="partial-startup")Check.That(ReferenceEquals(error,p.FactoryError)&&p.CursorDisposals==p.Opened&&p.Created.All(n=>!n.StartsWith("M",StringComparison.Ordinal))&&p.Released.SequenceEqual(p.Created.AsEnumerable().Reverse()),"partial acquisition preserves factory error and unwinds exactly the acquired sessions and cursor");
                else if(mode=="source-failure")Check.That(ReferenceEquals(error,p.SourceError)&&p.CursorDisposals==1,"source error and cursor cleanup");
                else if(mode=="cleanup-failure")Check.That(ReferenceEquals(error,p.CleanupError)&&p.CursorDisposals==1,"cleanup failure follows finally priority without skipping outer cleanup");
                else if(mode=="cancel-continue")Check.That(error is OperationCanceledException&&p.SinkCalls==1&&p.CursorDisposals==1,"true permits the next cancellation check");
                else{
                    Check.That(error==null,"normal terminal result");
                    var expected=mode=="short-circuit"?Array.Empty<int>():mode=="repeat"?new[]{10,11,10,11}:mode is "stop-cancel" or "pending-sink"?new[]{10}:new[]{10,11};
                    Check.That(p.Values.SequenceEqual(expected),"middleware branch result/order");
                    Check.That(p.Opened==(mode=="short-circuit"?0:mode=="repeat"?2:1)&&p.CursorDisposals==p.Opened,"normal EOF closes each lease before another open");
                }
                if(mode=="short-circuit"){
                    // Map is downstream of the opaque boundary; its owned session is acquired on first Move.
                    Check.That(p.HandlerFactoryDiCreated==0&&p.Opened==0&&p.CursorDisposals==0&&p.MiddlewareCursorDisposals==1,"zero-next middleware leaves upstream unopened and disposes exactly its returned cursor");
                    Check.That(p.Created.Select(n=>n[0]).Order().SequenceEqual(new[]{'M','W'})&&p.Released.SequenceEqual(p.Created.AsEnumerable().Reverse()),"acquired downstream Map and middleware sessions release once in reverse order");
                    Check.That(p.Maps==0&&p.BorrowedFilterDiCreated==0&&p.DiCreated==2,"empty middleware output invokes no Map callback or borrowed Filter resolution");
                }
                if(mode=="stop-cancel")Check.That(p.ReopenDenied==1&&p.Produced==1,"accepted false is monotonic and wins cancellation");
                if(mode=="pending-factory")Check.That(p.FactoryGate!.Consumptions==1,"factory ValueTask consumed once");
                if(mode=="pending-map")Check.That(p.MapGate.Consumptions==1,"map ValueTask consumed once");
                if(mode=="pending-sink")Check.That(p.SinkGate.Consumptions==1,"sink ValueTask consumed once");
                Check.That(p.Live.Count==0&&p.Created.Count==p.Released.Count&&p.DiReleased==0,"owned cleanup complete while DI services stay borrowed");
                await scope.DisposeAsync();Check.That(p.DiReleased==p.DiCreated,"DI owner releases factories and borrowed hooks");p.Cancellation.Dispose();
            }
            """);
        await RunNativeStreamFixture("middleware-session", source);
    }

    [Fact]
    public async Task Native_stream_pending_async_filter_false_advances_or_cancels_before_next_pull_and_cleans_once()
    {
        var source = NativeStreamFixture("""
            c.Handlers<Items>(DispatchFamily.Stream,HandlerEntry.Session<Handler,HandlerFactory>());
            c.Pipeline<Items>(DispatchFamily.Stream,Stage.Hook<PendingFilter>(),Stage.Hook<Mapper>());
            """, """
            public sealed record Items:IStreamRequest<int>;
            public sealed class Probe{
                public readonly CancellationTokenSource Cancellation=new();
                public readonly global::Zendiator.Generator.Tests.AsyncOnceGate<bool> FilterGate=new(false);
                public readonly List<int> Filtered=new(),Mapped=new(),Values=new();
                public readonly List<string> Cleanup=new();
                public int Opened,Pulls,CursorDisposals,SessionsCreated,SessionDisposals,FactoryDisposals,FilterDisposals;
            }
            [ZendiatorHandler]public sealed class Handler(Probe p):IStreamSourceHandler<Items,int,Cursor>,IAsyncDisposable{
                public ValueTask<Cursor> OpenAsync(Items r,CancellationToken t){
                    Check.That(t==p.Cancellation.Token,"source receives operation token");p.Opened++;return new(new Cursor(p,t));
                }
                public ValueTask DisposeAsync(){p.SessionDisposals++;p.Cleanup.Add("handler");return default;}
            }
            public sealed class HandlerFactory(Probe p):IOwnedSessionFactory<Items,Handler>,IDisposable{
                public Handler Create(Items r,CancellationToken t){p.SessionsCreated++;return new(p);}
                public void Dispose()=>p.FactoryDisposals++;
            }
            public sealed class Cursor(Probe p,CancellationToken token):IAsyncEnumerator<int>{
                private int item;public int Current=>item;
                public ValueTask<bool> MoveNextAsync(){p.Pulls++;token.ThrowIfCancellationRequested();return new(++item<=2);}
                public ValueTask DisposeAsync(){p.CursorDisposals++;p.Cleanup.Add("cursor");return default;}
            }
            public sealed class PendingFilter(Probe p):IAsyncStreamItemFilter<Items,int>,IDisposable{
                public ValueTask<bool> AcceptAsync(Items r,int item,CancellationToken t){
                    Check.That(t==p.Cancellation.Token,"filter receives operation token");p.Filtered.Add(item);
                    return item==1?p.FilterGate.Task:new(true);
                }
                public void Dispose()=>p.FilterDisposals++;
            }
            public sealed class Mapper(Probe p):IStreamItemMap<Items,int>{
                public int Map(Items r,int item,CancellationToken t){
                    Check.That(t==p.Cancellation.Token,"map receives operation token");p.Mapped.Add(item);return item*10;
                }
            }
            public readonly struct Sink(Probe p):IStreamSink<int>{
                public ValueTask<bool> OnNextAsync(int item,CancellationToken t){
                    Check.That(t==p.Cancellation.Token,"sink receives operation token");p.Values.Add(item);return new(true);
                }
            }
            """, """
            foreach(var cancel in new[]{false,true}){
                var p=new Probe();var services=new ServiceCollection();services.AddSingleton(p);Register(services);
                await using var provider=services.BuildServiceProvider();var scope=provider.CreateAsyncScope();
                var mediator=scope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
                var operation=mediator.StreamAsync(new Items(),p.Cancellation.Token).ConsumeAsync(new Sink(p)).AsTask();
                Check.That(!operation.IsCompleted&&p.FilterGate.Consumptions==0&&p.Opened==1&&p.SessionsCreated==1&&p.Pulls==1&&p.Filtered.SequenceEqual(new[]{1}),"first item waits in the actual async filter");
                Check.That(p.Mapped.Count==0&&p.Values.Count==0&&p.CursorDisposals==0&&p.SessionDisposals==0,"pending filter neither publishes nor releases acquired ownership");
                if(cancel){
                    p.Cancellation.Cancel();
                    Check.That(!operation.IsCompleted&&p.FilterGate.Consumptions==0&&p.CursorDisposals==0&&p.SessionDisposals==0,"cancellation drains the outstanding callback before cleanup");
                }
                // The callback itself completes false normally, including the cancellation run.
                p.FilterGate.Complete();
                Exception? error=null;try{await operation.ConfigureAwait(false);}catch(Exception caught){error=caught;}
                if(cancel){
                    Check.That(error is OperationCanceledException canceled&&canceled.CancellationToken==p.Cancellation.Token,"cancellation is checked after the filter completes false");
                    Check.That(p.Pulls==1&&p.Filtered.SequenceEqual(new[]{1})&&p.Mapped.Count==0&&p.Values.Count==0,"canceled rejected item cannot invoke downstream Map or pull another source item");
                }else{
                    Check.That(error==null&&p.Pulls==3&&p.Filtered.SequenceEqual(new[]{1,2}),"normal false advances to the accepted item and then normal EOF");
                    Check.That(p.Mapped.SequenceEqual(new[]{2})&&p.Values.SequenceEqual(new[]{20}),"only the accepted item reaches Map and the sink");
                }
                Check.That(p.FilterGate.Consumptions==1&&p.CursorDisposals==1&&p.SessionDisposals==1&&p.SessionsCreated==1&&p.Cleanup.SequenceEqual(new[]{"cursor","handler"}),"raw filter ValueTask is consumed once before cursor and owned session cleanup once");
                Check.That(p.FactoryDisposals==0&&p.FilterDisposals==0,"factory and filter remain borrowed until scope disposal");
                await scope.DisposeAsync();Check.That(p.FactoryDisposals==1&&p.FilterDisposals==1,"scope owns the borrowed factory and filter");p.Cancellation.Dispose();
            }
            """);
        await RunNativeStreamFixture("pending-filter-false", source);
    }
}
