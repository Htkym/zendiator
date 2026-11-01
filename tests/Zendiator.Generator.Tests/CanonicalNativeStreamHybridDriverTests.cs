using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    [Fact]
    public async Task Native_stream_pending_success_then_source_failure_cleans_cursor_and_owned_session_once()
    {
        var source = NativeStreamFixture("""
            c.Handlers<Items>(DispatchFamily.Stream,HandlerEntry.Session<Handler,Factory>());
            c.Pipeline<Items>(DispatchFamily.Stream);
            """, """
            public sealed record Items:IStreamRequest<int>;
            public sealed class Probe{
                public readonly string Mode;public readonly Exception Error;public readonly Exception CleanupError=new InvalidOperationException("owned cleanup");public readonly CancellationTokenSource Cancellation=new();
                public readonly global::Zendiator.Generator.Tests.AsyncOnceGate<bool> First=new(true),Failed=new(false);
                public readonly global::Zendiator.Generator.Tests.AsyncOnceGate<int> Cleanup=new(0);
                public readonly TaskCompletionSource<bool> CleanupStarted=new(TaskCreationOptions.RunContinuationsAsynchronously);
                public readonly List<string> Released=new();public int Opened,Pulls,Reads,CursorDisposals,SessionDisposals,FactoryDisposals;
                public Probe(string mode){
                    Mode=mode;if(mode=="cancel")Cancellation.Cancel();
                    Error=mode is "cancel" or "pending-cancel"?new OperationCanceledException("raw canceled",Cancellation.Token):new FormatException("raw Move");
                    if(mode is not ("pending-fault" or "pending-cancel"))Failed.Fail(Error);
                }
            }
            [ZendiatorHandler]public sealed class Handler(Probe p):IStreamSourceHandler<Items,int,Cursor>,IAsyncDisposable{
                public ValueTask<Cursor> OpenAsync(Items r,CancellationToken t){p.Opened++;return new(new Cursor(p));}
                public ValueTask DisposeAsync(){p.SessionDisposals++;p.Released.Add("handler");if(p.Mode=="pending-fault")throw p.CleanupError;return default;}
            }
            public sealed class Factory(Probe p):IOwnedSessionFactory<Items,Handler>,IDisposable{
                public Handler Create(Items r,CancellationToken t)=>new(p);
                public void Dispose()=>p.FactoryDisposals++;
            }
            public sealed class Cursor(Probe p):IAsyncEnumerator<int>{
                public int Current{get{p.Reads++;return 10;}}
                public ValueTask<bool> MoveNextAsync(){
                    p.Pulls++;if(p.Pulls==1)return new(true);if(p.Pulls==2)return p.First.Task;
                    Check.That(p.Pulls==3,"a terminal failure must not pull the source again");
                    if(p.Mode=="throw")throw p.Error;return p.Failed.Task;
                }
                public ValueTask DisposeAsync(){
                    p.CursorDisposals++;p.Released.Add("cursor");p.CleanupStarted.SetResult(true);return p.Cleanup.Void;
                }
            }
            """, """
            foreach(var mode in new[]{"throw","fault","cancel","pending-fault","pending-cancel"}){
                var p=new Probe(mode);var services=new ServiceCollection();services.AddSingleton(p);Register(services);
                await using var provider=services.BuildServiceProvider();var scope=provider.CreateAsyncScope();
                var mediator=scope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
                var cursor=mediator.StreamAsync(new Items()).GetAsyncEnumerator();
                Check.That(await cursor.MoveNextAsync()&&p.Opened==1&&p.Pulls==1&&p.Reads==0,"initial synchronous item leaves Current unread");
                var first=cursor.MoveNextAsync();
                Check.That(!first.IsCompleted&&p.Pulls==2&&p.First.Consumptions==0,"repeated raw Move is truly pending");
                p.First.Complete();Check.That(await first&&cursor.Current==10&&p.First.Consumptions==1,"pending true is consumed once before the next source operation");
                var failed=cursor.MoveNextAsync();
                Task<bool>? canceledTask=mode=="pending-cancel"?failed.AsTask():null;
                if(mode is "pending-fault" or "pending-cancel"){
                    Check.That(!(canceledTask?.IsCompleted??failed.IsCompleted)&&p.Pulls==3&&p.Failed.Consumptions==0&&p.CursorDisposals==0&&p.SessionDisposals==0,"the later source failure remains genuinely pending before completion");
                    if(mode=="pending-cancel")p.Cancellation.Cancel();
                    p.Failed.Fail(p.Error);
                }
                await p.CleanupStarted.Task.ConfigureAwait(false);
                Check.That(!(canceledTask?.IsCompleted??failed.IsCompleted)&&p.Pulls==3&&p.Reads==1&&p.CursorDisposals==1&&p.SessionDisposals==0,"source failure closes the cursor and waits for its pending cleanup");
                Check.That(p.Failed.Consumptions==(mode=="throw"?0:1)&&p.Cleanup.Consumptions==0,"raw fault or cancellation is consumed once before cleanup");
                Check.Invalid(()=>{_=cursor.Current;});Check.Invalid(()=>{_=cursor.MoveNextAsync();});Check.Invalid(()=>{_=cursor.DisposeAsync();});
                p.Cleanup.Complete();Exception? error=null;try{if(canceledTask is not null)await canceledTask.ConfigureAwait(false);else await failed.ConfigureAwait(false);}catch(Exception caught){error=caught;}
                if(mode=="pending-fault")Check.That(ReferenceEquals(error,p.CleanupError),"owned handler cleanup failure takes precedence after the pending source failure");
                else if(mode is "cancel" or "pending-cancel")Check.That(error is OperationCanceledException canceled&&canceled.CancellationToken==p.Cancellation.Token,"source cancellation retains its raw token");
                else Check.That(ReferenceEquals(error,p.Error),"driver shutdown preserves the raw synchronous failure");
                if(canceledTask is not null)Check.That(canceledTask.IsCanceled,"genuinely pending cancellation publishes a canceled public Task rather than a faulted Task");
                await cursor.DisposeAsync();await cursor.DisposeAsync();
                Check.That(!await cursor.MoveNextAsync()&&p.Pulls==3&&p.First.Consumptions==1&&p.Failed.Consumptions==(mode=="throw"?0:1)&&p.Cleanup.Consumptions==1,"closed ownership cannot resume an earlier pending operation or pull again");
                Check.That(p.CursorDisposals==1&&p.SessionDisposals==1&&p.Released.SequenceEqual(new[]{"cursor","handler"}),"cursor and owned handler each release once after the pending cleanup");
                Check.That(p.FactoryDisposals==0,"the factory remains borrowed until its scope ends");
                await scope.DisposeAsync();Check.That(p.FactoryDisposals==1,"the DI scope releases the borrowed factory");p.Cancellation.Dispose();
            }
            """);
        await RunNativeStreamFixture("pending-then-source-failure", source);
    }

    [Fact]
    public async Task Native_stream_mixed_pending_generations_keep_public_results_and_owner_cleanup_independent()
    {
        var source = NativeStreamFixture("""
            c.Handlers<Items>(DispatchFamily.Stream,HandlerEntry.Session<Handler,Factory>());
            c.Pipeline<Items>(DispatchFamily.Stream);
            """, """
            public sealed record Items(string Id):IStreamRequest<int>;
            public sealed class Probe{
                public static readonly AsyncLocal<string?> Ambient=new();
                public readonly Dictionary<string,Flow> Flows=new(){["A"]=new(true),["B"]=new(false),["C"]=new(true)};
                public Flow For(Items r)=>Flows[r.Id];
            }
            public sealed class Flow{
                public readonly MoveGate First,Later;public readonly global::Zendiator.Generator.Tests.AsyncOnceGate<int> Cleanup=new(0);
                public readonly TaskCompletionSource<bool> CleanupStarted=new(TaskCreationOptions.RunContinuationsAsynchronously);
                public readonly List<string?> Moves=new(),Completions=new();public readonly List<string> Released=new();
                public int Opened,Pulls,Reads,CursorDisposals,SessionDisposals;
                public Flow(bool later){First=new(this,true);Later=new(this,later);}
            }
            public sealed class MoveGate(Flow f,bool value):System.Threading.Tasks.Sources.IValueTaskSource<bool>{
                private readonly global::Zendiator.Generator.Tests.AsyncOnceGate<bool> inner=new(value);
                public ValueTask<bool> Task=>new(this,0);public int Consumptions=>inner.Consumptions;public void Complete()=>inner.Complete();
                public bool GetResult(short token){f.Completions.Add(Probe.Ambient.Value);return inner.GetResult(token);}
                public System.Threading.Tasks.Sources.ValueTaskSourceStatus GetStatus(short token)=>inner.GetStatus(token);
                public void OnCompleted(Action<object?> continuation,object? state,short token,System.Threading.Tasks.Sources.ValueTaskSourceOnCompletedFlags flags)=>inner.OnCompleted(continuation,state,token,flags);
            }
            [ZendiatorHandler]public sealed class Handler(Flow f):IStreamSourceHandler<Items,int,Cursor>,IAsyncDisposable{
                public ValueTask<Cursor> OpenAsync(Items r,CancellationToken t){f.Opened++;return new(new Cursor(f));}
                public ValueTask DisposeAsync(){f.SessionDisposals++;f.Released.Add("handler");return default;}
            }
            public sealed class Factory(Probe p):IOwnedSessionFactory<Items,Handler>{public Handler Create(Items r,CancellationToken t)=>new(p.For(r));}
            public sealed class Cursor(Flow f):IAsyncEnumerator<int>{
                public int Current{get{f.Reads++;return f.Pulls;}}
                public ValueTask<bool> MoveNextAsync(){
                    f.Moves.Add(Probe.Ambient.Value);f.Pulls++;
                    return f.Pulls switch{1=>new(true),2=>f.First.Task,3=>new(true),4=>f.Later.Task,5=>new(false),_=>throw new InvalidOperationException("source pulled after EOF")};
                }
                public ValueTask DisposeAsync(){f.CursorDisposals++;f.Released.Add("cursor");f.CleanupStarted.SetResult(true);return f.Cleanup.Void;}
            }
            """, """
            var p=new Probe();var services=new ServiceCollection();services.AddSingleton(p);Register(services);
            await using var provider=services.BuildServiceProvider();await using var scope=provider.CreateAsyncScope();
            var mediator=scope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
            var a=mediator.StreamAsync(new Items("A")).GetAsyncEnumerator();var b=mediator.StreamAsync(new Items("B")).GetAsyncEnumerator();
            var c=mediator.StreamAsync(new Items("C")).GetAsyncEnumerator();var fa=p.Flows["A"];var fb=p.Flows["B"];var fc=p.Flows["C"];
            try{
                Probe.Ambient.Value="A-initial";Check.That(await a.MoveNextAsync(),"A opens with a synchronous initial item");
                Probe.Ambient.Value="B-initial";Check.That(await b.MoveNextAsync(),"B opens with a synchronous initial item");
                Probe.Ambient.Value="C-initial";Check.That(await c.MoveNextAsync(),"C opens with a synchronous initial item");
                var firstCurrentA=0;var reenteredA=false;var laterA=default(ValueTask<bool>);Task? publishedLaterA=null;
                Probe.Ambient.Value="A-first";var firstA=a.MoveNextAsync();var publishedA=Published(firstA,()=>{
                    firstCurrentA=a.Current;var ambient=Probe.Ambient.Value;
                    try{
                        Probe.Ambient.Value="A-sync";var secondA=a.MoveNextAsync();
                        Check.That(secondA.IsCompletedSuccessfully&&secondA.GetAwaiter().GetResult()&&a.Current==3,"the public completion continuation can immediately publish the next synchronous item");
                        Probe.Ambient.Value="A-later";laterA=a.MoveNextAsync();
                        Check.That(!laterA.IsCompleted&&fa.Reads==2,"the same continuation can immediately start another pending generation without reading ahead");
                        publishedLaterA=Published(laterA);reenteredA=true;
                    }finally{Probe.Ambient.Value=ambient;}
                });
                Probe.Ambient.Value="B-first";var firstB=b.MoveNextAsync();var publishedB=Published(firstB);
                Check.That(!firstA.IsCompleted&&!firstB.IsCompleted&&fa.First.Consumptions==0&&fb.First.Consumptions==0,"independent owners each retain their pending first raw Move");
                Probe.Ambient.Value="completion";fb.First.Complete();await publishedB.ConfigureAwait(false);
                Check.That(!firstA.IsCompleted&&fa.First.Consumptions==0&&fb.First.Consumptions==1&&fb.Reads==0,"completing B neither consumes A nor reads Current");
                fa.First.Complete();await publishedA.ConfigureAwait(false);
                Check.That(reenteredA&&firstCurrentA==2&&b.Current==2,"A advances directly from its public completion while B retains its own published Current");
                Probe.Ambient.Value="B-sync";var secondB=b.MoveNextAsync();Check.That(secondB.IsCompletedSuccessfully&&await secondB&&b.Current==3,"a second owner keeps its synchronous source progress");
                Probe.Ambient.Value="B-later";var endB=b.MoveNextAsync();
                Check.That(!laterA.IsCompleted&&!endB.IsCompleted&&fa.Reads==2&&fb.Reads==2,"a later pending generation does not prefetch Current or complete another owner");
                Probe.Ambient.Value="completion";fa.Later.Complete();await publishedLaterA!.ConfigureAwait(false);
                Check.That(await laterA&&a.Current==4&&!endB.IsCompleted,"A can publish its later item while B remains pending");
                Check.That(await firstA&&await firstB,"earlier public results remain consumable after later generations reuse their owners");
                Probe.Ambient.Value="A-end";var endA=a.MoveNextAsync();await fa.CleanupStarted.Task.ConfigureAwait(false);
                Check.That(!endA.IsCompleted&&fa.Pulls==5&&fa.CursorDisposals==1&&fa.SessionDisposals==0,"synchronous EOF after pending success waits for user cleanup");
                fb.Later.Complete();await fb.CleanupStarted.Task.ConfigureAwait(false);
                Check.That(!endB.IsCompleted&&fb.Pulls==4&&fb.CursorDisposals==1&&fb.SessionDisposals==0&&fb.Later.Consumptions==1,"pending EOF drains once before awaiting its own user cleanup");
                fa.Cleanup.Complete();Check.That(!await endA&&!endB.IsCompleted,"closing A leaves B's pending cleanup intact");
                fb.Cleanup.Complete();Check.That(!await endB,"B reports EOF only after cleanup completes");

                Probe.Ambient.Value="C-first";var firstC=c.MoveNextAsync();Check.That(!firstC.IsCompleted,"the early-disposal owner first suspends in its raw source");
                Probe.Ambient.Value="completion";fc.First.Complete();Check.That(await firstC&&c.Current==2,"early disposal follows one published pending item");
                var dispose=c.DisposeAsync();var repeated=c.DisposeAsync();await fc.CleanupStarted.Task.ConfigureAwait(false);
                Check.That(!dispose.IsCompleted&&!repeated.IsCompleted&&fc.CursorDisposals==1&&fc.SessionDisposals==0,"repeated early disposal shares pending user cleanup");
                fc.Cleanup.Complete();await dispose;await repeated;
                await a.DisposeAsync();await b.DisposeAsync();await c.DisposeAsync();
                Check.That(!await a.MoveNextAsync()&&!await b.MoveNextAsync()&&!await c.MoveNextAsync(),"EOF and early disposal leave all owners closed");
                foreach(var f in p.Flows.Values){
                    Check.That(f.Opened==1&&f.First.Consumptions==1&&f.CursorDisposals==1&&f.SessionDisposals==1&&f.Cleanup.Consumptions==1&&f.Released.SequenceEqual(new[]{"cursor","handler"}),"every owner releases its cursor and owned handler once in order");
                }
                Check.That(fa.Pulls==5&&fb.Pulls==4&&fc.Pulls==2&&fc.Later.Consumptions==0&&fa.Reads==3&&fb.Reads==2&&fc.Reads==1,"cleanup does not advance or read a source again");
                Check.That(fa.Moves.SequenceEqual(new[]{"A-initial","A-first","A-sync","A-later","A-end"})&&fa.Completions.SequenceEqual(new[]{"A-first","A-later"}),"each pending generation resumes under its own calling context across synchronous bypass");
                Check.That(fb.Moves.SequenceEqual(new[]{"B-initial","B-first","B-sync","B-later"})&&fb.Completions.SequenceEqual(new[]{"B-first","B-later"})&&fc.Completions.SequenceEqual(new[]{"C-first"}),"completion callers and other owners cannot replace a pending operation's context");
            }finally{Probe.Ambient.Value=null;}

            static Task Published(ValueTask<bool> operation,Action? onCompleted=null){
                var completion=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                operation.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(()=>{
                    try{onCompleted?.Invoke();completion.SetResult(true);}catch(Exception error){completion.SetException(error);}
                });return completion.Task;
            }
            """);
        await RunNativeStreamFixture("mixed-pending-generations", source);
    }
}
