using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    // Expected values come from operation-callback-v1 and native await boundaries, not the oracle or generated trace.
    private sealed record CrossContextExpected(string Phase, string? Ambient, string Culture, string Context, string Scheduler);
    private static CrossContextExpected[] CrossContextGolden(string mode)
    {
        var pending = mode is "suspend" or "suppressed"; var suppressed = mode == "suppressed";
        var context = pending ? "none" : "mutated"; var scheduler = pending ? "default" : "entry";
        return [
            new("k.factory","caller","en-US","caller","entry"),
            new("k.before","k.factory","fr-FR","mutated","entry"),
            new("h.factory","k.before","de-DE","mutated","entry"),
            new("h.body","h.factory","it-IT","mutated","entry"),
            new("h.cleanup",suppressed?null:"h.body",suppressed?"":"ja-JP",context,scheduler),
            new("k.after","h.cleanup","en-GB",context,scheduler),
            new("k.finally","k.after","es-ES",context,scheduler),
            new("k.cleanup","k.finally","pt-PT",context,scheduler)
        ];
    }
    private static CrossContextExpected CrossCaller(string phase, CrossCuttingProbe p) => new(phase, CrossCuttingProbe.Ambient.Value, CultureInfo.CurrentCulture.Name, SynchronizationContext.Current is CrossSynchronizationContext c ? c.Name : SynchronizationContext.Current == null ? "none" : "other", ReferenceEquals(TaskScheduler.Current, p.EntryScheduler) ? "entry" : ReferenceEquals(TaskScheduler.Current, TaskScheduler.Default) ? "default" : "other");
    [Theory]
    [InlineData("sync")]
    [InlineData("completed")]
    [InlineData("suspend")]
    [InlineData("suppressed")]
    public async Task Cross_cutting_context_matches_independent_AsyncLocal_Culture_SyncContext_TaskScheduler_expectation_table(string mode)
    {
        var f = CrossRuntimeFixture(mode == "sync" ? "context-sync" : "context-async"); var priorDefault = CultureInfo.DefaultThreadCurrentCulture; CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            foreach (var generated in new[] { false, true })
            {
                var p = new CrossCuttingProbe { ContextEnabled = true, PendingBody = mode is "suspend" or "suppressed" }; var scheduler = new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default, maxConcurrencyLevel: 1); p.EntryScheduler = scheduler.ExclusiveScheduler;
                try
                {
                    Task<int>? contextOperation = null;
                    async Task Run()
                    {
                        var ambient = CrossCuttingProbe.Ambient.Value; var culture = CultureInfo.CurrentCulture; var context = SynchronizationContext.Current;
                        try
                        {
                            IServiceCollection services = new ServiceCollection(); services.AddSingleton(p); f.Register.Invoke(null, [services]); await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope(); var mediator = scope.ServiceProvider.GetRequiredService(f.Mediator);
                            CrossCuttingProbe.Ambient.Value = "caller"; CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US"); SynchronizationContext.SetSynchronizationContext(p.CallerContext); var request = CrossRequest(f, "context"); Task<int>? operation = null; int value = 0;
                            if (mode == "sync") value = (int)CrossInvoke(f, p, scope.ServiceProvider, mediator, request, generated);
                            else if (mode == "suppressed") { using (ExecutionContext.SuppressFlow()) { operation = (Task<int>)AsyncTask(CrossInvoke(f, p, scope.ServiceProvider, mediator, request, generated), false); } }
                            else operation = (Task<int>)AsyncTask(CrossInvoke(f, p, scope.ServiceProvider, mediator, request, generated), false);
                            // The direct caller observation precedes any await or async observation helper.
                            var returned = CrossCaller("caller.returned", p); Assert.Equal(mode == "sync" ? new CrossContextExpected("caller.returned", "k.cleanup", "en-US", "mutated", "entry") : new CrossContextExpected("caller.returned", "caller", "en-US", "caller", "entry"), returned);
                            if (operation != null) { Assert.Equal(!p.PendingBody, operation.IsCompleted); contextOperation = operation; if (p.PendingBody) Assert.Single(p.Gates); value = await operation.ConfigureAwait(false); }
                            Assert.Equal(7, value); var after = CrossCaller("caller.after", p); Assert.Equal(mode == "sync" ? new CrossContextExpected("caller.after", "k.cleanup", "en-US", "mutated", "entry") : p.PendingBody ? new CrossContextExpected("caller.after", "caller", "en-US", "none", "default") : new CrossContextExpected("caller.after", "caller", "en-US", "caller", "entry"), after);
                            var actual = p.Events.Select(e => new CrossContextExpected(e.Phase, e.Ambient, e.Culture, e.Context, e.Scheduler)).ToArray(); Assert.Equal(CrossContextGolden(mode), actual); Assert.Equal(0, p.CallerContext.Posts + p.MutatedContext.Posts); Assert.All(p.Gates.Values, g => Assert.Equal(1, g.Consumptions)); CrossIdentity(p); CrossEvidence("context-" + mode, generated, p, new { expected = CrossContextGolden(mode), actual, returned, after, result = value, posts = p.CallerContext.Posts + p.MutatedContext.Posts });
                        }
                        finally { CrossCuttingProbe.Ambient.Value = ambient; CultureInfo.CurrentCulture = culture; SynchronizationContext.SetSynchronizationContext(context); }
                    }
                    var worker = Task.Factory.StartNew(Run, CancellationToken.None, TaskCreationOptions.DenyChildAttach, scheduler.ExclusiveScheduler).Unwrap();
                    if (p.PendingBody)
                    {
                        // The same exclusive scheduler executes this only after Run's synchronous prefix
                        // returned its incomplete Task, so caller await is registered before completion.
                        await Task.Factory.StartNew(() => { Assert.False(worker.IsCompleted); Assert.NotNull(contextOperation); Assert.False(contextOperation.IsCompleted); p.CompleteBodies(); }, CancellationToken.None, TaskCreationOptions.DenyChildAttach, scheduler.ExclusiveScheduler);
                    }
                    await worker.WaitAsync(TimeSpan.FromSeconds(15));
                }
                finally { scheduler.Complete(); await scheduler.Completion.WaitAsync(TimeSpan.FromSeconds(10)); p.Cancellation.Dispose(); p.Foreign.Dispose(); }
            }
        }
        finally { CultureInfo.DefaultThreadCurrentCulture = priorDefault; }
    }
}
