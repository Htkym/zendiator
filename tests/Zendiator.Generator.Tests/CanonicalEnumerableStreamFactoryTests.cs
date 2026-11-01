using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    // Unexecuted prototype contract tests; no build or test run has certified this draft.
    private const string EnumerableFactoryDeclarations = """
        public sealed record Items(string Mode="ordinary"):IStreamRequest<int>;
        public sealed class Probe{
            public static readonly AsyncLocal<string?> Ambient=new();
            public int Resolved,Handled,Enumerated,Pulls,Reads,Disposed,HandlerDisposals;
            public readonly Exception Error=new FormatException("source failure");
            public readonly OperationCanceledException Oce=new("producer fault",null,new CancellationToken(false));
            public readonly List<CancellationToken> HandlerTokens=new(),EnumerationTokens=new();
            public readonly global::Zendiator.Generator.Tests.AsyncOnceGate<bool> PendingMove=new(true);
            public string? FirstMoveAmbient;public SynchronizationContext? FirstMoveContext;
            public Cursor? Last;
        }
        [ZendiatorHandler]public sealed class Handler:IStreamHandler<Items,int>,IDisposable{
            private readonly Probe p;public Handler(Probe p){this.p=p;p.Resolved++;}
            public IAsyncEnumerable<int> Handle(Items request,CancellationToken token){
                p.Handled++;p.HandlerTokens.Add(token);Probe.Ambient.Value="Handle";
                SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
                if(request.Mode=="handle-throw")throw p.Error;
                return request.Mode=="null-enumerable"?null!:new Feed(p,request.Mode);
            }
            public void Dispose()=>p.HandlerDisposals++;
        }
        public sealed class Feed(Probe p,string mode):IAsyncEnumerable<int>{
            public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken token=default){
                p.Enumerated++;p.EnumerationTokens.Add(token);
                Check.That(Probe.Ambient.Value=="Handle","acquisition shares its private Handle context");
                Probe.Ambient.Value="GetAsyncEnumerator";
                SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
                if(mode=="enumerator-throw")throw p.Error;
                if(mode=="null-cursor")return null!;
                return p.Last=new Cursor(p,mode);
            }
        }
        public sealed class Cursor(Probe p,string mode):IAsyncEnumerator<int>{
            private int moves;private bool closed;
            public int Current{get{p.Reads++;return 42;}}
            public ValueTask<bool> MoveNextAsync(){
                Check.That(!closed,"the raw cursor is not used after disposal");
                if(p.Pulls++==0){p.FirstMoveAmbient=Probe.Ambient.Value;p.FirstMoveContext=SynchronizationContext.Current;}
                if(mode=="pending")return p.PendingMove.Task;
                if(mode=="move-fault")return new(Task.FromException<bool>(p.Error));
                if(mode=="move-oce")return new(Task.FromException<bool>(p.Oce));
                return new(++moves==1);
            }
            public ValueTask DisposeAsync(){
                Check.That(!closed,"the acquired cursor is disposed once");closed=true;p.Disposed++;return default;
            }
        }
        """;

    [Fact]
    public void Enumerable_factory_excluded_shapes_preserve_generation_only_evidence()
    {
        foreach (var mode in new[] { "before", "map", "wrapper", "session", "native" })
        {
            var configuration = mode switch
            {
                "before" => "c.Handlers<Items>(DispatchFamily.Stream,typeof(Handler));c.Pipeline<Items>(DispatchFamily.Stream);c.StreamBefore<Items>(typeof(Before));",
                "map" => "c.Handlers<Items>(DispatchFamily.Stream,typeof(Handler));c.Pipeline<Items>(DispatchFamily.Stream,Stage.Hook<Map>());",
                "wrapper" => "c.Handlers<Items>(DispatchFamily.Stream,typeof(Handler));c.Pipeline<Items>(DispatchFamily.Stream,Stage.Wrapper<Relay>());",
                "session" => "c.Handlers<Items>(DispatchFamily.Stream,HandlerEntry.Session<SessionHandler,Factory>());c.Pipeline<Items>(DispatchFamily.Stream);",
                "native" => "c.Handlers<Items>(DispatchFamily.Stream,typeof(NativeHandler));c.Pipeline<Items>(DispatchFamily.Stream);",
                _ => throw new InvalidOperationException(mode)
            };
            var source = NativeStreamFixture(configuration, EnumerableFactoryDeclarations + """
                public sealed class Before:IAsyncStreamBefore<Items>{
                    public ValueTask BeforeAsync(Items request,CancellationToken token)=>default;
                }
                public sealed class Map:IStreamItemMap<Items,int>{
                    int IStreamItemMap<Items,int>.Map(Items request,int item,CancellationToken token)=>item;
                }
                [ZendiatorHandler]public sealed class SessionHandler(Probe p):IStreamHandler<Items,int>,IAsyncDisposable{
                    public IAsyncEnumerable<int> Handle(Items request,CancellationToken token)=>new Feed(p,request.Mode);
                    public ValueTask DisposeAsync()=>default;
                }
                public sealed class Factory(Probe p):IOwnedSessionFactory<Items,SessionHandler>{
                    public SessionHandler Create(Items request,CancellationToken token)=>new(p);
                }
                public sealed class Relay:IStreamMiddleware<Items,int>{
                    public ValueTask<IAsyncEnumerator<int>> OpenAsync(Items request,IStreamUpstream<Items,int> next,CancellationToken token)=>next.OpenAsync(request,token);
                }
                [ZendiatorHandler]public sealed class NativeHandler(Probe p):IStreamSourceHandler<Items,int,Cursor>{
                    public ValueTask<Cursor> OpenAsync(Items request,CancellationToken token)=>new(new Cursor(p,request.Mode));
                }
                """, "await Task.CompletedTask;");
            var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(
                SyncCompilation(source, "FactoryExcluded_" + mode), out var output, out var diagnostics);
            Assert.Empty(diagnostics);
            Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is Microsoft.CodeAnalysis.DiagnosticSeverity.Error or Microsoft.CodeAnalysis.DiagnosticSeverity.Warning));
            var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
            if (!string.IsNullOrEmpty(evidence))
            {
                var folder = Path.Combine(evidence, "native-stream-factory-excluded-" + mode);
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "input.cs"), source);
                foreach (var file in driver.GetRunResult().Results.Single().GeneratedSources)
                    File.WriteAllText(Path.Combine(folder, file.HintName), file.SourceText.ToString());
            }
        }
    }

    [Fact]
    public async Task Enumerable_factory_raw_route_acquires_at_Get_and_keeps_source_cleanup_and_failure_semantics()
    {
        var source = NativeStreamFixture("""
            c.Handlers<Items>(DispatchFamily.Stream,typeof(Handler));c.Pipeline<Items>(DispatchFamily.Stream);
            """, EnumerableFactoryDeclarations, """
            var p=new Probe();var services=new ServiceCollection();services.AddSingleton(p);Register(services);
            await using var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});
            var scope=provider.CreateAsyncScope();
            var m=scope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
            var feed=m.StreamAsync(new Items());
            Check.That(p.Resolved==0&&p.Handled==0&&p.Enumerated==0,"StreamAsync remains lazy");

            var cold=Acquire(feed);
            Check.That(ReferenceEquals(cold,p.Last)&&p.Resolved==1&&p.Handled==1&&p.Enumerated==1,
                "Get acquires once and returns the producer's actual cursor");
            Check.That(p.Pulls==0&&p.Reads==0,"acquisition does not pull or read Current");
            await cold.DisposeAsync();
            Check.That(p.Disposed==1&&p.Pulls==0&&p.HandlerDisposals==0,"cold raw Dispose closes only the acquired cursor");

            var first=Acquire(feed);var second=Acquire(feed);
            Check.That(!ReferenceEquals(first,second)&&ReferenceEquals(second,p.Last)&&p.Resolved==1&&p.Handled==3&&p.Enumerated==3,
                "each enumeration calls the cached borrowed handler and gets an independent raw cursor");
            var previousAmbient=Probe.Ambient.Value;var previousContext=SynchronizationContext.Current;
            var moveContext=new SynchronizationContext();ValueTask<bool> firstMove;
            try{
                Probe.Ambient.Value="Move caller";SynchronizationContext.SetSynchronizationContext(moveContext);
                firstMove=first.MoveNextAsync();
                Check.That(p.FirstMoveAmbient=="Move caller"&&ReferenceEquals(p.FirstMoveContext,moveContext),
                    "the first raw pull uses its Move caller rather than the private acquisition context");
            }finally{Probe.Ambient.Value=previousAmbient;SynchronizationContext.SetSynchronizationContext(previousContext);}
            Check.That(await firstMove&&p.Reads==0,"a successful raw Move does not prefetch Current");
            Check.That(first.Current==42&&p.Reads==1,"Current is read only when requested");
            Check.That(!await first.MoveNextAsync()&&p.Disposed==1,"raw EOF does not dispose on behalf of its caller");
            await first.DisposeAsync();
            Check.That(await second.MoveNextAsync()&&second.Current==42,"disposing one enumeration leaves its sibling live");
            await second.DisposeAsync();
            var again=Acquire(feed);Check.That(await again.MoveNextAsync()&&again.Current==42,"the same enumerable can be enumerated again after EOF");
            await again.DisposeAsync();

            var pendingCursor=Acquire(m.StreamAsync(new Items("pending")));var pendingReads=p.Reads;var pendingDisposals=p.Disposed;
            var pendingMove=pendingCursor.MoveNextAsync();
            Check.That(!pendingMove.IsCompleted&&p.PendingMove.Consumptions==0,"the raw source is genuinely pending before completion");
            var pendingTask=pendingMove.AsTask();
            Check.That(!pendingTask.IsCompleted&&p.PendingMove.Consumptions==0,"AsTask registers without consuming the pending result");
            p.PendingMove.Complete();
            Check.That(await pendingTask.ConfigureAwait(false)&&p.PendingMove.Consumptions==1&&p.Reads==pendingReads,
                "a completed raw pending Move is consumed once without prefetching Current");
            await pendingCursor.DisposeAsync();Check.That(p.Disposed==pendingDisposals+1,"the caller explicitly disposes the pending producer");

            foreach(var mode in new[]{"move-fault","move-oce"}){
                var cursor=Acquire(m.StreamAsync(new Items(mode)));var before=p.Disposed;
                var operation=cursor.MoveNextAsync().AsTask();
                Check.That(operation.IsFaulted&&!operation.IsCanceled,"raw producer Task status is preserved, including faulted OCE");
                Exception? caught=null;try{await operation;}catch(Exception error){caught=error;}
                Check.That(ReferenceEquals(caught,mode=="move-oce"?p.Oce:p.Error),"the raw operation preserves its original exception");
                Check.That(p.Disposed==before,"raw failure does not auto-dispose the producer");
                await cursor.DisposeAsync();Check.That(p.Disposed==before+1,"the caller disposes a failed producer once");
            }

            foreach(var mode in new[]{"handle-throw","enumerator-throw","null-enumerable","null-cursor"}){
                var beforeHandled=p.Handled;var beforeEnumerated=p.Enumerated;var beforeDisposed=p.Disposed;
                var failed=m.StreamAsync(new Items(mode));Check.That(p.Handled==beforeHandled,"even failed acquisition is deferred from StreamAsync to Get");
                Exception? caught=null;try{Acquire(failed);}catch(Exception error){caught=error;}
                Check.That(mode.EndsWith("throw",StringComparison.Ordinal)?ReferenceEquals(caught,p.Error):caught is InvalidOperationException,
                    "Get reports the original acquisition error or deliberate null rejection");
                Check.That(p.Handled==beforeHandled+1&&p.Enumerated==beforeEnumerated+(mode is "handle-throw" or "null-enumerable"?0:1)
                    &&p.Disposed==beforeDisposed&&p.HandlerDisposals==0,"failed acquisition does not dispose an unacquired cursor or borrowed handler");
            }

            Check.That(p.Resolved==1&&p.HandlerDisposals==0,"re-enumeration borrows the mediator's cached DI receiver");
            await scope.DisposeAsync();Check.That(p.HandlerDisposals==1,"DI alone disposes the borrowed receiver");
            var handled=p.Handled;var enumerated=p.Enumerated;Exception? scopeError=null;
            try{Acquire(m.StreamAsync(new Items()));}catch(Exception error){scopeError=error;}
            Check.That(scopeError is ObjectDisposedException&&p.Handled==handled&&p.Enumerated==enumerated,
                "the acquisition probe rejects a cache hit after scope disposal before invoking user code");

            static IAsyncEnumerator<int> Acquire(IAsyncEnumerable<int> sequence){
                var previousAmbient=Probe.Ambient.Value;var previousContext=SynchronizationContext.Current;
                var callerContext=new SynchronizationContext();Probe.Ambient.Value="caller";SynchronizationContext.SetSynchronizationContext(callerContext);
                try{
                    try{return sequence.GetAsyncEnumerator();}
                    finally{Check.That(Probe.Ambient.Value=="caller"&&ReferenceEquals(SynchronizationContext.Current,callerContext),
                        "successful or failed acquisition restores the caller's ExecutionContext and SynchronizationContext");}
                }finally{Probe.Ambient.Value=previousAmbient;SynchronizationContext.SetSynchronizationContext(previousContext);}
            }
            """);
        await RunNativeStreamFixture("enumerable-factory-raw", source);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("api")]
    [InlineData("enumeration")]
    [InlineData("same")]
    [InlineData("distinct")]
    public async Task Enumerable_factory_uses_raw_only_without_cancelable_tokens_and_keeps_owner_cancellation(string mode)
    {
        var source = NativeStreamFixture("""
            c.Handlers<Items>(DispatchFamily.Stream,typeof(Handler));c.Pipeline<Items>(DispatchFamily.Stream);
            """, EnumerableFactoryDeclarations, """
            var p=new Probe();var services=new ServiceCollection();services.AddSingleton(p);Register(services);
            await using var provider=services.BuildServiceProvider();var scope=provider.CreateAsyncScope();
            var m=scope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
            var mode="__MODE__";
            foreach(var cancelApi in mode=="distinct"?new[]{true,false}:new[]{mode is "api" or "same"}){
                using var api=new CancellationTokenSource();using var enumeration=new CancellationTokenSource();
                var apiToken=mode is "api" or "same" or "distinct"?api.Token:default;
                var enumerationToken=mode=="same"?api.Token:mode is "enumeration" or "distinct"?enumeration.Token:default;
                var handled=p.Handled;var enumerated=p.Enumerated;var disposed=p.Disposed;
                var feed=m.StreamAsync(new Items(),apiToken);
                Check.That(p.Handled==handled&&p.Enumerated==enumerated,"creating either route does not acquire the producer");
                var cursor=feed.GetAsyncEnumerator(enumerationToken);
                if(mode=="none"){
                    Check.That(ReferenceEquals(cursor,p.Last)&&p.Handled==handled+1&&p.Enumerated==enumerated+1,
                        "only two noncancelable tokens select the raw acquisition-at-Get route");
                }else Check.That(p.Handled==handled&&p.Enumerated==enumerated,"any cancelable token retains first-Move acquisition");
                Check.That(await cursor.MoveNextAsync(),"the first item is available");
                Check.That(p.HandlerTokens.Last()==p.EnumerationTokens.Last(),"Handle and Get use the owner's same effective token");
                if(mode=="none"){
                    Check.That(ReferenceEquals(cursor,p.Last)&&!p.HandlerTokens.Last().CanBeCanceled,"the noncancelable route stays raw");
                    await cursor.DisposeAsync();continue;
                }
                Check.That(!ReferenceEquals(cursor,p.Last)&&p.HandlerTokens.Last().CanBeCanceled,"cancelable routes keep the canonical owner");
                var pulls=p.Pulls;if(cancelApi)api.Cancel();else enumeration.Cancel();
                var operation=cursor.MoveNextAsync().AsTask();Exception? caught=null;
                try{await operation;}catch(Exception error){caught=error;}
                Check.That(caught is OperationCanceledException&&operation.IsCanceled&&p.Pulls==pulls,
                    "the owner observes API or enumeration cancellation even though the raw producer ignores every token");
                Check.That(p.Disposed==disposed+1,"canceled Move finishes source cleanup before publishing its failure");
                await cursor.DisposeAsync();Check.That(p.Disposed==disposed+1,"subsequent Dispose does not repeat canceled cleanup");
            }
            Check.That(p.Resolved==1&&p.HandlerDisposals==0,"both routes borrow the same cached handler");
            await scope.DisposeAsync();Check.That(p.HandlerDisposals==1,"only DI owns receiver disposal");
            """.Replace("__MODE__", mode));
        await RunNativeStreamFixture("enumerable-factory-token-" + mode, source);
    }
}
