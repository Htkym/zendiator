using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Zendiator.Tests.Contracts;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    public sealed class ZeroFastPathProbe
    {
        public static readonly AsyncLocal<string?> Ambient = new();
        public int Starts, Moves, Reads, Disposals;
        public Func<int, ValueTask<bool>> Move = _ => new(false);
        public Func<int, int> Read = index => index;
        public Func<ValueTask> Cleanup = () => default;

        public IAsyncEnumerable<int> Open()
        {
            Starts++;
            return new Source(this);
        }

        private sealed class Source(ZeroFastPathProbe probe) : IAsyncEnumerable<int>
        {
            public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken token = default) => new Cursor(probe);
        }

        private sealed class Cursor(ZeroFastPathProbe probe) : IAsyncEnumerator<int>
        {
            public int Current { get { probe.Reads++; return probe.Read(probe.Moves); } }
            public ValueTask<bool> MoveNextAsync() => probe.Move(++probe.Moves);
            public ValueTask DisposeAsync() { probe.Disposals++; return probe.Cleanup(); }
        }
    }

    private sealed record PublicZeroFastPathFixture(Action<IServiceCollection> Register,
        Func<IServiceProvider, IAsyncEnumerable<int>> Stream);

    private static readonly Lazy<PublicZeroFastPathFixture> PublicZeroFastPath = new(CompilePublicZeroFastPath);

    private static PublicZeroFastPathFixture CompilePublicZeroFastPath()
    {
        const string source = """
            using System; using System.Collections.Generic; using System.Threading;
            using Zendiator; using Zendiator.DependencyInjection; using Microsoft.Extensions.DependencyInjection;
            namespace App {
                public sealed record Request : IStreamRequest<int>;
                [ZendiatorComposition("reads")] public sealed class Reads;
                [ZendiatorHandler] public sealed class H(global::Zendiator.Generator.Tests.GeneratorTests.ZeroFastPathProbe p)
                    : IStreamRequestHandler<Request,int> {
                    public IAsyncEnumerable<int> HandleAsync(Request request,CancellationToken token)=>p.Open();
                }
                public static class Entry {
                    public static void Register(IServiceCollection services)=>services.AddZendiator<Reads>(c=>{
                        c.Handlers<Request>(DispatchFamily.Stream,typeof(H)); c.Pipeline<Request>(DispatchFamily.Stream);
                    });
                    public static IAsyncEnumerable<int> Stream(IServiceProvider provider)=>
                        provider.GetRequiredService<global::App.Generated.Reads.IZendiator>().StreamAsync(new Request());
                }
            }
            """;
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(
            SyncCompilation(source, "PublicZeroFastPath_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        using var bytes = new MemoryStream();
        var emitted = output.Emit(bytes);
        var generated = driver.GetRunResult().Results.Single().GeneratedSources;
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_STREAM_PUBLIC_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(evidence))
        {
            var folder = Path.Combine(evidence, "zero-fast-path");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "input.cs"), source);
            foreach (var file in generated) File.WriteAllText(Path.Combine(folder, file.HintName), file.SourceText.ToString());
            File.WriteAllText(Path.Combine(folder, "compile.json"), JsonSerializer.Serialize(new
            {
                emitted.Success,
                diagnostics = diagnostics.Select(d => new { d.Id, severity = d.Severity.ToString(), message = d.GetMessage() }),
                compiler = emitted.Diagnostics.Select(d => new { d.Id, severity = d.Severity.ToString(), message = d.GetMessage() }),
                sources = generated.Select(f => f.HintName)
            }));
        }
        Assert.Empty(diagnostics);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        Assert.Empty(emitted.Diagnostics.Where(d => d.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error));
        Assert.Single(generated,
            f => f.HintName.StartsWith("Zendiator.StreamCanonical.", StringComparison.Ordinal));
        bytes.Position = 0;
        var entry = AssemblyLoadContext.Default.LoadFromStream(bytes).GetType("App.Entry")!;
        return new(entry.GetMethod("Register")!.CreateDelegate<Action<IServiceCollection>>(),
            entry.GetMethod("Stream")!.CreateDelegate<Func<IServiceProvider, IAsyncEnumerable<int>>>());
    }

    private static ServiceProvider ZeroFastPathProvider(ZeroFastPathProbe probe)
    {
        var services = new ServiceCollection();
        services.AddSingleton(probe);
        PublicZeroFastPath.Value.Register(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    [Fact]
    public async Task Stream_public_zero_hook_completed_source_results_consume_once_and_isolate_synchronous_context()
    {
        var gates = new[] { new ContractOnce<bool>(), new ContractOnce<bool>(), new ContractOnce<bool>() };
        gates[0].Complete(true); gates[1].Complete(true); gates[2].Complete(false);
        var probe = new ZeroFastPathProbe
        {
            Move = index => { ZeroFastPathProbe.Ambient.Value = "producer-" + index; return gates[index - 1].Task; },
            Read = index =>
            {
                Assert.Equal("producer-" + index, ZeroFastPathProbe.Ambient.Value);
                ZeroFastPathProbe.Ambient.Value = "getter-" + index;
                return index * 10;
            },
            Cleanup = () => { ZeroFastPathProbe.Ambient.Value = "cleanup"; return default; }
        };
        using var provider = ZeroFastPathProvider(probe);
        using var scope = provider.CreateScope();
        var cursor = PublicZeroFastPath.Value.Stream(scope.ServiceProvider).GetAsyncEnumerator();
        var saved = ZeroFastPathProbe.Ambient.Value;
        try
        {
            for (var index = 1; index <= 3; index++)
            {
                var caller = "caller-" + index;
                ZeroFastPathProbe.Ambient.Value = caller;
                var move = cursor.MoveNextAsync();
                Assert.Equal(caller, ZeroFastPathProbe.Ambient.Value);
                Assert.Equal(index < 3, await move);
                Assert.Equal(caller, ZeroFastPathProbe.Ambient.Value);
                if (index < 3)
                {
                    Assert.Equal(index * 10, cursor.Current);
                    Assert.Equal(index * 10, cursor.Current);
                    Assert.Equal(index, probe.Reads);
                }
            }
            Assert.All(gates, gate => Assert.Equal(1, gate.Consumptions));
            Assert.Equal(1, probe.Starts); Assert.Equal(3, probe.Moves); Assert.Equal(2, probe.Reads);
            Assert.Equal(1, probe.Disposals);
        }
        finally { await cursor.DisposeAsync(); ZeroFastPathProbe.Ambient.Value = saved; }
        Assert.Equal(1, probe.Disposals);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Stream_public_zero_hook_pending_second_move_consumes_once_and_publishes_only_a_real_item(bool hasItem)
    {
        var first = new ContractOnce<bool>(); first.Complete(true);
        var second = new ContractOnce<bool>();
        var probe = new ZeroFastPathProbe
        {
            Move = index => { ZeroFastPathProbe.Ambient.Value = "producer-" + index; return index == 1 ? first.Task : second.Task; },
            Read = index => { Assert.Equal("producer-" + index, ZeroFastPathProbe.Ambient.Value); return index; }
        };
        using var provider = ZeroFastPathProvider(probe);
        using var scope = provider.CreateScope();
        using var cancellation = new CancellationTokenSource();
        var cursor = PublicZeroFastPath.Value.Stream(scope.ServiceProvider).GetAsyncEnumerator(cancellation.Token);
        var saved = ZeroFastPathProbe.Ambient.Value;
        try
        {
            Assert.True(await cursor.MoveNextAsync()); Assert.Equal(1, cursor.Current);
            ZeroFastPathProbe.Ambient.Value = "caller-second";
            var move = cursor.MoveNextAsync().AsTask();
            Assert.Equal("caller-second", ZeroFastPathProbe.Ambient.Value);
            Assert.False(move.IsCompleted); Assert.Equal(0, second.Consumptions); Assert.Equal(1, probe.Reads);
            cancellation.Cancel();
            await Task.Run(() => { ZeroFastPathProbe.Ambient.Value = "foreign-releaser"; second.Complete(hasItem); });
            Assert.Equal(hasItem, await move.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal("caller-second", ZeroFastPathProbe.Ambient.Value);
            Assert.Equal(1, first.Consumptions); Assert.Equal(1, second.Consumptions);
            Assert.Equal(hasItem ? 2 : 1, probe.Reads);
            if (hasItem) Assert.Equal(2, cursor.Current);
            else { Assert.Equal(1, probe.Disposals); Assert.False(await cursor.MoveNextAsync()); }
        }
        finally { await cursor.DisposeAsync(); ZeroFastPathProbe.Ambient.Value = saved; }
        Assert.Equal(2, probe.Moves); Assert.Equal(1, probe.Disposals);
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [InlineData(1, false)]
    public async Task Stream_public_zero_hook_Current_failure_closes_once_and_preserves_cleanup_error_priority(int failingMove, bool cleanupFails)
    {
        var first = new ContractOnce<bool>(); first.Complete(true);
        var second = new ContractOnce<bool>(); second.Complete(true);
        var cleanup = new ContractOnce<bool>();
        var currentError = new FormatException("source-current");
        var cleanupError = new InvalidOperationException("source-cleanup");
        var probe = new ZeroFastPathProbe
        {
            Move = index => index == 1 ? first.Task : second.Task,
            Read = index => index < failingMove ? 1 : throw currentError,
            Cleanup = () => cleanup.Void
        };
        using var provider = ZeroFastPathProvider(probe);
        using var scope = provider.CreateScope();
        var cursor = PublicZeroFastPath.Value.Stream(scope.ServiceProvider).GetAsyncEnumerator();
        Task<bool>? pendingMove = null;
        var cleanupReleased = false;
        try
        {
            if (failingMove == 2)
            { Assert.True(await cursor.MoveNextAsync()); Assert.Equal(1, cursor.Current); }
            var move = pendingMove = cursor.MoveNextAsync().AsTask();
            Assert.False(move.IsCompleted); Assert.Equal(1, probe.Disposals); Assert.Equal(0, cleanup.Consumptions);
            cleanupReleased = true;
            if (cleanupFails) cleanup.Fail(cleanupError); else cleanup.Complete(true);
            var error = await Record.ExceptionAsync(async () => { await move.WaitAsync(TimeSpan.FromSeconds(5)); });
            Assert.Same(cleanupFails ? cleanupError : currentError, error);
            Assert.True(move.IsFaulted); Assert.False(move.IsCanceled);
            Assert.Equal(1, first.Consumptions); Assert.Equal(failingMove == 2 ? 1 : 0, second.Consumptions); Assert.Equal(1, cleanup.Consumptions);
            Assert.Throws<InvalidOperationException>(() => cursor.Current);
            Assert.False(await cursor.MoveNextAsync());
            Assert.Equal(failingMove, probe.Moves); Assert.Equal(failingMove, probe.Reads);
        }
        finally
        {
            if (!cleanupReleased) cleanup.Complete(true);
            if (pendingMove is not null)
            {
                try { await pendingMove.WaitAsync(TimeSpan.FromSeconds(5)); }
                catch { }
            }
            await cursor.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.Equal(1, probe.Disposals);
    }
}
