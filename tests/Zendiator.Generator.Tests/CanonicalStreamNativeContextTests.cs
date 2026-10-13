using System.Globalization;
using Zendiator.Tests.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private sealed record NativeContextObservation(string Phase, string? Ambient, string Culture);
    private sealed record NativeContextResult(string[] Values, NativeContextObservation[] Observations,
        int Reads, int Moves, int Disposals, int MoveConsumptions, int DisposeConsumptions,
        int NativeMovePendingReturns, int NativeDisposePendingReturns, string Error, bool Faulted, bool Canceled);

    [Theory]
    [InlineData("canonical", "sync")] [InlineData("canonical", "pending")]
    [InlineData("canonical", "pending-failure")] [InlineData("fused", "pending")]
    public async Task Stream_context_native_producer_factory_current_callback_and_caller_boundaries_match(string backend, string profile)
    {
        var fixture = FallbackFixtureFor(backend == "fused" ? "int" : "long");
        var method = typeof(GeneratorTests).GetMethod(nameof(ObserveNativeContext), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .MakeGenericMethod(fixture.Item);
        var reference = await ((Task<NativeContextResult>)method.Invoke(null, [fixture, "reference", profile])!);
        var generated = await ((Task<NativeContextResult>)method.Invoke(null, [fixture, backend, profile])!);
        Assert.Equal(reference.Values, generated.Values); Assert.Equal(reference.Observations, generated.Observations);
        Assert.Equal(reference with { Values = generated.Values, Observations = generated.Observations }, generated);
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(evidence))
        {
            var folder = Path.Combine(evidence, "stream-native-context"); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, backend + "-" + profile + ".json"), System.Text.Json.JsonSerializer.Serialize(new
                { backend, profile, reference, generated, candidateOnly = true, productionEnabled = false, nativeProducer = "user async helper forwarded by source;owns C# async boundary" }));
        }
    }
    private static async Task<NativeContextResult> ObserveNativeContext<T>(FallbackFixture f, string backend, string profile)
    {
        var observations = new List<NativeContextObservation>(); var pending = profile != "sync"; var failing = profile == "pending-failure";
        var moveGate = new ContractOnce<bool>(); var disposeGate = new ContractOnce<bool>();
        var disposeReturned = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sourceError = new FormatException("native-source"); var cleanupError = new InvalidOperationException("native-cleanup");
        var p = new CanonicalStreamFallbackProbe { Input = [1, 1], ApiToken = default };
        var request = Activator.CreateInstance(f.Request, [1])!; p.Outer = request;
        var previous = CanonicalStreamFallbackProbe.Ambient.Value; var previousCulture = CultureInfo.CurrentCulture;
        var previousContext = SynchronizationContext.Current;
        void Set(string value, string culture) { CanonicalStreamFallbackProbe.Ambient.Value = value; CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture); }
        void Observe(string phase, object? ignored = null, CancellationToken token = default)
        {
            observations.Add(new(phase, CanonicalStreamFallbackProbe.Ambient.Value, CultureInfo.CurrentCulture.Name));
            if (phase == "native-dispose.return") disposeReturned.TrySetResult(true);
            if (phase.StartsWith("factory:", StringComparison.Ordinal)) Set(phase, "fr-FR");
            if (phase == "Current") Set("getter", "en-US");
            if (phase == "F1") Set("callback:F1", "ja-JP");
            if (phase == "Map") Set("callback:Map", "it-IT");
            if (phase == "F2") Set("callback:F2", "ja-JP");
            if (phase == "H.finally") Set("source-sync-dispose", "en-GB");
        }
        p.Observe = Observe; var nativeMoves = 0;
        async ValueTask<bool> NativeMove(bool result)
        {
            Observe("native-move.enter"); Set("native-move-local", "de-DE");
            if (pending && nativeMoves++ == 0) result = await moveGate.Task.ConfigureAwait(false);
            else await Task.CompletedTask.ConfigureAwait(false);
            Observe("native-move.resume"); Assert.Equal("native-move-local", CanonicalStreamFallbackProbe.Ambient.Value);
            Assert.Equal("de-DE", CultureInfo.CurrentCulture.Name); Set("native-move-return", "it-IT");
            if (failing) throw sourceError;
            return result;
        }
        async ValueTask NativeDispose()
        {
            Observe("native-dispose.enter"); Set("native-dispose-local", "de-DE");
            if (pending) await disposeGate.Void.ConfigureAwait(false); else await Task.CompletedTask.ConfigureAwait(false);
            Observe("native-dispose.resume"); Assert.Equal("native-dispose-local", CanonicalStreamFallbackProbe.Ambient.Value);
            Assert.Equal("de-DE", CultureInfo.CurrentCulture.Name); Set("native-dispose-return", "it-IT");
            if (failing) throw cleanupError;
        }
        p.NativeMove = NativeMove; p.NativeDispose = NativeDispose;
        var collection = new ServiceCollection(); var roles = new[] { "H", "F1", "Map", "F2" };
        for (var i = 0; i < f.Services.Length; i++)
        { var type = f.Services[i]; var role = roles[i]; collection.AddTransient(type, _ => { p.Resolve(role); return Activator.CreateInstance(type, p)!; }); }
        using var provider = collection.BuildServiceProvider();
        var values = new List<string>(); Exception? failure = null; bool faulted = false, canceled = false;
        try
        {
            SynchronizationContext.SetSynchronizationContext(null); Set("caller-create", "en-US");
            IAsyncEnumerable<T> stream;
            if (backend == "reference")
            {
                var bridge = Activator.CreateInstance(f.Bridge, provider)!;
                var start = f.Start.CreateDelegate(typeof(Func<,,>).MakeGenericType(f.Request, typeof(CancellationToken), typeof(IAsyncEnumerable<T>)), bridge);
                stream = (IAsyncEnumerable<T>)typeof(CanonicalStreamItemReference).GetMethod("FilterMapFilter")!
                    .MakeGenericMethod(f.Request, typeof(T), f.Services[1], f.Services[2], f.Services[3]).Invoke(null, [provider, request, start, default(CancellationToken), null])!;
            }
            else stream = (IAsyncEnumerable<T>)(backend == "fused" ? f.Fused! : f.Canonical).Invoke(null, [provider, request, default(CancellationToken)])!;
            var cursor = stream.GetAsyncEnumerator(); Assert.Empty(p.Trace); Assert.Empty(observations);
            for (var i = 0; i < 2; i++)
            {
                var caller = i == 0 ? "caller-first" : "caller-second"; Set(caller, "en-AU"); Observe("consumer.move.enter");
                var move = cursor.MoveNextAsync().AsTask(); Observe("consumer.move.return");
                Assert.Equal(caller, CanonicalStreamFallbackProbe.Ambient.Value); Assert.Equal("en-AU", CultureInfo.CurrentCulture.Name);
                if (pending && i == 0)
                {
                    Assert.False(move.IsCompleted); Assert.Equal(0, p.Reads);
                    await Task.Run(() => { Set("foreign-move-releaser", "zh-CN"); moveGate.Complete(true); });
                    if (failing)
                    {
                        await disposeReturned.Task.WaitAsync(TimeSpan.FromSeconds(5));
                        await Task.Run(() => { Set("foreign-dispose-releaser", "zh-CN"); disposeGate.Complete(true); });
                    }
                }
                try
                {
                    Assert.True(await move.WaitAsync(TimeSpan.FromSeconds(5))); Observe("consumer.move.completed");
                    Assert.Equal(caller, CanonicalStreamFallbackProbe.Ambient.Value); Assert.Equal("en-AU", CultureInfo.CurrentCulture.Name);
                    var reads = p.Reads; values.Add(Convert.ToString(cursor.Current, CultureInfo.InvariantCulture)!); _ = cursor.Current;
                    Assert.Equal(reads, p.Reads); Assert.Equal(caller, CanonicalStreamFallbackProbe.Ambient.Value);
                }
                catch (Exception error) { failure = error; faulted = move.IsFaulted; canceled = move.IsCanceled; Observe("consumer.move.failed"); break; }
            }
            Set("caller-dispose", "en-US"); Observe("consumer.dispose.enter");
            var dispose = cursor.DisposeAsync().AsTask(); Observe("consumer.dispose.return");
            Assert.Equal("caller-dispose", CanonicalStreamFallbackProbe.Ambient.Value); Assert.Equal("en-US", CultureInfo.CurrentCulture.Name);
            if (pending && !failing)
            {
                Assert.False(dispose.IsCompleted);
                await Task.Run(() => { Set("foreign-dispose-releaser", "zh-CN"); disposeGate.Complete(true); });
            }
            await dispose.WaitAsync(TimeSpan.FromSeconds(5)); Observe("consumer.dispose.completed");
            Assert.Equal("caller-dispose", CanonicalStreamFallbackProbe.Ambient.Value); Assert.Equal("en-US", CultureInfo.CurrentCulture.Name);
            await cursor.DisposeAsync(); Assert.Equal(1, p.Disposals);
            Assert.Equal(pending ? 1 : 0, moveGate.Consumptions); Assert.Equal(pending ? 1 : 0, disposeGate.Consumptions);
            Assert.Equal(pending ? 1 : 0, p.NativeMovePendingReturns); Assert.Equal(pending ? 1 : 0, p.NativeDisposePendingReturns);
            Assert.Equal("source-sync-dispose", observations.First(o => o.Phase == "native-dispose.return").Ambient);
            Assert.Equal("en-GB", observations.First(o => o.Phase == "native-dispose.return").Culture);
            Assert.Equal("factory:H", observations.First(o => o.Phase == "native-move.return").Ambient);
            Assert.Equal("fr-FR", observations.First(o => o.Phase == "native-move.return").Culture);
            if (failing)
            {
                Assert.Same(cleanupError, failure); Assert.True(faulted); Assert.False(canceled); Assert.Equal(0, p.Reads);
                Assert.DoesNotContain(observations, o => o.Phase.StartsWith("factory:F", StringComparison.Ordinal));
            }
            else
            {
                Assert.Null(failure); Assert.Equal(2, p.Reads); Assert.Equal(new[] { "2", "2" }, values);
                Assert.Equal("factory:H", observations.First(o => o.Phase == "H").Ambient);
                Assert.Equal("fr-FR", observations.First(o => o.Phase == "H").Culture);
                Assert.Equal("factory:H", observations.First(o => o.Phase == "Current").Ambient);
                Assert.Equal("getter", observations.First(o => o.Phase == "factory:F1").Ambient);
                Assert.Equal("en-US", observations.First(o => o.Phase == "factory:F1").Culture);
                Assert.Equal("factory:F1", observations.First(o => o.Phase == "F1").Ambient);
                Assert.Equal("callback:F1", observations.First(o => o.Phase == "factory:Map").Ambient);
                Assert.Equal("ja-JP", observations.First(o => o.Phase == "factory:Map").Culture);
                Assert.Equal("factory:Map", observations.First(o => o.Phase == "Map").Ambient);
                Assert.Equal("callback:Map", observations.First(o => o.Phase == "factory:F2").Ambient);
                Assert.Equal("it-IT", observations.First(o => o.Phase == "factory:F2").Culture);
                Assert.Equal("factory:F2", observations.First(o => o.Phase == "F2").Ambient);
                Assert.Equal("caller-second", observations.Where(o => o.Phase == "Move").Last().Ambient);
            }
            return new([.. values], [.. observations], p.Reads, p.Moves, p.Disposals, moveGate.Consumptions, disposeGate.Consumptions,
                p.NativeMovePendingReturns, p.NativeDisposePendingReturns,
                failure == null ? "none" : ReferenceEquals(failure, cleanupError) ? "cleanup" : "unexpected", faulted, canceled);
        }
        finally { CanonicalStreamFallbackProbe.Ambient.Value = previous; CultureInfo.CurrentCulture = previousCulture; SynchronizationContext.SetSynchronizationContext(previousContext); }
    }
}
