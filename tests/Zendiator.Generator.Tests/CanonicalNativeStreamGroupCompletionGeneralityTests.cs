using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    [Fact]
    public async Task Native_stream_group_completion_keeps_three_stage_iterator_and_native_endpoints_general()
    {
        var source = NativeStreamFixture("""
            c.Handlers<IteratorItems>(DispatchFamily.Stream,typeof(IteratorHandler));
            c.Pipeline<IteratorItems>(DispatchFamily.Stream,Stage.Wrapper<Relay1<IteratorItems>>(),Stage.Wrapper<Relay2<IteratorItems>>(),Stage.Wrapper<Relay3<IteratorItems>>());
            c.Handlers<NativeItems>(DispatchFamily.Stream,typeof(NativeHandler));
            c.Pipeline<NativeItems>(DispatchFamily.Stream,Stage.Wrapper<Relay1<NativeItems>>(),Stage.Wrapper<Relay2<NativeItems>>(),Stage.Wrapper<Relay3<NativeItems>>());
            """, """
            public sealed record IteratorItems(int Count,bool Controlled):IStreamRequest<int>;
            public sealed record NativeItems(int Count):IStreamRequest<int>;
            public sealed class Payload{public readonly byte[] Bytes=new byte[65536];}
            public sealed class Probe{
                public static readonly AsyncLocal<object?> Ambient=new();
                public readonly List<global::Zendiator.Generator.Tests.IAsyncOnceGate> Gates=new();
                public readonly List<int> OpenOrder=new();
                public global::Zendiator.Generator.Tests.StreamHookRaceSource<bool>? RetentionGate;public bool SawPayloadAtResult;
                public CancellationToken ExpectedToken;public int Handled,RawMoves,Produced,Reads,SourceDisposals,BorrowedDisposals;
                public void Token(CancellationToken token)=>Check.That(token==ExpectedToken,"every opening and raw Move uses the effective enumeration token");
            }
            [ZendiatorHandler]public sealed class IteratorHandler(Probe p):IStreamSourceHandler<IteratorItems,int,IteratorCursor>,IDisposable{
                public ValueTask<IteratorCursor> OpenAsync(IteratorItems r,CancellationToken token){p.Token(token);p.Handled++;return new(new IteratorCursor(p,Feed(p,r,token).GetAsyncEnumerator(token)));}
                private static async IAsyncEnumerable<int> Feed(Probe p,IteratorItems r,[System.Runtime.CompilerServices.EnumeratorCancellation]CancellationToken token){
                    try{
                        for(var i=0;i<r.Count;i++){
                            p.Token(token);token.ThrowIfCancellationRequested();
                            if(r.Controlled){var gate=new global::Zendiator.Generator.Tests.AsyncOnceGate<int>(0);p.Gates.Add(gate);await gate.Task.ConfigureAwait(false);}
                            p.Produced++;yield return i;
                        }
                    }finally{p.SourceDisposals++;}
                }
                public void Dispose()=>p.BorrowedDisposals++;
            }
            public sealed class IteratorCursor(Probe p,IAsyncEnumerator<int> source):IAsyncEnumerator<int>{
                public int Current{get{p.Reads++;return source.Current;}}
                public ValueTask<bool> MoveNextAsync(){p.RawMoves++;return source.MoveNextAsync();}
                public ValueTask DisposeAsync()=>source.DisposeAsync();
            }
            [ZendiatorHandler]public sealed class NativeHandler(Probe p):IStreamSourceHandler<NativeItems,int,NativeCursor>,IDisposable{
                public ValueTask<NativeCursor> OpenAsync(NativeItems r,CancellationToken token){p.Token(token);p.Handled++;return new(new NativeCursor(p,r.Count,token));}
                public void Dispose()=>p.BorrowedDisposals++;
            }
            public sealed class NativeCursor(Probe p,int count,CancellationToken token):IAsyncEnumerator<int>,System.Threading.Tasks.Sources.IValueTaskSource<bool>{
                private int index=-1;private bool closed;
                public int Current{get{p.Reads++;return index;}}
                public ValueTask<bool> MoveNextAsync(){
                    Check.That(!closed,"a closed native source cannot be advanced");p.Token(token);token.ThrowIfCancellationRequested();p.RawMoves++;
                    if(++index==count)return new(false);
                    p.Produced++;
                    if(p.RetentionGate is not null)return index==0?new(true):new(this,0);
                    var gate=new global::Zendiator.Generator.Tests.AsyncOnceGate<bool>(true);p.Gates.Add(gate);return gate.Task;
                }
                public ValueTask DisposeAsync(){Check.That(!closed,"the native source is disposed once");closed=true;p.SourceDisposals++;return default;}
                public bool GetResult(short token){p.SawPayloadAtResult=Probe.Ambient.Value is Payload;return p.RetentionGate!.GetResult(token);}
                public System.Threading.Tasks.Sources.ValueTaskSourceStatus GetStatus(short token)=>p.RetentionGate!.GetStatus(token);
                public void OnCompleted(Action<object?> continuation,object? state,short token,System.Threading.Tasks.Sources.ValueTaskSourceOnCompletedFlags flags)=>p.RetentionGate!.OnCompleted(continuation,state,token,flags);
            }
            public abstract class Relay<T>(Probe p,int role):IStreamMiddleware<T,int>,IDisposable where T:IStreamRequest<int>{
                public ValueTask<IAsyncEnumerator<int>> OpenAsync(T request,IStreamUpstream<T,int> next,CancellationToken token){p.Token(token);p.OpenOrder.Add(role);return next.OpenAsync(request,token);}
                public void Dispose()=>p.BorrowedDisposals++;
            }
            public sealed class Relay1<T>(Probe p):Relay<T>(p,1) where T:IStreamRequest<int>;
            public sealed class Relay2<T>(Probe p):Relay<T>(p,2) where T:IStreamRequest<int>;
            public sealed class Relay3<T>(Probe p):Relay<T>(p,3) where T:IStreamRequest<int>;
            public sealed class Held(IAsyncEnumerator<int> owner,ValueTask<bool> consumed,WeakReference payload){
                public readonly IAsyncEnumerator<int> Owner=owner;public readonly ValueTask<bool> Consumed=consumed;public readonly WeakReference Payload=payload;
            }
            public static class Retention{
                [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
                public static Held Create(global::NativeStream.Generated.Reads.IZendiator mediator,Probe p){
                    var owner=mediator.StreamAsync(new NativeItems(2)).GetAsyncEnumerator(p.ExpectedToken);
                    var initial=owner.MoveNextAsync();Check.That(initial.IsCompletedSuccessfully&&initial.GetAwaiter().GetResult(),"retention opens three owners with one synchronous item");
                    var payload=new Payload();var weak=new WeakReference(payload);ValueTask<bool> pending;
                    try{Probe.Ambient.Value=payload;pending=owner.MoveNextAsync();}finally{Probe.Ambient.Value=null;}
                    var gate=p.RetentionGate!;
                    Check.That(!pending.IsCompleted&&gate.Consumptions==0,"the repeated group Move captures payload context while genuinely pending");
                    var awaiter=pending.ConfigureAwait(false).GetAwaiter();var consumed=false;Exception? failure=null;
                    awaiter.UnsafeOnCompleted(()=>{
                        try{Check.That(awaiter.GetResult()&&gate.Completing,"the public group result is consumed inside its raw callback");consumed=true;}
                        catch(Exception error){failure=error;}
                    });
                    gate.Complete();if(failure is not null)throw failure;
                    Check.That(consumed&&p.SawPayloadAtResult&&gate.Consumptions==1&&Probe.Ambient.Value is null,"captured raw context restores the caller after inline notification retirement");
                    return new(owner,pending,weak);
                }
                [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
                public static bool Alive(WeakReference weak)=>weak.IsAlive;
            }
            """, """
            foreach(var mode in new[]{"iterator-controlled-full","native-controlled-early","iterator-sync-early"}){
                var controlled=mode!="iterator-sync-early";var full=mode=="iterator-controlled-full";var count=full?1024:16;
                var p=new Probe();using var token=new CancellationTokenSource();p.ExpectedToken=token.Token;
                var services=new ServiceCollection();services.AddSingleton(p);Register(services);
                await using var provider=services.BuildServiceProvider();var scope=provider.CreateAsyncScope();
                var mediator=scope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
                IAsyncEnumerable<int> feed=mode=="native-controlled-early"?mediator.StreamAsync(new NativeItems(count)):mediator.StreamAsync(new IteratorItems(count,controlled));
                var cursor=feed.GetAsyncEnumerator(token.Token);
                Check.That(p.Handled==0&&p.RawMoves==0&&p.OpenOrder.Count==0,"all three endpoints acquire lazily on their first Move");
                Check.Invalid(()=>{_=cursor.Current;});var values=new List<int>();
                while(true){
                    var operation=cursor.MoveNextAsync();var expectedItem=values.Count<count;
                    if(controlled&&expectedItem){
                        Check.That(!operation.IsCompleted&&p.Gates.Count==values.Count+1,"the raw source is explicitly gated until its public result is obtained");
                        var gate=p.Gates[^1];Check.That(gate.Consumptions==0,"the pending raw result is not consumed before completion");
                        Check.Invalid(()=>{_=cursor.Current;});Check.Invalid(()=>{_=cursor.MoveNextAsync();});Check.Invalid(()=>{_=cursor.DisposeAsync();});
                        gate.Complete();
                    }else Check.That(operation.IsCompletedSuccessfully,"the synchronous input and compiler iterator EOF complete inline");
                    var moved=await operation.ConfigureAwait(false);
                    Check.That(p.Reads==values.Count,"publication and cleanup never pre-read lazy Current");
                    if(!moved)break;
                    Check.That(p.OpenOrder.Count==3&&p.OpenOrder.Distinct().Count()==3,"exactly three independent middleware owners open");
                    values.Add(cursor.Current);Check.That(values[^1]==values.Count-1,"values retain source order");
                    if(!full)break;
                }
                var consumed=full?count:1;
                Check.That(values.SequenceEqual(Enumerable.Range(0,consumed))&&p.Handled==1&&p.Produced==consumed,"Full consumes all1024 and Early consumes only its first of16");
                Check.That(p.RawMoves==consumed+(full?1:0)&&p.Reads==consumed,"no duplicate pull or eager Current read");
                Check.That(p.Gates.Count==(controlled?consumed:0)&&p.Gates.All(g=>g.Consumptions==1),"each explicitly pending raw source is consumed once");
                if(full)Check.That(p.SourceDisposals==1,"EOF cleanup precedes the published false result");
                await cursor.DisposeAsync();await cursor.DisposeAsync();
                Check.That(p.SourceDisposals==1&&!await cursor.MoveNextAsync()&&p.BorrowedDisposals==0,"closed cursor is idempotent and DI receivers remain borrowed");
                Check.Invalid(()=>{_=cursor.Current;});await scope.DisposeAsync();
                Check.That(p.BorrowedDisposals==4,"DI alone disposes the handler and its three middleware receivers");
            }
            var retainedProbe=new Probe{RetentionGate=new(true,"manual")};var retainedServices=new ServiceCollection();retainedServices.AddSingleton(retainedProbe);Register(retainedServices);
            await using var retainedProvider=retainedServices.BuildServiceProvider();var retainedScope=retainedProvider.CreateAsyncScope();
            var retainedMediator=retainedScope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
            var held=Retention.Create(retainedMediator,retainedProbe);
            try{
                for(var round=0;round<3;round++){GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();}
                Check.That(!Retention.Alive(held.Payload),"the group cache releases captured context while owner and consumed ValueTask stay held");
                Check.That(retainedProbe.OpenOrder.Count==3&&retainedProbe.RawMoves==2&&retainedProbe.Reads==0&&retainedProbe.SourceDisposals==0,"retention observation does not pull, read Current or dispose the held owner");
                GC.KeepAlive(held);
            }finally{await held.Owner.DisposeAsync();GC.KeepAlive(held);}
            Check.That(retainedProbe.SourceDisposals==1&&retainedProbe.BorrowedDisposals==0,"the independently held group source still performs one cleanup");
            await retainedScope.DisposeAsync();Check.That(retainedProbe.BorrowedDisposals==4,"DI retains its handler and three middleware disposal responsibilities");
            """);
        await RunNativeStreamFixture("group-completion-three-stage-generality", source);
    }
}
