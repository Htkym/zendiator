using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_stream_mixed_fallback_retains_independent_pending_generations_and_cleanup(bool failCleanup)
    {
        var source = NativeStreamFixture("""
            c.Handlers<Items>(DispatchFamily.Stream,typeof(Handler));
            c.Pipeline<Items>(DispatchFamily.Stream,Stage.Wrapper<Relay>(),Stage.Hook<Filter>());
            """, """
            public sealed record Items:IStreamRequest<int>;
            public sealed class Probe{
                public readonly global::Zendiator.Generator.Tests.StreamHookRaceSource<bool> First=new(true,"manual"),Second=new(true,"manual"),Third=new(true,"manual");
                public readonly Exception CleanupError=new FormatException("cleanup");
                public int Pulls,Reads,Disposals,Opened;public bool FailCleanup;
            }
            [ZendiatorHandler]public sealed class Handler(Probe p):IStreamSourceHandler<Items,int,Cursor>{
                public ValueTask<Cursor> OpenAsync(Items r,CancellationToken token){p.Opened++;return new(new Cursor(p));}
            }
            public sealed class Cursor(Probe p):IAsyncEnumerator<int>{
                public int Current{get{p.Reads++;return p.Pulls;}}
                public ValueTask<bool> MoveNextAsync(){p.Pulls++;return p.Pulls switch{1=>new(true),2=>p.First.Task,3=>p.Second.Task,4=>p.Third.Task,_=>new(false)};}
                public ValueTask DisposeAsync(){p.Disposals++;if(p.FailCleanup)throw p.CleanupError;return default;}
            }
            public sealed class Relay:IStreamMiddleware<Items,int>{
                public ValueTask<IAsyncEnumerator<int>> OpenAsync(Items r,IStreamUpstream<Items,int> next,CancellationToken token)=>next.OpenAsync(r,token);
            }
            public sealed class Filter:IStreamItemFilter<Items,int>{
                public bool Accept(Items r,int item,CancellationToken token)=>true;
            }
            """, """
            var p=new Probe{FailCleanup=FAIL_CLEANUP};var services=new ServiceCollection();services.AddSingleton(p);Register(services);
            await using var provider=services.BuildServiceProvider();await using var scope=provider.CreateAsyncScope();
            var mediator=scope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
            var cursor=mediator.StreamAsync(new Items()).GetAsyncEnumerator();
            Check.That(p.Opened==0,"mixed fallback acquisition stays lazy");
            Check.That(await cursor.MoveNextAsync()&&cursor.Current==1,"initial item opens the mixed hook boundary");
            var first=cursor.MoveNextAsync();Check.That(!first.IsCompleted,"first repeated move is pending");
            Check.Invalid(()=>{_=cursor.Current;});Check.Invalid(()=>{_=cursor.MoveNextAsync();});Check.Invalid(()=>{_=cursor.DisposeAsync();});
            var firstReady=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            first.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(()=>firstReady.TrySetResult(true));
            p.First.Complete();await firstReady.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Check.That(first.IsCompleted,"first result remains available without consumption");
            var second=cursor.MoveNextAsync();Check.That(!second.IsCompleted,"second repeated move is pending");
            var secondReady=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            second.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(()=>secondReady.TrySetResult(true));
            p.Second.Complete();await secondReady.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Check.That(second.IsCompleted,"second result remains available without consumption");
            var third=cursor.MoveNextAsync();Check.That(!third.IsCompleted,"third move can proceed while two generations remain unconsumed");
            var saved=third.AsTask();p.Third.Complete();
            Check.That(await saved&&cursor.Current==4,"independent overflow generation publishes the current item");
            Check.That(await first&&await second,"retained generations preserve their own result");
            Check.That(p.First.Consumptions==1&&p.Second.Consumptions==1&&p.Third.Consumptions==1,"each producer operation is consumed once");
            try{
                Check.That(!await cursor.MoveNextAsync()&&!p.FailCleanup,"EOF succeeds only when cleanup succeeds");
            }catch(Exception error){Check.That(p.FailCleanup&&ReferenceEquals(error,p.CleanupError),"cleanup failure identity is preserved");}
            Check.That(p.Disposals==1&&p.Pulls==5,"EOF performs exactly one cleanup and no read ahead");
            await cursor.DisposeAsync();await cursor.DisposeAsync();
            Check.That(await saved&&await saved,"saved Task remains stable after later generations and cleanup");
            Check.That(p.Disposals==1,"repeated disposal cannot clean twice");
            """.Replace("FAIL_CLEANUP", failCleanup ? "true" : "false"));
        var completionSources = 0;
        await RunNativeStreamFixture("mixed-fallback-completion-" + failCleanup, source, generatedTransform: generated =>
        {
            if (generated.Contains("private sealed class PendingTaskRunner"))
            {
                Assert.Contains("PublishMove(operation, moveCompletion)", generated);
                Assert.Contains("_firstCompletion, _secondCompletion, _completion", generated);
                Assert.DoesNotContain("AsyncTaskMethodBuilder<bool> _promise", generated);
                completionSources++;
            }
            return generated;
        });
        Assert.True(completionSources > 0);
    }
}
