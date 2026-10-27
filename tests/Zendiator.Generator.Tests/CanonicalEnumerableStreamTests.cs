using Microsoft.CodeAnalysis;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    [Fact]
    public async Task Enumerable_stream_source_is_lazy_independent_and_keeps_Current_tokens_and_DI_ownership()
    {
        var source = NativeStreamFixture("""
            c.Handlers<Items>(DispatchFamily.Stream,typeof(Handler));c.Pipeline<Items>(DispatchFamily.Stream,Stage.Wrapper<Relay>());
            """, """
            public sealed record Items(int Count):IStreamRequest<int>;
            public sealed class Probe{
                public int Resolved,Handled,Enumerated,Reads,Disposed,HandlerDisposals,Wrapped;
                public readonly List<CancellationToken> HandlerTokens=new();public readonly List<int> Closed=new();
            }
            [ZendiatorHandler]public sealed class Handler: IStreamHandler<Items,int>,IDisposable{
                private readonly Probe p;public Handler(Probe p){this.p=p;p.Resolved++;}
                public IAsyncEnumerable<int> Handle(Items request,CancellationToken token){p.Handled++;p.HandlerTokens.Add(token);return new Feed(p,request.Count,token);}
                public void Dispose()=>p.HandlerDisposals++;
            }
            public sealed class Relay(Probe p):IStreamMiddleware<Items,int>{
                public ValueTask<IAsyncEnumerator<int>> OpenAsync(Items request,IStreamUpstream<Items,int> next,CancellationToken token){p.Wrapped++;return next.OpenAsync(request,token);}
            }
            public sealed class Feed(Probe p,int count,CancellationToken handlerToken):IAsyncEnumerable<int>{
                public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken token=default){
                    Check.That(token==handlerToken,"Handle and GetAsyncEnumerator receive the same effective token");return new Cursor(p,++p.Enumerated,count,token);
                }
            }
            public sealed class Cursor(Probe p,int id,int count,CancellationToken token):IAsyncEnumerator<int>{
                private int index=-1;private bool closed;public int Current{get{p.Reads++;return index;}}
                public ValueTask<bool> MoveNextAsync(){Check.That(!closed,"a different enumeration cannot close this cursor");token.ThrowIfCancellationRequested();return new(++index<count);}
                public ValueTask DisposeAsync(){Check.That(!closed,"each acquired cursor is disposed once");closed=true;p.Disposed++;p.Closed.Add(id);return default;}
            }
            """, """
            var p=new Probe();var services=new ServiceCollection();services.AddSingleton(p);Register(services);
            await using var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});
            var scope=provider.CreateAsyncScope();var m=scope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
            using var api=new CancellationTokenSource();using var a=new CancellationTokenSource();using var b=new CancellationTokenSource();
            IAsyncEnumerable<int> feed=m.StreamAsync(new Items(3),api.Token);
            var unused=feed.GetAsyncEnumerator(a.Token);await unused.DisposeAsync();
            Check.That(p.Resolved==0&&p.Handled==0&&p.Enumerated==0,"source creation, Get and cold Dispose do not resolve, Handle or acquire");
            var first=feed.GetAsyncEnumerator(a.Token);var second=feed.GetAsyncEnumerator(b.Token);
            Check.Invalid(()=>{_=first.Current;});Check.That(p.Reads==0&&p.Handled==0,"invalid Current does not read the input or start acquisition");
            Check.That(await first.MoveNextAsync()&&p.Handled==1&&p.Enumerated==1&&p.Wrapped==1&&p.Reads==0,"first Move acquires once through middleware without reading ahead into Current");
            Check.That(first.Current==0&&await second.MoveNextAsync()&&second.Current==0,"one reusable source creates independent enumerators");
            a.Cancel();await Canceled(first);
            Check.That(await second.MoveNextAsync()&&second.Current==1&&p.Disposed==1,"canceling one enumeration leaves the other live");
            api.Cancel();await Canceled(second);await first.DisposeAsync();await second.DisposeAsync();
            Check.That(p.Disposed==2&&p.HandlerTokens.All(t=>t.CanBeCanceled),"effective API and enumeration cancellation close only acquired cursors");
            IAsyncEnumerable<int> reusable=m.StreamAsync(new Items(2));
            var values=new List<int>();await foreach(var item in reusable.ConfigureAwait(false))values.Add(item);
            Check.That(values.SequenceEqual(new[]{0,1})&&p.Disposed==3,"EOF releases its cursor");
            var early=reusable.GetAsyncEnumerator();Check.That(await early.MoveNextAsync(),"early item");var reads=p.Reads;
            await early.DisposeAsync();await early.DisposeAsync();
            Check.That(p.Reads==reads&&p.Disposed==4,"unread Current remains unread during early cleanup");
            values.Clear();await foreach(var item in reusable.ConfigureAwait(false))values.Add(item);
            Check.That(values.SequenceEqual(new[]{0,1})&&p.Handled==5&&p.Enumerated==5&&p.Wrapped==5&&p.Disposed==5&&p.Closed.Distinct().Count()==5,"enumeration after EOF starts and closes a new input cursor through middleware");
            Check.That(p.Resolved==1&&p.HandlerDisposals==0,"operations borrow the one DI handler");
            await scope.DisposeAsync();Check.That(p.HandlerDisposals==1,"DI alone disposes the handler");
            static async ValueTask Canceled(IAsyncEnumerator<int> cursor){
                var canceled=false;try{await cursor.MoveNextAsync();}catch(OperationCanceledException){canceled=true;}
                Check.That(canceled,"effective token cancellation must be observed");Check.Invalid(()=>{_=cursor.Current;});
            }
            """);
        await RunNativeStreamFixture("enumerable-lifecycle", source);
    }

    [Theory]
    [InlineData("null-enumerable")]
    [InlineData("null-cursor")]
    [InlineData("handle-throw")]
    [InlineData("enumerator-throw")]
    [InlineData("enumerator-throw-cleanup")]
    public async Task Enumerable_stream_failed_acquisition_keeps_exception_priority_and_releases_only_acquired_ownership(string mode)
    {
        var source = NativeStreamFixture("""
            c.Handlers<Items>(DispatchFamily.Stream,HandlerEntry.Session<Handler,Factory>());c.Pipeline<Items>(DispatchFamily.Stream);
            """, """
            public sealed record Items:IStreamRequest<int>;
            public sealed class Probe(string mode){
                public readonly string Mode=mode;public readonly Exception Error=new InvalidOperationException("source");public readonly Exception CleanupError=new InvalidOperationException("cleanup");
                public int Handled,Enumerated,Sessions,SessionDisposals,FactoryDisposals;
            }
            [ZendiatorHandler]public sealed class Handler(Probe p):IStreamHandler<Items,int>,IAsyncDisposable{
                public IAsyncEnumerable<int> Handle(Items request,CancellationToken token){p.Handled++;if(p.Mode=="handle-throw")throw p.Error;return p.Mode=="null-enumerable"?null!:new Feed(p);}
                public ValueTask DisposeAsync(){p.SessionDisposals++;return p.Mode.EndsWith("-cleanup",StringComparison.Ordinal)?ValueTask.FromException(p.CleanupError):default;}
            }
            public sealed class Factory(Probe p):IOwnedSessionFactory<Items,Handler>,IDisposable{
                public Handler Create(Items request,CancellationToken token){p.Sessions++;return new(p);}public void Dispose()=>p.FactoryDisposals++;
            }
            public sealed class Feed(Probe p):IAsyncEnumerable<int>{
                public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken token=default){p.Enumerated++;if(p.Mode=="null-cursor")return null!;throw p.Error;}
            }
            """, """
            var p=new Probe("__MODE__");var services=new ServiceCollection();services.AddSingleton(p);Register(services);
            await using var provider=services.BuildServiceProvider();var scope=provider.CreateAsyncScope();
            var m=scope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
            var cursor=m.StreamAsync(new Items()).GetAsyncEnumerator();Check.That(p.Sessions==0,"failed sources stay lazy before Move");
            Exception? error=null;try{await cursor.MoveNextAsync();}catch(Exception caught){error=caught;}
            if(p.Mode.EndsWith("-cleanup",StringComparison.Ordinal))Check.That(ReferenceEquals(error,p.CleanupError),"owned cleanup failure takes priority over GetAsyncEnumerator failure");
            else if(p.Mode.Contains("throw",StringComparison.Ordinal))Check.That(ReferenceEquals(error,p.Error),"Handle or GetAsyncEnumerator preserves the original failure");
            else Check.That(error is InvalidOperationException,"null enumerable or cursor is rejected deliberately");
            Check.That(p.Sessions==1&&p.Handled==1&&p.Enumerated==(p.Mode is "null-enumerable" or "handle-throw"?0:1)&&p.SessionDisposals==1,"partial acquisition closes only its owned handler session once");
            try{await cursor.DisposeAsync();}catch(Exception caught){Check.That(ReferenceEquals(caught,p.CleanupError),"only the recorded cleanup error may recur");}
            Check.That(!await cursor.MoveNextAsync()&&p.SessionDisposals==1&&p.FactoryDisposals==0,"closed sources cannot acquire again and factories stay borrowed");
            await scope.DisposeAsync();Check.That(p.FactoryDisposals==1,"DI retains factory disposal ownership");
            """.Replace("__MODE__", mode));
        await RunNativeStreamFixture("enumerable-acquisition-" + mode, source);
    }

    [Fact]
    public async Task Enumerable_stream_open_generic_explicit_value_handler_keeps_its_box_and_compiler_iterator_lifetime()
    {
        var source = NativeStreamFixture("""
            c.Handlers(typeof(Items<>),DispatchFamily.Stream,typeof(Handler<>));c.Pipeline(typeof(Items<>),DispatchFamily.Stream,Stage.Hook(typeof(Pass<>)));
            """, """
            public sealed class Tag;
            public readonly record struct Items<T>(int Value):IStreamRequest<int> where T:class,new();
            public sealed class Probe{
                public int Constructed,Finals,Maps;public readonly List<int> Calls=new(),DisposedCalls=new();
                public readonly List<global::Zendiator.Generator.Tests.AsyncOnceGate<bool>> Gates=new();
            }
            [ZendiatorHandler]internal struct Handler<T>:IStreamHandler<Items<T>,int>,IDisposable where T:class,new(){
                private readonly Probe p;private int calls;public Handler(Probe p){this.p=p;calls=0;p.Constructed++;}
                IAsyncEnumerable<int> IStreamHandler<Items<T>,int>.Handle(Items<T> request,CancellationToken token){
                    p.Calls.Add(++calls);var gate=new global::Zendiator.Generator.Tests.AsyncOnceGate<bool>(true);p.Gates.Add(gate);return Produce(p,request.Value,token,gate);
                }
                private static async IAsyncEnumerable<int> Produce(Probe p,int value,[System.Runtime.CompilerServices.EnumeratorCancellation]CancellationToken token,global::Zendiator.Generator.Tests.AsyncOnceGate<bool> gate){
                    try{token.ThrowIfCancellationRequested();yield return value;await gate.Task.ConfigureAwait(false);token.ThrowIfCancellationRequested();yield return value+1;}
                    finally{p.Finals++;}
                }
                public void Dispose()=>p.DisposedCalls.Add(calls);
            }
            public sealed class Pass<T>(Probe p):IStreamItemMap<Items<T>,int> where T:class,new(){
                public int Map(Items<T> request,int item,CancellationToken token){p.Maps++;return item;}
            }
            public readonly struct StopSink(List<int> values):IStreamSink<int>{public ValueTask<bool> OnNextAsync(int item,CancellationToken token){values.Add(item);return new(false);}}
            """, """
            var p=new Probe();var services=new ServiceCollection();services.AddSingleton(p);Register(services);
            await using var provider=services.BuildServiceProvider();var scope=provider.CreateAsyncScope();
            var m=scope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
            var request=new Items<Tag>(7);IAsyncEnumerable<int> feed=m.StreamAsync(request);request=new(99);
            Check.That(p.Constructed==0&&p.Calls.Count==0,"open generic compiler iterator remains lazy");
            var first=feed.GetAsyncEnumerator();Check.That(await first.MoveNextAsync()&&first.Current==7,"value request captured before later caller assignment");
            var pending=first.MoveNextAsync().AsTask();Check.That(!pending.IsCompleted&&p.Gates[0].Consumptions==0,"the compiler iterator is truly pending on its gate");
            p.Gates[0].Complete();Check.That(await pending&&first.Current==8&&p.Gates[0].Consumptions==1,"pending source operation consumed once");
            Check.That(!await first.MoveNextAsync()&&p.Finals==1,"compiler iterator finally runs at EOF");await first.DisposeAsync();
            var second=feed.GetAsyncEnumerator();Check.That(await second.MoveNextAsync()&&second.Current==7,"same enumerable opens an independent compiler iterator after EOF");
            await second.DisposeAsync();Check.That(p.Finals==2&&p.Gates[1].Consumptions==0,"early disposal closes without entering a later wait");
            var values=new List<int>();await m.StreamSource(new Items<Tag>(20)).ConsumeAsync(new StopSink(values));
            Check.That(values.SequenceEqual(new[]{20})&&p.Finals==3&&p.Maps==4&&p.Calls.SequenceEqual(new[]{1,2,3})&&p.Constructed==1,"typed terminal and public pull use the original captured handler box and the same generic hook");
            Check.That(p.DisposedCalls.Count==0,"operation never disposes the borrowed value handler");
            await scope.DisposeAsync();Check.That(p.DisposedCalls.SequenceEqual(new[]{3}),"DI disposes the original mutable handler box");
            """);
        await RunNativeStreamFixture("enumerable-open-generic", source);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Enumerable_stream_dual_contract_and_missing_marker_are_diagnosed_before_route_emission(bool dual)
    {
        var declarations = """
            public sealed record Items:IStreamRequest<int>;
            __MARKER__public sealed class Handler:IStreamHandler<Items,int>__TYPED__{
                public IAsyncEnumerable<int> Handle(Items request,CancellationToken token)=>throw new NotImplementedException();
                public ValueTask<Cursor> OpenAsync(Items request,CancellationToken token)=>new(new Cursor());
            }
            public sealed class Cursor:IAsyncEnumerator<int>{public int Current=>0;public ValueTask<bool> MoveNextAsync()=>new(false);public ValueTask DisposeAsync()=>default;}
            """.Replace("__MARKER__", dual ? "[ZendiatorHandler]" : "")
            .Replace("__TYPED__", dual ? ",IStreamSourceHandler<Items,int,Cursor>" : "");
        var source = NativeStreamFixture("c.Handlers<Items>(DispatchFamily.Stream,typeof(Handler));c.Pipeline<Items>(DispatchFamily.Stream);", declarations, "await Task.CompletedTask;");
        var compilation = SyncCompilation(source, "EnumerableStreamRejected_" + dual);
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        var driver = SyncDriver().RunGenerators(compilation);
        var result = driver.GetRunResult();
        Assert.Contains(result.Diagnostics, d => d.Id == "ZEN0035" && d.Location.IsInSource &&
            (dual || d.Properties.TryGetValue("Zendiator.Reason", out var reason) && reason == "MissingHandlerAttribute"));
        AssertNoCanonicalOutput(result.Results.Single().GeneratedSources);
    }
}
