using Microsoft.Extensions.DependencyInjection;
using Zendiator.Tests.Contracts;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private sealed record StreamBoundaryResult(string Error, bool Canceled, bool Faulted, bool? TokenPreserved,
        int Starts, int Moves, int Reads, int Disposals, int Effects, int AfterResolves, int Consumed, string[] Trace);
    [Theory]
    [InlineData("pre-api")] [InlineData("pre-enum")] [InlineData("pending-api")] [InlineData("pending-enum")]
    [InlineData("hook-false")] [InlineData("hook-true")]
    [InlineData("foreign-oce")] [InlineData("cancel-cleanup-error")] [InlineData("error-cleanup-oce")] [InlineData("get-failure")]
    public async Task Stream_adoption_canonical_cancellation_and_escaping_status_match_new_contract_reference(string profile)
    {
        var f = FallbackFixtureFor("long");
        async Task<StreamBoundaryResult> Run(bool generated)
        {
            using var api = new CancellationTokenSource(); using var enumeration = new CancellationTokenSource(); using var foreign = new CancellationTokenSource();
            var linked = profile is "pre-enum" or "pending-api" or "pending-enum" or "cancel-cleanup-error" or "get-failure";
            var pending = profile.StartsWith("pending-", StringComparison.Ordinal) || profile == "cancel-cleanup-error";
            var source = new ApplicationException("source"); var cleanup = new InvalidOperationException("cleanup");
            var foreignError = new OperationCanceledException(foreign.Token); var cleanupOce = new OperationCanceledException(foreign.Token);
            var p = new CanonicalStreamFallbackProbe { ApiToken = api.Token, Pending = pending, Input = [1, 2],
                CancelInFirst = profile.StartsWith("hook-", StringComparison.Ordinal) ? api : null, CancelAndAccept = profile == "hook-true",
                MoveError = profile == "foreign-oce" ? foreignError : profile == "error-cleanup-oce" ? source : null,
                DisposeError = profile == "cancel-cleanup-error" ? cleanup : profile == "error-cleanup-oce" ? cleanupOce : null,
                GetEnumeratorError = profile == "get-failure" ? source : null };
            if (linked) p.ValidateStartToken = token => { Assert.True(token.CanBeCanceled); Assert.NotEqual(api.Token, token); Assert.NotEqual(enumeration.Token, token); };
            var request = Activator.CreateInstance(f.Request, [1])!; p.Outer = request;
            var collection = new ServiceCollection(); var roles = new[] { "H", "F1", "Map", "F2" };
            for (var i = 0; i < f.Services.Length; i++)
            { var type = f.Services[i]; var role = roles[i]; collection.AddTransient(type, _ => { p.Resolve(role); return Activator.CreateInstance(type, p)!; }); }
            using var services = collection.BuildServiceProvider(); IAsyncEnumerable<long> stream;
            if (generated) stream = (IAsyncEnumerable<long>)f.Canonical.Invoke(null, [services, request, api.Token])!;
            else
            {
                var bridge = Activator.CreateInstance(f.Bridge, services)!;
                var start = f.Start.CreateDelegate(typeof(Func<,,>).MakeGenericType(f.Request, typeof(CancellationToken), typeof(IAsyncEnumerable<long>)), bridge);
                stream = (IAsyncEnumerable<long>)typeof(CanonicalStreamItemReference).GetMethod("FilterMapFilter")!
                    .MakeGenericMethod(f.Request, typeof(long), f.Services[1], f.Services[2], f.Services[3]).Invoke(null, [services, request, start, api.Token, null])!;
            }
            if (profile == "pre-api") api.Cancel(); if (profile == "pre-enum") enumeration.Cancel();
            var cursor = stream.GetAsyncEnumerator(linked ? enumeration.Token : default);
            Assert.Equal(0, p.Starts); Assert.Empty(p.Trace); Exception? error = null; bool canceled = false, faulted = false;
            var move = cursor.MoveNextAsync(); var task = move.AsTask();
            if (pending)
            {
                Assert.False(task.IsCompleted); Assert.Equal(0, p.Reads); Assert.Equal(0, p.MoveGate.Consumptions);
                if (profile == "pending-enum") enumeration.Cancel(); else api.Cancel();
                Assert.True(p.SourceToken.IsCancellationRequested);
                // These cancellation cases suspend Move only; cleanup must not wait on an unreleased gate.
                if (profile is "pending-api" or "pending-enum") p.DisposeGate.Complete(true);
                p.MoveGate.Fail(new OperationCanceledException(p.SourceToken));
            }
            try { await task.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception e) { error = e; canceled = task.IsCanceled; faulted = task.IsFaulted; }
            Assert.NotNull(error);
            Exception? expected = profile == "cancel-cleanup-error" ? cleanup : profile == "error-cleanup-oce" ? cleanupOce :
                profile == "foreign-oce" ? foreignError : profile == "get-failure" ? source : null;
            if (expected != null) Assert.Same(expected, error);
            var oce = error as OperationCanceledException;
            bool? tokenPreserved = oce == null ? true : p.Starts == 0 ? null : oce.CancellationToken.Equals(
                profile is "foreign-oce" or "error-cleanup-oce" ? foreign.Token : p.SourceToken);
            if (tokenPreserved.HasValue) Assert.True(tokenPreserved.Value);
            Assert.Equal(profile is not ("cancel-cleanup-error" or "get-failure"), canceled);
            Assert.Equal(profile is "cancel-cleanup-error" or "get-failure", faulted);
            await cursor.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)); await cursor.DisposeAsync();
            Assert.False(await cursor.MoveNextAsync());
            Assert.Equal(profile.StartsWith("pre-", StringComparison.Ordinal) || profile == "get-failure" ? 0 : 1, p.Disposals);
            if (profile.StartsWith("pre-", StringComparison.Ordinal)) Assert.Equal(0, p.Starts);
            if (profile.StartsWith("hook-", StringComparison.Ordinal)) { Assert.Equal(1, p.Moves); Assert.Equal(0, p.Effects); Assert.Equal(0, p.AfterResolves); }
            if (pending) Assert.Equal(1, p.MoveGate.Consumptions);
            var effective = oce?.CancellationToken ?? p.SourceToken;
            if (linked)
            {
                var callbacks = 0; using var registration = effective.Register(() => callbacks++); var immediate = callbacks;
                api.Cancel(); enumeration.Cancel(); Assert.Equal(0, callbacks - immediate);
            }
            return new(profile == "cancel-cleanup-error" ? "cleanup" : profile == "error-cleanup-oce" ? "cleanup-oce" :
                profile == "foreign-oce" ? "foreign-oce" : profile == "get-failure" ? "startup" : "cancel",
                canceled, faulted, tokenPreserved, p.Starts, p.Moves, p.Reads, p.Disposals, p.Effects, p.AfterResolves, p.MoveGate.Consumptions, [.. p.Trace]);
        }
        var reference = await Run(false); var canonical = await Run(true);
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(evidence)) File.WriteAllText(Path.Combine(evidence, "stream-fallback", "boundary-" + profile + ".json"),
            System.Text.Json.JsonSerializer.Serialize(new { profile, reference, canonical, candidateOnly = true, productionEnabled = false }));
        Assert.Equal(reference.Trace, canonical.Trace); Assert.Equal(reference with { Trace = canonical.Trace }, canonical);
    }

    [Theory]
    [InlineData("direct")] [InlineData("early-break")]
    public async Task Stream_adoption_wrapper_sync_and_native_async_context_boundaries_match_reference(string mode)
    {
        var f = FallbackFixtureFor("wrapper");
        var reference = await RunFallback<int>(f, "reference", mode, context: true);
        var canonical = await RunFallback<int>(f, "canonical", mode, context: true);
        FallbackEvidence("context-wrapper-" + mode, reference, canonical); EqualFallback(reference, canonical);
        Assert.Contains(canonical.Trace, t => t.StartsWith("F2:", StringComparison.Ordinal) && t.EndsWith("@wrapper-sync", StringComparison.Ordinal));
        Assert.Contains(canonical.Trace, t => t.StartsWith("H:", StringComparison.Ordinal) &&
            t.EndsWith(mode == "direct" ? "@wrapper-sync" : "@wrapper-async", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Stream_adoption_canonical_context_and_suppressed_TaskRun_representatives_match_reference(bool suppress)
    {
        var f = FallbackFixtureFor("long");
        async Task<(FallbackResult Result, string[] Context)> Run(string backend)
        {
            var contexts = new Dictionary<string, StreamTrackingContext>(); var observations = new List<string>();
            void Observe(string phase, object? request, CancellationToken token)
            {
                var current = SynchronizationContext.Current;
                var name = current is StreamTrackingContext tracked ? tracked.Name : current == null ? "none" : "external";
                if (current is StreamTrackingContext instance) Assert.Same(contexts[instance.Name], instance);
                observations.Add(phase + "@" + name + ":" + ExecutionContext.IsFlowSuppressed());
                if (!suppress) SynchronizationContext.SetSynchronizationContext(contexts.GetValueOrDefault(phase) ?? (contexts[phase] = new(phase)));
            }
            async Task<(FallbackResult, string[])> Execute()
            {
                if (suppress) Assert.Null(CanonicalStreamFallbackProbe.Ambient.Value);
                var before = SynchronizationContext.Current;
                if (!suppress) { var caller = new StreamTrackingContext("caller"); contexts.Add("caller", caller); SynchronizationContext.SetSynchronizationContext(caller); }
                try
                {
                    var caller = SynchronizationContext.Current;
                    var result = await RunFallback<long>(f, backend, observe: Observe);
                    Assert.Same(caller, SynchronizationContext.Current);
                    return (result, [.. observations]);
                }
                finally { SynchronizationContext.SetSynchronizationContext(before); }
            }
            if (!suppress) return await Execute();
            var outer = CanonicalStreamFallbackProbe.Ambient.Value;
            CanonicalStreamFallbackProbe.Ambient.Value = "suppressed-outer";
            try
            {
                Task<(FallbackResult, string[])> task;
                using (ExecutionContext.SuppressFlow()) task = Task.Run(Execute);
                return await task;
            }
            finally { CanonicalStreamFallbackProbe.Ambient.Value = outer; }
        }
        var reference = await Run("reference"); var canonical = await Run("canonical");
        EqualFallback(reference.Result, canonical.Result); Assert.Equal(reference.Context, canonical.Context);
        Assert.NotEmpty(canonical.Context);
        if (suppress) Assert.All(canonical.Context, x => Assert.EndsWith(":False", x));
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(evidence)) File.WriteAllText(Path.Combine(evidence, "stream-fallback", "context-boundary-" + suppress + ".json"),
            System.Text.Json.JsonSerializer.Serialize(new { suppress, reference = new { reference.Result, reference.Context }, canonical = new { canonical.Result, canonical.Context },
                scope = suppress ? "Task.Run flow suppression representative" : "synchronous actual SynchronizationContext identity representative", candidateOnly = true, productionEnabled = false }));
    }

    [Fact]
    public async Task Stream_adoption_scope_destroyed_during_pending_wrapper_move_stops_new_DI_and_cleans_owned_cursor()
    {
        var f = StreamGenericFixtureFor("int", wrapper: true); var p = new StreamGenericScopeProbe { PendingSource = true };
        var collection = new ServiceCollection(); collection.AddSingleton(p);
        foreach (var type in f.Services) collection.AddScoped(type.GetGenericTypeDefinition(), type.GetGenericTypeDefinition());
        using var provider = collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var scope = provider.CreateScope(); var request = Activator.CreateInstance(f.Request, [7])!;
        var stream = (IAsyncEnumerable<int>)f.Create.Invoke(null, [scope.ServiceProvider, request, default(CancellationToken)])!;
        await using var cursor = stream.GetAsyncEnumerator(); var task = cursor.MoveNextAsync().AsTask();
        Assert.False(task.IsCompleted); Assert.Equal(2, p.Created.Count); scope.Dispose(); Assert.Equal(2, p.Released.Count);
        p.PendingGate.Complete(true); await Assert.ThrowsAsync<ObjectDisposedException>(async () => await task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(task.IsFaulted); Assert.Equal(2, p.Created.Count); Assert.Equal(2, p.Released.Count); Assert.Equal(1, p.SourceDisposals);
        Assert.Equal(1, p.PendingGate.Consumptions); await cursor.DisposeAsync(); Assert.False(await cursor.MoveNextAsync());
        StreamGenericEvidence("active-scope-violation", new { stoppedAtNextDI = true, created = p.Created.Count, releasedByScope = p.Released.Count,
            p.SourceDisposals, sourceConsumed = p.PendingGate.Consumptions, safeScopeEscapeGuaranteed = false });
    }
}
