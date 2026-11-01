using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    [Fact]
    public async Task Native_stream_pending_public_consumption_reentry_preserves_later_generations_and_saved_Task()
    {
        var source = NativeStreamFixture("""
            c.Handlers(typeof(Items<>),DispatchFamily.Stream,typeof(Handler<>));
            c.Pipeline(typeof(Items<>),DispatchFamily.Stream);
            """, """
            public sealed record Items<MoveCompletion>:IStreamRequest<int>;
            public sealed class Probe{
                public readonly global::Zendiator.Generator.Tests.StreamHookRaceSource<bool> First=new(true,"manual"),Second=new(true,"manual"),Race=new(true,"before-registration"),Inline=new(true,"inside-registration"),DirectA=new(true,"manual"),DirectB=new(true,"manual");
                public int Opened,Pulls,Reads,Disposals;
            }
            [ZendiatorHandler]public sealed class Handler<MoveCompletion>(Probe p):IStreamSourceHandler<Items<MoveCompletion>,int,Cursor>{
                public ValueTask<Cursor> OpenAsync(Items<MoveCompletion> r,CancellationToken t){p.Opened++;return new(new Cursor(p));}
            }
            public sealed class Cursor(Probe p):IAsyncEnumerator<int>{
                public int Current{get{p.Reads++;return p.Pulls;}}
                public ValueTask<bool> MoveNextAsync(){
                    p.Pulls++;
                    return p.Pulls switch{1=>new(true),2=>p.First.Task,3=>p.Second.Task,4=>p.Race.Task,5=>p.Inline.Task,6=>p.DirectA.Task,7=>p.DirectB.Task,8=>new(false),_=>throw new InvalidOperationException("source pulled after EOF")};
                }
                public ValueTask DisposeAsync(){p.Disposals++;return default;}
            }
            public sealed class StopSink(Probe p):IStreamSink<int>{
                public readonly List<int> Values=new();public bool StoppedInline;
                public ValueTask<bool> OnNextAsync(int item,CancellationToken token){
                    Values.Add(item);if(item==2){StoppedInline=p.First.Completing;return new(false);}return new(true);
                }
            }
            """, """
            var p=new Probe();var services=new ServiceCollection();services.AddSingleton(p);Register(services);
            await using var provider=services.BuildServiceProvider();await using var scope=provider.CreateAsyncScope();
            var mediator=scope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
            var cursor=mediator.StreamAsync(new Items<string>()).GetAsyncEnumerator();
            Check.That(p.Opened==0&&p.Pulls==0,"enumeration acquisition remains lazy");
            Check.Invalid(()=>{_=cursor.Current;});
            Check.That(await cursor.MoveNextAsync()&&p.Pulls==1&&p.Reads==0,"initial synchronous item leaves Current unread");
            Check.That(cursor.Current==1&&p.Reads==1,"Current reads the published first item once");

            var first=cursor.MoveNextAsync();
            Check.That(!first.IsCompleted&&p.Pulls==2&&p.First.Consumptions==0,"the next raw Move is genuinely pending");
            Check.Invalid(()=>{_=cursor.Current;});Check.Invalid(()=>{_=cursor.MoveNextAsync();});Check.Invalid(()=>{_=cursor.DisposeAsync();});
            var firstAwaiter=first.ConfigureAwait(false).GetAwaiter();
            var following=default(ValueTask<bool>);var inline=false;
            var reentered=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            firstAwaiter.UnsafeOnCompleted(()=>{
                try{
                    Check.That(firstAwaiter.GetResult(),"the public continuation consumes its completed result once");
                    inline=p.First.Completing;
                    Check.That(cursor.Current==2&&p.Reads==2,"Current is published before the consuming continuation");
                    following=cursor.MoveNextAsync();
                    Check.That(!following.IsCompleted&&p.Pulls==3&&p.Reads==2&&p.Second.Consumptions==0,"consumption can reenter another pending generation without reading ahead");
                    reentered.SetResult(true);
                }catch(Exception error){reentered.SetException(error);}
            });
            await Task.Run(()=>{
                Check.That(SynchronizationContext.Current==null&&TaskScheduler.Current==TaskScheduler.Default,"raw completion runs under the default scheduling context");
                p.First.Complete();
            }).ConfigureAwait(false);
            await reentered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Check.That(inline&&p.First.Consumptions==1&&p.First.InlineCallbacks==1,"public consumption and reentry occur before the old raw callback returns");

            var savedTask=following.AsTask();
            Check.That(!savedTask.IsCompleted&&p.Second.Consumptions==0,"AsTask preserves the still pending second generation");
            await Task.Run(p.Second.Complete).ConfigureAwait(false);
            Check.That(await savedTask.ConfigureAwait(false)&&cursor.Current==3&&p.Reads==3,"the saved Task publishes its own item");
            Check.That(await cursor.MoveNextAsync()&&p.Pulls==4&&p.Reads==3,"completion after a pending status probe does not issue another raw Move");
            Check.That(p.Race.Consumptions==1&&p.Race.Registrations<=1&&p.Race.InlineCallbacks==p.Race.Registrations,"completion before possible registration settles the raw result once");
            Check.That(cursor.Current==4&&p.Reads==4,"the raced completion keeps Current lazy");
            Check.That(await cursor.MoveNextAsync()&&p.Pulls==5&&p.Reads==4,"inline registration completion publishes without another pull or pre-reading Current");
            Check.That(p.Inline.Registrations==1&&p.Inline.Consumptions==1&&p.Inline.InlineCallbacks==1,"completion inside registration settles once before the public operation is observed");
            Check.That(cursor.Current==5&&p.Reads==5,"Current remains lazy after inline registration completion");

            var directA=cursor.MoveNextAsync();
            Check.That(!directA.IsCompleted&&p.Pulls==6&&p.Reads==5,"the first later direct await is genuinely pending");
            var completeA=Task.Run(p.DirectA.Complete);
            Check.That(await directA.ConfigureAwait(false)&&cursor.Current==6&&p.Reads==6,"a later generation supports ordinary direct await");
            await completeA.ConfigureAwait(false);
            var directB=cursor.MoveNextAsync();
            Check.That(!directB.IsCompleted&&p.Pulls==7&&p.Reads==6,"the next direct await starts independently after consumed completion");
            var completeB=Task.Run(p.DirectB.Complete);
            Check.That(await directB.ConfigureAwait(false)&&cursor.Current==7&&p.Reads==7,"another consumed generation publishes its own lazy Current");
            await completeB.ConfigureAwait(false);

            Check.That(!await cursor.MoveNextAsync()&&p.Pulls==8&&p.Disposals==1,"EOF performs cleanup before public false");
            Check.Invalid(()=>{_=cursor.Current;});
            await cursor.DisposeAsync();await cursor.DisposeAsync();
            Check.That(await savedTask.ConfigureAwait(false)&&await savedTask.ConfigureAwait(false),"an obtained Task keeps its original result after later generations and disposal");
            Check.That(!await cursor.MoveNextAsync()&&p.Pulls==8&&p.Reads==7&&p.Disposals==1,"closed ownership cannot pull, read or clean again");
            foreach(var raw in new[]{p.First,p.Second,p.Race,p.Inline,p.DirectA,p.DirectB})
                Check.That(raw.Consumptions==1,"every raw source result is consumed exactly once");

            var terminalProbe=new Probe();var terminalServices=new ServiceCollection();terminalServices.AddSingleton(terminalProbe);Register(terminalServices);
            await using var terminalProvider=terminalServices.BuildServiceProvider();await using var terminalScope=terminalProvider.CreateAsyncScope();
            var terminalMediator=terminalScope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
            var sink=new StopSink(terminalProbe);var plan=terminalMediator.StreamSource(new Items<string>());
            var terminal=plan.ConsumeAsync(sink);
            await terminalProbe.First.Registered.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Check.That(!terminal.IsCompleted&&terminalProbe.Pulls==2&&terminalProbe.Reads==1&&terminalProbe.First.Consumptions==0,"typed terminal waits for the second source item after consuming only the first Current");
            await Task.Run(terminalProbe.First.Complete).ConfigureAwait(false);
            await terminal.ConfigureAwait(false);
            Check.That(sink.Values.SequenceEqual(new[]{1,2})&&sink.StoppedInline,"the terminal consumes the pending item and stops inside its raw completion callback");
            Check.That(terminalProbe.Opened==1&&terminalProbe.Pulls==2&&terminalProbe.Reads==2&&terminalProbe.Disposals==1&&terminalProbe.First.Consumptions==1&&terminalProbe.Second.Consumptions==0,"sink false disposes its private owner once without another pull while old completion retirement returns");

            var overflowProbe=new Probe();var overflowServices=new ServiceCollection();overflowServices.AddSingleton(overflowProbe);Register(overflowServices);
            await using var overflowProvider=overflowServices.BuildServiceProvider();await using var overflowScope=overflowProvider.CreateAsyncScope();
            var overflowMediator=overflowScope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
            var overflowCursor=overflowMediator.StreamAsync(new Items<string>()).GetAsyncEnumerator();
            Check.That(await overflowCursor.MoveNextAsync()&&overflowProbe.Reads==0,"the overflow lane starts with a lazy synchronous item");
            var oldFirst=overflowCursor.MoveNextAsync();Check.That(!oldFirst.IsCompleted,"the first retained public generation begins pending");
            overflowProbe.First.Complete();Check.That(oldFirst.IsCompleted&&overflowProbe.First.Consumptions==1,"the first raw result settles while its public result remains unconsumed");
            var oldSecond=overflowCursor.MoveNextAsync();Check.That(!oldSecond.IsCompleted,"the second retained public generation begins pending");
            overflowProbe.Second.Complete();Check.That(oldSecond.IsCompleted&&overflowProbe.Second.Consumptions==1,"the second raw result settles without consuming either public result");
            Check.That(await overflowCursor.MoveNextAsync()&&overflowProbe.Pulls==4&&overflowProbe.Reads==0,"a completed source race leaves the two older public results unconsumed");
            var overflow=overflowCursor.MoveNextAsync();
            Check.That(await overflow&&overflowProbe.Inline.Registrations==1&&overflowProbe.Inline.Consumptions==1&&overflowProbe.Inline.InlineCallbacks==1,"inline completion succeeds with two older generations still unconsumed");
            Check.That(overflowCursor.Current==5&&overflowProbe.Reads==1,"the independent newer result does not pre-read Current");
            Check.That(await oldFirst&&await oldSecond,"both old public results remain independently consumable after the newer inline generation");
            await overflowCursor.DisposeAsync();await overflowCursor.DisposeAsync();
            Check.That(overflowProbe.Opened==1&&overflowProbe.Pulls==5&&overflowProbe.Reads==1&&overflowProbe.Disposals==1&&overflowProbe.DirectA.Consumptions==0&&overflowProbe.DirectB.Consumptions==0,"closing the overflow lane cleans once without advancing unused source operations");
            """);
        await RunNativeStreamFixture("pending-public-consumption-reentry", source);
    }

    [Fact]
    public async Task Native_stream_consumed_pending_generation_releases_context_while_owner_and_result_remain_held()
    {
        var source = NativeStreamFixture("""
            c.Handlers<Items>(DispatchFamily.Stream,typeof(Handler));
            c.Pipeline<Items>(DispatchFamily.Stream);
            """, """
            public sealed record Items:IStreamRequest<int>;
            public sealed class Payload{public readonly byte[] Bytes=new byte[65536];}
            public sealed class Probe{
                public static readonly AsyncLocal<object?> Ambient=new();
                public readonly global::Zendiator.Generator.Tests.StreamHookRaceSource<bool> Gate=new(true,"manual");
                public int Pulls,Reads,Disposals;public bool SawPayloadAtResult;
            }
            [ZendiatorHandler]public sealed class Handler(Probe p):IStreamSourceHandler<Items,int,Cursor>{
                public ValueTask<Cursor> OpenAsync(Items r,CancellationToken t)=>new(new Cursor(p));
            }
            public sealed class Cursor(Probe p):IAsyncEnumerator<int>,System.Threading.Tasks.Sources.IValueTaskSource<bool>{
                public int Current{get{p.Reads++;return p.Pulls;}}
                public ValueTask<bool> MoveNextAsync(){p.Pulls++;return p.Pulls==1?new(true):new(this,0);}
                public ValueTask DisposeAsync(){p.Disposals++;return default;}
                public bool GetResult(short token){p.SawPayloadAtResult=Probe.Ambient.Value is Payload;return p.Gate.GetResult(token);}
                public System.Threading.Tasks.Sources.ValueTaskSourceStatus GetStatus(short token)=>p.Gate.GetStatus(token);
                public void OnCompleted(Action<object?> continuation,object? state,short token,System.Threading.Tasks.Sources.ValueTaskSourceOnCompletedFlags flags)=>p.Gate.OnCompleted(continuation,state,token,flags);
            }
            public sealed class Held(IAsyncEnumerator<int> owner,ValueTask<bool> consumed,WeakReference payload){
                public readonly IAsyncEnumerator<int> Owner=owner;
                public readonly ValueTask<bool> Consumed=consumed;
                public readonly WeakReference Payload=payload;
            }
            public static class Retention{
                [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
                public static Held Create(global::NativeStream.Generated.Reads.IZendiator mediator,Probe p){
                    var owner=mediator.StreamAsync(new Items()).GetAsyncEnumerator();
                    var initial=owner.MoveNextAsync();Check.That(initial.IsCompletedSuccessfully&&initial.GetAwaiter().GetResult(),"retention owner opens with one synchronous item");
                    var payload=new Payload();var weak=new WeakReference(payload);ValueTask<bool> pending;
                    try{Probe.Ambient.Value=payload;pending=owner.MoveNextAsync();}
                    finally{Probe.Ambient.Value=null;}
                    Check.That(!pending.IsCompleted&&p.Gate.Consumptions==0,"payload context is captured by a genuinely pending repeated Move");
                    var awaiter=pending.ConfigureAwait(false).GetAwaiter();var consumed=false;Exception? failure=null;
                    awaiter.UnsafeOnCompleted(()=>{
                        try{Check.That(awaiter.GetResult()&&p.Gate.Completing,"the public result is consumed inside the raw completion callback");consumed=true;}
                        catch(Exception error){failure=error;}
                    });
                    p.Gate.Complete();
                    if(failure is not null)throw failure;
                    Check.That(consumed&&p.SawPayloadAtResult&&p.Gate.Consumptions==1&&Probe.Ambient.Value is null,"raw completion sees its captured context and then restores the caller without retaining an unconsumed result");
                    return new Held(owner,pending,weak);
                }
                [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
                public static bool Alive(WeakReference weak)=>weak.IsAlive;
            }
            """, """
            var p=new Probe();var services=new ServiceCollection();services.AddSingleton(p);Register(services);
            await using var provider=services.BuildServiceProvider();await using var scope=provider.CreateAsyncScope();
            var mediator=scope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
            var held=Retention.Create(mediator,p);
            try{
                for(var round=0;round<3;round++){GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();}
                Check.That(!Retention.Alive(held.Payload),"the consumed and retired generation releases its captured payload while the owner and consumed ValueTask remain held");
                Check.That(p.Pulls==2&&p.Reads==0&&p.Disposals==0,"retention observation does not advance, pre-read or dispose the held owner");
                GC.KeepAlive(held);
            }finally{await held.Owner.DisposeAsync();GC.KeepAlive(held);}
            Check.That(p.Disposals==1,"the independently held owner still performs one cleanup");
            """);
        await RunNativeStreamFixture("consumed-pending-context-release", source);
    }
}
