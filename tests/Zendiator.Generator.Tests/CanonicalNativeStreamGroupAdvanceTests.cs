using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private const string GroupAdvanceConfiguration = """
        c.Handlers<Items>(DispatchFamily.Stream,HandlerEntry.Session<Handler,HandlerFactory>());
        c.Pipeline<Items>(DispatchFamily.Stream,Stage.Wrapper<Relay1,Factory1>(),Stage.Wrapper<Relay2,Factory2>(),Stage.Wrapper<Relay3,Factory3>(),Stage.Wrapper<Relay4,Factory4>(),Stage.Wrapper<Relay5,Factory5>());
        """;

    private const string GroupAdvanceDeclarations = """
        public sealed record Items(string Id):IStreamRequest<int>;
        public sealed class Probe(string mode){
            public static readonly AsyncLocal<string?> Ambient=new();public static Action<int>? AfterRelease;
            public readonly string Mode=mode;public readonly CancellationTokenSource Cancellation=new(),ReopenToken=new();
            public readonly Dictionary<string,Flow> Flows=new();public int FactoryDisposals;
            public Flow For(Items r){if(!Flows.TryGetValue(r.Id,out var flow))Flows.Add(r.Id,flow=new(this,r.Id));return flow;}
        }
        public sealed class Flow{
            public readonly Probe Probe;public readonly string Id;
            public readonly List<int> OpenOrder=new();public readonly List<string> Released=new();
            public readonly Dictionary<int,IAsyncEnumerator<int>> Aliases=new();
            public readonly Dictionary<int,IStreamUpstream<Items,int>> Next=new();
            public readonly Dictionary<int,CancellationTokenSource> ChildTokens=new();
            public readonly Dictionary<int,int> SessionDisposals=new();public readonly HashSet<int> FailCleanup=new();
            public readonly Dictionary<int,Exception> CleanupErrors=new();
            public readonly Exception RawError=new FormatException("raw Move");
            public readonly Gate First,Later;public readonly InlineRace Race;
            public readonly global::Zendiator.Generator.Tests.AsyncOnceGate<bool> Outer=new(true);
            public readonly global::Zendiator.Generator.Tests.AsyncOnceGate<int> Cleanup=new(0);
            public readonly TaskCompletionSource<bool> CleanupStarted=new(TaskCreationOptions.RunContinuationsAsynchronously);
            public readonly global::Zendiator.Generator.Tests.AsyncOnceGate<int> MiddleCleanup=new(0);
            public readonly TaskCompletionSource<bool> MiddleCleanupStarted=new(TaskCreationOptions.RunContinuationsAsynchronously);
            public readonly TaskCompletionSource<bool> OuterWaiting=new(TaskCreationOptions.RunContinuationsAsynchronously);
            public readonly List<string?> Moves=new(),Results=new();
            public IAsyncEnumerator<int>? Reopened;public Task<bool>? OpaqueResult;
            public string? CleanupEntry,MiddleCleanupEntry,RaceEntry;public Exception? SeamFailure;
            public int Opened,RawMoves,Reads,CursorDisposals,WrapperReads,WrapperDisposals,Seams;
            public Flow(Probe p,string id){Probe=p;Id=id;First=new(this,"first");Later=new(this,"later");Race=new(this);for(var i=1;i<=5;i++){ChildTokens.Add(i,new());CleanupErrors.Add(i,new InvalidOperationException("cleanup "+i));}}
            public int Disposals(int role)=>SessionDisposals.TryGetValue(role,out var count)?count:0;
            public ValueTask CloseSession(int role){
                SessionDisposals[role]=Disposals(role)+1;Released.Add(role==0?"handler":"relay"+role);
                if(FailCleanup.Contains(role))throw CleanupErrors[role];
                if(role==3&&Probe.Mode=="context"&&Id=="A"){
                    MiddleCleanupEntry=global::NativeStream.Probe.Ambient.Value;MiddleCleanupStarted.TrySetResult(true);return MiddleCleanup.Void;
                }
                return default;
            }
            public void CloseTokens(){foreach(var token in ChildTokens.Values)token.Dispose();}
        }
        public sealed class Gate(Flow flow,string name):System.Threading.Tasks.Sources.IValueTaskSource<bool>{
            private readonly global::Zendiator.Generator.Tests.AsyncOnceGate<bool> inner=new(true);
            public ValueTask<bool> Task=>new(this,0);public int Consumptions=>inner.Consumptions;
            public void Complete()=>inner.Complete();public void Fail(Exception error)=>inner.Fail(error);
            public bool GetResult(short token){flow.Results.Add(name+":"+Probe.Ambient.Value);return inner.GetResult(token);}
            public System.Threading.Tasks.Sources.ValueTaskSourceStatus GetStatus(short token)=>inner.GetStatus(token);
            public void OnCompleted(Action<object?> continuation,object? state,short token,System.Threading.Tasks.Sources.ValueTaskSourceOnCompletedFlags flags)=>inner.OnCompleted(continuation,state,token,flags);
        }
        public sealed class InlineRace(Flow flow):System.Threading.Tasks.Sources.IValueTaskSource<bool>{
            private bool completed;public int PendingStatuses,Registrations,Consumptions;public string? ResultContext;
            public ValueTask<bool> Task=>new(this,0);
            public bool GetResult(short token){if(!completed||++Consumptions!=1)throw new InvalidOperationException("invalid inline result consumption");ResultContext=Probe.Ambient.Value;return true;}
            public System.Threading.Tasks.Sources.ValueTaskSourceStatus GetStatus(short token){if(completed)return System.Threading.Tasks.Sources.ValueTaskSourceStatus.Succeeded;PendingStatuses++;return System.Threading.Tasks.Sources.ValueTaskSourceStatus.Pending;}
            public void OnCompleted(Action<object?> continuation,object? state,short token,System.Threading.Tasks.Sources.ValueTaskSourceOnCompletedFlags flags){Registrations++;completed=true;continuation(state);}
            public void Enter(){flow.RaceEntry=Probe.Ambient.Value;Probe.Ambient.Value="raw-sync-"+flow.Id;}
        }
        [ZendiatorHandler]public sealed class Handler(Flow flow):IStreamSourceHandler<Items,int,Cursor>,IAsyncDisposable{
            public ValueTask<Cursor> OpenAsync(Items r,CancellationToken token){flow.Opened++;return new(new Cursor(flow,token));}
            public ValueTask DisposeAsync()=>flow.CloseSession(0);
        }
        public sealed class HandlerFactory(Probe p):IOwnedSessionFactory<Items,Handler>,IDisposable{
            public Handler Create(Items r,CancellationToken token)=>new(p.For(r));public void Dispose()=>p.FactoryDisposals++;
        }
        public struct Cursor(Flow flow,CancellationToken token):IAsyncEnumerator<int>{
            private int index;public int Current{get{flow.Reads++;return index;}}
            public ValueTask<bool> MoveNextAsync(){
                token.ThrowIfCancellationRequested();flow.RawMoves++;index++;flow.Moves.Add(Probe.Ambient.Value);
                if(index==1)return new(true);
                var mode=flow.Probe.Mode;
                if(mode=="retirement"){
                    if(index==2)return flow.First.Task;if(index==3)return new(true);if(index==4)return flow.Later.Task;
                    if(index==5){flow.Race.Enter();return flow.Race.Task;}if(index==6)return new(false);
                }else if(mode=="opaque"){
                    if(index==2)return flow.First.Task;if(index==3)return new(true);if(index==4)return new(false);
                }else if(index==2){
                    if(mode=="throw")throw flow.RawError;
                    if(mode is "busy" or "closed" or "completed-fault" or "pending-cancel" or "cancel-cleanup" or "context")return flow.First.Task;
                    return new(false);
                }else if(index==3&&mode=="context")return new(false);
                throw new InvalidOperationException("source pulled after terminal result");
            }
            public ValueTask DisposeAsync(){flow.CursorDisposals++;flow.Released.Add("cursor");flow.CleanupEntry=Probe.Ambient.Value;flow.CleanupStarted.TrySetResult(true);return flow.Probe.Mode=="context"?flow.Cleanup.Void:default;}
        }
        public abstract class Relay(Probe p,Flow flow,int role):IStreamMiddleware<Items,int>,IAsyncDisposable{
            public async ValueTask<IAsyncEnumerator<int>> OpenAsync(Items r,IStreamUpstream<Items,int> next,CancellationToken token){
                flow.OpenOrder.Add(role);flow.Next.Add(role,next);
                var child=await next.OpenAsync(r,flow.ChildTokens[role].Token).ConfigureAwait(false);flow.Aliases.Add(role,child);
                if(role==3&&p.Mode=="old-a-return"){
                    while(await child.MoveNextAsync().ConfigureAwait(false)){}
                    flow.Reopened=await next.OpenAsync(new Items("B"),p.ReopenToken.Token).ConfigureAwait(false);
                }
                return role==3&&p.Mode=="opaque"?new DelayedWrapper(flow,child):child;
            }
            public ValueTask DisposeAsync()=>flow.CloseSession(role);
        }
        public sealed class Relay1(Probe p,Flow f):Relay(p,f,1);
        public sealed class Relay2(Probe p,Flow f):Relay(p,f,2);
        public sealed class Relay3(Probe p,Flow f):Relay(p,f,3);
        public sealed class Relay4(Probe p,Flow f):Relay(p,f,4);
        public sealed class Relay5(Probe p,Flow f):Relay(p,f,5);
        public sealed class Factory1(Probe p):IOwnedSessionFactory<Items,Relay1>,IDisposable{public Relay1 Create(Items r,CancellationToken t)=>new(p,p.For(r));public void Dispose()=>p.FactoryDisposals++;}
        public sealed class Factory2(Probe p):IOwnedSessionFactory<Items,Relay2>,IDisposable{public Relay2 Create(Items r,CancellationToken t)=>new(p,p.For(r));public void Dispose()=>p.FactoryDisposals++;}
        public sealed class Factory3(Probe p):IOwnedSessionFactory<Items,Relay3>,IDisposable{public Relay3 Create(Items r,CancellationToken t)=>new(p,p.For(r));public void Dispose()=>p.FactoryDisposals++;}
        public sealed class Factory4(Probe p):IOwnedSessionFactory<Items,Relay4>,IDisposable{public Relay4 Create(Items r,CancellationToken t)=>new(p,p.For(r));public void Dispose()=>p.FactoryDisposals++;}
        public sealed class Factory5(Probe p):IOwnedSessionFactory<Items,Relay5>,IDisposable{public Relay5 Create(Items r,CancellationToken t)=>new(p,p.For(r));public void Dispose()=>p.FactoryDisposals++;}
        public sealed class DelayedWrapper(Flow flow,IAsyncEnumerator<int> child):IAsyncEnumerator<int>{
            private int current,moves;public int Current{get{flow.WrapperReads++;return current;}}
            public async ValueTask<bool> MoveNextAsync(){
                moves++;var operation=child.MoveNextAsync();var durable=operation.AsTask();if(moves==2)flow.OpaqueResult=durable;
                if(!await durable.ConfigureAwait(false))return false;current=child.Current+100;
                if(moves==2){flow.OuterWaiting.TrySetResult(true);await flow.Outer.Task.ConfigureAwait(false);}return true;
            }
            public ValueTask DisposeAsync(){flow.WrapperDisposals++;return child.DisposeAsync();}
        }
        """;

    [Fact]
    public async Task Native_stream_group_partial_claims_preserve_unclaimed_work_and_outer_cleanup_priority()
    {
        var source = NativeStreamFixture(GroupAdvanceConfiguration, GroupAdvanceDeclarations, """
            foreach(var mode in new[]{"busy","closed","token","throw","completed-fault","pending-cancel","cancel-cleanup"}){
                var p=new Probe(mode);var services=new ServiceCollection();services.AddSingleton(p);Register(services);
                await using var provider=services.BuildServiceProvider();var scope=provider.CreateAsyncScope();
                var mediator=scope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
                var root=mediator.StreamAsync(new Items("A"),p.Cancellation.Token).GetAsyncEnumerator();var flow=p.For(new Items("A"));
                Check.That(flow.Opened==0,"G5 construction remains lazy");
                Check.That(await root.MoveNextAsync()&&flow.Opened==1&&flow.OpenOrder.Count==5&&flow.Reads==0,"first Move keeps the existing six-owner Open path without reading Current");
                var middle=flow.Aliases[flow.OpenOrder[2]];
                if(mode=="busy"){
                    var alias=middle.MoveNextAsync();Check.That(!alias.IsCompleted&&flow.RawMoves==2&&flow.First.Consumptions==0,"the middle public alias owns its pending lower subtree");
                    flow.FailCleanup.UnionWith(flow.OpenOrder.Take(3));
                    Exception? error=null;try{await root.MoveNextAsync();}catch(Exception caught){error=caught;}
                    Check.That(ReferenceEquals(error,flow.CleanupErrors[flow.OpenOrder[0]])&&flow.CursorDisposals==0&&flow.Disposals(0)==0&&flow.RawMoves==2,"failed partial claim cannot dispose or reissue the unclaimed pending source and outer cleanup wins");
                    Check.That(flow.Released.SequenceEqual(flow.OpenOrder.Take(3).Reverse().Select(role=>"relay"+role)),"only acquired ancestors unwind before the alias settles");
                    Check.Invalid(()=>{_=middle.MoveNextAsync();});Check.Invalid(()=>{_=middle.DisposeAsync();});
                    flow.First.Complete();Check.That(await alias&&flow.First.Consumptions==1,"the unclaimed alias retains its original operation and consumes it once");
                    await middle.DisposeAsync();Check.That(flow.CursorDisposals==1&&flow.Disposals(0)==1,"the alias drains before its owned lower subtree is released");
                }else if(mode=="closed"){
                    var alias=middle.MoveNextAsync();flow.First.Fail(flow.RawError);
                    Exception? error=null;try{await alias;}catch(Exception caught){error=caught;}
                    Check.That(ReferenceEquals(error,flow.RawError)&&flow.First.Consumptions==1,"the retained middle owner closes from its own fault");
                    Check.Invalid(()=>{_=flow.Next[flow.OpenOrder[2]].OpenAsync(new Items("B"),default);});
                    Check.That(!await root.MoveNextAsync()&&!await middle.MoveNextAsync()&&flow.RawMoves==2&&flow.CursorDisposals==1,"closed target is NoNewOperation rather than another raw Move or cleanup");
                    Check.That(!p.Flows.ContainsKey("B"),"a failed old target never grants a new lease");
                }else{
                    CancellationToken expectedToken=default;
                    if(mode=="token"){
                        var middleToken=flow.ChildTokens[flow.OpenOrder[1]];expectedToken=middleToken.Token;middleToken.Cancel();
                    }
                    if(mode=="completed-fault")flow.First.Fail(flow.RawError);
                    if(mode=="cancel-cleanup")flow.FailCleanup.UnionWith(new[]{flow.OpenOrder[0],flow.OpenOrder[^1]});
                    var operation=root.MoveNextAsync().AsTask();
                    if(mode is "pending-cancel" or "cancel-cleanup"){
                        Check.That(!operation.IsCompleted&&flow.RawMoves==2&&flow.First.Consumptions==0&&flow.CursorDisposals==0,"the full acquired prefix holds one raw pending operation before cancellation");
                        p.Cancellation.Cancel();expectedToken=p.Cancellation.Token;flow.First.Fail(new OperationCanceledException(expectedToken));
                    }
                    Exception? error=null;try{await operation;}catch(Exception caught){error=caught;}
                    if(mode is "token" or "pending-cancel")Check.That(error is OperationCanceledException canceled&&canceled.CancellationToken==expectedToken&&operation.IsCanceled,"cancellation without cleanup failure publishes a canceled Task with its token");
                    else if(mode=="cancel-cleanup")Check.That(ReferenceEquals(error,flow.CleanupErrors[flow.OpenOrder[0]])&&operation.IsFaulted,"outer cleanup failure replaces cancellation and inner cleanup failure");
                    else Check.That(ReferenceEquals(error,flow.RawError),"synchronous throw and already-completed raw fault retain the source exception");
                    Check.That(flow.RawMoves==(mode=="token"?1:2)&&flow.First.Consumptions==(mode is "token" or "throw"?0:1),"a failed token stops descent and every accepted raw result is consumed once");
                    Check.That(flow.Released.SequenceEqual(new[]{"cursor","handler"}.Concat(flow.OpenOrder.AsEnumerable().Reverse().Select(role=>"relay"+role))),"bottom-up settlement continues all reverse cleanup after errors");
                }
                await root.DisposeAsync();await root.DisposeAsync();Check.That(!await root.MoveNextAsync(),"failed or completed root stays closed");
                Check.That(flow.Reads==0&&flow.CursorDisposals==1&&Enumerable.Range(0,6).All(role=>flow.Disposals(role)==1),"no result getter or owned resource is duplicated by rollback");
                Check.That(p.FactoryDisposals==0,"all native factories stay borrowed");await scope.DisposeAsync();Check.That(p.FactoryDisposals==6,"native scope owns the six factories");
                flow.CloseTokens();p.Cancellation.Dispose();p.ReopenToken.Dispose();
            }
            """);
        await RunNativeStreamFixture("group-partial-claims", source);
    }

    [Fact]
    public async Task Native_stream_group_retired_leaf_and_middle_generations_preserve_lazy_Current_and_inline_completion()
    {
        var source = NativeStreamFixture(GroupAdvanceConfiguration, GroupAdvanceDeclarations, """
            var p=new Probe("retirement");var services=new ServiceCollection();services.AddSingleton(p);Register(services);
            await using var provider=services.BuildServiceProvider();await using var scope=provider.CreateAsyncScope();
            var mediator=scope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
            var root=mediator.StreamAsync(new Items("A")).GetAsyncEnumerator();var flow=p.For(new Items("A"));
            try{
                Probe.Ambient.Value="A-initial";Check.That(await root.MoveNextAsync()&&root.Current==1&&flow.Reads==1,"initial item keeps lazy Current");
                var leaf=flow.Aliases[flow.OpenOrder[^1]];var middle=flow.Aliases[flow.OpenOrder[2]];
                Probe.Ambient.Value="A-private";var oldRoot=root.MoveNextAsync().AsTask();
                Check.That(!oldRoot.IsCompleted&&flow.First.Consumptions==0&&flow.RawMoves==2,"the G5 private operation waits on one raw result");
                var aliasPending=default(ValueTask<bool>);
                Probe.AfterRelease=segment=>{
                    try{
                        if(segment==0){
                            flow.Seams++;Check.That(!oldRoot.IsCompleted&&flow.First.Consumptions==1,"leaf release precedes root publication");
                            Probe.Ambient.Value="A-leaf-sync";var moved=leaf.MoveNextAsync();
                            Check.That(moved.IsCompletedSuccessfully&&moved.GetAwaiter().GetResult()&&flow.RawMoves==3&&flow.Reads==1,"a retired leaf can advance synchronously without a hidden Current read");
                        }else if(segment==2){
                            flow.Seams++;Probe.AfterRelease=null;
                            Check.That(!oldRoot.IsCompleted,"middle release still precedes root publication");
                            Probe.Ambient.Value="A-middle-pending";aliasPending=middle.MoveNextAsync();
                            Check.That(!aliasPending.IsCompleted&&flow.RawMoves==4&&flow.Later.Consumptions==0,"the retired middle starts its next public lower-subtree generation");
                        }
                    }catch(Exception error){flow.SeamFailure=error;Probe.AfterRelease=null;}
                };
                var published=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                oldRoot.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(()=>published.TrySetResult(true));
                flow.First.Complete();await published.Task.ConfigureAwait(false);
                Check.That(flow.SeamFailure==null&&flow.Seams==2&&oldRoot.IsCompletedSuccessfully&&!aliasPending.IsCompleted&&flow.Reads==1,"old unwind preserves both retired generations without observing Current");
                Check.Invalid(()=>{_=root.Current;});Check.Invalid(()=>{_=middle.Current;});
                Check.Invalid(()=>{_=middle.MoveNextAsync();});Check.Invalid(()=>{_=middle.DisposeAsync();});Check.Invalid(()=>{_=leaf.DisposeAsync();});
                flow.Later.Complete();Check.That(await aliasPending&&middle.Current==4&&flow.Later.Consumptions==1,"the alias result remains owned and readable after old root completion");
                Probe.Ambient.Value="A-race";Check.That(await root.MoveNextAsync()&&root.Current==5,"a later full-prefix Move survives inline completion during registration");
                Check.That(flow.Race.PendingStatuses==2&&flow.Race.Registrations==1&&flow.Race.Consumptions==1&&flow.RaceEntry=="A-race"&&flow.Race.ResultContext=="raw-sync-A","status/registration race consumes once under the current operation context");
                Check.That(await oldRoot&&flow.First.Consumptions==1&&flow.Seams==2,"old public result remains durable after later generations");
                Probe.Ambient.Value="A-close";Check.That(!await root.MoveNextAsync()&&flow.RawMoves==6&&flow.Reads==3&&flow.CursorDisposals==1,"EOF closes once and no driver reads Current or prefetches");
                await root.DisposeAsync();Check.That(!await root.MoveNextAsync()&&Enumerable.Range(0,6).All(role=>flow.Disposals(role)==1),"each G5 session and raw cursor closes once");
            }finally{Probe.AfterRelease=null;Probe.Ambient.Value=null;flow.CloseTokens();p.Cancellation.Dispose();p.ReopenToken.Dispose();}
            """);
        var insertions = 0;
        await RunNativeStreamFixture("group-retired-generations", source, generatedTransform: generated =>
        {
            if (!generated.Contains("_groupP0")) return generated;
            var syntax = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(generated).GetRoot();
            var cursor = Assert.Single(syntax.DescendantNodes()
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>(), type => type.Identifier.ValueText == "Cursor5");
            var runner = Assert.Single(cursor.Members.OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>(),
                type => type.Identifier.ValueText == "PendingTaskRunner");
            var release = Assert.Single(runner.Members.OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>(),
                method => method.Identifier.ValueText == "ReleaseDepth");
            var original = release.ToFullString();
            var replacement = original;
            foreach (var segment in new[] { 0, 2 })
            {
                var depth = 4 - segment;
                var marker = @"(case\s+" + depth + @"\s*:\s*\{\s*var\s+child\s*=\s*_groupP" + segment
                    + @";\s*_groupP" + segment + @"\s*=\s*null;\s*child!\.ReleaseGroupMove\(\);)(\s*return;)";
                Assert.Single(System.Text.RegularExpressions.Regex.Matches(replacement, marker));
                replacement = System.Text.RegularExpressions.Regex.Replace(replacement, marker,
                    "$1 global::NativeStream.Probe.AfterRelease?.Invoke(" + segment + ");$2");
                insertions++;
            }
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(generated, System.Text.RegularExpressions.Regex.Escape(original)));
            return generated.Replace(original, replacement, StringComparison.Ordinal);
        });
        Assert.Equal(2, insertions);
    }

    [Fact]
    public async Task Native_stream_group_barriers_old_leases_and_request_contexts_keep_independent_lifetimes()
    {
        var source = NativeStreamFixture(GroupAdvanceConfiguration, GroupAdvanceDeclarations, """
            foreach(var mode in new[]{"opaque","pin-reopen","old-a-return","context"}){
                var p=new Probe(mode);var services=new ServiceCollection();services.AddSingleton(p);Register(services);
                await using var provider=services.BuildServiceProvider();var scope=provider.CreateAsyncScope();
                var mediator=scope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
                var root=mediator.StreamAsync(new Items("A")).GetAsyncEnumerator();var a=p.For(new Items("A"));
                Probe.Ambient.Value="A-initial";var initial=root.MoveNextAsync();
                if(mode=="old-a-return"){
                    Check.That(!await initial&&a.RawMoves==2&&a.Reopened!=null&&p.Flows["B"].Opened==1&&p.Flows["B"].RawMoves==0,"an opaque Open can return ended A after opening active B without redirecting old A");
                    Check.That(a.CursorDisposals==1&&p.Flows["B"].CursorDisposals==1,"old returned A and unused active B each retain their own cleanup");
                }else{
                    Check.That(await initial&&a.RawMoves==1,"all normal G5 routes publish their first item");
                    if(mode=="opaque"){
                        Check.That(root.Current==101&&a.WrapperReads==1&&a.Reads==1,"the opaque wrapper owns its transformed Current");
                        var pending=root.MoveNextAsync();Check.That(!pending.IsCompleted&&a.First.Consumptions==0,"the identity prefix stops at an actually distinct wrapper");
                        a.First.Complete();
                        await a.OuterWaiting.Task.ConfigureAwait(false);
                        Check.That(!pending.IsCompleted&&a.First.Consumptions==1&&a.WrapperReads==1,"inner true cannot bypass the wrapper's second user await or read its Current");
                        var inner=a.Aliases[3];Check.That(await inner.MoveNextAsync()&&a.RawMoves==3,"the lower public island remains independent while the opaque parent waits");
                        a.Outer.Complete();Check.That(await pending&&a.WrapperReads==1,"root true publication leaves opaque Current lazy");
                        Check.That(root.Current==102&&a.WrapperReads==2&&a.Reads==2,"the consumer reads the wrapper's cached value rather than the advanced lower alias");
                        Check.That(await a.OpaqueResult!&&a.First.Consumptions==1,"the lower public Task remains durable after alias reuse");
                        Check.That(!await root.MoveNextAsync()&&a.WrapperDisposals==1,"opaque EOF and returned-wrapper cleanup stay independent");
                    }else if(mode=="pin-reopen"){
                        var old=a.Aliases[3];Check.That(!await old.MoveNextAsync(),"the pinned middle A ends normally");
                        a.Reopened=await a.Next[3].OpenAsync(new Items("B"),p.ReopenToken.Token);
                        Check.That(p.Flows["B"].RawMoves==0&&!await root.MoveNextAsync(),"consumer prefix uses pinned A instead of newly active B");
                        Check.That(a.RawMoves==2&&p.Flows["B"].RawMoves==0&&p.Flows["B"].CursorDisposals==1,"B is cleaned without being advanced or borrowed by old A");
                    }else{
                        var other=mediator.StreamAsync(new Items("B")).GetAsyncEnumerator();var b=p.For(new Items("B"));
                        Probe.Ambient.Value="B-initial";Check.That(await other.MoveNextAsync(),"B has a separate lazy G5 owner set");
                        Probe.Ambient.Value="A-repeat";var pendingA=root.MoveNextAsync();
                        Probe.Ambient.Value="B-repeat";var pendingB=other.MoveNextAsync();
                        Check.That(!pendingA.IsCompleted&&!pendingB.IsCompleted&&a.Reads==0&&b.Reads==0,"both repeated operations stay pending without Current reads");
                        await CompleteOutsideContext(b.First,"complete-B");Check.That(await pendingB&&other.Current==2&&!pendingA.IsCompleted&&a.First.Consumptions==0,"B completion cannot consume or publish A");
                        await CompleteOutsideContext(a.First,"complete-A");Check.That(await pendingA&&root.Current==2,"A completes independently under its own advance context");
                        Check.That(a.Results.SequenceEqual(new[]{"first:A-repeat"})&&b.Results.SequenceEqual(new[]{"first:B-repeat"}),"request contexts are captured per pending phase rather than shared through the group");
                        Probe.Ambient.Value="A-close";var endA=root.MoveNextAsync();await a.CleanupStarted.Task.ConfigureAwait(false);
                        Probe.Ambient.Value="B-close";var closeB=other.DisposeAsync();await b.CleanupStarted.Task.ConfigureAwait(false);
                        Check.That(!endA.IsCompleted&&!closeB.IsCompleted&&a.CleanupEntry=="A-close"&&b.CleanupEntry=="B-close"&&a.Disposals(0)==0&&b.Disposals(0)==0,"each pending cleanup retains its own context and owned handler");
                        a.Cleanup.Complete();await a.MiddleCleanupStarted.Task.ConfigureAwait(false);
                        Check.That(!endA.IsCompleted&&!closeB.IsCompleted&&a.Cleanup.Consumptions==1&&a.MiddleCleanup.Consumptions==0&&a.MiddleCleanupEntry=="A-close"&&a.Disposals(3)==1&&a.Disposals(4)==0&&a.Disposals(5)==0,"after leaf cleanup the middle owner suspends under its own context without releasing outer owners or B");
                        a.MiddleCleanup.Complete();Check.That(!await endA&&!closeB.IsCompleted,"settling A's middle cleanup continues its remaining outer cleanup without releasing B");
                        b.Cleanup.Complete();await closeB;await other.DisposeAsync();
                        Check.That(a.Cleanup.Consumptions==1&&b.Cleanup.Consumptions==1&&a.MiddleCleanup.Consumptions==1&&b.MiddleCleanup.Consumptions==0&&a.Disposals(3)==1&&a.RawMoves==3&&b.RawMoves==2,"each source, leaf cleanup and acquired middle cleanup result is consumed once without another pull");
                    }
                }
                await root.DisposeAsync();Check.That(!await root.MoveNextAsync(),"the public root remains closed");
                Check.That(p.Flows.Values.All(f=>f.CursorDisposals==1&&f.SessionDisposals.Count==f.OpenOrder.Count+1&&f.OpenOrder.Append(0).All(role=>f.Disposals(role)==1)),"every acquired subtree closes exactly once");
                Check.That(p.FactoryDisposals==0,"native factories remain borrowed");await scope.DisposeAsync();Check.That(p.FactoryDisposals==6,"native scope owns all six captured factories");
                foreach(var flow in p.Flows.Values)flow.CloseTokens();p.Cancellation.Dispose();p.ReopenToken.Dispose();Probe.Ambient.Value=null;
            }
            static async Task CompleteOutsideContext(Gate gate,string marker){
                Task completion;using(ExecutionContext.SuppressFlow())completion=Task.Run(()=>{Probe.Ambient.Value=marker;gate.Complete();});await completion.ConfigureAwait(false);
            }
            """);
        await RunNativeStreamFixture("group-barriers-leases-context", source);
    }
}
