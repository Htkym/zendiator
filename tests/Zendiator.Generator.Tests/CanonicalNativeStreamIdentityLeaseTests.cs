using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    [Fact]
    public async Task Native_stream_identity_lease_keeps_struct_Current_public_generations_and_terminal_ownership()
    {
        var source = NativeStreamFixture("""
            c.Handlers<Items>(DispatchFamily.Stream,HandlerEntry.Session<Handler,HandlerFactory>());
            c.Pipeline<Items>(DispatchFamily.Stream,Stage.Wrapper<Relay1,RelayFactory1>(),Stage.Wrapper<Relay2,RelayFactory2>(),Stage.Wrapper<Relay3,RelayFactory3>());
            """, """
            public sealed record Items:IStreamRequest<int>;
            public sealed class Payload{public readonly byte[] Bytes=new byte[65536];}
            public static class EofDiagnostic{
                public static readonly bool Enabled=Environment.GetEnvironmentVariable("ZENDIATOR_LEASE_EOF_DIAGNOSTIC")=="1";
                private static readonly List<(string Name,int Thread)> observations=new();private static readonly object sync=new();
                public static int WorkerThread;public static bool CompletedBeforeGetResult,Joined;public static Held? Result;public static Task<Held>? Suspended;public static Exception? Error;public static Task? Observer;
                public static void Record(string name){if(!Enabled)return;lock(sync)observations.Add((name,Environment.CurrentManagedThreadId));}
                public static void Observe(Task terminal){if(!Enabled)return;Record("root.observer.register");Observer=terminal.ContinueWith(static _=>Record("root.terminal.notification"),CancellationToken.None,TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);}
                [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
                private static Held? WorkerRun(){
                    WorkerThread=Environment.CurrentManagedThreadId;Record("worker.enter");
                    Check.That(SynchronizationContext.Current is null&&TaskScheduler.Current==TaskScheduler.Default,"the diagnostic worker has no synchronization-context pump");
                    var operation=Lifetime.Run("eof");CompletedBeforeGetResult=operation.IsCompleted;Record("lifetime.task-returned");
                    if(!CompletedBeforeGetResult){Suspended=operation;return null;}
                    var held=operation.GetAwaiter().GetResult();Record("lifetime.result-obtained");return held;
                }
                [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
                public static Held? RunJoined(){
                    var worker=new Thread(static()=>{try{Result=WorkerRun();}catch(Exception error){Error=error;}finally{Record("worker.exit");}},8*1024*1024){IsBackground=true};
                    worker.Start();Joined=worker.Join(TimeSpan.FromSeconds(10));return Joined?Result:null;
                }
                public static bool Proven{
                    get{lock(sync)return Joined&&CompletedBeforeGetResult&&Error is null&&Result is not null&&Observer is {IsCompletedSuccessfully:true}
                        &&observations.All(x=>x.Thread==WorkerThread)&&new[]{"lifetime.before-return","root.terminal.notification","source.Boolean.result","source.Int32.result","source.Boolean.complete.return","source.Int32.complete.return","handler.cleanup","relay.1.cleanup","relay.2.cleanup","relay.3.cleanup"}.All(name=>observations.Any(x=>x.Name==name));}
                }
                private static string Quote(string text)=>"\""+text.Replace("\\","\\\\").Replace("\"","\\\"").Replace("\r","\\r").Replace("\n","\\n")+"\"";
                public static void Save(string state,string details){
                    var path=Environment.GetEnvironmentVariable("ZENDIATOR_LEASE_EOF_RECEIPT");
                    if(string.IsNullOrEmpty(path)){var folder=Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");if(string.IsNullOrEmpty(folder))return;path=System.IO.Path.Combine(folder,"identity-lease-eof-diagnostic.json");}
                    var directory=System.IO.Path.GetDirectoryName(path);if(!string.IsNullOrEmpty(directory))System.IO.Directory.CreateDirectory(directory);
                    string events;lock(sync)events=string.Join(",",observations.Select(x=>"{\"event\":"+Quote(x.Name)+",\"thread\":"+x.Thread+"}"));
                    System.IO.File.WriteAllText(path,"{\"state\":"+Quote(state)+",\"details\":"+Quote(details)+",\"mode\":\"eof\",\"fixtureOptimization\":\"unchanged Debug\",\"stackBytes\":8388608,\"gcRounds\":3,\"workerThread\":"+WorkerThread+",\"joined\":"+Joined.ToString().ToLowerInvariant()+",\"completedBeforeGetResult\":"+CompletedBeforeGetResult.ToString().ToLowerInvariant()+",\"observedChainStayedWorker\":"+Proven.ToString().ToLowerInvariant()+",\"actualConsumedValueTaskRetained\":true,\"priorEofConsumedFieldWasDefault\":true,\"observerLimit\":\"ExecuteSynchronously is a request, not a producer-retirement guarantee; physical Join and recorded source-return events provide this diagnostic boundary. Unobserved runtime work is not proven.\",\"error\":"+Quote(Error?.ToString()??"")+",\"observations\":["+events+"]}");
                }
            }
            public static class LeaseObservation{
                public static readonly bool RemainingOnly=Environment.GetEnvironmentVariable("ZENDIATOR_LEASE_REMAINING_LIFECYCLES")=="1";
                public static bool InUse;private static readonly List<string> rows=new();
                public sealed class Observation(Held? value,bool joined,bool completed,int thread,Exception? error){
                    public readonly Held? Value=value;public readonly bool Joined=joined,CompletedBeforeGetResult=completed;public readonly int Thread=thread;public readonly Exception? Error=error;
                }
                [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
                private static Held RunActor(string mode,out bool completed){var operation=Lifetime.Run(mode);completed=operation.IsCompleted;return operation.GetAwaiter().GetResult();}
                [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
                public static Observation RunJoinedMode(string mode){
                    InUse=true;Held? result=null;Exception? failure=null;var completed=false;var threadId=0;
                    var worker=new Thread(()=>{threadId=Environment.CurrentManagedThreadId;try{result=RunActor(mode,out completed);}catch(Exception error){failure=error;}},8*1024*1024){IsBackground=true};
                    worker.Start();var joined=worker.Join(TimeSpan.FromSeconds(10));return new(result,joined,completed,threadId,failure);
                }
                private static string Quote(string text)=>"\""+text.Replace("\\","\\\\").Replace("\"","\\\"").Replace("\r","\\r").Replace("\n","\\n")+"\"";
                public static void Save(string mode,Observation observation,string state,string details){
                    rows.Add("{\"mode\":"+Quote(mode)+",\"state\":"+Quote(state)+",\"workerThread\":"+observation.Thread+",\"workerJoined\":"+observation.Joined.ToString().ToLowerInvariant()+",\"actorCompletedBeforeGetResult\":"+observation.CompletedBeforeGetResult.ToString().ToLowerInvariant()+",\"details\":"+Quote(details)+",\"error\":"+Quote(observation.Error?.ToString()??"")+"}");
                    var path=Environment.GetEnvironmentVariable("ZENDIATOR_LEASE_LIFECYCLE_RECEIPT");
                    if(string.IsNullOrEmpty(path)){var folder=Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");if(string.IsNullOrEmpty(folder))return;path=System.IO.Path.Combine(folder,"identity-lease-lifecycle-observations.json");}
                    var directory=System.IO.Path.GetDirectoryName(path);if(!string.IsNullOrEmpty(directory))System.IO.Directory.CreateDirectory(directory);
                    System.IO.File.WriteAllText(path,"{\"remainingOnly\":"+RemainingOnly.ToString().ToLowerInvariant()+",\"fixtureOptimization\":\"unchanged Debug\",\"stackBytes\":8388608,\"gcRoundsPerMode\":3,\"allRuntimeFramesOnWorker\":null,\"boundaryLimit\":\"Physical Join retires the dedicated observer worker stack. A legitimately suspended actor may use ThreadPool continuations; this is not proof that all runtime callback frames ran or retired on that worker. Root, saved Task and actual consumed ValueTask remain held.\",\"modes\":["+string.Join(",",rows)+"]}");
                }
            }
            public sealed class Probe(string mode){
                public static readonly AsyncLocal<object?> Ambient=new();public readonly string Mode=mode;
                public bool Partial=>Mode.StartsWith("partial-",StringComparison.Ordinal);
                public readonly CancellationTokenSource Cancellation=new(),BoundaryCancellation=new();public CancellationTokenSource? CallerLinked;public CancellationToken SourceToken;
                public readonly ControlledSource<bool> Raw=new(true);
                public readonly ControlledSource<int> Cleanup=new(0);
                public readonly global::Zendiator.Generator.Tests.StreamHookRaceSource<bool> First=new(true,"manual"),Second=new(true,"manual"),Race=new(true,"before-registration"),Inline=new(true,"inside-registration");
                public readonly TaskCompletionSource<bool> CleanupStarted=new(TaskCreationOptions.RunContinuationsAsynchronously);
                public readonly Exception RawError=new FormatException("raw"),GetterError=new ArithmeticException("Current"),CleanupError=new InvalidOperationException("cursor cleanup"),HandlerError=new NotSupportedException("handler cleanup"),OuterError=new ApplicationException("outer cleanup");
                public readonly List<int> Positions=new(),OpenOrder=new();public readonly List<string> Released=new();
                public IAsyncEnumerator<int>? Root;public IStreamUpstream<Items,int>? Next;
                public readonly List<IAsyncEnumerator<int>> Aliases=new();public readonly List<WeakReference> Children=new();
                public WeakReference? HandlerWeak;public bool ThrowCurrent,LeaseBeforeFirstRaw,RootClosedDuringCleanup;
                public int RawMoves,Reads,CursorDisposals,HandlerDisposals,RelayDisposals,FactoryDisposals,DisposedPosition;
                public void RejectAlias(bool operation=false){
                    Check.That(Aliases.Count==3,"all three returned cursors are initialized before the lease endpoint runs");
                    if(Partial&&!operation)return;
                    foreach(var alias in Aliases){
                        Check.Invalid(()=>{_=alias.MoveNextAsync();});Check.Invalid(()=>{_=alias.Current;});Check.Invalid(()=>{_=alias.DisposeAsync();});
                        Check.Invalid(()=>{_=((Zendiator.Streaming.StreamCursorState)alias).DisposeAsync();});
                    }
                }
                [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
                public int ReadBoundaryCurrent()=>Aliases.First(alias=>((Zendiator.Streaming.StreamCursorState)alias).CancellationToken!=Cancellation.Token).Current;
                [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
                public int ReadSameTokenCurrent()=>Aliases.First(alias=>((Zendiator.Streaming.StreamCursorState)alias).CancellationToken==Cancellation.Token).Current;
                [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
                public void AssertFallbackGroup(){
                    const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                    var type=Root!.GetType();Check.That(type.GetField("_identityLease",flags)!.GetValue(Root) is null,"a partial token path does not retain an allocated lease descriptor");
                    var runner=type.GetField("_pendingTaskRunner",flags)!.GetValue(Root);Check.That(runner is not null,"the ordinary pending group runner is active");
                    var children=runner!.GetType().GetFields(flags).Where(field=>field.Name.StartsWith("_groupP",StringComparison.Ordinal)).Select(field=>field.GetValue(runner)).OfType<Zendiator.Streaming.StreamCursorState>().ToArray();
                    Check.That(children.Length==3&&Aliases.All(alias=>children.Any(child=>ReferenceEquals(child,alias))),"fallback holds the original per-operation group across the token boundary instead of scalar public forwarding");
                }
                public CancellationToken ForwardBoundaryToken(CancellationToken incoming)=>Mode=="partial-token"?BoundaryCancellation.Token:(CallerLinked=CancellationTokenSource.CreateLinkedTokenSource(incoming,BoundaryCancellation.Token)).Token;
            }
            public sealed class ControlledSource<T>(T value):System.Threading.Tasks.Sources.IValueTaskSource<T>,System.Threading.Tasks.Sources.IValueTaskSource{
                private System.Threading.Tasks.Sources.ManualResetValueTaskSourceCore<T> core;
                private readonly TaskCompletionSource<bool> registered=new(TaskCreationOptions.RunContinuationsAsynchronously);
                private int lifecycle;private const int Consumed=1,Retired=2;
                public int Consumptions{get;private set;}public Task Registered=>registered.Task;
                public ValueTask<T> Task=>new(this,core.Version);public ValueTask Void=>new(this,core.Version);
                public void Complete(){try{core.SetResult(value);}finally{Retire();EofDiagnostic.Record("source."+typeof(T).Name+".complete.return");}}
                public void Fail(Exception error){try{core.SetException(error);}finally{Retire();EofDiagnostic.Record("source."+typeof(T).Name+".fail.return");}}
                private void Retire(){if((Interlocked.Or(ref lifecycle,Retired)&Consumed)!=0)core.Reset();}
                public T GetResult(short token){
                    if(++Consumptions!=1)throw new InvalidOperationException("one controlled result consumption");
                    try{return core.GetResult(token);}finally{if((Interlocked.Or(ref lifecycle,Consumed)&Retired)!=0)core.Reset();EofDiagnostic.Record("source."+typeof(T).Name+".result");}
                }
                void System.Threading.Tasks.Sources.IValueTaskSource.GetResult(short token)=>GetResult(token);
                public System.Threading.Tasks.Sources.ValueTaskSourceStatus GetStatus(short token)=>core.GetStatus(token);
                public void OnCompleted(Action<object?> continuation,object? state,short token,System.Threading.Tasks.Sources.ValueTaskSourceOnCompletedFlags flags){
                    core.OnCompleted(continuation,state,token,flags);registered.TrySetResult(true);
                }
            }
            [ZendiatorHandler]public sealed class Handler(Probe p):IStreamSourceHandler<Items,int,Cursor>,IAsyncDisposable{
                public ValueTask<Cursor> OpenAsync(Items r,CancellationToken token){Check.That((token==p.Cancellation.Token)==!p.Partial,"only the token-boundary fallback endpoint has a different effective token");p.SourceToken=token;return new(new Cursor(p,token));}
                public ValueTask DisposeAsync(){EofDiagnostic.Record("handler.cleanup");p.HandlerDisposals++;p.Released.Add("handler");if(p.Mode=="cleanup-fault")throw p.HandlerError;return default;}
            }
            public sealed class HandlerFactory(Probe p):IOwnedSessionFactory<Items,Handler>,IDisposable{
                public Handler Create(Items r,CancellationToken token){var handler=new Handler(p);p.HandlerWeak=new(handler);return handler;}
                public void Dispose()=>p.FactoryDisposals++;
            }
            public struct Cursor(Probe p,CancellationToken token):IAsyncEnumerator<int>{
                private int index=-1;private bool closed;
                public int Current{get{p.Reads++;if(p.ThrowCurrent)throw p.GetterError;return index;}}
                public ValueTask<bool> MoveNextAsync(){
                    token.ThrowIfCancellationRequested();Check.That(token==p.SourceToken&&!closed,"the endpoint keeps its original effective token and mutable struct storage");
                    p.RejectAlias(operation:true);if(p.RawMoves==0)p.LeaseBeforeFirstRaw=true;p.RawMoves++;p.Positions.Add(++index);
                    if(index==0)return new(true);
                    if(p.Mode.StartsWith("publication-",StringComparison.Ordinal))
                        return index switch{1=>p.First.Task,2=>p.Second.Task,3=>p.Race.Task,4=>p.Inline.Task,5=>new(false),_=>throw new InvalidOperationException("publication pull after EOF")};
                    return index switch{1=>p.Raw.Task,2=>new(false),_=>throw new InvalidOperationException("terminal pull after EOF")};
                }
                public ValueTask DisposeAsync(){
                    EofDiagnostic.Record("cursor.cleanup");
                    Check.That(!closed,"the original raw struct closes once");closed=true;p.CursorDisposals++;p.DisposedPosition=index;p.Released.Add("cursor");
                    p.RootClosedDuringCleanup=((Zendiator.Streaming.StreamCursorState)p.Root!).IsClosed;p.RejectAlias(operation:true);
                    Check.That(p.Partial||p.RootClosedDuringCleanup,"root closure precedes fully transferred endpoint cleanup; all-or-none fallback retains ordinary cleanup timing");
                    p.CleanupStarted.TrySetResult(true);return p.Cleanup.Void;
                }
            }
            public abstract class Relay(Probe p,int role):IStreamMiddleware<Items,int>,IAsyncDisposable{
                public async ValueTask<IAsyncEnumerator<int>> OpenAsync(Items r,IStreamUpstream<Items,int> next,CancellationToken token){
                    p.OpenOrder.Add(role);var child=await next.OpenAsync(r,p.Partial&&role==2?p.ForwardBoundaryToken(token):token).ConfigureAwait(false);p.Next=next;p.Aliases.Add(child);p.Children.Add(new(child));return child;
                }
                public ValueTask DisposeAsync(){
                    EofDiagnostic.Record("relay."+role+".cleanup");
                    p.RelayDisposals++;p.Released.Add("relay"+role);if(p.Mode=="cleanup-fault"&&role==p.OpenOrder[0])throw p.OuterError;return default;
                }
            }
            public sealed class Relay1(Probe p):Relay(p,1);public sealed class Relay2(Probe p):Relay(p,2);public sealed class Relay3(Probe p):Relay(p,3);
            public sealed class RelayFactory1(Probe p):IOwnedSessionFactory<Items,Relay1>,IDisposable{public Relay1 Create(Items r,CancellationToken token)=>new(p);public void Dispose()=>p.FactoryDisposals++;}
            public sealed class RelayFactory2(Probe p):IOwnedSessionFactory<Items,Relay2>,IDisposable{public Relay2 Create(Items r,CancellationToken token)=>new(p);public void Dispose()=>p.FactoryDisposals++;}
            public sealed class RelayFactory3(Probe p):IOwnedSessionFactory<Items,Relay3>,IDisposable{public Relay3 Create(Items r,CancellationToken token)=>new(p);public void Dispose()=>p.FactoryDisposals++;}
            public sealed class Captured(ValueTask<bool> operation,WeakReference payload){public readonly ValueTask<bool> Operation=operation;public readonly WeakReference Payload=payload;}
            public sealed class Held(IAsyncEnumerator<int> root,Task<bool> saved,ValueTask<bool> consumed,WeakReference[] children,WeakReference handler,WeakReference? payload){
                public readonly IAsyncEnumerator<int> Root=root;public readonly Task<bool> Saved=saved;public readonly ValueTask<bool> Consumed=consumed;
                public readonly WeakReference[] Children=children;public readonly WeakReference Handler=handler;public readonly WeakReference? Payload=payload;
            }
            public static class Lifetime{
                [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
                public static Captured Capture(IAsyncEnumerator<int> root){
                    var payload=new Payload();var weak=new WeakReference(payload);ValueTask<bool> operation;
                    try{Probe.Ambient.Value=payload;operation=root.MoveNextAsync();}finally{Probe.Ambient.Value=null;}
                    return new(operation,weak);
                }
                [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
                public static bool Alive(WeakReference weak)=>weak.IsAlive;
                public static string RetainedPaths(Held held){
                    var targets=held.Children.Select((weak,index)=>(Target:weak.Target,Name:"child"+index)).Append((Target:held.Handler.Target,Name:"handler")).Where(x=>x.Target is not null).ToArray();
                    var queue=new Queue<(object Value,string Path,int Depth)>();queue.Enqueue((held.Root,"held.Root",0));queue.Enqueue((held.Saved,"held.SavedTask",0));queue.Enqueue((held.Consumed,"held.ConsumedValueTask",0));queue.Enqueue((Probe.Ambient,"fixture.Probe.Ambient",0));if(EofDiagnostic.Observer is { } observer)queue.Enqueue((observer,"fixture.EofDiagnostic.Observer",0));
                    var seen=new HashSet<object>(ReferenceEqualityComparer.Instance);var found=new List<string>();
                    var expanded=EofDiagnostic.Enabled||LeaseObservation.InUse;var limit=expanded?4096:96;var depth=expanded?14:6;
                    while(queue.Count!=0&&seen.Count<limit){
                        var node=queue.Dequeue();if(!seen.Add(node.Value)||node.Depth>depth)continue;foreach(var target in targets)if(ReferenceEquals(node.Value,target.Target))found.Add(node.Path+"->"+target.Name);
                        if(node.Value is Array array){if(array.Rank==1&&array.GetType().GetElementType() is {IsPrimitive:false,IsEnum:false})for(var i=0;i<Math.Min(array.Length,1024);i++){var value=array.GetValue(i);if(value is not null)queue.Enqueue((value,node.Path+"["+i+"]",node.Depth+1));}continue;}
                        for(var type=node.Value.GetType();type is not null&&type!=typeof(object);type=type.BaseType){
                            foreach(var field in type.GetFields(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.DeclaredOnly)){
                                var value=field.GetValue(node.Value);if(value is null)continue;var path=node.Path+"."+field.Name;
                                foreach(var target in targets)if(ReferenceEquals(value,target.Target))found.Add(path+"->"+target.Name);
                                var name=value.GetType().FullName??"";
                                var traversable=!value.GetType().IsPrimitive&&!value.GetType().IsEnum&&value is not string&&value is not Type&&value is not System.Reflection.MemberInfo&&value is not WeakReference&&value is not IntPtr&&value is not UIntPtr;
                                if(expanded?traversable:name.StartsWith("NativeStream.Generated.",StringComparison.Ordinal)||name.StartsWith("Zendiator.Streaming.",StringComparison.Ordinal))queue.Enqueue((value,path,node.Depth+1));
                            }
                        }
                    }
                    return found.Count==0?"no strong field path within bound nodes="+seen.Count:string.Join(";",found);
                }
                public static async Task<Held> Run(string mode){
                    var p=new Probe(mode);var services=new ServiceCollection();services.AddSingleton(p);Bridge.Register(services);
                    await using var provider=services.BuildServiceProvider();var scope=provider.CreateAsyncScope();
                    var mediator=scope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
                    var root=mediator.StreamAsync(new Items(),p.Cancellation.Token).GetAsyncEnumerator();p.Root=root;
                    Check.That(p.RawMoves==0&&p.Aliases.Count==0,"the public source remains lazy");
                    var initial=root.MoveNextAsync();Check.That(await initial&&p.LeaseBeforeFirstRaw&&p.RawMoves==1&&p.Reads==0,"initialized owners guard the first raw pull without Current reads");
                    p.RejectAlias();p.ThrowCurrent=true;Exception? getterFailure=null;try{_=root.Current;}catch(Exception error){getterFailure=error;}
                    Check.That(ReferenceEquals(getterFailure,p.GetterError)&&p.RawMoves==1&&p.CursorDisposals==0,"Current exceptions occur only at the requested getter and do not close or advance");
                    p.ThrowCurrent=false;Check.That(root.Current==0&&p.Reads==2,"the getter reads the original mutable struct field");
                    if(p.Partial){
                        Check.That(p.Aliases.Count(alias=>((Zendiator.Streaming.StreamCursorState)alias).CancellationToken==p.Cancellation.Token)==1,"the token boundary lies after a nonempty same-token outer prefix");
                        Check.That(p.ReadSameTokenCurrent()==0&&p.ReadBoundaryCurrent()==0,"all-or-none fallback leaves both outer and inner aliases publicly readable after root true");
                    }
                    Task terminal;Task? disposalJoin=null;Task<bool> saved=Task.FromResult(true);ValueTask<bool> heldFirst=mode=="early"?initial:default,heldSecond=default;WeakReference? payload=null;
                    Exception? expected=null;
                    if(mode=="early"){
                        terminal=root.DisposeAsync().AsTask();disposalJoin=root.DisposeAsync().AsTask();
                    }else{
                        var captured=Capture(root);payload=captured.Payload;var first=captured.Operation;
                        heldFirst=first;
                        Check.That(!first.IsCompleted&&p.Reads==(p.Partial?4:2),"the second source move is explicitly pending without pre-reading Current");p.RejectAlias(operation:true);if(p.Partial)p.AssertFallbackGroup();
                        if(mode=="publication-reentry"){
                            var awaiter=first.ConfigureAwait(false).GetAwaiter();var following=default(ValueTask<bool>);var inline=false;
                            var reentered=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                            awaiter.UnsafeOnCompleted(()=>{
                                try{
                                    Check.That(awaiter.GetResult()&&root.Current==1,"the root consumes its pending result before reentry");p.RejectAlias();inline=p.First.Completing;
                                    following=root.MoveNextAsync();Check.That(!following.IsCompleted&&p.RawMoves==3&&p.Second.Consumptions==0,"root reentry obtains another pending result before old notification retirement");reentered.SetResult(true);
                                }catch(Exception error){reentered.SetException(error);}
                            });
                            p.First.Complete();await reentered.Task.ConfigureAwait(false);Check.That(inline,"public consumption/reentry occurs inside the old raw callback");
                            saved=following.AsTask();p.Second.Complete();Check.That(await saved&&root.Current==2,"saved Task retains the reentered result and source position");
                            Check.That(await root.MoveNextAsync()&&p.Race.Consumptions==1&&p.Race.Registrations<=1,"completion before registration consumes one raw operation");
                            Check.That(await root.MoveNextAsync()&&p.Inline.Consumptions==1&&p.Inline.Registrations==1,"inline registration publishes once without a duplicate pull");
                            terminal=root.MoveNextAsync().AsTask();
                        }else if(mode=="publication-overflow"){
                            heldFirst=first;p.First.Complete();Check.That(heldFirst.IsCompleted,"old first result is complete but unconsumed");
                            heldSecond=root.MoveNextAsync();Check.That(!heldSecond.IsCompleted,"second retained generation begins pending");p.Second.Complete();
                            Check.That(heldSecond.IsCompleted&&await root.MoveNextAsync(),"a later raced completion does not consume old results");
                            Check.That(await root.MoveNextAsync()&&root.Current==4&&p.Inline.Consumptions==1,"a newer inline generation survives both unconsumed cached lanes");
                            terminal=root.MoveNextAsync().AsTask();
                        }else{
                            await p.Raw.Registered.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                            saved=first.AsTask();
                            if(mode is "eof" or "partial-token"){p.Raw.Complete();Check.That(await saved&&root.Current==1,"a pending struct item advances its original field once");terminal=root.MoveNextAsync().AsTask();}
                            else{
                                expected=p.RawError;
                                if(mode=="cancel"){p.Cancellation.Cancel();expected=new OperationCanceledException(p.Cancellation.Token);}
                                if(mode=="partial-root-cancel"){p.Cancellation.Cancel();Check.That(!p.BoundaryCancellation.IsCancellationRequested&&p.SourceToken.IsCancellationRequested,"root cancellation reaches its linked source without canceling the independent boundary token");expected=new OperationCanceledException(p.SourceToken);}
                                if(mode=="partial-boundary-cancel"){p.BoundaryCancellation.Cancel();Check.That(!p.Cancellation.IsCancellationRequested&&p.SourceToken.IsCancellationRequested,"boundary cancellation reaches its linked source without canceling the root token");expected=new OperationCanceledException(p.SourceToken);}
                                p.Raw.Fail(expected);terminal=saved;
                            }
                        }
                    }
                    await p.CleanupStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    await p.Cleanup.Registered.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    Check.That(!terminal.IsCompleted&&(p.Partial||p.RootClosedDuringCleanup)&&p.CursorDisposals==1&&p.HandlerDisposals==0,"public terminal completion includes pending child cleanup at either ownership boundary");
                    p.RejectAlias(operation:true);Check.Invalid(()=>{_=p.Next!.OpenAsync(new Items(),default);});
                    if(mode=="early")Check.That(!disposalJoin!.IsCompleted,"repeated pending root Dispose joins instead of releasing twice");
                    EofDiagnostic.Observe(terminal);
                    if(mode=="cleanup-fault"){p.Cleanup.Fail(p.CleanupError);expected=p.OuterError;}else p.Cleanup.Complete();
                    Exception? failure=null;try{await terminal.ConfigureAwait(false);}catch(Exception error){failure=error;}
                    Check.That(ReferenceEquals(failure,expected),"source/cancel exception identity is preserved and the outer cleanup error wins");
                    if(mode is "cancel" or "partial-root-cancel" or "partial-boundary-cancel")Check.That(terminal.IsCanceled,"OCE is published as Canceled, not Faulted");
                    if(disposalJoin is not null)await disposalJoin.ConfigureAwait(false);
                    if(mode=="publication-overflow")Check.That(await heldFirst&&await heldSecond,"unconsumed old results survive EOF cleanup and lease retirement");
                    if(mode is "eof" or "partial-token" or "publication-reentry")Check.That(await saved&&await saved,"materialized Task results remain durable after terminal cleanup");
                    Check.That(p.Positions.SequenceEqual(Enumerable.Range(0,p.RawMoves))&&p.DisposedPosition==p.RawMoves-1,"raw struct Move and Dispose operate on the same storage");
                    var requestedReads=p.Partial?(mode=="partial-token"?5:4):mode switch{"eof"=>3,"publication-reentry"=>4,"publication-overflow"=>3,_=>2};
                    Check.That(p.Reads==requestedReads,"only explicitly requested Current getters run, including the requested throwing read");
                    Check.That(p.Cleanup.Consumptions==1&&p.CursorDisposals==1&&p.HandlerDisposals==1&&p.RelayDisposals==3&&p.Released.SequenceEqual(new[]{"cursor","handler"}.Concat(p.OpenOrder.AsEnumerable().Reverse().Select(role=>"relay"+role))),"cleanup consumes once in inner-to-outer order despite failures");
                    Check.That(p.Raw.Consumptions==(mode is "early" or "publication-reentry" or "publication-overflow"?0:1),"the terminal raw source is consumed once when issued");
                    if(mode.StartsWith("publication-",StringComparison.Ordinal))Check.That(new[]{p.First,p.Second,p.Race,p.Inline}.All(g=>g.Consumptions==1),"every root publication raw result is consumed once");
                    Check.That(p.FactoryDisposals==0&&!await root.MoveNextAsync(),"DI factories remain borrowed and a retired root cannot advance");
                    await root.DisposeAsync();await root.DisposeAsync();
                    var held=new Held(root,saved,heldFirst,p.Children.ToArray(),p.HandlerWeak!,payload);p.Root=null;p.Aliases.Clear();p.Next=null;
                    await scope.DisposeAsync();Check.That(p.FactoryDisposals==4,"native DI owns all four captured factories");p.CallerLinked?.Dispose();p.Cancellation.Dispose();p.BoundaryCancellation.Dispose();EofDiagnostic.Record("lifetime.before-return");return held;
                }
            }
            """, """
            if(EofDiagnostic.Enabled){
                var joined=EofDiagnostic.RunJoined();
                if(!EofDiagnostic.Proven||joined is null){EofDiagnostic.Save("unproved-boundary","Lifetime.Run suspended, observed callback escaped the worker, worker failed, or Join timed out.");Check.That(false,"EOF diagnostic producer-retirement boundary is unproved");return;}
                for(var round=0;round<3;round++){GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();}
                var children=joined.Children.Select((child,index)=>(Alive:Lifetime.Alive(child),Index:index)).Where(x=>x.Alive).Select(x=>x.Index).ToArray();var handler=Lifetime.Alive(joined.Handler);var payload=joined.Payload is not null&&Lifetime.Alive(joined.Payload);
                var detail="mode=eof aliveChildren=["+string.Join(",",children)+"] handlerAlive="+handler+" payloadAlive="+payload;
                if(children.Length!=0||handler||payload)detail+=" paths="+Lifetime.RetainedPaths(joined);
                EofDiagnostic.Save(children.Length==0&&!handler&&!payload?"released-after-joined-worker":"retained-after-joined-worker",detail);
                Check.That(children.Length==0&&!handler,detail);Check.That(!payload,detail);GC.KeepAlive(joined);return;
            }
            var modes=LeaseObservation.RemainingOnly?new[]{"fault","cancel","early","cleanup-fault","publication-reentry","publication-overflow","partial-token","partial-root-cancel","partial-boundary-cancel"}:new[]{"eof","fault","cancel","early","cleanup-fault","publication-reentry","publication-overflow","partial-token","partial-root-cancel","partial-boundary-cancel"};
            foreach(var mode in modes){
                var observation=LeaseObservation.RunJoinedMode(mode);var held=observation.Value;
                if(!observation.Joined||observation.Error is not null||held is null){LeaseObservation.Save(mode,observation,"worker-incomplete-or-failed","No weak-release pass is claimed.");Check.That(false,"mode="+mode+" joined worker did not return its Held result: "+observation.Error);return;}
                await Task.CompletedTask.ConfigureAwait(false);
                for(var round=0;round<3;round++){GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();}
                var aliveChildren=held.Children.Select((child,index)=>(Alive:Lifetime.Alive(child),Index:index)).Where(x=>x.Alive).Select(x=>x.Index).ToArray();var handlerAlive=Lifetime.Alive(held.Handler);
                var payloadAlive=held.Payload is not null&&Lifetime.Alive(held.Payload);var detail="mode="+mode+" aliveChildren=["+string.Join(",",aliveChildren)+"] handlerAlive="+handlerAlive+" payloadAlive="+payloadAlive;
                if(aliveChildren.Length!=0||handlerAlive||payloadAlive)detail+=" paths="+Lifetime.RetainedPaths(held);
                LeaseObservation.Save(mode,observation,aliveChildren.Length==0&&!handlerAlive&&!payloadAlive?"released-after-worker-join":"retained-after-worker-join",detail);
                Check.That(aliveChildren.Length==0&&!handlerAlive,detail);Check.That(!payloadAlive,detail);
                GC.KeepAlive(held);
            }
            """);
        await RunNativeStreamFixture("identity-lease-struct-lifecycle", source);
    }

    [Fact]
    public async Task Native_stream_identity_lease_partial_busy_handoff_keeps_unclaimed_work_and_outer_cleanup_priority()
    {
        var source = NativeStreamFixture("""
            c.Handlers<Items>(DispatchFamily.Stream,HandlerEntry.Session<Handler,HandlerFactory>());
            c.Pipeline<Items>(DispatchFamily.Stream,Stage.Wrapper<Hidden,HiddenFactory>(),Stage.Wrapper<Outer,OuterFactory>());
            """, """
            public sealed record Items:IStreamRequest<int>;
            public sealed class Probe(bool afterScan){
                public readonly bool AfterScanMode=afterScan;public static Action? AfterScan;public int ScanSeams;
                public IAsyncEnumerator<int>? Root,Claimed,Leaf;public IStreamUpstream<Items,int>? OuterPort,InnerPort;
                public Task<bool>? Pending;public readonly global::Zendiator.Generator.Tests.AsyncOnceGate<bool> Move=new(true);
                public readonly Exception InnerError=new ArithmeticException("claimed cleanup"),OuterError=new FormatException("outer cleanup");
                public readonly List<string> Released=new();public bool ClaimedGuardObserved,RootClosedBeforeClaimedCleanup;
                public int Opened,RawMoves,Reads,CursorDisposals,HandlerDisposals,HiddenDisposals,OuterDisposals,FactoryDisposals;
            }
            [ZendiatorHandler]public sealed class Handler(Probe p):IStreamSourceHandler<Items,int,Cursor>,IAsyncDisposable{
                public ValueTask<Cursor> OpenAsync(Items request,CancellationToken token){p.Opened++;return new(new Cursor(p));}
                public ValueTask DisposeAsync(){p.HandlerDisposals++;p.Released.Add("handler");return default;}
            }
            public struct Cursor(Probe p):IAsyncEnumerator<int>{
                public int Current{get{p.Reads++;return 1;}}
                public ValueTask<bool> MoveNextAsync(){p.RawMoves++;return p.Move.Task;}
                public ValueTask DisposeAsync(){p.CursorDisposals++;p.Released.Add("cursor");return default;}
            }
            public sealed class HandlerFactory(Probe p):IOwnedSessionFactory<Items,Handler>,IDisposable{
                public Handler Create(Items request,CancellationToken token)=>new(p);public void Dispose()=>p.FactoryDisposals++;
            }
            public sealed class Hidden(Probe p):IStreamMiddleware<Items,int>,IAsyncDisposable{
                public async ValueTask<IAsyncEnumerator<int>> OpenAsync(Items request,IStreamUpstream<Items,int> next,CancellationToken token){p.InnerPort=next;p.Leaf=await next.OpenAsync(request,token).ConfigureAwait(false);return p.Leaf;}
                public ValueTask DisposeAsync(){
                    p.RootClosedBeforeClaimedCleanup=((Zendiator.Streaming.StreamCursorState)p.Root!).IsClosed;
                    Check.That(p.RootClosedBeforeClaimedCleanup,"failed attachment closes the root before its already-claimed child cleanup");
                    Check.Invalid(()=>{_=p.Claimed!.MoveNextAsync();});Check.Invalid(()=>{_=((Zendiator.Streaming.StreamCursorState)p.Claimed!).DisposeAsync();});
                    p.ClaimedGuardObserved=true;p.HiddenDisposals++;p.Released.Add("hidden");throw p.InnerError;
                }
            }
            public sealed class HiddenFactory(Probe p):IOwnedSessionFactory<Items,Hidden>,IDisposable{
                public Hidden Create(Items request,CancellationToken token)=>new(p);public void Dispose()=>p.FactoryDisposals++;
            }
            public sealed class Outer(Probe p):IStreamMiddleware<Items,int>,IAsyncDisposable{
                public async ValueTask<IAsyncEnumerator<int>> OpenAsync(Items request,IStreamUpstream<Items,int> next,CancellationToken token){
                    p.OuterPort=next;p.Claimed=await next.OpenAsync(request,token).ConfigureAwait(false);
                    Check.That(((Zendiator.Streaming.StreamCursorState)p.Claimed).CancellationToken==token&&((Zendiator.Streaming.StreamCursorState)p.Leaf!).CancellationToken==token,"both candidate edges have the same initialized effective token");
                    if(!p.AfterScanMode){p.Pending=p.Leaf!.MoveNextAsync().AsTask();Check.That(!p.Pending.IsCompleted&&p.RawMoves==1&&p.Move.Consumptions==0,"only the next inner child owns pending raw work before handoff");}
                    return p.Claimed;
                }
                public ValueTask DisposeAsync(){p.OuterDisposals++;p.Released.Add("outer");throw p.OuterError;}
            }
            public sealed class OuterFactory(Probe p):IOwnedSessionFactory<Items,Outer>,IDisposable{
                public Outer Create(Items request,CancellationToken token)=>new(p);public void Dispose()=>p.FactoryDisposals++;
            }
            """, """
            foreach(var afterScan in new[]{false,true}){
            var p=new Probe(afterScan);var services=new ServiceCollection();services.AddSingleton(p);Register(services);
            await using var provider=services.BuildServiceProvider();await using var scope=provider.CreateAsyncScope();using var token=new CancellationTokenSource();
            var mediator=scope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
            var root=mediator.StreamAsync(new Items(),token.Token).GetAsyncEnumerator();p.Root=root;
            if(afterScan)Probe.AfterScan=()=>{Probe.AfterScan=null;p.ScanSeams++;p.Pending=p.Leaf!.MoveNextAsync().AsTask();Check.That(!p.Pending.IsCompleted&&p.RawMoves==1&&p.Move.Consumptions==0,"the scanned inner child becomes busy before acquisition begins");};
            try{
            var initial=root.MoveNextAsync().AsTask();Exception? failure=null;try{await initial.ConfigureAwait(false);}catch(Exception error){failure=error;}
            Check.That(p.ScanSeams==(afterScan?1:0),"the diagnostic source seam runs exactly once only for the post-scan race");
            Check.That(ReferenceEquals(failure,p.OuterError)&&initial.IsFaulted,"the later outer cleanup error replaces attachment and claimed-child cleanup errors");
            Check.That(p.RootClosedBeforeClaimedCleanup&&p.ClaimedGuardObserved&&p.HiddenDisposals==1&&p.OuterDisposals==1,"a nonempty claimed prefix closes under its held claim rather than treating handoff as an all-ineligible fallback");
            Check.That(p.Opened==1&&p.RawMoves==1&&p.Reads==0&&p.Move.Consumptions==0&&p.Pending is {IsCompleted:false},"failed inner acquisition does not reissue or consume its existing raw operation");
            Check.That(p.CursorDisposals==0&&p.HandlerDisposals==0&&p.Released.SequenceEqual(new[]{"hidden","outer"}),"root cleanup closes only its claimed prefix and its own session");
            Check.That(((Zendiator.Streaming.StreamCursorState)p.Claimed!).IsClosed&&!await p.Claimed.MoveNextAsync(),"the claimed child's gate is retired only after its cleanup completes");
            Check.That(!((Zendiator.Streaming.StreamCursorState)p.Leaf!).IsClosed,"the unclaimed leaf is not closed by partial attachment rollback");
            Check.Invalid(()=>{_=p.Leaf!.MoveNextAsync();});Check.Invalid(()=>{_=p.Leaf!.DisposeAsync();});
            Check.Invalid(()=>{_=p.OuterPort!.OpenAsync(new Items(),token.Token);});Check.Invalid(()=>{_=p.InnerPort!.OpenAsync(new Items(),token.Token);});
            p.Move.Complete();Check.That(await p.Pending!&&p.Move.Consumptions==1&&p.RawMoves==1,"the original leaf operation still consumes its own result once after root failure");
            await p.Leaf!.DisposeAsync();Check.That(p.CursorDisposals==1&&p.HandlerDisposals==1&&p.Released.SequenceEqual(new[]{"hidden","outer","cursor","handler"}),"the retained leaf performs its own cleanup after its pending work settles");
            await root.DisposeAsync();await root.DisposeAsync();Check.That(!await root.MoveNextAsync()&&!await p.Leaf.MoveNextAsync(),"both closed owners remain terminal without another pull");
            Check.That(p.FactoryDisposals==0,"all session factories remain borrowed");await scope.DisposeAsync();Check.That(p.FactoryDisposals==3,"native DI owns the three captured factories");
            }finally{Probe.AfterScan=null;}
            }
            """);
        var insertions = 0;
        await RunNativeStreamFixture("identity-lease-partial-busy-handoff", source, generatedTransform: generated =>
        {
            if (!generated.Contains("private void AttachIdentityLease()")) return generated;
            const string marker = "var lease = new IdentityLease();";
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(generated, System.Text.RegularExpressions.Regex.Escape(marker)));
            insertions++;
            return generated.Replace(marker, "global::NativeStream.Probe.AfterScan?.Invoke();\n" + marker, StringComparison.Ordinal);
        });
        Assert.Equal(1, insertions);
    }
}
