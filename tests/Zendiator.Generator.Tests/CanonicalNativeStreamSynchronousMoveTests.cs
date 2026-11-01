using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    [Fact]
    public async Task Native_stream_second_synchronous_raw_Move_consumes_once_and_GetResult_failure_closes_ownership()
    {
        var source = NativeStreamFixture("""
            c.Handlers<Items>(DispatchFamily.Stream,HandlerEntry.Session<Handler,Factory>());
            c.Pipeline<Items>(DispatchFamily.Stream);
            """, """
            public sealed record Items:IStreamRequest<int>;
            public sealed class Probe(bool fail){
                public readonly bool Fail=fail;public readonly Exception Error=new FormatException("raw GetResult");
                public Cursor? Alias;public int Opened,Pulls,Reads,Consumptions,CursorDisposals,SessionDisposals,FactoryDisposals;
                public readonly List<string> Cleanup=new();
            }
            [ZendiatorHandler]public sealed class Handler(Probe p):IStreamSourceHandler<Items,int,Cursor>,IAsyncDisposable{
                public ValueTask<Cursor> OpenAsync(Items r,CancellationToken t){p.Opened++;var cursor=new Cursor(p);p.Alias=cursor;return new(cursor);}
                public ValueTask DisposeAsync(){p.SessionDisposals++;p.Cleanup.Add("handler");return default;}
            }
            public sealed class Factory(Probe p):IOwnedSessionFactory<Items,Handler>,IDisposable{
                public Handler Create(Items r,CancellationToken t)=>new(p);
                public void Dispose()=>p.FactoryDisposals++;
            }
            public sealed class Cursor(Probe p):IAsyncEnumerator<int>,System.Threading.Tasks.Sources.IValueTaskSource<bool>{
                private int value;
                public int Current{get{Check.That(ReferenceEquals(p.Alias,this),"native cursor alias identity at Current");p.Reads++;return value;}}
                public ValueTask<bool> MoveNextAsync(){
                    Check.That(ReferenceEquals(p.Alias,this),"native cursor alias identity at Move");p.Pulls++;
                    if(p.Pulls==1){value=10;return new(true);}
                    if(p.Pulls==2)return new(this,0);
                    return new(false);
                }
                public ValueTask DisposeAsync(){Check.That(ReferenceEquals(p.Alias,this),"native cursor alias identity at cleanup");p.CursorDisposals++;p.Cleanup.Add("cursor");return default;}
                bool System.Threading.Tasks.Sources.IValueTaskSource<bool>.GetResult(short token){
                    Check.That(token==0&&++p.Consumptions==1&&p.Pulls==2,"raw second Move GetResult exactly once");
                    if(p.Fail)throw p.Error;
                    value=20;return true;
                }
                System.Threading.Tasks.Sources.ValueTaskSourceStatus System.Threading.Tasks.Sources.IValueTaskSource<bool>.GetStatus(short token){
                    Check.That(token==0,"raw operation version");return System.Threading.Tasks.Sources.ValueTaskSourceStatus.Succeeded;
                }
                void System.Threading.Tasks.Sources.IValueTaskSource<bool>.OnCompleted(Action<object?> continuation,object? state,short token,System.Threading.Tasks.Sources.ValueTaskSourceOnCompletedFlags flags)=>throw new InvalidOperationException("completed raw source must not register a continuation");
            }
            """, """
            foreach(var fail in new[]{false,true}){
                var p=new Probe(fail);var services=new ServiceCollection();services.AddSingleton(p);Register(services);
                await using var provider=services.BuildServiceProvider();var scope=provider.CreateAsyncScope();
                var mediator=scope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
                var cursor=mediator.StreamAsync(new Items()).GetAsyncEnumerator();
                Check.That(p.Opened==0&&p.Alias==null,"enumerator acquisition remains lazy");
                Check.That(await cursor.MoveNextAsync()&&p.Opened==1&&p.Pulls==1&&p.Reads==0,"first Move publishes without reading Current or advancing ahead");
                var alias=p.Alias;Check.That(cursor.Current==10&&p.Reads==1,"consumer reads first Current after Move true");
                Exception? error=null;var moved=false;
                try{moved=await cursor.MoveNextAsync();}catch(Exception caught){error=caught;}
                Check.That(p.Consumptions==1&&p.Pulls==2&&p.Reads==1&&ReferenceEquals(p.Alias,alias),"second raw Move is consumed once without reading Current or copying the native cursor");
                if(fail){
                    Check.That(ReferenceEquals(error,p.Error)&&!moved,"Succeeded status does not hide a GetResult exception");
                    Check.Invalid(()=>{_=cursor.Current;});
                    Check.That(!await cursor.MoveNextAsync()&&p.Pulls==2,"GetResult failure closes owner without another source pull");
                }else{
                    Check.That(error==null&&moved&&p.CursorDisposals==0&&p.SessionDisposals==0,"synchronous raw true remains live until EOF or disposal");
                    Check.That(cursor.Current==20&&p.Reads==2&&p.Pulls==2,"second Current observes mutation on the original class cursor without prefetch");
                    Check.That(!await cursor.MoveNextAsync()&&p.Pulls==3,"normal EOF after the two published items");
                    Check.Invalid(()=>{_=cursor.Current;});
                }
                await cursor.DisposeAsync();await cursor.DisposeAsync();
                Check.That(p.Consumptions==1&&p.CursorDisposals==1&&p.SessionDisposals==1&&p.Cleanup.SequenceEqual(new[]{"cursor","handler"}),"cursor and owned Handler cleanup each once in ownership order");
                Check.That(p.FactoryDisposals==0,"DI factory is borrowed until its scope ends");
                await scope.DisposeAsync();Check.That(p.FactoryDisposals==1,"DI scope owns the factory");
            }
            """);
        await RunNativeStreamFixture("synchronous-raw-move", source);
    }
}
