using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public async Task Native_stream_pooled_start_preserves_pending_open_move_and_durable_public_task(int wrappers)
    {
        var stages = string.Concat(Enumerable.Range(1, wrappers).Select(i => ",Stage.Wrapper<Relay" + i + ">()"));
        var source = NativeStreamFixture("c.Handlers<Items>(DispatchFamily.Stream,typeof(Handler));c.Pipeline<Items>(DispatchFamily.Stream" + stages + ");", """
            public sealed record Items:IStreamRequest<int>;
            public sealed class Probe(string phase){
                public static readonly AsyncLocal<string?> Ambient=new();
                public readonly string Phase=phase;
                public global::Zendiator.Generator.Tests.IAsyncOnceGate? Gate;
                public readonly TaskCompletionSource<bool> Started=new(TaskCreationOptions.RunContinuationsAsynchronously);
                public readonly Exception Error=new FormatException("startup sentinel");
                public int Opened,Pulls,Disposals;public string? OpenAmbient;
            }
            [ZendiatorHandler]public sealed class Handler(Probe p):IStreamSourceHandler<Items,int,Cursor>{
                public ValueTask<Cursor> OpenAsync(Items r,CancellationToken token){
                    p.Opened++;p.OpenAmbient=Probe.Ambient.Value;Probe.Ambient.Value="inside-open";
                    var cursor=new Cursor(p);
                    if(p.Phase!="open")return new(cursor);
                    var gate=new global::Zendiator.Generator.Tests.AsyncOnceGate<Cursor>(cursor);p.Gate=gate;p.Started.SetResult(true);return gate.Task;
                }
            }
            public sealed class Cursor(Probe p):IAsyncEnumerator<int>{
                public int Current=>42;
                public ValueTask<bool> MoveNextAsync(){
                    if(++p.Pulls!=1)return new(false);
                    if(p.Phase!="move")return new(true);
                    var gate=new global::Zendiator.Generator.Tests.AsyncOnceGate<bool>(true);p.Gate=gate;p.Started.SetResult(true);return gate.Task;
                }
                public ValueTask DisposeAsync(){p.Disposals++;return default;}
            }
            public class Relay:IStreamMiddleware<Items,int>{
                public ValueTask<IAsyncEnumerator<int>> OpenAsync(Items r,IStreamUpstream<Items,int> next,CancellationToken token)=>next.OpenAsync(r,token);
            }
            public sealed class Relay1:Relay;public sealed class Relay2:Relay;public sealed class Relay3:Relay;public sealed class Relay4:Relay;public sealed class Relay5:Relay;
            """, """
            foreach(var phase in new[]{"open","move"})foreach(var outcome in new[]{"success","fault","cancel"}){
                var p=new Probe(phase);var services=new ServiceCollection();services.AddSingleton(p);Register(services);
                await using var provider=services.BuildServiceProvider();await using var scope=provider.CreateAsyncScope();using var cancellation=new CancellationTokenSource();
                var mediator=scope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
                var cursor=mediator.StreamAsync(new Items()).GetAsyncEnumerator(cancellation.Token);
                Probe.Ambient.Value="caller";
                var saved=cursor.MoveNextAsync().AsTask();
                Check.That(Probe.Ambient.Value=="caller","private async entry restores caller context before returning");
                await p.Started.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Check.That(!saved.IsCompleted&&p.Opened==1&&p.Gate!.Consumptions==0,"first public Move remains genuinely pending");
                Check.Invalid(()=>{_=cursor.Current;});Check.Invalid(()=>{_=cursor.MoveNextAsync();});Check.Invalid(()=>{_=cursor.DisposeAsync();});
                if(outcome=="success")p.Gate!.Complete();
                else if(outcome=="fault")p.Gate!.Fail(p.Error);
                else {cancellation.Cancel();p.Gate!.Fail(new OperationCanceledException(cancellation.Token));}
                for(var read=0;read<2;read++){
                    if(outcome=="success")Check.That(await saved.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false),"saved public Task retains its successful first result");
                    else{
                        Exception? observed=null;try{await saved.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);}catch(Exception error){observed=error;}
                        Check.That(outcome=="fault"?ReferenceEquals(observed,p.Error):observed is OperationCanceledException oce&&oce.CancellationToken==cancellation.Token,"startup preserves fault identity or cancellation token");
                    }
                }
                Check.That(p.Gate!.Consumptions==1&&Probe.Ambient.Value=="caller"&&p.OpenAmbient=="caller","raw startup is consumed once and does not leak its ExecutionContext");
                if(outcome=="success")Check.That(cursor.Current==42,"successful first item is published");
                await cursor.DisposeAsync();await cursor.DisposeAsync();
                Check.That(p.Disposals==(phase=="open"&&outcome!="success"?0:1),"only acquired cursors are disposed exactly once");
                if(outcome=="success")Check.That(await saved.ConfigureAwait(false),"public Task remains durable after disposal and pooled-state retirement");
                Probe.Ambient.Value=null;
            }
            """);
        var pooledStarts = 0;
        await RunNativeStreamFixture("pooled-start-" + wrappers, source, generatedTransform: generated =>
        {
            if (generated.Contains("StartMoveAsync()"))
            {
                Assert.Matches(@"\[global::System\.Runtime\.CompilerServices\.AsyncMethodBuilder\(typeof\(global::System\.Runtime\.CompilerServices\.PoolingAsyncValueTaskMethodBuilder<>\)\)\]\s+private async global::System\.Threading\.Tasks\.ValueTask<bool> StartMoveAsync\(\)", generated);
                Assert.Contains("operation = StartMoveAsync();", generated);
                Assert.Contains("return PublishMove(operation);", generated);
                pooledStarts++;
            }
            return generated;
        });
        Assert.True(pooledStarts > 0);
    }
}
