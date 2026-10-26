using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private static string NativeStreamFixture(string configuration, string declarations, string run) => """
        using System;using System.Collections.Generic;using System.Linq;using System.Threading;using System.Threading.Tasks;
        using Zendiator;using Zendiator.DependencyInjection;using Microsoft.Extensions.DependencyInjection;
        namespace NativeStream{
        [ZendiatorComposition("native-stream")] public sealed class Reads;
        public static class Check{
            public static void That(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
            public static void Invalid(Action action){var caught=false;try{action();}catch(InvalidOperationException){caught=true;}That(caught,"Expected invalid cursor state");}
        }
        public static class Bridge{
            public static IServiceCollection Register(IServiceCollection services)=>services.AddZendiator<Reads>(c=>{__CONFIG__});
            public static async Task Run(){__RUN__}
        }
        __DECLARATIONS__
        }
        """.Replace("__CONFIG__", configuration).Replace("__DECLARATIONS__", declarations).Replace("__RUN__", run);

    private static async Task RunNativeStreamFixture(string name, string source, string entryType = "NativeStream.Bridge",
        Func<string, string>? generatedTransform = null)
    {
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(
            SyncCompilation(source, "NativeStream_" + name + "_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        var transformedSources = new Dictionary<string, string>();
        if (generatedTransform is not null)
        {
            foreach (var file in driver.GetRunResult().Results.Single().GeneratedSources)
            {
                var transformed = generatedTransform(file.SourceText.ToString());
                transformedSources.Add(file.HintName, transformed);
                if (transformed != file.SourceText.ToString())
                {
                    var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(transformed,
                        (Microsoft.CodeAnalysis.CSharp.CSharpParseOptions)file.SyntaxTree.Options, file.SyntaxTree.FilePath,
                        encoding: System.Text.Encoding.UTF8);
                    output = output.ReplaceSyntaxTree(file.SyntaxTree, tree);
                }
            }
            Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        }
        using var bytes = new MemoryStream();
        var emitted = output.Emit(bytes);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(evidence))
        {
            var folder = Path.Combine(evidence, "native-stream-" + name); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "input.cs"), source);
            foreach (var file in driver.GetRunResult().Results.Single().GeneratedSources)
                File.WriteAllText(Path.Combine(folder, file.HintName), file.SourceText.ToString());
            if (generatedTransform is not null)
            {
                var transformedFolder = Path.Combine(folder, "execution-transformed"); Directory.CreateDirectory(transformedFolder);
                foreach (var file in transformedSources)
                    File.WriteAllText(Path.Combine(transformedFolder, file.Key), file.Value);
            }
            File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
        }
        bytes.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var run = (Task)assembly.GetType(entryType)!.GetMethod("Run")!.Invoke(null, null)!;
        await run.WaitAsync(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task Native_stream_public_source_keeps_lazy_independent_value_cursors_and_one_consumption_pipeline()
    {
        var source = NativeStreamFixture("""
            c.Handlers<PlainItems>(DispatchFamily.Stream,typeof(Handler));c.Pipeline<PlainItems>(DispatchFamily.Stream);
            c.Handlers<EmptyItems>(DispatchFamily.Stream,typeof(EmptyHandler));c.Pipeline<EmptyItems>(DispatchFamily.Stream);
            c.Handlers<Items>(DispatchFamily.Stream,typeof(Handler));
            c.Pipeline<Items>(DispatchFamily.Stream,Stage.Hook<Positive>(),Stage.Hook<DoubleItem>());
            """, """
            public readonly record struct PlainItems(int Count):IStreamRequest<int>;
            public readonly record struct EmptyItems:IStreamRequest<int>;
            public sealed record Items(int Count):IStreamRequest<int>;
            public sealed class Probe{public int Resolved,ServiceResolutions,Opened,EmptyOpened,Produced,Reads,CursorDisposals,ServiceDisposals,Filters,Maps,DisposedMapCalls;public readonly List<int> MapVersions=new();}
            [ZendiatorHandler] public sealed class Handler: IStreamSourceHandler<PlainItems,int,Cursor>,IStreamSourceHandler<Items,int,Cursor>,IDisposable{
                private readonly Probe p;public Handler(Probe p){this.p=p;p.Resolved++;p.ServiceResolutions++;}
                public ValueTask<Cursor> OpenAsync(PlainItems r,CancellationToken t){p.Opened++;return new(new Cursor(p,r.Count,t));}
                public ValueTask<Cursor> OpenAsync(Items r,CancellationToken t){p.Opened++;return new(new Cursor(p,r.Count,t));}
                public void Dispose()=>p.ServiceDisposals++;
            }
            [ZendiatorHandler]public sealed class EmptyHandler(Probe p):IStreamSourceHandler<EmptyItems,int,EmptyCursor>{
                public ValueTask<EmptyCursor> OpenAsync(EmptyItems r,CancellationToken t){p.EmptyOpened++;return new(default(EmptyCursor));}
            }
            public readonly struct EmptyCursor:IAsyncEnumerator<int>{
                public static int Disposals;
                public int Current=>throw new InvalidOperationException("Empty cursor has no Current");
                public ValueTask<bool> MoveNextAsync()=>new(false);
                public ValueTask DisposeAsync(){Disposals++;return default;}
            }
            public struct Cursor:IAsyncEnumerator<int>{
                private readonly Probe p;private readonly int count;private readonly CancellationToken token;private int next,current;
                public Cursor(Probe p,int count,CancellationToken token){this.p=p;this.count=count;this.token=token;next=0;current=-1;}
                public int Current=>throw new InvalidOperationException("Wrong public Current sentinel");
                int IAsyncEnumerator<int>.Current{get{p.Reads++;return current;}}
                public ValueTask<bool> MoveNextAsync()=>throw new InvalidOperationException("Wrong public Move sentinel");
                ValueTask<bool> IAsyncEnumerator<int>.MoveNextAsync(){token.ThrowIfCancellationRequested();if(next==count)return new(false);current=next++;p.Produced++;return new(true);}
                public ValueTask DisposeAsync()=>throw new InvalidOperationException("Wrong public Dispose sentinel");
                ValueTask IAsyncDisposable.DisposeAsync(){p.CursorDisposals++;return default;}
            }
            public sealed class Positive:IStreamItemFilter<Items,int>,IDisposable{
                private readonly Probe p;public Positive(Probe p){this.p=p;p.ServiceResolutions++;}
                public bool Accept(Items r,int item,CancellationToken t){p.Filters++;return item>0;}
                public void Dispose()=>p.ServiceDisposals++;
            }
            public struct DoubleItem:IStreamItemMap<Items,int>,IDisposable{
                private readonly Probe p;private int calls;public DoubleItem(Probe p){this.p=p;calls=0;p.ServiceResolutions++;}
                public int Map(Items r,int item,CancellationToken t){p.Maps++;p.MapVersions.Add(++calls);return item*2;}
                public void Dispose(){p.ServiceDisposals++;p.DisposedMapCalls=calls;}
            }
            public sealed class State{public readonly List<int> Values=new();public int Limit=int.MaxValue;public bool SinkDisposed;}
            public readonly struct Sink(State state):IStreamSink<int>,IDisposable{
                public ValueTask<bool> OnNextAsync(int item,CancellationToken t){t.ThrowIfCancellationRequested();state.Values.Add(item);return new(state.Values.Count<state.Limit);}
                public void Dispose()=>state.SinkDisposed=true;
            }
            """, """
            var p=new Probe();var services=new ServiceCollection();services.AddSingleton(p);Register(services);
            await using var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});
            var scope=provider.CreateAsyncScope();var mediator=scope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
            var feed=mediator.StreamAsync(new PlainItems(3));IAsyncEnumerable<int> http=feed;
            Check.That(p.Resolved==0&&p.Opened==0,"HTTP source must be lazy");
            var unused=http.GetAsyncEnumerator();await unused.DisposeAsync();
            Check.That(!await unused.MoveNextAsync()&&p.Resolved==0&&p.Opened==0,"pre-start disposal must not resolve or open");
            Check.Invalid(()=>{_=unused.Current;});
            var first=http.GetAsyncEnumerator();var second=http.GetAsyncEnumerator();
            Check.That(!ReferenceEquals(first,second),"each enumeration owns its state");Check.Invalid(()=>{_=first.Current;});
            Check.That(await first.MoveNextAsync()&&p.Reads==0,"Current getter stays lazy on plain route");
            Check.That(first.Current==0&&await second.MoveNextAsync()&&second.Current==0,"first item of independent value cursors");
            Check.That(await first.MoveNextAsync()&&first.Current==1&&second.Current==0,"value cursor field mutation must survive each move");
            Check.That(await first.MoveNextAsync()&&first.Current==2&&!await first.MoveNextAsync(),"normal EOF");
            Check.Invalid(()=>{_=first.Current;});await first.DisposeAsync();await first.DisposeAsync();await second.DisposeAsync();
            Check.That(p.CursorDisposals==2,"cursor cleanup exactly once per started enumeration");
            var plan=mediator.StreamSource(new PlainItems(3));var all=new State();await plan.ConsumeAsync(new Sink(all));
            Check.That(all.Values.SequenceEqual(new[]{0,1,2}),"terminal sink uses same value cursor contract");
            var filtered=new List<int>();await foreach(var item in mediator.StreamAsync(new Items(4)).ConfigureAwait(false))filtered.Add(item);
            Check.That(filtered.SequenceEqual(new[]{2,4,6}),"ordered reject and map");
            var before=p.Produced;var filters=p.Filters;var maps=p.Maps;var stopped=new State{Limit=1};
            await mediator.StreamSource(new Items(4)).ConsumeAsync(new Sink(stopped));
            Check.That(stopped.Values.SequenceEqual(new[]{2})&&p.Produced-before==2&&p.Filters-filters==2&&p.Maps-maps==1,"reject continues, sink false stops without reading ahead");
            Check.That(p.MapVersions.SequenceEqual(new[]{1,2,3,4}),"mutable value hook keeps its captured box across callbacks and operations");
            var empty=mediator.StreamAsync(new EmptyItems()).GetAsyncEnumerator();
            Check.That(p.EmptyOpened==0&&EmptyCursor.Disposals==0,"default-valued cursor remains unopened before Move");
            Check.That(!await empty.MoveNextAsync()&&p.EmptyOpened==1&&EmptyCursor.Disposals==1,"default is a valid acquired empty struct cursor and EOF disposes it");
            Check.Invalid(()=>{_=empty.Current;});await empty.DisposeAsync();await empty.DisposeAsync();
            Check.That(EmptyCursor.Disposals==1,"EOF and repeated Dispose do not dispose the default cursor twice");
            var emptySink=new State();await mediator.StreamSource(new EmptyItems()).ConsumeAsync(new Sink(emptySink));
            Check.That(p.EmptyOpened==2&&EmptyCursor.Disposals==2&&emptySink.Values.Count==0&&!emptySink.SinkDisposed,"terminal EOF and finally dispose one default cursor without publishing an item");
            Check.That(p.ServiceDisposals==0&&!all.SinkDisposed&&!stopped.SinkDisposed,"DI receivers and caller sink stay borrowed");
            await scope.DisposeAsync();Check.That(p.ServiceDisposals==p.ServiceResolutions&&p.DisposedMapCalls==4,"DI owner releases the same captured hook box");
            """);
        await RunNativeStreamFixture("value-lifecycle", source);
    }

    [Fact]
    public async Task Native_stream_public_owner_drains_open_move_and_cleanup_once_and_rejects_overlap()
    {
        var source = NativeStreamFixture("""
            c.Handlers<Items>(DispatchFamily.Stream,typeof(Handler));c.Pipeline<Items>(DispatchFamily.Stream);
            """, """
            public sealed record Items:IStreamRequest<int>;
            public sealed class Probe{
                public static readonly AsyncLocal<string?> Ambient=new();
                public int Opened,Moves,Reads,Disposed;public string? Seen,MovedAmbient;
                public global::Zendiator.Generator.Tests.AsyncOnceGate<Cursor>? OpenGate;
                public readonly global::Zendiator.Generator.Tests.AsyncOnceGate<bool> Middle=new(true);
                public readonly global::Zendiator.Generator.Tests.AsyncOnceGate<int> Cleanup=new(0);
            }
            [ZendiatorHandler]public sealed class Handler(Probe p):IStreamSourceHandler<Items,int,Cursor>{
                public ValueTask<Cursor> OpenAsync(Items r,CancellationToken t){p.Opened++;p.Seen=Probe.Ambient.Value;p.OpenGate=new(new Cursor(p,t));return p.OpenGate.Task;}
            }
            public sealed class Cursor(Probe p,CancellationToken token):IAsyncEnumerator<int>{
                private int move;public int Current{get{p.Reads++;return move;}}
                public ValueTask<bool> MoveNextAsync(){token.ThrowIfCancellationRequested();p.Moves++;p.MovedAmbient=Probe.Ambient.Value;move++;return move==2?p.Middle.Task:new(move<3);}
                public ValueTask DisposeAsync(){p.Disposed++;return p.Cleanup.Void;}
            }
            """, """
            var p=new Probe();var services=new ServiceCollection();services.AddSingleton(p);Register(services);
            await using var provider=services.BuildServiceProvider();await using var scope=provider.CreateAsyncScope();
            var mediator=scope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
            using var api=new CancellationTokenSource();using var enumeration=new CancellationTokenSource();
            var e=mediator.StreamAsync(new Items(),api.Token).GetAsyncEnumerator(enumeration.Token);
            Check.That(p.Opened==0,"GetAsyncEnumerator must not open I/O");Probe.Ambient.Value="request";
            var open=e.MoveNextAsync().AsTask();Check.That(!open.IsCompleted&&p.Opened==1&&p.Moves==0,"initial Open is truly pending");
            Check.Invalid(()=>{_=e.Current;});
            var overlap=false;try{await e.MoveNextAsync();}catch(InvalidOperationException){overlap=true;}Check.That(overlap,"overlapping Move must fail");
            overlap=false;try{await e.DisposeAsync();}catch(InvalidOperationException){overlap=true;}Check.That(overlap&&p.Disposed==0,"overlapping Dispose does not clean resources");
            p.OpenGate!.Complete();Check.That(await open&&p.OpenGate.Consumptions==1&&p.Seen=="request"&&p.MovedAmbient=="request"&&e.Current==1,"open completes once and ExecutionContext flows across pending Open");
            var middle=e.MoveNextAsync().AsTask();Check.That(!middle.IsCompleted&&p.Middle.Consumptions==0,"middle Move is truly pending");
            Check.Invalid(()=>{_=e.Current;});p.Middle.Complete();Check.That(await middle&&e.Current==2&&p.Middle.Consumptions==1,"middle completion once");
            var first=e.DisposeAsync().AsTask();var repeated=e.DisposeAsync().AsTask();
            Check.That(!first.IsCompleted&&!repeated.IsCompleted&&p.Disposed==1&&p.Cleanup.Consumptions==0,"pending disposal is shared without consuming raw source twice");
            Check.Invalid(()=>{_=e.Current;});p.Cleanup.Complete();await Task.WhenAll(first,repeated);
            Check.That(p.Cleanup.Consumptions==1&&!await e.MoveNextAsync(),"one cleanup and terminal closed state");
            Probe.Ambient.Value=null;
            """);
        await RunNativeStreamFixture("pending-owner", source);
    }
}
