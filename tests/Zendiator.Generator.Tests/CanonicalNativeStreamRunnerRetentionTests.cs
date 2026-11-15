// Regression coverage for retained results and later generation cleanup.
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    [Fact]
    public async Task Native_stream_retained_first_result_does_not_keep_later_closed_generation_alive()
    {
        var source = NativeStreamFixture("""
            c.Handlers<Items>(DispatchFamily.Stream,typeof(Handler));
            c.Pipeline<Items>(DispatchFamily.Stream);
            """, """
            public sealed record Items:IStreamRequest<int>;
            public sealed class Payload { public readonly byte[] Bytes=new byte[65536]; }
            public sealed class PayloadFailure(Payload payload):Exception("later generation") {
                public readonly Payload Payload=payload;
            }
            public sealed class Probe(string mode) {
                public static readonly AsyncLocal<object?> Ambient=new();
                public readonly string Mode=mode;
                public readonly global::Zendiator.Generator.Tests.StreamHookRaceSource<bool> First=new(true,"manual"),Second=new(true,"manual");
                public int Pulls,Disposals,ConsumerCallbacks;public bool SawAmbient;public WeakReference? ErrorPayload;
            }
            [Handler]public sealed class Handler(Probe p):IStreamSourceHandler<Items,int,Cursor> {
                public ValueTask<Cursor> OpenAsync(Items request,CancellationToken token)=>new(new Cursor(p));
            }
            public sealed class Cursor(Probe p):IAsyncEnumerator<int>,System.Threading.Tasks.Sources.IValueTaskSource<bool> {
                public int Current=>p.Pulls;
                public ValueTask<bool> MoveNextAsync() {
                    p.Pulls++;
                    return p.Pulls switch {1=>new(true),2=>p.First.Task,3=>new(this,0),_=>throw new InvalidOperationException("unexpected raw pull")};
                }
                public ValueTask DisposeAsync(){p.Disposals++;return default;}
                public bool GetResult(short token) {
                    p.Second.GetResult(token);p.SawAmbient=Probe.Ambient.Value is Payload;
                    if(p.Mode=="fault"){
                        var payload=new Payload();p.ErrorPayload=new WeakReference(payload);
                        throw new PayloadFailure(payload);
                    }
                    return p.Mode!="eof";
                }
                public System.Threading.Tasks.Sources.ValueTaskSourceStatus GetStatus(short token)=>p.Second.GetStatus(token);
                public void OnCompleted(Action<object?> continuation,object? state,short token,System.Threading.Tasks.Sources.ValueTaskSourceOnCompletedFlags flags)=>p.Second.OnCompleted(continuation,state,token,flags);
            }
            public sealed class Held(ValueTask<bool> first,WeakReference owner,WeakReference ambient,WeakReference consumerPayload,WeakReference? errorPayload) {
                public readonly ValueTask<bool> First=first;
                public readonly WeakReference Owner=owner,Ambient=ambient,ConsumerPayload=consumerPayload;
                public readonly WeakReference? ErrorPayload=errorPayload;
            }
            public static class Retention {
                [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
                public static Held Create(string mode) {
                    var p=new Probe(mode);var services=new ServiceCollection();services.AddSingleton(p);Bridge.Register(services);
                    using var provider=services.BuildServiceProvider();using var scope=provider.CreateScope();
                    var mediator=scope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
                    var cursor=mediator.StreamAsync(new Items()).GetAsyncEnumerator();
                    Check.That(cursor.MoveNextAsync().GetAwaiter().GetResult(),"initial synchronous item");
                    var first=cursor.MoveNextAsync();Check.That(!first.IsCompleted,"first begins pending");
                    p.First.Complete();Check.That(first.IsCompletedSuccessfully,"first is completed but remains unconsumed");
                    var payload=new Payload();var weakPayload=new WeakReference(payload);ValueTask<bool> later;
                    try{Probe.Ambient.Value=payload;later=cursor.MoveNextAsync();}
                    finally{Probe.Ambient.Value=null;}
                    Check.That(!later.IsCompleted&&p.Second.Consumptions==0,"second generation is genuinely pending while first is retained");
                    var weakConsumer=RegisterConsumer(later,p);
                    p.Second.Complete();
                    Check.That(p.ConsumerCallbacks==1,"the later consumer callback ran without consuming its public result");
                    Check.That(p.SawAmbient&&Probe.Ambient.Value is null,"later raw result sees its own context and restores the completion caller");
                    Check.That(mode=="fault"?later.IsFaulted:later.IsCompletedSuccessfully,"later public result completes with its own classification");
                    // Deliberately do not consume `later`. Holding only `first` must not root its failure.
                    cursor.DisposeAsync().GetAwaiter().GetResult();cursor.DisposeAsync().GetAwaiter().GetResult();
                    Check.That(!cursor.MoveNextAsync().GetAwaiter().GetResult()&&p.Pulls==3&&p.Disposals==1,"closed owner does not pull or dispose twice");
                    Check.That(p.First.Consumptions==1&&p.Second.Consumptions==1,"each raw result is consumed once");
                    return new(first,new WeakReference(cursor),weakPayload,weakConsumer,p.ErrorPayload);
                }
                [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
                private static WeakReference RegisterConsumer(ValueTask<bool> later,Probe p) {
                    var payload=new Payload();var weak=new WeakReference(payload);
                    later.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(()=>{
                        Check.That(payload.Bytes.Length==65536,"the consumer callback owns its private payload");
                        p.ConsumerCallbacks++;
                        // Intentionally no GetResult: closure state remains on an unconsumed second cell.
                    });
                    return weak;
                }
                [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
                public static bool Alive(WeakReference weak)=>weak.IsAlive;
            }
            """, """
            foreach(var mode in new[]{"success-dispose","eof","fault"}){
                var held=Retention.Create(mode);
                for(var round=0;round<3;round++){GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();}
                Check.That(!Retention.Alive(held.Owner),"holding the old result alone does not keep the closed owner alive: "+mode);
                Check.That(!Retention.Alive(held.Ambient),"holding the old result alone does not keep later captured context alive: "+mode);
                Check.That(!Retention.Alive(held.ConsumerPayload),"holding the old result alone does not keep a discarded later callback alive: "+mode);
                if(held.ErrorPayload is not null)Check.That(!Retention.Alive(held.ErrorPayload),"holding first must not keep discarded second-generation failure alive");
                Check.That(await held.First.ConfigureAwait(false),"first remains independently consumable after later generation and closure");
                GC.KeepAlive(held);
            }
            """);
        await RunNativeStreamFixture("old-first-later-closed-retention", source);
    }
}
