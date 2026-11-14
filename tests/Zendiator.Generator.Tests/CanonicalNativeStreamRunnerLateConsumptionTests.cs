// Regression coverage for late result consumption and inline public disposal.
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    [Fact]
    public async Task Native_stream_retained_second_result_survives_pending_cleanup_and_owner_closure()
    {
        var source = NativeStreamFixture("""
            c.Handlers<Items>(DispatchFamily.Stream,typeof(Handler));
            c.Pipeline<Items>(DispatchFamily.Stream);
            """, """
            public sealed record Items:IStreamRequest<int>;
            public sealed class Probe {
                public readonly string Mode;
                public readonly CancellationTokenSource Cancellation=new();
                public readonly global::Zendiator.Generator.Tests.StreamHookRaceSource<bool> First=new(true,"manual"),Second=new(true,"manual");
                public readonly global::Zendiator.Generator.Tests.AsyncOnceGate<int> Cleanup=new(0);
                public readonly TaskCompletionSource<bool> Published=new(TaskCreationOptions.RunContinuationsAsynchronously);
                public readonly Exception? Error;
                public int Pulls,Disposals,PublicCallbacks;
                public Probe(string mode){Mode=mode;Error=mode=="fault"?new FormatException("later raw failure"):mode=="cancel"?new OperationCanceledException("later raw cancellation",Cancellation.Token):null;}
            }
            [Handler]public sealed class Handler(Probe p):IStreamSourceHandler<Items,int,Cursor> {
                public ValueTask<Cursor> OpenAsync(Items request,CancellationToken token)=>new(new Cursor(p));
            }
            public sealed class Cursor(Probe p):IAsyncEnumerator<int>,System.Threading.Tasks.Sources.IValueTaskSource<bool> {
                public int Current=>p.Pulls;
                public ValueTask<bool> MoveNextAsync(){
                    p.Pulls++;
                    return p.Pulls switch{1=>new(true),2=>p.First.Task,3=>new(this,0),_=>throw new InvalidOperationException("unexpected raw pull")};
                }
                public ValueTask DisposeAsync(){p.Disposals++;return p.Cleanup.Void;}
                public bool GetResult(short token){p.Second.GetResult(token);if(p.Error is not null)throw p.Error;return p.Mode!="eof";}
                public System.Threading.Tasks.Sources.ValueTaskSourceStatus GetStatus(short token)=>p.Second.GetStatus(token);
                public void OnCompleted(Action<object?> continuation,object? state,short token,System.Threading.Tasks.Sources.ValueTaskSourceOnCompletedFlags flags)=>p.Second.OnCompleted(continuation,state,token,flags);
            }
            """, """
            foreach(var mode in new[]{"success-dispose","eof","fault","cancel"}){
                var p=new Probe(mode);var services=new ServiceCollection();services.AddSingleton(p);Register(services);
                await using var provider=services.BuildServiceProvider();await using var scope=provider.CreateAsyncScope();
                var mediator=scope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
                var cursor=mediator.StreamAsync(new Items(),p.Cancellation.Token).GetAsyncEnumerator();
                try{
                    Check.That(await cursor.MoveNextAsync(),"initial synchronous item");
                    var first=cursor.MoveNextAsync();Check.That(!first.IsCompleted,"retained first starts pending");
                    p.First.Complete();Check.That(first.IsCompletedSuccessfully,"first completes and remains unconsumed");
                    var second=cursor.MoveNextAsync();Check.That(!second.IsCompleted,"second starts independently while first is unconsumed");
                    second.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(()=>{
                        p.PublicCallbacks++;p.Published.SetResult(true);
                        // Notification is not consumption. Keep second for exactly one late GetResult below.
                    });
                    if(mode=="cancel")p.Cancellation.Cancel();
                    p.Second.Complete();
                    Check.That(p.Second.Consumptions==1&&p.First.Consumptions==1,"each raw Move settles once");
                    if(mode=="success-dispose"){
                        await p.Published.Task.ConfigureAwait(false);
                        Check.That(second.IsCompletedSuccessfully&&p.Disposals==0,"successful second publishes before explicit disposal");
                        var disposing=cursor.DisposeAsync().AsTask();var joined=cursor.DisposeAsync().AsTask();
                        Check.That(ReferenceEquals(disposing,joined)&&!disposing.IsCompleted&&p.Disposals==1&&p.Cleanup.Consumptions==0,"two disposers share one pending cleanup");
                        Check.Invalid(()=>{_=cursor.Current;});Check.Invalid(()=>{_=cursor.MoveNextAsync();});
                        p.Cleanup.Complete();await disposing.ConfigureAwait(false);await joined.ConfigureAwait(false);
                    }else{
                        Check.That(!second.IsCompleted&&p.PublicCallbacks==0&&p.Disposals==1&&p.Cleanup.Consumptions==0,"EOF/failure/cancellation does not publish before pending cleanup finishes");
                        Check.Invalid(()=>{_=cursor.Current;});Check.Invalid(()=>{_=cursor.MoveNextAsync();});Check.Invalid(()=>{_=cursor.DisposeAsync();});
                        p.Cleanup.Complete();await p.Published.Task.ConfigureAwait(false);
                        await cursor.DisposeAsync();await cursor.DisposeAsync();
                    }
                    Check.That(p.Disposals==1&&p.Cleanup.Consumptions==1&&p.PublicCallbacks==1,"cleanup and public notification each settle once");
                    Check.Invalid(()=>{_=cursor.Current;});
                    Check.That(!await cursor.MoveNextAsync()&&p.Pulls==3,"closure never restarts the source");
                    // The second cache may now be cleared; the external ValueTask must still own its generation.
                    var secondWasCanceled=second.IsCanceled;
                    Exception? caught=null;var moved=false;
                    try{moved=await second.ConfigureAwait(false);}catch(Exception error){caught=error;}
                    if(p.Error is null)Check.That(caught is null&&moved==(mode=="success-dispose"),"late second consumption returns its own success/EOF result");
                    else Check.That(ReferenceEquals(caught,p.Error),"late second consumption preserves the exact original error object");
                    if(mode=="cancel")Check.That(secondWasCanceled&&caught is OperationCanceledException canceled&&canceled.CancellationToken==p.Cancellation.Token,"late cancellation preserves its source token and canceled classification");
                    Check.That(await first.ConfigureAwait(false),"the older first result remains consumable after second and closure");
                    Check.That(p.Disposals==1&&p.Pulls==3&&p.First.Consumptions==1&&p.Second.Consumptions==1&&p.Cleanup.Consumptions==1,"late consumption does not repeat source work or cleanup");
                }finally{p.Cancellation.Dispose();}
            }
            """);
        await RunNativeStreamFixture("retained-second-pending-cleanup-and-closure", source);
    }

    [Fact]
    public async Task Native_stream_consuming_first_inline_continuation_can_dispose_before_publisher_retires()
    {
        var source = NativeStreamFixture("""
            c.Handlers<Items>(DispatchFamily.Stream,typeof(Handler));
            c.Pipeline<Items>(DispatchFamily.Stream);
            """, """
            public sealed record Items:IStreamRequest<int>;
            public sealed class Probe {
                public readonly global::Zendiator.Generator.Tests.StreamHookRaceSource<bool> First=new(true,"manual");
                public readonly global::Zendiator.Generator.Tests.AsyncOnceGate<int> Cleanup=new(0);
                public readonly Exception CleanupError=new FormatException("inline disposal cleanup");
                public int Pulls,Disposals;
            }
            [Handler]public sealed class Handler(Probe p):IStreamSourceHandler<Items,int,Cursor> {
                public ValueTask<Cursor> OpenAsync(Items request,CancellationToken token)=>new(new Cursor(p));
            }
            public sealed class Cursor(Probe p):IAsyncEnumerator<int> {
                public int Current=>p.Pulls;
                public ValueTask<bool> MoveNextAsync(){p.Pulls++;return p.Pulls switch{1=>new(true),2=>p.First.Task,_=>throw new InvalidOperationException("unexpected raw pull")};}
                public ValueTask DisposeAsync(){p.Disposals++;return p.Cleanup.Void;}
            }
            """, """
            foreach(var cleanupFails in new[]{false,true}){
                var p=new Probe();var services=new ServiceCollection();services.AddSingleton(p);Register(services);
                await using var provider=services.BuildServiceProvider();await using var scope=provider.CreateAsyncScope();
                var mediator=scope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
                var cursor=mediator.StreamAsync(new Items()).GetAsyncEnumerator();
                Check.That(await cursor.MoveNextAsync(),"initial synchronous item");
                var first=cursor.MoveNextAsync();Check.That(!first.IsCompleted,"first public completion starts pending");
                var awaiter=first.ConfigureAwait(false).GetAwaiter();
                Task? disposing=null,joined=null;Exception? callbackFailure=null;var called=false;
                awaiter.UnsafeOnCompleted(()=>{
                    try{
                        Check.That(p.First.Completing,"public continuation runs inside the raw publisher callback");
                        Check.That(awaiter.GetResult()&&cursor.Current==2,"first is consumed exactly once before disposal reentry");
                        disposing=cursor.DisposeAsync().AsTask();joined=cursor.DisposeAsync().AsTask();
                        Check.That(ReferenceEquals(disposing,joined)&&!disposing.IsCompleted&&p.Disposals==1&&p.Cleanup.Consumptions==0,"inline public Dispose starts once and another Dispose joins");
                        Check.Invalid(()=>{_=cursor.Current;});Check.Invalid(()=>{_=cursor.MoveNextAsync();});
                        called=true;
                    }catch(Exception error){callbackFailure=error;}
                });
                p.First.Complete();
                if(callbackFailure is not null)throw callbackFailure;
                Check.That(called&&!p.First.Completing&&p.First.Consumptions==1&&p.Disposals==1,"old publication returns after inline consumption and disposal without a second cleanup");
                Check.That(disposing is not null&&joined is not null&&!disposing.IsCompleted&&!joined.IsCompleted,"publisher retirement does not overwrite pending disposal state");
                if(cleanupFails)p.Cleanup.Fail(p.CleanupError);else p.Cleanup.Complete();
                Exception? firstError=null,joinedError=null;
                try{await disposing!.ConfigureAwait(false);}catch(Exception error){firstError=error;}
                try{await joined!.ConfigureAwait(false);}catch(Exception error){joinedError=error;}
                if(cleanupFails)Check.That(ReferenceEquals(firstError,p.CleanupError)&&ReferenceEquals(joinedError,p.CleanupError),"both disposers receive the same cleanup failure");
                else Check.That(firstError is null&&joinedError is null,"both disposers complete successfully");
                var repeated=cursor.DisposeAsync().AsTask();Check.That(ReferenceEquals(repeated,disposing),"later disposal retains the shared completion");
                Exception? repeatedError=null;try{await repeated.ConfigureAwait(false);}catch(Exception error){repeatedError=error;}
                Check.That(cleanupFails?ReferenceEquals(repeatedError,p.CleanupError):repeatedError is null,"later disposal preserves the settled result");
                Check.That(!await cursor.MoveNextAsync()&&p.Pulls==2&&p.Disposals==1&&p.Cleanup.Consumptions==1,"closed state cannot be overwritten by the old publisher retirement");
                Check.Invalid(()=>{_=cursor.Current;});
                GC.KeepAlive(first); // The consumed ValueTask may remain held; never consume it twice.
            }
            """);
        await RunNativeStreamFixture("first-consumption-inline-public-disposal", source);
    }
}
