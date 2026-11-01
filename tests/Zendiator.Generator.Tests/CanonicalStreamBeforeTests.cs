using Microsoft.CodeAnalysis;
using Zendiator.SourceGenerator;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private const string StreamBeforeDeclarations = """
        public sealed record Items(string Mode,int Count=1):IStreamRequest<int>;
        public sealed class Probe{
            public static readonly AsyncLocal<string?> Ambient=new();
            public readonly SynchronizationContext SynchronousContext=new(),ResultContext=new();
            public readonly CancellationTokenSource Cancellation=new();
            public readonly Exception Failure=new InvalidOperationException("Before failed");
            public readonly List<string> Events=new(),OpenContexts=new(),MoveContexts=new();
            public readonly List<int> BeforeStates=new();
            public BeforeGate? Gate;public WeakReference? GateWeak,PayloadWeak;
            public readonly global::Zendiator.Generator.Tests.AsyncOnceGate<bool> SourceGate=new(true);
            public bool HoldSource;public int BeforeCreated,BeforeDisposed,ACalls,BCalls,CCreated,CCalls,Consumptions,HandlerCreated,HandlerDisposed,Opened,Moves,Reads,Closed;
        }
        public sealed class BeforeA: IAsyncStreamBefore<Items>,IDisposable{
            private readonly Probe p;private int calls;
            public BeforeA(Probe p){this.p=p;p.BeforeCreated++;}
            public ValueTask BeforeAsync(Items request,CancellationToken token){
                Check.That(token==p.Cancellation.Token,"Before sees the effective operation token");
                p.ACalls++;p.BeforeStates.Add(++calls);p.Events.Add("A");Probe.Ambient.Value="A";
                SynchronizationContext.SetSynchronizationContext(p.SynchronousContext);return default;
            }
            public void Dispose()=>p.BeforeDisposed++;
        }
        public sealed class BeforeB: IAsyncStreamBefore<Items>,IDisposable{
            private readonly Probe p;public BeforeB(Probe p){this.p=p;p.BeforeCreated++;}
            public ValueTask BeforeAsync(Items request,CancellationToken token){
                Check.That(token==p.Cancellation.Token&&Probe.Ambient.Value=="A","Before callbacks retain declared order and activation context");
                p.BCalls++;p.Events.Add("B");
                if(request.Mode=="ordinary"){Probe.Ambient.Value="B";SynchronizationContext.SetSynchronizationContext(p.ResultContext);return default;}
                var gate=new BeforeGate(p,request.Mode=="completed");p.Gate=gate;p.GateWeak=new WeakReference(gate);return gate.Task;
            }
            public void Dispose()=>p.BeforeDisposed++;
        }
        public sealed class BeforeC: IAsyncStreamBefore<Items>,IDisposable{
            private readonly Probe p;public BeforeC(Probe p){this.p=p;p.CCreated++;}
            public ValueTask BeforeAsync(Items request,CancellationToken token){p.CCalls++;p.Events.Add("C");return default;}
            public void Dispose()=>p.BeforeDisposed++;
        }
        public sealed class BeforeGate:global::System.Threading.Tasks.Sources.IValueTaskSource{
            private readonly Probe p;private object? payload=new object();
            private global::System.Threading.Tasks.Sources.ManualResetValueTaskSourceCore<bool> core;
            public BeforeGate(Probe p,bool completed){this.p=p;p.PayloadWeak=new WeakReference(payload!);if(completed)core.SetResult(true);}
            public ValueTask Task=>new(this,core.Version);
            public void Complete()=>core.SetResult(true);
            public void Fail(Exception error)=>core.SetException(error);
            public global::System.Threading.Tasks.Sources.ValueTaskSourceStatus GetStatus(short token)=>core.GetStatus(token);
            public void GetResult(short token){
                var kept=payload;
                try{
                    p.Consumptions++;p.Events.Add("B.GetResult");Probe.Ambient.Value="B";
                    SynchronizationContext.SetSynchronizationContext(p.ResultContext);core.GetResult(token);
                }
                finally{payload=null;GC.KeepAlive(kept);}
            }
            public void OnCompleted(Action<object?> continuation,object? state,short token,
                global::System.Threading.Tasks.Sources.ValueTaskSourceOnCompletedFlags flags)=>core.OnCompleted(continuation,state,token,flags);
        }
        [ZendiatorHandler]public sealed class Handler: IStreamSourceHandler<Items,int,Cursor>,IDisposable{
            private readonly Probe p;public Handler(Probe p){this.p=p;p.HandlerCreated++;}
            public ValueTask<Cursor> OpenAsync(Items request,CancellationToken token){
                p.Opened++;p.Events.Add("open");p.OpenContexts.Add(Probe.Ambient.Value??"null");return new(new Cursor(p,request.Count,token));
            }
            public void Dispose()=>p.HandlerDisposed++;
        }
        public sealed class Cursor(Probe p,int count,CancellationToken token):IAsyncEnumerator<int>{
            private int index=-1;private bool closed;
            public int Current{get{Check.That(!closed,"Current source remains live");p.Reads++;return index;}}
            public ValueTask<bool> MoveNextAsync(){
                Check.That(!closed,"source not disposed by another enumeration");token.ThrowIfCancellationRequested();
                p.Moves++;p.MoveContexts.Add(Probe.Ambient.Value??"null");index++;
                return p.HoldSource&&index==0?p.SourceGate.Task:new(index<count);
            }
            public ValueTask DisposeAsync(){Check.That(!closed,"source cleanup once");closed=true;p.Closed++;return default;}
        }
        public sealed class Repeat: IStreamMiddleware<Items,int>{
            public ValueTask<IAsyncEnumerator<int>> OpenAsync(Items request,IStreamUpstream<Items,int> next,CancellationToken token)
                =>new(Produce(request,next,token).GetAsyncEnumerator(token));
            private static async IAsyncEnumerable<int> Produce(Items request,IStreamUpstream<Items,int> next,
                [System.Runtime.CompilerServices.EnumeratorCancellation]CancellationToken token){
                for(var branch=0;branch<2;branch++){
                    await using var cursor=await next.OpenAsync(request,token).ConfigureAwait(false);
                    while(await cursor.MoveNextAsync().ConfigureAwait(false))yield return cursor.Current;
                }
            }
        }
        public readonly struct Sink(List<int> values):IStreamSink<int>{
            public ValueTask<bool> OnNextAsync(int item,CancellationToken token){values.Add(item);return new(true);}
        }
        """;

    [Fact]
    public async Task Stream_before_is_lazy_ordered_once_per_root_and_keeps_context_reopening_and_DI_ownership()
    {
        var source = NativeStreamFixture("""
            c.Handlers<Items>(DispatchFamily.Stream,typeof(Handler));
            c.Pipeline<Items>(DispatchFamily.Stream,Stage.Wrapper<ObservedRepeat>());
            c.StreamBefore<Items>(typeof(BeforeA),typeof(BeforeB));
            """, StreamBeforeDeclarations + """
            public sealed class ObservedRepeat:IStreamMiddleware<Items,int>{
                public static int Reads;
                public ValueTask<IAsyncEnumerator<int>> OpenAsync(Items request,IStreamUpstream<Items,int> next,CancellationToken token){
                    var opened=new Repeat().OpenAsync(request,next,token);
                    Check.That(opened.IsCompletedSuccessfully,"the fixture opens its Repeat iterator synchronously");
                    return new(new WatchCurrent(opened.GetAwaiter().GetResult()));
                }
                private sealed class WatchCurrent(IAsyncEnumerator<int> inner):IAsyncEnumerator<int>{
                    public int Current{get{Reads++;return inner.Current;}}
                    public ValueTask<bool> MoveNextAsync()=>inner.MoveNextAsync();
                    public ValueTask DisposeAsync()=>inner.DisposeAsync();
                }
            }
            """, """
            foreach(var mode in new[]{"ordinary","completed","pending"}){
                ObservedRepeat.Reads=0;
                var p=new Probe();var services=new ServiceCollection();services.AddSingleton(p);Register(services);
                Check.That(new[]{typeof(BeforeA),typeof(BeforeB),typeof(Handler)}.All(type=>services.Single(d=>d.ServiceType==type).Lifetime==ServiceLifetime.Transient),
                    "the generated fixture registers transient receivers and handler");
                await using var provider=services.BuildServiceProvider();var scope=provider.CreateAsyncScope();
                var m=scope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
                var request=new Items(mode);var feed=m.StreamAsync(request,p.Cancellation.Token);
                var cold=feed.GetAsyncEnumerator();await cold.DisposeAsync();
                Check.That(p.BeforeCreated==0&&p.HandlerCreated==0&&p.ACalls==0,"plan creation, Get and cold Dispose do not acquire Before or handler");
                var originalContext=SynchronizationContext.Current;
                SynchronizationContext? callerContext=mode=="pending"?null:new SynchronizationContext();
                try{
                    SynchronizationContext.SetSynchronizationContext(callerContext);
                    await Public(false);await Public(true);
                    Probe.Ambient.Value="request";var values=new List<int>();var terminalReads=ObservedRepeat.Reads;var terminalLeafReads=p.Reads;
                    var operation=m.StreamSource(request,p.Cancellation.Token).ConsumeAsync(new Sink(values));
                    Check.That(ReferenceEquals(SynchronizationContext.Current,callerContext),"typed terminal immediately restores synchronous Before context changes");
                    Release(operation.IsCompleted);
                    Check.That(ReferenceEquals(SynchronizationContext.Current,callerContext),"inline Before completion restores its terminal caller context");
                    await operation.ConfigureAwait(false);
                    Check.That(Probe.Ambient.Value=="request"&&ReferenceEquals(SynchronizationContext.Current,callerContext)&&values.SequenceEqual(new[]{0,0}),
                        "typed terminal also runs root preprocessing and ordinary inner reopen without leaking ambient changes");
                    Check.That(ObservedRepeat.Reads==terminalReads+2&&p.Reads==terminalLeafReads+2,
                        "typed consumption reads each returned Current once after Repeat explicitly reads its leaf");
                    Check.That(p.ACalls==3&&p.BCalls==3&&p.BeforeStates.SequenceEqual(new[]{1,2,3}),"a transient Before receiver is cached per mediator rather than recreated per enumeration");
                    Check.That(p.Consumptions==(mode=="ordinary"?0:3),"each completed or pending Before source is consumed once");
                    Check.That(p.BeforeCreated==2&&p.HandlerCreated==1&&p.BeforeDisposed==0&&p.HandlerDisposed==0,
                        "Before and handler remain borrowed across repeated use of one mediator");
                    var calls=p.ACalls;await scope.DisposeAsync();
                    Check.That(p.BeforeDisposed==2&&p.HandlerDisposed==1,"DI alone disposes the cached transient receivers");
                    var rejected=feed.GetAsyncEnumerator();Exception? failure=null;
                    try{await rejected.MoveNextAsync();}catch(Exception error){failure=error;}
                    Check.That(failure is ObjectDisposedException&&p.ACalls==calls,"a cache hit cannot invoke Before after its DI scope was disposed");
                }finally{Probe.Ambient.Value=null;SynchronizationContext.SetSynchronizationContext(originalContext);await scope.DisposeAsync().ConfigureAwait(false);p.Cancellation.Dispose();}

                async ValueTask Public(bool early){
                    Probe.Ambient.Value="request";var oldA=p.ACalls;var oldOpen=p.Opened;var oldReads=p.Reads;var oldOuterReads=ObservedRepeat.Reads;
                    var opens=p.OpenContexts.Count;var moves=p.MoveContexts.Count;
                    var cursor=feed.GetAsyncEnumerator();var first=cursor.MoveNextAsync();
                    Check.That(Probe.Ambient.Value=="request"&&ReferenceEquals(SynchronizationContext.Current,callerContext),
                        "synchronous Before and completed GetResult restore the first-Move caller immediately");
                    Release(first.IsCompleted);
                    Check.That(first.IsCompleted&&Probe.Ambient.Value=="request"&&ReferenceEquals(SynchronizationContext.Current,callerContext),
                        "controlled inline Before completion preserves its completing caller context");
                    Check.That(await first.ConfigureAwait(false)&&ObservedRepeat.Reads==oldOuterReads&&p.Reads==oldReads+1,
                        "first Move does not prefetch the returned wrapper Current; Repeat explicitly reads its leaf to yield");
                    Check.That(Probe.Ambient.Value=="request"&&ReferenceEquals(SynchronizationContext.Current,callerContext)&&cursor.Current==0,
                        "first result preserves caller ambient state and Current");
                    Check.That(ObservedRepeat.Reads==oldOuterReads+1&&p.Reads==oldReads+1,
                        "consumer Current reads the returned wrapper once without rereading the leaf");
                    var values=new List<int>{0};
                    if(!early)while(await cursor.MoveNextAsync().ConfigureAwait(false))values.Add(cursor.Current);
                    await cursor.DisposeAsync().ConfigureAwait(false);
                    Check.That(ObservedRepeat.Reads==oldOuterReads+values.Count&&p.Reads==oldReads+values.Count,
                        "Full and Early expose exactly the requested number of wrapper Current reads");
                    Check.That(p.ACalls==oldA+1&&p.BCalls==p.ACalls&&p.Opened==oldOpen+(early?1:2),
                        "inner middleware reopen never repeats root Before; a new root enumeration does");
                    Check.That(values.SequenceEqual(early?new[]{0}:new[]{0,0})&&p.OpenContexts[opens]=="B"&&p.MoveContexts[moves]=="B",
                        "Before changes reach pipeline initialization and its first raw pull");
                    if(!early)Check.That(p.OpenContexts[opens+1]=="request"&&p.MoveContexts.Skip(moves+1).All(v=>v=="request"),
                        "later pulls and inner reopen do not replay the first activation's ambient state");
                }
                void Release(bool completed){
                    if(mode=="pending"){
                        Check.That(!completed&&p.Gate is not null,"Before genuinely suspends its root operation");
                        var gate=p.Gate;p.Gate=null;gate!.Complete();
                    }else p.Gate=null;
                }
            }
            """);
        await RunNativeStreamFixture("before-root-lifecycle", source);
    }

    [Fact]
    public async Task Stream_before_pending_cancel_and_failure_stop_later_resolution_and_consume_once()
    {
        var source = NativeStreamFixture("""
            c.Handlers<Items>(DispatchFamily.Stream,typeof(Handler));
            c.StreamBefore(typeof(Items),typeof(BeforeA),typeof(BeforeB),typeof(BeforeC));
            """, StreamBeforeDeclarations, """
            foreach(var mode in new[]{"cancel-after-pending","failure-after-cancel","pre-cancel"}){
                var p=new Probe();var services=new ServiceCollection();services.AddSingleton(p);Register(services);
                Check.That(new[]{typeof(BeforeA),typeof(BeforeB),typeof(BeforeC),typeof(Handler)}.All(type=>services.Single(d=>d.ServiceType==type).Lifetime==ServiceLifetime.Transient),
                    "the pending fixture uses transient receiver and handler registrations");
                await using var provider=services.BuildServiceProvider();var scope=provider.CreateAsyncScope();
                var m=scope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
                try{
                    Probe.Ambient.Value="request";if(mode=="pre-cancel")p.Cancellation.Cancel();
                    var root=m.StreamAsync(new Items("pending"),p.Cancellation.Token).GetAsyncEnumerator();
                    var first=root.MoveNextAsync();
                    if(mode!="pre-cancel"){
                        Check.That(!first.IsCompleted&&p.Consumptions==0&&p.Gate is not null&&p.CCreated==0&&p.HandlerCreated==0,
                            "pending Before does not resolve later callbacks or handler");
                        Check.Invalid(()=>{_=root.DisposeAsync();});
                        Check.That(p.Consumptions==0&&p.Gate is not null,"Dispose rejects pending initial Move without consuming or cleaning it");
                        p.Cancellation.Cancel();var gate=p.Gate;p.Gate=null;
                        if(mode=="failure-after-cancel")gate!.Fail(p.Failure);else gate!.Complete();
                    }
                    Exception? failure=null;try{await first;}catch(Exception error){failure=error;}
                    if(mode=="failure-after-cancel")Check.That(ReferenceEquals(failure,p.Failure)&&first.IsFaulted&&!first.IsCanceled,
                        "the original pending Before failure wins a later cancellation check");
                    else Check.That(failure is OperationCanceledException&&first.IsCanceled,"normal completion followed by cancellation is canceled");
                    Check.That(p.CCreated==0&&p.CCalls==0&&p.HandlerCreated==0&&p.Opened==0&&p.Reads==0&&p.Closed==0,
                        "failed preprocessing never constructs or opens the handler and publishes no item");
                    Check.That(p.Consumptions==(mode=="pre-cancel"?0:1)&&p.BeforeCreated==(mode=="pre-cancel"?0:2),
                        "entry cancellation precedes DI; otherwise the issued Before operation is consumed once");
                    Check.That(Probe.Ambient.Value=="request"&&p.BeforeDisposed==0,"failed preprocessing restores caller context and leaves borrowed receivers to DI");
                    await root.DisposeAsync();Check.That(!await root.MoveNextAsync()&&p.ACalls==(mode=="pre-cancel"?0:1),"closed root cannot run preprocessing again");
                    await scope.DisposeAsync();Check.That(p.BeforeDisposed==p.BeforeCreated,"scope owns every resolved Before receiver");
                }finally{Probe.Ambient.Value=null;await scope.DisposeAsync();p.Cancellation.Dispose();}
            }
            {
                var p=new Probe();var services=new ServiceCollection();services.AddSingleton(p);Register(services);
                Check.That(new[]{typeof(BeforeA),typeof(BeforeB),typeof(BeforeC),typeof(Handler)}.All(type=>services.Single(d=>d.ServiceType==type).Lifetime==ServiceLifetime.Transient),
                    "the scope-disposal fixture warms transient receivers and handler");
                await using var provider=services.BuildServiceProvider();var scope=provider.CreateAsyncScope();
                var m=scope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
                try{
                    Probe.Ambient.Value="request";
                    var warm=m.StreamAsync(new Items("ordinary"),p.Cancellation.Token).GetAsyncEnumerator();
                    Check.That(await warm.MoveNextAsync()&&warm.Current==0,"warm root resolves all Before receivers and handler");
                    await warm.DisposeAsync();
                    Check.That(p.ACalls==1&&p.BCalls==1&&p.CCalls==1&&p.CCreated==1&&p.HandlerCreated==1&&p.Opened==1&&p.Closed==1,
                        "warm root closes after populating both later receiver caches");
                    var root=m.StreamAsync(new Items("pending"),p.Cancellation.Token).GetAsyncEnumerator();
                    var first=root.MoveNextAsync();
                    Check.That(!first.IsCompleted&&p.Gate is not null&&p.ACalls==2&&p.BCalls==2&&p.CCalls==1&&p.Opened==1,
                        "cached Before waits before invoking the cached later callback or opening the cached handler");
                    await scope.DisposeAsync();
                    Check.That(p.BeforeDisposed==3&&p.HandlerDisposed==1&&!p.Cancellation.IsCancellationRequested,
                        "scope disposal owns cached services without canceling the pending Before operation");
                    var gate=p.Gate;p.Gate=null;gate!.Complete();
                    Exception? failure=null;try{await first.ConfigureAwait(false);}catch(Exception error){failure=error;}
                    Check.That(failure is ObjectDisposedException&&first.IsFaulted&&!first.IsCanceled&&p.Consumptions==1,
                        "the issued Before result is consumed once before the resumed scope check faults the root");
                    Check.That(p.CCalls==1&&p.CCreated==1&&p.HandlerCreated==1&&p.Opened==1&&p.Closed==1&&p.Reads==1,
                        "resumption after scope disposal cannot invoke cached later callbacks or acquire another source");
                    Check.That(Probe.Ambient.Value=="request","failed resumption restores caller ambient state");
                    await root.DisposeAsync();
                }finally{Probe.Ambient.Value=null;await scope.DisposeAsync();p.Cancellation.Dispose();}
            }
            """);
        await RunNativeStreamFixture("before-pending-short-circuit", source);
    }

    [Fact]
    public async Task Stream_before_releases_consumed_awaiter_while_the_first_source_move_is_still_pending()
    {
        var source = NativeStreamFixture("""
            c.Handlers<Items>(DispatchFamily.Stream,typeof(Handler));
            c.StreamBefore<Items>(typeof(BeforeA),typeof(BeforeB));
            """, StreamBeforeDeclarations + """
            public sealed record Held(Probe Probe,ServiceProvider Provider,IServiceScope Scope,IAsyncEnumerator<int> Root,Task<bool> Move);
            public static class Lifetime{
                [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
                public static Held Start(){
                    var p=new Probe{HoldSource=true};var services=new ServiceCollection();services.AddSingleton(p);Bridge.Register(services);
                    var provider=services.BuildServiceProvider();var scope=provider.CreateScope();
                    var m=scope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
                    Probe.Ambient.Value="request";var root=m.StreamAsync(new Items("pending"),p.Cancellation.Token).GetAsyncEnumerator();
                    var first=root.MoveNextAsync();var saved=first.AsTask();
                    Check.That(!saved.IsCompleted&&p.Gate is not null&&p.Opened==0,"Before is pending before source acquisition");
                    var gate=p.Gate;p.Gate=null;gate!.Complete();
                    Check.That(!saved.IsCompleted&&p.Opened==1&&p.Moves==1&&p.SourceGate.Consumptions==0&&p.Consumptions==1,
                        "Before settled once but the first raw source Move remains truly pending");
                    return new(p,provider,scope,root,saved);
                }
            }
            """, """
            Held? held=null;Exception? workerFailure=null;
            var worker=new Thread(()=>{try{held=Lifetime.Start();}catch(Exception error){workerFailure=error;}}){IsBackground=true};
            worker.Start();Check.That(worker.Join(TimeSpan.FromSeconds(10)),"observer worker physically retires");
            if(workerFailure is not null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(workerFailure).Throw();
            Check.That(held is not null,"worker returned its live root/provider and pending result");
            var kept=held!;
            try{
                for(var i=0;i<3;i++){GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();}
                Check.That(kept.Probe.GateWeak is {IsAlive:false}&&kept.Probe.PayloadWeak is {IsAlive:false},
                    "consumed Before awaiter and payload are not retained while the later source operation remains pending");
                Check.That(kept.Probe.BeforeDisposed==0&&!kept.Move.IsCompleted,"cached receivers and provider stay live; no receiver collection is required");
                kept.Probe.SourceGate.Complete();Check.That(await kept.Move&&kept.Root.Current==0,"saved first-Move Task retains its result after Before awaiter collection");
                await kept.Root.DisposeAsync();
            }finally{
                if(!kept.Move.IsCompleted)kept.Probe.SourceGate.Complete();
                await kept.Move.ConfigureAwait(false);await kept.Root.DisposeAsync();
                kept.Scope.Dispose();kept.Provider.Dispose();kept.Probe.Cancellation.Dispose();GC.KeepAlive(kept);
            }
            """);
        await RunNativeStreamFixture("before-consumed-awaiter-lifetime", source);
    }

    [Fact]
    public async Task Stream_before_open_request_binding_keeps_the_cached_mutable_value_receiver()
    {
        var source = NativeStreamFixture("""
            c.Handlers(typeof(Items<>),DispatchFamily.Stream,typeof(Handler<>));
            c.StreamBefore(typeof(Items<>),typeof(Before<>));
            """, """
            public sealed class Tag;
            public readonly record struct Items<T>(int Value):IStreamRequest<int> where T:class,new();
            public sealed class Probe{public int Created,Closed;public readonly List<int> Calls=new(),DisposedCalls=new();}
            public struct Before<T>:IAsyncStreamBefore<Items<T>>,IDisposable where T:class,new(){
                private readonly Probe p;private int calls;
                public Before(Probe p){this.p=p;calls=0;p.Created++;}
                ValueTask IAsyncStreamBefore<Items<T>>.BeforeAsync(Items<T> request,CancellationToken token){p.Calls.Add(++calls);return default;}
                public void Dispose()=>p.DisposedCalls.Add(calls);
            }
            [ZendiatorHandler]public sealed class Handler<T>(Probe p):IStreamSourceHandler<Items<T>,int,Cursor> where T:class,new(){
                public ValueTask<Cursor> OpenAsync(Items<T> request,CancellationToken token)=>new(new Cursor(p,request.Value));
            }
            public sealed class Cursor(Probe p,int value):IAsyncEnumerator<int>{
                private int index;public int Current=>value;
                public ValueTask<bool> MoveNextAsync()=>new(index++==0);
                public ValueTask DisposeAsync(){p.Closed++;return default;}
            }
            public readonly struct Sink(List<int> values):IStreamSink<int>{
                public ValueTask<bool> OnNextAsync(int item,CancellationToken token){values.Add(item);return new(true);}
            }
            """, """
            var p=new Probe();var services=new ServiceCollection();services.AddSingleton(p);Register(services);
            Check.That(new[]{typeof(Before<>),typeof(Handler<>)}.All(type=>services.Single(d=>d.ServiceType==type).Lifetime==ServiceLifetime.Transient),
                "open generic Before and handler registrations are transient");
            await using var provider=services.BuildServiceProvider();var scope=provider.CreateAsyncScope();
            var m=scope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
            var feed=m.StreamAsync(new Items<Tag>(7));Check.That(p.Created==0,"open generic Before remains lazy");
            var values=new List<int>();await foreach(var item in feed.ConfigureAwait(false))values.Add(item);
            await m.StreamSource(new Items<Tag>(9)).ConsumeAsync(new Sink(values));
            Check.That(values.SequenceEqual(new[]{7,9})&&p.Closed==2&&p.Created==1&&p.Calls.SequenceEqual(new[]{1,2}),
                "request-only closure and both public paths retain the original cached Before box");
            Check.That(p.DisposedCalls.Count==0,"borrowed Before is not operation-owned");
            await scope.DisposeAsync();Check.That(p.DisposedCalls.SequenceEqual(new[]{2}),"DI disposes the original mutated Before box");
            """);
        await RunNativeStreamFixture("before-open-value-receiver", source);
    }

    [Theory]
    [InlineData("wrong-contract", "StageContractMismatch")]
    [InlineData("wrong-request", "StageTypeMismatch")]
    [InlineData("orphan", "UnsupportedGenerationShape")]
    [InlineData("non-stream", "UnsupportedDispatchFamily")]
    [InlineData("empty", "UnsupportedGenerationShape")]
    public void Stream_before_invalid_route_and_callback_contracts_are_diagnosed(string mode, string reason)
    {
        const string declarations = """
            public sealed record Items:IStreamRequest<int>,IRequest<int>;
            public sealed record Other:IStreamRequest<int>;
            [ZendiatorHandler]public sealed class Handler:IStreamSourceHandler<Items,int,Cursor>{
                public ValueTask<Cursor> OpenAsync(Items request,CancellationToken token)=>new(new Cursor());
            }
            [ZendiatorHandler]public sealed class SendHandler:IRequestHandler<Items,int>{
                public ValueTask<int> HandleAsync(Items request,CancellationToken token)=>new(0);
            }
            public sealed class Cursor:IAsyncEnumerator<int>{public int Current=>0;public ValueTask<bool> MoveNextAsync()=>new(false);public ValueTask DisposeAsync()=>default;}
            public sealed class Before__CONTRACT__{public ValueTask BeforeAsync(__REQUEST__ request,CancellationToken token)=>default;}
            """;
        var callback = declarations.Replace("__CONTRACT__", mode == "wrong-contract" ? "" : ":IAsyncStreamBefore<" + (mode == "wrong-request" ? "Other" : "Items") + ">")
            .Replace("__REQUEST__", mode == "wrong-request" ? "Other" : "Items");
        var configuration = mode switch
        {
            "orphan" => "c.StreamBefore<Items>(typeof(Before));",
            "non-stream" => "c.Handlers<Items>(DispatchFamily.SendAsync,typeof(SendHandler));c.StreamBefore<Items>(typeof(Before));",
            "empty" => "c.Handlers<Items>(DispatchFamily.Stream,typeof(Handler));c.StreamBefore<Items>();",
            _ => "c.Handlers<Items>(DispatchFamily.Stream,typeof(Handler));c.StreamBefore<Items>(typeof(Before));"
        };
        AssertStreamBeforeRejected(mode, reason, NativeStreamFixture(configuration, callback, "await Task.CompletedTask;"));
    }

    [Theory]
    [InlineData("overlap", "AmbiguousPipelineOrder")]
    [InlineData("same-service", "UnsupportedGenerationShape")]
    [InlineData("constraint", "UnsupportedGenerationShape")]
    [InlineData("uninferred", "UnsupportedGenerationShape")]
    public void Stream_before_open_and_closed_overlap_or_invalid_request_only_closure_is_diagnosed(string mode, string reason)
    {
        const string declarations = """
            public sealed class Tag;
            public readonly record struct Items<T>(int Value):IStreamRequest<int>;
            [ZendiatorHandler]public sealed class Handler<T>:IStreamSourceHandler<Items<T>,int,Cursor>{
                public ValueTask<Cursor> OpenAsync(Items<T> request,CancellationToken token)=>new(new Cursor());
            }
            public sealed class Cursor:IAsyncEnumerator<int>{public int Current=>0;public ValueTask<bool> MoveNextAsync()=>new(false);public ValueTask DisposeAsync()=>default;}
            public sealed class Before<T__EXTRA__>:IAsyncStreamBefore<Items<T>> __CONSTRAINT__{
                public ValueTask BeforeAsync(Items<T> request,CancellationToken token)=>default;
            }
            """;
        var callback = declarations.Replace("__EXTRA__", mode == "uninferred" ? ",TUnused" : "")
            .Replace("__CONSTRAINT__", mode == "constraint" ? "where T:struct" : "");
        const string route = "c.Handlers<Items<Tag>>(DispatchFamily.Stream,typeof(Handler<Tag>));";
        var configuration = route + (mode switch
        {
            "overlap" => "c.StreamBefore(typeof(Items<>),typeof(Before<>));c.StreamBefore<Items<Tag>>(typeof(Before<Tag>));",
            "same-service" => "c.StreamBefore<Items<Tag>>(typeof(Before<>),typeof(Before<Tag>));",
            "uninferred" => "c.StreamBefore<Items<Tag>>(typeof(Before<,>));",
            _ => "c.StreamBefore<Items<Tag>>(typeof(Before<>));"
        });
        AssertStreamBeforeRejected(mode, reason, NativeStreamFixture(configuration, callback, "await Task.CompletedTask;"));
    }

    private static void AssertStreamBeforeRejected(string mode, string reason, string source)
    {
        var input = SyncCompilation(source, "StreamBeforeRejected_" + mode);
        Assert.Empty(input.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        var result = SyncDriver().RunGenerators(input).GetRunResult();
        var descriptor = reason switch
        {
            "StageContractMismatch" => GeneratorDiagnostics.StageContractMismatch,
            "StageTypeMismatch" => GeneratorDiagnostics.StageTypeMismatch,
            "AmbiguousPipelineOrder" => GeneratorDiagnostics.AmbiguousPipelineOrder,
            "UnsupportedDispatchFamily" => GeneratorDiagnostics.UnsupportedDispatchFamily,
            "UnsupportedGenerationShape" => GeneratorDiagnostics.UnsupportedGenerationShape,
            _ => throw new ArgumentOutOfRangeException(nameof(reason))
        };
        var message = mode switch
        {
            "wrong-contract" or "wrong-request" => "StreamBefore requires exactly one trusted IAsyncStreamBefore<TRequest> contract for this route.",
            "orphan" => "StreamBefore requires an existing explicit Stream Pipeline or Handlers list with a matching request declaration.",
            "non-stream" => "StreamBefore can only attach to an explicit Stream route.",
            "empty" => "StreamBefore requires at least one service; omit an empty prelude declaration.",
            "overlap" => "StreamBefore requires one authoritative, nonoverlapping request list; open/closed specialization priority is not inferred.",
            "same-service" => "StreamBefore entries close to the same service type.",
            "constraint" => "Inferred Stream service type arguments do not satisfy its generic constraints.",
            "uninferred" => "Not every Stream service type argument is determined by its request/item contract. Supply a closed service type.",
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Descriptor == descriptor && d.GetMessage() == message));
        Assert.True(diagnostic.Location.IsInSource);
        Assert.Same(input.SyntaxTrees.Single(), diagnostic.Location.SourceTree);
        var anchor = source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length);
        Assert.True(anchor.StartsWith("c.StreamBefore", StringComparison.Ordinal) || anchor.StartsWith("typeof(Before", StringComparison.Ordinal), anchor);
        Assert.DoesNotContain(result.Results.Single().GeneratedSources,
            s => s.HintName.StartsWith("Zendiator.StreamCanonical.", StringComparison.Ordinal));
    }
}
