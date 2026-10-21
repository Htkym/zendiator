using Microsoft.CodeAnalysis;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    [Fact]
    public async Task Native_stream_open_value_request_type_changing_stages_hide_internal_cursor_and_keep_Send_routes()
    {
        var source = NativeStreamFixture("""
            c.Handlers(typeof(Items<>),DispatchFamily.Stream,typeof(Handler<>));
            c.StreamPipeline(typeof(Items<>),typeof(long),Stage.Hook(typeof(Text<>)),Stage.Wrapper(typeof(TextBoundary<>)),Stage.Hook(typeof(Number<>)));
            c.Handlers<Query>(DispatchFamily.SendSync,typeof(QueryHandler));c.Pipeline<Query>(DispatchFamily.SendSync);
            """, """
            public sealed class Tag{public int Version;}
            public struct Items<T>:IStreamRequest<int> where T:class,new(){public int Value;public T Shared;public Items(int value,T shared){Value=value;Shared=shared;}}
            public sealed record Query(int Value):ISyncRequest<int>;
            public sealed class Probe{public int Opened,Disposed,Sends,Boundaries;public readonly List<int> Seen=new();}
            [ZendiatorHandler]internal sealed class Handler<T>(Probe p):IStreamSourceHandler<Items<T>,int,ValueCursor<T>> where T:class,new(){
                public ValueTask<ValueCursor<T>> OpenAsync(Items<T> request,CancellationToken t){p.Opened++;p.Seen.Add(request.Value);var start=request.Value;request.Value=900;return new(new ValueCursor<T>(p,start,t));}
            }
            internal struct ValueCursor<T>:IAsyncEnumerator<int> where T:class,new(){
                private readonly Probe p;private readonly int start;private readonly CancellationToken token;private int index;
                public ValueCursor(Probe p,int start,CancellationToken token){this.p=p;this.start=start;this.token=token;index=-1;}
                public int Current=>start+index;
                public ValueTask<bool> MoveNextAsync(){token.ThrowIfCancellationRequested();return new(++index<2);}
                public ValueTask DisposeAsync(){p.Disposed++;return default;}
            }
            public sealed class Text<T>(Probe p):IStreamItemMap<Items<T>,int,string?> where T:class,new(){
                public string? Map(Items<T> request,int item,CancellationToken t){p.Seen.Add(request.Value);request.Value=901;return item.ToString(System.Globalization.CultureInfo.InvariantCulture);}
            }
            public sealed class Number<T>(Probe p):IAsyncStreamItemMap<Items<T>,string?,long> where T:class,new(){
                public ValueTask<long> MapAsync(Items<T> request,string? item,CancellationToken t){p.Seen.Add(request.Value);return new(long.Parse(item!,System.Globalization.CultureInfo.InvariantCulture)+100);}
            }
            public sealed class TextBoundary<T>(Probe p):IStreamMiddleware<Items<T>,string?> where T:class,new(){
                public ValueTask<IAsyncEnumerator<string?>> OpenAsync(Items<T> request,IStreamUpstream<Items<T>,string?> next,CancellationToken t){p.Boundaries++;p.Seen.Add(request.Value);return next.OpenAsync(request,t);}
            }
            [ZendiatorHandler]public sealed class QueryHandler(Probe p):ISyncRequestHandler<Query,int>{public int Handle(Query r,CancellationToken t){p.Sends++;return r.Value+1;}}
            public readonly struct Sink(List<long> values):IStreamSink<long>{public ValueTask<bool> OnNextAsync(long item,CancellationToken t){values.Add(item);return new(true);}}
            """, """
            var p=new Probe();var services=new ServiceCollection();services.AddSingleton(p);Register(services);
            await using var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});
            await using var scope=provider.CreateAsyncScope();var m=scope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
            var request=new Items<Tag>(7,new Tag());var feed=m.StreamAsync(request);request.Value=99;
            IAsyncEnumerable<long> http=feed;Check.That(p.Opened==0&&p.Seen.Count==0,"open generic HTTP source stays lazy");
            Check.That(m.Send(new Query(41))==42,"closed Send before native generic Stream");
            var values=new List<long>();await foreach(var item in http.ConfigureAwait(false))values.Add(item);
            Check.That(values.SequenceEqual(new[]{107L,108L})&&p.Seen.All(v=>v==7)&&request.Value==99,"value request copies and concrete type-changing item ports");
            var terminal=new List<long>();await feed.ConsumeAsync(new Sink(terminal));
            Check.That(terminal.SequenceEqual(values)&&p.Opened==2&&p.Disposed==2&&p.Boundaries==2,"typed terminal and type-changed opaque port share one independently cleaned cursor per operation");
            Check.That(m.Send(new Query(9))==10&&p.Sends==2,"closed Send after native generic Stream");
            """);
        await RunNativeStreamFixture("open-value-type-change", source);
    }

    [Fact]
    public void Native_stream_inaccessible_cursor_is_diagnosed_without_leaking_it_through_public_facade()
    {
        var source = """
            using System.Collections.Generic;using System.Threading;using System.Threading.Tasks;using Zendiator;using Zendiator.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection;
            namespace PrivateStream{
            public sealed record Items:IStreamRequest<int>;
            [ZendiatorComposition("private-stream")]public sealed class Reads;
            public static class Container{
                [ZendiatorHandler]private sealed class Handler:IStreamSourceHandler<Items,int,Handler.Cursor>{
                    public ValueTask<Cursor> OpenAsync(Items r,CancellationToken t)=>new(new Cursor());
                    public sealed class Cursor:IAsyncEnumerator<int>{public int Current=>0;public ValueTask<bool> MoveNextAsync()=>new(false);public ValueTask DisposeAsync()=>default;}
                }
                public static IServiceCollection Register(IServiceCollection services)=>services.AddZendiator<Reads>(c=>c.Handlers<Items>(DispatchFamily.Stream,typeof(Handler)));
            }}
            """;
        var input = SyncCompilation(source, "InaccessibleNativeCursor");
        Assert.Empty(input.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        var driver = SyncDriver().RunGenerators(input);
        var result = driver.GetRunResult();
        Assert.Contains(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error && d.Location.IsInSource);
        Assert.DoesNotContain(result.Results.Single().GeneratedSources,
            file => file.SourceText.ToString().Contains("PrivateStream.Container.Handler.Cursor", StringComparison.Ordinal));
    }
}
