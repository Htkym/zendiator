using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    [Fact]
    public async Task Native_stream_nested_pending_opens_keep_gates_and_request_contexts_independent()
    {
        var source = NativeStreamFixture("""
            c.Handlers<Items>(DispatchFamily.Stream,typeof(Handler));
            c.Pipeline<Items>(DispatchFamily.Stream,Stage.Wrapper<Relay<Five>>(),Stage.Wrapper<Relay<Four>>(),Stage.Wrapper<Relay<Three>>(),Stage.Wrapper<Relay<Two>>(),Stage.Wrapper<Relay<One>>());
            """, """
            public sealed record Items(string Id,int Value):IStreamRequest<int>;
            public sealed class One{}public sealed class Two{}public sealed class Three{}public sealed class Four{}public sealed class Five{}
            public sealed class Flow{
                public readonly Dictionary<string,IStreamUpstream<Items,int>> Next=new();
                public readonly List<string?> Before=new(),After=new();
                public global::Zendiator.Generator.Tests.AsyncOnceGate<Cursor>? OpenGate;
                public Cursor? Alias;public string? OpenAmbient,CreatedAmbient,MoveAmbient;
                public int Opened,Moves,Reads,Disposed;
            }
            public sealed class Probe{
                public static readonly AsyncLocal<string?> Ambient=new();
                public readonly Dictionary<string,Flow> Flows=new(){["A"]=new(),["B"]=new()};
                public int HandlerCreated,HandlerDisposed;
                public Flow For(Items r)=>Flows[r.Id];
            }
            [ZendiatorHandler]public sealed class Handler:IStreamSourceHandler<Items,int,Cursor>,IDisposable{
                private readonly Probe p;
                public Handler(Probe p){this.p=p;p.HandlerCreated++;}
                public ValueTask<Cursor> OpenAsync(Items r,CancellationToken t){
                    var f=p.For(r);f.Opened++;f.OpenAmbient=Probe.Ambient.Value;
                    Probe.Ambient.Value="handler-"+r.Id;
                    f.OpenGate=new(new Cursor(f,r.Value,t));return f.OpenGate.Task;
                }
                public void Dispose()=>p.HandlerDisposed++;
            }
            public sealed class Cursor:IAsyncEnumerator<int>{
                private readonly Flow f;private readonly int value;private readonly CancellationToken token;
                private bool disposed;private int index;
                public Cursor(Flow f,int value,CancellationToken token){this.f=f;this.value=value;this.token=token;f.Alias=this;f.CreatedAmbient=Probe.Ambient.Value;}
                public int Current{get{Check.That(!disposed&&ReferenceEquals(f.Alias,this),"Current retains the acquired class cursor");f.Reads++;return value;}}
                public ValueTask<bool> MoveNextAsync(){
                    Check.That(!disposed&&ReferenceEquals(f.Alias,this),"Move retains its own live cursor");token.ThrowIfCancellationRequested();
                    f.Moves++;f.MoveAmbient=Probe.Ambient.Value;return new(index++==0);
                }
                public ValueTask DisposeAsync(){Check.That(!disposed&&ReferenceEquals(f.Alias,this),"raw cleanup occurs once on the acquired cursor");disposed=true;f.Disposed++;return default;}
            }
            public sealed class Relay<T>(Probe p):IStreamMiddleware<Items,int>{
                public async ValueTask<IAsyncEnumerator<int>> OpenAsync(Items r,IStreamUpstream<Items,int> next,CancellationToken t){
                    var f=p.For(r);f.Next.Add(typeof(T).Name,next);f.Before.Add(Probe.Ambient.Value);
                    var cursor=await next.OpenAsync(r,t).ConfigureAwait(false);
                    f.After.Add(Probe.Ambient.Value);return cursor;
                }
            }
            """, """
            var p=new Probe();var services=new ServiceCollection();services.AddSingleton(p);Register(services);
            await using var provider=services.BuildServiceProvider();await using var scope=provider.CreateAsyncScope();
            var mediator=scope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
            using var tokenA=new CancellationTokenSource();using var tokenB=new CancellationTokenSource();
            var requestA=new Items("A",10);var requestB=new Items("B",20);
            var a=mediator.StreamAsync(requestA,tokenA.Token).GetAsyncEnumerator();
            var b=mediator.StreamAsync(requestB,tokenB.Token).GetAsyncEnumerator();
            var fa=p.For(requestA);var fb=p.For(requestB);
            try{
                Check.That(fa.Opened==0&&fb.Opened==0,"both independent enumerators remain lazy");
                Probe.Ambient.Value="request-A";var moveA=a.MoveNextAsync();
                Check.That(!moveA.IsCompleted&&Probe.Ambient.Value=="request-A","first pending acquisition restores its operation caller");
                Probe.Ambient.Value="request-B";var moveB=b.MoveNextAsync();
                Check.That(!moveB.IsCompleted&&Probe.Ambient.Value=="request-B","second pending acquisition keeps its own operation caller");
                Check.That(fa.Next.Count==5&&fb.Next.Count==5&&fa.Opened==1&&fb.Opened==1,"all five opaque opens are pending in each operation");
                Check.That(fa.OpenGate!.Consumptions==0&&fb.OpenGate!.Consumptions==0&&fa.Moves==0&&fb.Moves==0,"pending raw Opens are neither consumed nor advanced early");

                RejectOverlappingOpen(fa,requestA,tokenA.Token);RejectOverlappingOpen(fb,requestB,tokenB.Token);
                Check.Invalid(()=>{_=a.Current;});Check.Invalid(()=>{_=b.Current;});
                Check.Invalid(()=>{_=a.MoveNextAsync();});Check.Invalid(()=>{_=b.MoveNextAsync();});
                Check.Invalid(()=>{_=a.DisposeAsync();});Check.Invalid(()=>{_=b.DisposeAsync();});
                Check.That(fa.Reads==0&&fb.Reads==0&&fa.Disposed==0&&fb.Disposed==0&&fa.Opened==1&&fb.Opened==1,"rejected overlap does not read, acquire or clean either operation");

                await CompleteOutsideContext(fb,"completion-B");
                Check.That(await moveB&&b.Current==20,"second request completes first with its own value");
                Check.That(!moveA.IsCompleted&&fa.OpenGate!.Consumptions==0&&fa.Moves==0&&fa.Disposed==0,"completing B leaves A pending and owned");
                VerifyFlow(fb,"B");
                await b.DisposeAsync();await b.DisposeAsync();
                Check.That(fb.Disposed==1&&fa.Disposed==0&&p.HandlerDisposed==0,"early cleanup belongs only to B and does not dispose the borrowed handler");
                RejectClosedOpen(fb,requestB,tokenB.Token);

                await CompleteOutsideContext(fa,"completion-A");
                Check.That(await moveA&&a.Current==10&&Probe.Ambient.Value=="request-B","A resumes under its own captured context without replacing its consumer context");
                VerifyFlow(fa,"A");
                await a.DisposeAsync();await a.DisposeAsync();
                RejectClosedOpen(fa,requestA,tokenA.Token);
                Check.That(!await a.MoveNextAsync()&&!await b.MoveNextAsync(),"closed public owners do not pull again");
                Check.That(fa.Disposed==1&&fb.Disposed==1&&fa.Moves==1&&fb.Moves==1&&fa.Reads==1&&fb.Reads==1,"each raw cursor is read and cleaned once without another pull");
                Check.That(p.HandlerDisposed==0,"DI services remain borrowed until their native scope closes");
                await scope.DisposeAsync();Check.That(p.HandlerCreated==1&&p.HandlerDisposed==1,"native DI owns the shared captured handler");
            }finally{Probe.Ambient.Value=null;}

            static void RejectOverlappingOpen(Flow f,Items request,CancellationToken token){
                foreach(var next in f.Next.Values){
                    Exception? error=null;try{_=next.OpenAsync(request,token);}catch(Exception caught){error=caught;}
                    Check.That(error?.GetType()==typeof(InvalidOperationException),"each pending upstream Open keeps its own opening gate");
                }
            }
            static void RejectClosedOpen(Flow f,Items request,CancellationToken token){
                foreach(var next in f.Next.Values){
                    Exception? error=null;try{_=next.OpenAsync(request,token);}catch(Exception caught){error=caught;}
                    Check.That(error is ObjectDisposedException,"closed upstream observes root closure rather than a stranded opening gate");
                }
            }
            static void VerifyFlow(Flow f,string id){
                Check.That(f.Opened==1&&f.OpenGate!.Consumptions==1&&f.Moves==1&&f.Reads==1,"one raw Open consumption and one delivered item");
                Check.That(f.OpenAmbient=="request-"+id&&f.CreatedAmbient=="handler-"+id,"nonasync Handler Open actually changes its enclosing initialization context");
                Check.That(f.Before.Count==5&&f.After.Count==5&&f.Before.All(v=>v=="request-"+id)&&f.After.All(v=>v=="request-"+id),"ordinary user async middleware continuations retain their request context");
                Check.That(f.MoveAmbient=="request-"+id,"first raw Move observes its enclosing operation rather than another request or completion thread");
            }
            static async Task CompleteOutsideContext(Flow f,string marker){
                Task completion;
                using(ExecutionContext.SuppressFlow())completion=Task.Run(()=>{Probe.Ambient.Value=marker;f.OpenGate!.Complete();});
                await completion.ConfigureAwait(false);
            }
            """);
        await RunNativeStreamFixture("nested-open-fusion", source);
    }
}
