using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    [Fact]
    public async Task Native_stream_first_owner_claim_is_lazy_and_clones_keep_independent_original_tokens()
    {
        var source = NativeStreamFixture("""
            c.Handlers<Items>(DispatchFamily.Stream,typeof(Handler));c.Pipeline<Items>(DispatchFamily.Stream);
            """, """
            public sealed record Items:IStreamRequest<int>;
            public sealed class Probe{
                public int Resolved,Opened,HandlerDisposals;public readonly HashSet<int> Active=new();public readonly List<int> Disposed=new();
            }
            [ZendiatorHandler]public sealed class Handler:IStreamSourceHandler<Items,int,Cursor>,IDisposable{
                private readonly Probe p;public Handler(Probe p){this.p=p;p.Resolved++;}
                public ValueTask<Cursor> OpenAsync(Items r,CancellationToken t){var id=++p.Opened;p.Active.Add(id);return new(new Cursor(p,id,t));}
                public void Dispose()=>p.HandlerDisposals++;
            }
            public sealed class Cursor(Probe p,int id,CancellationToken token):IAsyncEnumerator<int>{
                private int index=-1;private bool disposed;public int Current=>index;
                public ValueTask<bool> MoveNextAsync(){Check.That(!disposed,"another owner must not dispose this native cursor");token.ThrowIfCancellationRequested();return new(++index<2);}
                public ValueTask DisposeAsync(){Check.That(!disposed&&p.Active.Remove(id),"native cursor cleanup occurs exactly once");disposed=true;p.Disposed.Add(id);return default;}
            }
            """, """
            var p=new Probe();var services=new ServiceCollection();services.AddSingleton(p);Register(services);
            await using var provider=services.BuildServiceProvider();await using var scope=provider.CreateAsyncScope();
            var mediator=scope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
            IAsyncEnumerable<int> unused=mediator.StreamAsync(new Items());var unissued=(IAsyncEnumerator<int>)unused;
            Check.That(p.Resolved==0&&p.Opened==0,"creating an unused standard source does not acquire a handler or native cursor");
            Check.Invalid(()=>{_=unissued.Current;});var rejected=false;
            try{await unissued.MoveNextAsync();}catch(InvalidOperationException){rejected=true;}
            Check.That(rejected&&p.Resolved==0&&p.Opened==0,"a source cast cannot move before its first GetAsyncEnumerator");
            var issued=unused.GetAsyncEnumerator();Check.That(ReferenceEquals(unused,issued),"rejected unissued Move leaves the first-owner claim available");
            await issued.DisposeAsync();await issued.DisposeAsync();
            Check.That(p.Resolved==0&&p.Opened==0&&!await issued.MoveNextAsync(),"disposing the claimed owner before its first Move stays lazy and closed");

            IAsyncEnumerable<int> racing=mediator.StreamAsync(new Items());var racingOwner=(IAsyncEnumerator<int>)racing;
            using(var raceStart=new Barrier(3)){
                async Task DisposeRacingOwner(){
                    raceStart.SignalAndWait();
                    try{await racingOwner.DisposeAsync();}catch(InvalidOperationException){/* A concurrent token binding may reject disposal. */}
                }
                var firstDispose=Task.Run(DisposeRacingOwner);var secondDispose=Task.Run(DisposeRacingOwner);
                raceStart.SignalAndWait();var raced=racing.GetAsyncEnumerator();
                await Task.WhenAll(firstDispose,secondDispose);await raced.DisposeAsync();
                var afterRace=racing.GetAsyncEnumerator();
                Check.That(!ReferenceEquals(racing,afterRace),"the first owner is never claimed again after competing disposal");
                await afterRace.DisposeAsync();await racingOwner.DisposeAsync();
                Check.That(p.Resolved==0&&p.Opened==0,"cold disposal competitors settle without acquiring or stranding a shared completion");
            }

            IAsyncEnumerable<int> cold=mediator.StreamAsync(new Items());var coldOwner=(IAsyncEnumerator<int>)cold;
            await coldOwner.DisposeAsync();await coldOwner.DisposeAsync();
            Check.That(p.Resolved==0&&p.Opened==0,"disposing an unissued source closes only that owner without acquisition");
            var replacement=cold.GetAsyncEnumerator();
            Check.That(!ReferenceEquals(cold,replacement)&&await replacement.MoveNextAsync()&&replacement.Current==0,"Get after source-cast disposal opens an independent clone");
            await replacement.DisposeAsync();Check.That(p.Opened==1&&p.Disposed.Count==1,"the replacement owns exactly one native cursor");

            using var api=new CancellationTokenSource();using var a=new CancellationTokenSource();using var b=new CancellationTokenSource();using var laterToken=new CancellationTokenSource();
            IAsyncEnumerable<int> http=mediator.StreamAsync(new Items(),api.Token);
            using var start=new Barrier(3);
            var left=Task.Run(()=>{start.SignalAndWait();return http.GetAsyncEnumerator(a.Token);});
            var right=Task.Run(()=>{start.SignalAndWait();return http.GetAsyncEnumerator(b.Token);});
            start.SignalAndWait();var ea=await left;var eb=await right;
            Check.That((ReferenceEquals(http,ea)^ReferenceEquals(http,eb))&&!ReferenceEquals(ea,eb),"concurrent Get claims the source once and gives the other enumeration a clone");
            Check.That(p.Opened==1,"concurrent first-owner publication does not start either enumeration");
            Check.That(await ea.MoveNextAsync()&&ea.Current==0&&await eb.MoveNextAsync()&&eb.Current==0,"both token-bound enumerations start from their own first item");
            var owner=ReferenceEquals(http,ea)?ea:eb;var other=ReferenceEquals(http,ea)?eb:ea;
            var ownerToken=ReferenceEquals(http,ea)?a:b;ownerToken.Cancel();
            await ExpectCanceled(owner);
            Check.That(await other.MoveNextAsync()&&other.Current==1&&p.Disposed.Count==2,"the first owner's enumeration cancellation leaves its concurrent clone live");
            var later=http.GetAsyncEnumerator(laterToken.Token);
            Check.That(await later.MoveNextAsync()&&later.Current==0,"later clones use the immutable API token rather than the canceled first owner's effective token");
            api.Cancel();await ExpectCanceled(other);await ExpectCanceled(later);
            await ea.DisposeAsync();await eb.DisposeAsync();await later.DisposeAsync();
            Check.That(p.Opened==4&&p.Active.Count==0&&p.Disposed.Distinct().Count()==4&&p.Disposed.Count==4,"all started native cursors close once, including API-canceled clones");
            Check.That(p.Resolved==1&&p.HandlerDisposals==0,"independent owners borrow the same captured DI handler");
            await scope.DisposeAsync();Check.That(p.HandlerDisposals==1,"the DI scope retains handler disposal ownership");

            static async ValueTask ExpectCanceled(IAsyncEnumerator<int> cursor){
                var canceled=false;try{await cursor.MoveNextAsync();}catch(OperationCanceledException){canceled=true;}
                Check.That(canceled,"the owner must observe its cancellation token");Check.Invalid(()=>{_=cursor.Current;});
            }
            """);
        await RunNativeStreamFixture("first-owner-claim", source);
    }
}
