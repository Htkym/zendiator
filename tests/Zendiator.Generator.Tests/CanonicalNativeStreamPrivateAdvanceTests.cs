using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    [Fact]
    public async Task Native_stream_identity_transfer_keeps_root_generations_and_request_contexts_independent()
    {
        var source = NativeStreamFixture("""
            c.Handlers<Items>(DispatchFamily.Stream,HandlerEntry.Session<Handler,HandlerFactory>());
            c.Pipeline<Items>(DispatchFamily.Stream,Stage.Wrapper<Relay>());
            """, """
            public sealed record Items(string Id):IStreamRequest<int>;
            public sealed class Probe{
                public static readonly AsyncLocal<string?> Ambient=new();
                public readonly Dictionary<string,Flow> Flows=new(){["A"]=new(),["B"]=new()};
                public int FactoryDisposals,RelayDisposals;
            }
            public sealed class Flow{
                public IAsyncEnumerator<int>? Alias;
                public readonly MoveGate First,Later;
                public readonly global::Zendiator.Generator.Tests.AsyncOnceGate<int> Cleanup=new(0);
                public readonly TaskCompletionSource<bool> CleanupStarted=new(TaskCreationOptions.RunContinuationsAsynchronously);
                public readonly List<string?> Moves=new(),Completions=new(),CleanupContexts=new();
                public readonly List<string> Released=new();
                public int Opened,Pulls,Reads,CursorDisposals,HandlerDisposals;public bool FirstRawObservedLease;
                public Flow(){First=new(this);Later=new(this);}
            }
            public sealed class MoveGate(Flow f):System.Threading.Tasks.Sources.IValueTaskSource<bool>{
                private readonly global::Zendiator.Generator.Tests.AsyncOnceGate<bool> inner=new(true);
                public ValueTask<bool> Task=>new(this,0);public int Consumptions=>inner.Consumptions;
                public void Complete()=>inner.Complete();
                public bool GetResult(short token){f.Completions.Add(Probe.Ambient.Value);return inner.GetResult(token);}
                public System.Threading.Tasks.Sources.ValueTaskSourceStatus GetStatus(short token)=>inner.GetStatus(token);
                public void OnCompleted(Action<object?> continuation,object? state,short token,System.Threading.Tasks.Sources.ValueTaskSourceOnCompletedFlags flags)=>inner.OnCompleted(continuation,state,token,flags);
            }
            [ZendiatorHandler]public sealed class Handler(Flow f):IStreamSourceHandler<Items,int,Cursor>,IAsyncDisposable{
                public ValueTask<Cursor> OpenAsync(Items r,CancellationToken token){f.Opened++;return new(new Cursor(f,token));}
                public ValueTask DisposeAsync(){f.HandlerDisposals++;f.Released.Add("handler");f.CleanupContexts.Add(Probe.Ambient.Value);return default;}
            }
            public sealed class HandlerFactory(Probe p):IOwnedSessionFactory<Items,Handler>,IDisposable{
                public Handler Create(Items r,CancellationToken token)=>new(p.Flows[r.Id]);
                public void Dispose()=>p.FactoryDisposals++;
            }
            public sealed class Cursor(Flow f,CancellationToken token):IAsyncEnumerator<int>{
                public int Current{get{f.Reads++;return f.Pulls;}}
                public ValueTask<bool> MoveNextAsync(){
                    token.ThrowIfCancellationRequested();
                    if(f.Pulls==0){
                        Check.Invalid(()=>{_=f.Alias!.MoveNextAsync();});Check.Invalid(()=>{_=f.Alias!.Current;});Check.Invalid(()=>{_=f.Alias!.DisposeAsync();});
                        Check.Invalid(()=>{_=((Zendiator.Streaming.StreamCursorState)f.Alias!).DisposeAsync();});f.FirstRawObservedLease=true;
                    }
                    f.Moves.Add(Probe.Ambient.Value);f.Pulls++;
                    return f.Pulls switch{1=>new(true),2=>f.First.Task,3=>new(true),4=>f.Later.Task,5=>new(false),_=>throw new InvalidOperationException("pull after EOF")};
                }
                public ValueTask DisposeAsync(){f.CursorDisposals++;f.Released.Add("cursor");f.CleanupContexts.Add(Probe.Ambient.Value);f.CleanupStarted.SetResult(true);return f.Cleanup.Void;}
            }
            public sealed class Relay(Probe p):IStreamMiddleware<Items,int>,IDisposable{
                public async ValueTask<IAsyncEnumerator<int>> OpenAsync(Items r,IStreamUpstream<Items,int> next,CancellationToken token){
                    var child=await next.OpenAsync(r,token).ConfigureAwait(false);p.Flows[r.Id].Alias=child;return child;
                }
                public void Dispose()=>p.RelayDisposals++;
            }
            """, """
            var p=new Probe();var services=new ServiceCollection();services.AddSingleton(p);Register(services);
            await using var provider=services.BuildServiceProvider();var scope=provider.CreateAsyncScope();
            var mediator=scope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
            using var tokenA=new CancellationTokenSource();using var tokenB=new CancellationTokenSource();
            var a=mediator.StreamAsync(new Items("A"),tokenA.Token).GetAsyncEnumerator();
            var b=mediator.StreamAsync(new Items("B"),tokenB.Token).GetAsyncEnumerator();
            var fa=p.Flows["A"];var fb=p.Flows["B"];
            try{
                Check.That(fa.Opened==0&&fb.Opened==0,"public enumeration remains lazy");
                Probe.Ambient.Value="A-initial";Check.That(await a.MoveNextAsync(),"A initial item");
                Probe.Ambient.Value="B-initial";Check.That(await b.MoveNextAsync(),"B initial item");
                Check.That(fa.FirstRawObservedLease&&fb.FirstRawObservedLease,"same-token child operation rights transfer before each first raw Move");RejectLease(fa.Alias!);RejectLease(fb.Alias!);
                Probe.Ambient.Value="A-first";var firstA=a.MoveNextAsync().AsTask();
                Probe.Ambient.Value="B-first";var firstB=b.MoveNextAsync().AsTask();
                Check.That(!firstA.IsCompleted&&!firstB.IsCompleted&&fa.First.Consumptions==0&&fb.First.Consumptions==0,"both two-owner chains hold their raw pending operation");
                RejectPending(a,fa.Alias!);RejectPending(b,fb.Alias!);
                await CompleteOutsideContext(fb.First,"complete-B");Check.That(await firstB&&b.Current==2,"B completes with its own Current");
                Check.That(!firstA.IsCompleted&&fa.First.Consumptions==0&&fa.Reads==0,"B completion cannot consume or publish A");
                var laterA=default(ValueTask<bool>);var reentered=false;
                var publishedA=Published(firstA,()=>{
                    Check.That(a.Current==2,"root publication exposes the settled child Current");
                    RejectLease(fa.Alias!);Probe.Ambient.Value="A-root-sync";
                    var synchronous=a.MoveNextAsync();Check.That(synchronous.IsCompletedSuccessfully&&synchronous.GetAwaiter().GetResult()&&a.Current==3,"only the root advances the transferred child for a synchronous item");
                    Probe.Ambient.Value="A-later";laterA=a.MoveNextAsync();
                    Check.That(!laterA.IsCompleted&&fa.Pulls==4&&fa.Later.Consumptions==0,"root reentry can immediately start a fresh private generation");
                    reentered=true;
                });
                await CompleteOutsideContext(fa.First,"complete-A");await publishedA.ConfigureAwait(false);
                Check.That(reentered&&!laterA.IsCompleted&&fa.Reads==2,"publication performs no hidden Current read or extra pull");RejectPending(a,fa.Alias!);
                await CompleteOutsideContext(fa.Later,"complete-A-later");Check.That(await laterA&&a.Current==4,"the later generation publishes independently");
                Check.That(await firstA&&await firstB,"old public results remain durable after owner reuse");
                Probe.Ambient.Value="A-end";var endA=a.MoveNextAsync();await fa.CleanupStarted.Task.ConfigureAwait(false);
                Check.That(!endA.IsCompleted&&fa.Pulls==5&&fa.CursorDisposals==1&&fa.HandlerDisposals==0,"child EOF drains pending cleanup before parent completion");RejectPending(a,fa.Alias!);
                Probe.Ambient.Value="B-dispose";var closeB=b.DisposeAsync();var repeatedB=b.DisposeAsync();
                await fb.CleanupStarted.Task.ConfigureAwait(false);RejectLease(fb.Alias!);
                Check.That(!closeB.IsCompleted&&!repeatedB.IsCompleted&&fb.CursorDisposals==1&&fb.HandlerDisposals==0,"repeated root Dispose joins its cleanup while transferred alias disposal remains rejected");
                fa.Cleanup.Complete();Check.That(!await endA&&!closeB.IsCompleted,"A close cannot release B ownership");
                fb.Cleanup.Complete();await closeB;await repeatedB;
                await a.DisposeAsync();await b.DisposeAsync();
                Check.That(!await a.MoveNextAsync()&&!await b.MoveNextAsync(),"both closed roots stay terminal");
                Check.That(fa.Moves.SequenceEqual(new[]{"A-initial","A-first","A-root-sync","A-later","A-end"})&&fa.Completions.SequenceEqual(new[]{"A-first","A-later"}),"each root advance uses its own calling context rather than an old request");
                Check.That(fb.Moves.SequenceEqual(new[]{"B-initial","B-first"})&&fb.Completions.SequenceEqual(new[]{"B-first"}),"another request's context never enters B");
                foreach(var f in p.Flows.Values)Check.That(f.Opened==1&&f.First.Consumptions==1&&f.CursorDisposals==1&&f.HandlerDisposals==1&&f.Cleanup.Consumptions==1&&f.Released.SequenceEqual(new[]{"cursor","handler"}),"each chain releases its cursor and owned handler once in order");
                Check.That(fa.Later.Consumptions==1&&fb.Later.Consumptions==0&&fa.Reads==3&&fb.Reads==1,"raw results are consumed once and unused generations stay untouched");
                Check.That(fa.CleanupContexts.All(v=>v=="A-end")&&fb.CleanupContexts.All(v=>v=="B-dispose"),"pending cleanup resumes with its own closing context");
                Check.That(p.FactoryDisposals==0&&p.RelayDisposals==0,"factory and middleware stay borrowed until scope disposal");
                await scope.DisposeAsync();Check.That(p.FactoryDisposals==1&&p.RelayDisposals==1,"native DI disposes borrowed dependencies");
            }finally{Probe.Ambient.Value=null;}

            static void RejectPending(IAsyncEnumerator<int> root,IAsyncEnumerator<int> alias){
                Check.Invalid(()=>{_=root.Current;});Check.Invalid(()=>{_=alias.Current;});
                Check.Invalid(()=>{_=root.MoveNextAsync();});Check.Invalid(()=>{_=alias.MoveNextAsync();});
                Check.Invalid(()=>{_=root.DisposeAsync();});Check.Invalid(()=>{_=alias.DisposeAsync();});
                Check.Invalid(()=>{_=((Zendiator.Streaming.StreamCursorState)alias).DisposeAsync();});
            }
            static void RejectLease(IAsyncEnumerator<int> alias){
                Check.Invalid(()=>{_=alias.Current;});Check.Invalid(()=>{_=alias.MoveNextAsync();});Check.Invalid(()=>{_=alias.DisposeAsync();});
                Check.Invalid(()=>{_=((Zendiator.Streaming.StreamCursorState)alias).DisposeAsync();});
            }
            static Task Published(Task<bool> operation,Action callback){
                var done=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                operation.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(()=>{try{callback();done.SetResult(true);}catch(Exception error){done.SetException(error);}});return done.Task;
            }
            static async Task CompleteOutsideContext(MoveGate gate,string marker){
                Task completion;using(ExecutionContext.SuppressFlow())completion=Task.Run(()=>{Probe.Ambient.Value=marker;gate.Complete();});
                await completion.ConfigureAwait(false);
            }
            """);
        await RunNativeStreamFixture("private-identity-generations", source);
    }

    [Fact]
    public async Task Native_stream_identity_edge_pins_old_leases_and_preserves_fallback_and_cleanup_priority()
    {
        var source = NativeStreamFixture("""
            c.Handlers<Items>(DispatchFamily.Stream,HandlerEntry.Session<Handler,HandlerFactory>());
            c.Pipeline<Items>(DispatchFamily.Stream,Stage.Wrapper<Relay,RelayFactory>());
            """, """
            public sealed record Items(string Id):IStreamRequest<int>;
            public sealed class Probe(string mode){
                public readonly string Mode=mode;public readonly CancellationTokenSource Cancellation=new(),SecondCancellation=new();
                public static Action? AfterScan;public int ScanSeams;
                public IStreamUpstream<Items,int>? Next;public IAsyncEnumerator<int>? A,B;public Task<bool>? BusyHandoff;
                public readonly global::Zendiator.Generator.Tests.AsyncOnceGate<bool> Move=new(true),Outer=new(true);
                public readonly global::Zendiator.Generator.Tests.AsyncOnceGate<int> Cleanup=new(0);
                public readonly TaskCompletionSource<bool> CleanupStarted=new(TaskCreationOptions.RunContinuationsAsynchronously);
                public readonly Exception SourceError=new FormatException("source"),RawCleanupError=new ArithmeticException("raw cleanup"),ChildCleanupError=new InvalidOperationException("child cleanup"),OuterCleanupError=new NotSupportedException("outer cleanup");
                public readonly List<string> Released=new();public readonly Dictionary<string,int> Pulls=new(),Disposals=new();
                public int HandlerDisposals,RelayDisposals,WrapperDisposals,FactoryDisposals,Reads;
                public bool CleanupFailure=>Mode is "eof-cleanup" or "cancel-cleanup";
            }
            [ZendiatorHandler]public sealed class Handler(Probe p,string id):IStreamSourceHandler<Items,int,Cursor>,IAsyncDisposable{
                public ValueTask<Cursor> OpenAsync(Items r,CancellationToken token){p.Pulls.Add(id,0);p.Disposals.Add(id,0);return new(new Cursor(p,id,token));}
                public ValueTask DisposeAsync(){p.HandlerDisposals++;p.Released.Add("handler:"+id);if(p.CleanupFailure)throw p.ChildCleanupError;return default;}
            }
            public sealed class HandlerFactory(Probe p):IOwnedSessionFactory<Items,Handler>,IDisposable{
                public Handler Create(Items r,CancellationToken token)=>new(p,r.Id);public void Dispose()=>p.FactoryDisposals++;
            }
            public sealed class Cursor(Probe p,string id,CancellationToken token):IAsyncEnumerator<int>{
                public int Current{get{p.Reads++;return p.Pulls[id];}}
                public ValueTask<bool> MoveNextAsync(){token.ThrowIfCancellationRequested();var n=++p.Pulls[id];if(n==1)return new(true);if(n>2)throw new InvalidOperationException("pull after terminal result");return p.Mode is "fault" or "cancel" or "cancel-cleanup" or "cancel-private" or "busy-claim" or "busy-handoff"?p.Move.Task:new(false);}
                public ValueTask DisposeAsync(){p.Disposals[id]++;p.Released.Add("cursor:"+id);p.CleanupStarted.TrySetResult(true);return p.CleanupFailure?p.Cleanup.Void:default;}
            }
            public sealed class Relay(Probe p):IStreamMiddleware<Items,int>,IAsyncDisposable{
                public async ValueTask<IAsyncEnumerator<int>> OpenAsync(Items r,IStreamUpstream<Items,int> next,CancellationToken token){
                    p.Next=next;p.A=await next.OpenAsync(r,p.Mode is "busy-handoff" or "edge-after-scan"?token:p.SecondCancellation.Token).ConfigureAwait(false);
                    if(p.Mode=="busy-handoff"){
                        Check.That(await p.A.MoveNextAsync(),"middleware may use its child while Open still owns handoff");p.BusyHandoff=p.A.MoveNextAsync().AsTask();
                        Check.That(!p.BusyHandoff.IsCompleted&&p.Move.Consumptions==0,"the same-token child is already pending before root attachment");
                    }
                    if(p.Mode=="old-a-return"){
                        while(await p.A!.MoveNextAsync().ConfigureAwait(false)){}
                        p.B=await next.OpenAsync(new Items("B"),p.SecondCancellation.Token).ConfigureAwait(false);
                    }
                    return p.Mode=="distinct"?new Wrapper(p,p.A):p.A;
                }
                public ValueTask DisposeAsync(){p.RelayDisposals++;p.Released.Add("relay");if(p.CleanupFailure||p.Mode is "busy-claim" or "busy-handoff" or "edge-after-scan")throw p.OuterCleanupError;return default;}
            }
            public sealed class RelayFactory(Probe p):IOwnedSessionFactory<Items,Relay>,IDisposable{
                public Relay Create(Items r,CancellationToken token)=>new(p);public void Dispose()=>p.FactoryDisposals++;
            }
            public sealed class Wrapper(Probe p,IAsyncEnumerator<int> child):IAsyncEnumerator<int>{
                private int current;private bool first=true;public int Current=>current;
                public async ValueTask<bool> MoveNextAsync(){if(!await child.MoveNextAsync().ConfigureAwait(false))return false;current=child.Current;if(first){first=false;await p.Outer.Task.ConfigureAwait(false);}return true;}
                public ValueTask DisposeAsync(){p.WrapperDisposals++;return child.DisposeAsync();}
            }
            """, """
            foreach(var mode in new[]{"reopen","old-a-return","distinct","fault","cancel","early","eof-cleanup","cancel-cleanup","cancel-private","busy-claim","busy-handoff","edge-after-scan"}){
                var p=new Probe(mode);var services=new ServiceCollection();services.AddSingleton(p);Register(services);
                await using var provider=services.BuildServiceProvider();var scope=provider.CreateAsyncScope();
                var mediator=scope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
                var root=mediator.StreamAsync(new Items("A"),p.Cancellation.Token).GetAsyncEnumerator();
                if(mode=="edge-after-scan")Probe.AfterScan=()=>{
                    Probe.AfterScan=null;p.ScanSeams++;
                    var first=p.A!.MoveNextAsync();Check.That(first.IsCompletedSuccessfully&&first.GetAwaiter().GetResult(),"the scanned old A can still yield under its own public operation");
                    var end=p.A.MoveNextAsync();Check.That(end.IsCompletedSuccessfully&&!end.GetAwaiter().GetResult(),"old A closes normally before acquisition starts");
                    var opened=p.Next!.OpenAsync(new Items("B"),p.Cancellation.Token);Check.That(opened.IsCompletedSuccessfully,"the replacement B opens synchronously without advancing");p.B=opened.GetAwaiter().GetResult();
                    Check.That(!ReferenceEquals(p.A,p.B)&&p.Pulls["A"]==2&&p.Pulls["B"]==0,"the upstream now owns B while the root source still references old A");
                };
                try{
                var initial=root.MoveNextAsync();
                Check.That(p.A is not null&&(((Zendiator.Streaming.StreamCursorState)p.A).CancellationToken==p.Cancellation.Token)==(mode is "busy-handoff" or "edge-after-scan"),"the two attachment races use equal tokens; ordinary alias/reopen modes retain different-token fallback");
                if(mode=="busy-handoff"){
                    Exception? attachmentError=null;try{await initial;}catch(Exception error){attachmentError=error;}
                    Check.That(ReferenceEquals(attachmentError,p.OuterCleanupError)&&p.BusyHandoff is {IsCompleted:false}&&p.Move.Consumptions==0&&p.Pulls["A"]==2,"busy attachment neither steals nor reissues the unclaimed child operation and outer cleanup error wins");
                    Check.That(p.Disposals["A"]==0&&p.HandlerDisposals==0&&p.RelayDisposals==1,"busy handoff cleans only ownership actually acquired by the root");
                    Check.Invalid(()=>{_=p.A!.DisposeAsync();});Check.Invalid(()=>{_=p.Next!.OpenAsync(new Items("B"),default);});
                    p.Move.Complete();Check.That(await p.BusyHandoff!&&p.Move.Consumptions==1,"the old child retains its pending result after failed attachment");
                    await p.A!.DisposeAsync();
                }else if(mode=="edge-after-scan"){
                    Exception? attachmentError=null;try{await initial;}catch(Exception error){attachmentError=error;}
                    Check.That(p.ScanSeams==1&&ReferenceEquals(attachmentError,p.OuterCleanupError),"the legal post-scan A-to-B change closes the root and preserves outer cleanup priority");
                    Check.That(p.A is not null&&p.B is not null&&p.Pulls["A"]==2&&p.Pulls["B"]==0&&p.Reads==0,"a saved closed A cannot redirect raw Move or Current to the new active B");
                    Check.That(p.Disposals["A"]==1&&p.Disposals["B"]==1&&p.HandlerDisposals==2,"only the owning upstream performs B's legitimate cleanup without pulling it");
                }else if(mode=="old-a-return"){
                    Check.That(!await initial&&p.A!=null&&p.B!=null&&p.Pulls["A"]==2&&p.Pulls["B"]==0,"Open can return normal-ended A after acquiring B without redirecting A's next Move to B");
                    Check.That(p.Disposals["A"]==1&&p.Disposals["B"]==1&&p.HandlerDisposals==2,"old returned A and active B each close their own acquired resources");
                }else if(mode=="distinct"){
                    Check.That(!initial.IsCompleted&&p.A!=null&&p.A.Current==1&&p.Pulls["A"]==1,"a distinct opaque wrapper can remain pending after inner true");
                    Check.That(!await p.A!.MoveNextAsync(),"the retained child can finish while its distinct parent still awaits");
                    Check.That(!initial.IsCompleted,"identity selection must not absorb a distinct wrapper's user await");
                    p.Outer.Complete();Check.That(await initial&&root.Current==1&&p.Outer.Consumptions==1,"the wrapper retains its own Current and pending result");
                    Check.That(!await root.MoveNextAsync(),"distinct wrapper EOF remains ordinary fallback");
                    Check.That(p.WrapperDisposals==1,"fallback owns the returned wrapper exactly once");
                }else{
                    Check.That(await initial&&root.Current==1&&p.A!=null,"identity Open supplies the initial item normally");
                    if(mode=="reopen"){
                        Check.That(!await p.A!.MoveNextAsync(),"A completes normally before B opens");
                        using var tokenB=new CancellationTokenSource();p.B=await p.Next!.OpenAsync(new Items("B"),tokenB.Token);
                        Check.That(p.Pulls["B"]==0,"opening B does not advance it");
                        Check.That(!await root.MoveNextAsync(),"the parent's pinned old A remains EOF rather than following active B");
                        Check.That(p.Pulls["A"]==2&&p.Pulls["B"]==0&&p.Disposals["A"]==1&&p.Disposals["B"]==1&&p.HandlerDisposals==2,"parent cleanup releases old A and active B without reading B");
                    }else if(mode=="busy-claim"){
                        var alias=p.A!.MoveNextAsync();Check.That(!alias.IsCompleted&&p.Move.Consumptions==0,"the alias owns a pending public Move before the parent tries to claim it");
                        Exception? parentFailure=null;try{await root.MoveNextAsync();}catch(Exception error){parentFailure=error;}
                        Check.That(ReferenceEquals(parentFailure,p.OuterCleanupError)&&p.Disposals["A"]==0&&p.HandlerDisposals==0&&p.RelayDisposals==1,"a failed private claim cannot bypass child cleanup rejection and later outer cleanup error still wins");
                        Check.Invalid(()=>{_=p.Next!.OpenAsync(new Items("B"),default);});Check.Invalid(()=>{_=p.A!.DisposeAsync();});
                        p.Move.Complete();Check.That(await alias&&p.Move.Consumptions==1,"the original alias operation remains owned and is consumed once after the parent failure");
                        await p.A!.DisposeAsync();Check.That(p.Released.SequenceEqual(new[]{"relay","cursor:A","handler:A"})&&p.Disposals["A"]==1&&p.HandlerDisposals==1,"the retained alias drains before its own resources can be disposed");
                    }else if(mode is "fault" or "cancel" or "early"){
                        if(mode=="early")await p.A!.DisposeAsync();
                        else{
                            var failed=p.A!.MoveNextAsync();Check.That(!failed.IsCompleted&&p.Move.Consumptions==0,"alias failure is truly pending");
                            Exception expected=p.SourceError;if(mode=="cancel"){p.Cancellation.Cancel();expected=new OperationCanceledException(p.Cancellation.Token);}p.Move.Fail(expected);
                            Exception? failure=null;try{await failed;}catch(Exception error){failure=error;}
                            Check.That((mode=="cancel"?failure is OperationCanceledException canceled&&canceled.CancellationToken==p.Cancellation.Token:ReferenceEquals(failure,expected))&&p.Move.Consumptions==1,"the child preserves its public failure and consumes once");
                        }
                        Check.Invalid(()=>{_=p.Next!.OpenAsync(new Items("B"),default);});
                        if(mode=="cancel"){
                            Exception? canceledRoot=null;try{await root.MoveNextAsync();}catch(Exception error){canceledRoot=error;}
                            Check.That(canceledRoot is OperationCanceledException canceled&&canceled.CancellationToken==p.Cancellation.Token,"a later parent Move still honors its own canceled token");
                        }else Check.That(!await root.MoveNextAsync(),"already-closed A remains closed without a new operation");
                        Check.That(!await p.A!.MoveNextAsync(),"the old child cannot resume after failure or early disposal");
                        Check.That(!p.Pulls.ContainsKey("B")&&p.Pulls["A"]==(mode=="early"?1:2),"failed or early-disposed A never grants another lease or pulls again");
                    }else{
                        var closing=root.MoveNextAsync();var published=closing.AsTask();
                        if(mode is "cancel-cleanup" or "cancel-private"){
                            Check.That(!published.IsCompleted&&p.Move.Consumptions==0&&p.Disposals["A"]==0,"private raw cancellation is pending before cleanup");
                            p.Cancellation.Cancel();p.Move.Fail(new OperationCanceledException(p.Cancellation.Token));
                        }
                        if(p.CleanupFailure){
                            await p.CleanupStarted.Task.ConfigureAwait(false);
                            Check.That(!published.IsCompleted&&p.Disposals["A"]==1&&p.HandlerDisposals==0&&p.RelayDisposals==0&&p.Cleanup.Consumptions==0,"child cleanup settles before parent cleanup");
                            Check.Invalid(()=>{_=p.A!.DisposeAsync();});Check.Invalid(()=>{_=p.A!.MoveNextAsync();});
                            p.Cleanup.Fail(p.RawCleanupError);
                        }
                        Exception? failure=null;try{await published;}catch(Exception error){failure=error;}
                        if(p.CleanupFailure)Check.That(ReferenceEquals(failure,p.OuterCleanupError)&&p.Cleanup.Consumptions==1,"later outer cleanup error takes precedence over raw failure and both inner cleanup errors");
                        else Check.That(failure is OperationCanceledException canceled&&canceled.CancellationToken==p.Cancellation.Token&&published.IsCanceled,"true pending private cancellation publishes a canceled Task");
                        Check.That(p.Move.Consumptions==(mode=="eof-cleanup"?0:1),"private raw result is consumed exactly once");
                        Check.That(p.Released.SequenceEqual(new[]{"cursor:A","handler:A","relay"})&&p.Disposals["A"]==1&&p.HandlerDisposals==1&&p.RelayDisposals==1,"cleanup continues in reverse order without entering a failed child twice");
                    }
                }
                await root.DisposeAsync();await root.DisposeAsync();Check.That(!await root.MoveNextAsync(),"root remains closed after repeated disposal");
                Check.That(p.Disposals.Values.All(n=>n==1)&&p.RelayDisposals==1,"all acquired cursors and outer session release once");
                Check.That(p.FactoryDisposals==0,"session factories remain borrowed");await scope.DisposeAsync();Check.That(p.FactoryDisposals==2,"DI releases both borrowed factories");p.Cancellation.Dispose();p.SecondCancellation.Dispose();
                }finally{Probe.AfterScan=null;}
            }
            """);
        var insertions = 0;
        await RunNativeStreamFixture("private-identity-leases-cleanup", source, generatedTransform: generated =>
        {
            if (!generated.Contains("private void AttachIdentityLease()"))
                return generated;
            const string marker = "var lease = new IdentityLease();";
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(generated, System.Text.RegularExpressions.Regex.Escape(marker)));
            insertions++;
            return generated.Replace(marker, "global::NativeStream.Probe.AfterScan?.Invoke();\n" + marker, StringComparison.Ordinal);
        });
        Assert.Equal(1, insertions);
    }

    [Fact]
    public async Task Native_stream_private_child_release_does_not_mutate_alias_generation_before_root_publication()
    {
        var source = NativeStreamFixture("""
            c.Handlers<Items>(DispatchFamily.Stream,typeof(Handler));
            c.Pipeline<Items>(DispatchFamily.Stream,Stage.Wrapper<Relay>());
            """, """
            public sealed record Items:IStreamRequest<int>;
            public sealed class Probe{
                public static Action? AfterRelease;
                public static readonly AsyncLocal<string?> Ambient=new();
                public readonly CancellationTokenSource ChildCancellation=new();
                public IAsyncEnumerator<int>? Alias;
                public readonly global::Zendiator.Generator.Tests.AsyncOnceGate<bool> First=new(true),AliasMove=new(true);
                public readonly InlineRace Race=new();
                public Exception? SeamFailure;public int Opened,Pulls,Reads,Disposed,Seams;
                public string? RaceEntry,CleanupEntry;
            }
            public sealed class InlineRace:System.Threading.Tasks.Sources.IValueTaskSource<bool>{
                private bool completed;public int PendingStatuses,Registrations,Consumptions;public bool SchedulingContextRequested;public string? ResultContext;
                public ValueTask<bool> Task=>new(this,0);
                public bool GetResult(short token){if(!completed||++Consumptions!=1)throw new InvalidOperationException("invalid inline result consumption");ResultContext=Probe.Ambient.Value;return true;}
                public System.Threading.Tasks.Sources.ValueTaskSourceStatus GetStatus(short token){if(completed)return System.Threading.Tasks.Sources.ValueTaskSourceStatus.Succeeded;PendingStatuses++;return System.Threading.Tasks.Sources.ValueTaskSourceStatus.Pending;}
                public void OnCompleted(Action<object?> continuation,object? state,short token,System.Threading.Tasks.Sources.ValueTaskSourceOnCompletedFlags flags){
                    Registrations++;SchedulingContextRequested|=flags.HasFlag(System.Threading.Tasks.Sources.ValueTaskSourceOnCompletedFlags.UseSchedulingContext);
                    completed=true;continuation(state);
                }
            }
            [ZendiatorHandler]public sealed class Handler(Probe p):IStreamSourceHandler<Items,int,Cursor>{
                public ValueTask<Cursor> OpenAsync(Items r,CancellationToken token){p.Opened++;return new(new Cursor(p,token));}
            }
            public struct Cursor(Probe p,CancellationToken token):IAsyncEnumerator<int>{
                private int index;
                public int Current{get{p.Reads++;return index;}}
                public ValueTask<bool> MoveNextAsync(){
                    token.ThrowIfCancellationRequested();p.Pulls++;index++;
                    if(index==4){p.RaceEntry=Probe.Ambient.Value;Probe.Ambient.Value="raw-sync-change";}
                    return index switch{1=>new(true),2=>p.First.Task,3=>p.AliasMove.Task,4=>p.Race.Task,5=>new(false),_=>throw new InvalidOperationException("pull after EOF")};
                }
                public ValueTask DisposeAsync(){p.Disposed++;p.CleanupEntry=Probe.Ambient.Value;Probe.Ambient.Value="cleanup-change";return default;}
            }
            public sealed class Relay(Probe p):IStreamMiddleware<Items,int>{
                public async ValueTask<IAsyncEnumerator<int>> OpenAsync(Items r,IStreamUpstream<Items,int> next,CancellationToken token){
                    p.Alias=await next.OpenAsync(r,p.ChildCancellation.Token).ConfigureAwait(false);return p.Alias;
                }
            }
            """, """
            var p=new Probe();var services=new ServiceCollection();services.AddSingleton(p);Register(services);
            await using var provider=services.BuildServiceProvider();await using var scope=provider.CreateAsyncScope();
            var mediator=scope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
            var root=mediator.StreamAsync(new Items()).GetAsyncEnumerator();
            try{
                Check.That(await root.MoveNextAsync()&&root.Current==1,"initial Open and synchronous item keep their existing path");
                Check.That(((Zendiator.Streaming.StreamCursorState)p.Alias!).CancellationToken.CanBeCanceled,"the release-window fixture uses a different child token and keeps ordinary fallback ownership");
                var oldRoot=root.MoveNextAsync().AsTask();
                Check.That(!oldRoot.IsCompleted&&p.First.Consumptions==0&&p.Pulls==2,"the private raw operation is pending before the diagnostic release window");
                var aliasOperation=default(ValueTask<bool>);
                Probe.AfterRelease=()=>{
                    Probe.AfterRelease=null;p.Seams++;
                    try{
                        Check.That(!oldRoot.IsCompleted&&p.First.Consumptions==1,"child release occurs before the old root promise is published");
                        aliasOperation=p.Alias!.MoveNextAsync();
                        Check.That(!aliasOperation.IsCompleted&&p.Pulls==3&&p.AliasMove.Consumptions==0,"the released child starts a new public pending generation inside the window");
                        Check.Invalid(()=>{_=p.Alias!.MoveNextAsync();});Check.Invalid(()=>{_=p.Alias!.DisposeAsync();});
                    }catch(Exception error){p.SeamFailure=error;}
                };
                var published=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                oldRoot.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(()=>published.TrySetResult(true));
                p.First.Complete();await published.Task.ConfigureAwait(false);
                Check.That(p.SeamFailure==null&&p.Seams==1&&oldRoot.IsCompletedSuccessfully,"the diagnostic window accepts alias reentry before one successful root publication");
                Check.That(!aliasOperation.IsCompleted&&p.AliasMove.Consumptions==0&&p.Pulls==3&&p.Reads==1,"the old driver neither retires the new child operation nor reads Current");
                Check.Invalid(()=>{_=root.Current;});Check.Invalid(()=>{_=p.Alias!.Current;});
                Check.Invalid(()=>{_=p.Alias!.MoveNextAsync();});Check.Invalid(()=>{_=p.Alias!.DisposeAsync();});
                p.AliasMove.Complete();Check.That(await aliasOperation&&p.Alias!.Current==3&&p.AliasMove.Consumptions==1,"the alias's own pending result remains consumable once");
                Probe.Ambient.Value="race-request";
                var raced=root.MoveNextAsync();Check.That(await raced&&root.Current==4,"the next private raw operation can complete inline during registration");
                Check.That(p.RaceEntry=="race-request"&&p.Race.ResultContext=="raw-sync-change"&&Probe.Ambient.Value=="raw-sync-change","synchronous raw ambient changes enter that operation's pending boundary without replaying an old request");
                Check.That(p.Race.PendingStatuses==2&&p.Race.Registrations==1&&p.Race.Consumptions==1&&!p.Race.SchedulingContextRequested,"two pending status observations followed by inline completion consume the raw operation exactly once");
                Check.That(await oldRoot&&p.First.Consumptions==1&&p.Seams==1,"the previous public Task is durable across alias and later private generations");
                Probe.Ambient.Value="closing-request";
                Check.That(!await root.MoveNextAsync()&&p.Pulls==5&&p.Reads==3&&p.Disposed==1,"normal EOF closes once without a hidden pull or Current read");
                Check.That(p.CleanupEntry=="closing-request"&&Probe.Ambient.Value=="closing-request","cleanup keeps its async restoration boundary rather than leaking its synchronous ambient write");
                await root.DisposeAsync();Check.That(!await root.MoveNextAsync()&&p.Disposed==1,"final Dispose does not reopen or release twice");
            }finally{Probe.AfterRelease=null;Probe.Ambient.Value=null;p.ChildCancellation.Dispose();}
            """);
        var insertions = 0;
        const string marker = @"(child\.ReleasePrivateMove\(\);)(\s*CompleteParent\(moved, failure\);)";
        await RunNativeStreamFixture("private-release-window", source, generatedTransform: generated =>
        {
            if (!generated.Contains("private void CompleteChild("))
                return generated;
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(generated, marker));
            insertions++;
            return System.Text.RegularExpressions.Regex.Replace(generated, marker,
                "$1 global::NativeStream.Probe.AfterRelease?.Invoke();$2");
        });
        Assert.Equal(1, insertions);
    }
}
