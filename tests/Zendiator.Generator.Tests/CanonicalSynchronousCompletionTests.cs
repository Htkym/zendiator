using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks.Sources;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed class SynchronousCompletionProbe : CapabilityProbe, IValueTaskSource<int>, IValueTaskSource
{
    public static readonly AsyncLocal<string?> Ambient = new();
    private ManualResetValueTaskSourceCore<int> _source = new() { RunContinuationsAsynchronously = true };
    private int _value;
    public string Mode = "success";
    public Exception Failure = null!;
    public int Consumptions;
    public string? ConsumptionAmbient;
    private static void Mutate(string phase)
    {
        Ambient.Value = phase;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
    }
    public new R Before<R>(int id, R request, R next, CancellationToken token) where R : class
    { var result = base.Before(id, request, next, token); Mutate("before"); return result; }
    private void Begin(object request, int value, CancellationToken token)
    {
        base.Handle(request, value, token); _value = value; Mutate("handler");
        if (Mode is "throw" or "throw-oce") throw Failure;
        if (Mode == "success") _source.SetResult(value);
        if (Mode == "source-oce") _source.SetException(Failure);
    }
    public new ValueTask<int> HandleAsync(object request, int value, CancellationToken token)
    {
        Begin(request, value, token);
        return Mode == "fault-oce" ? ValueTask.FromException<int>(Failure) : new(this, _source.Version);
    }
    public new ValueTask HandleVoidAsync(object request, CancellationToken token)
    {
        Begin(request, 0, token);
        return Mode == "fault-oce" ? ValueTask.FromException(Failure) : new(this, _source.Version);
    }
    public void Complete()
    { if (Mode == "pending-oce") _source.SetException(Failure); else _source.SetResult(_value); }
    private int Consume(short token)
    {
        Consumptions++; ConsumptionAmbient = Ambient.Value; Mutate("get-result"); return _source.GetResult(token);
    }
    int IValueTaskSource<int>.GetResult(short token) => Consume(token);
    void IValueTaskSource.GetResult(short token) => Consume(token);
    public ValueTaskSourceStatus GetStatus(short token) => _source.GetStatus(token);
    public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) =>
        _source.OnCompleted(continuation, state, token, flags);
}

public sealed partial class GeneratorTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Synchronous_success_retains_context_consumption_and_async_failure_status(bool isVoid, bool withHooks)
    {
        var source = CapabilitySource(true, isVoid, "s---", true).Replace("CapabilityProbe", "SynchronousCompletionProbe");
        if (!withHooks) source = source.Replace(",Stage.Hook<Hook0>(),Stage.Hook<Hook1>()", "");
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "SyncCompletion_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        using var bytes = new MemoryStream(); var emitted = output.Emit(bytes);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(evidence))
        {
            var path = Path.Combine(evidence, "sync-completion-" + isVoid + "-" + withHooks); Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "input.cs"), source);
            foreach (var generated in driver.GetRunResult().Results.Single().GeneratedSources)
                File.WriteAllText(Path.Combine(path, generated.HintName), generated.SourceText.ToString());
        }
        bytes.Position = 0; var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var bridge = assembly.GetType("App.Bridge")!; var requestType = assembly.GetType("App.Ping")!;
        var mediatorType = assembly.GetType("App.Generated.Reads.Zendiator")!;
        using var foreign = new CancellationTokenSource();
        foreach (var mode in new[] { "success", "pending", "throw", "throw-oce", "fault-oce", "source-oce", "pending-oce", "pre-cancel" })
        {
            var probe = new SynchronousCompletionProbe
            {
                Mode = mode,
                Failure = mode.Contains("oce", StringComparison.Ordinal)
                ? new OperationCanceledException("foreign, uncanceled token", foreign.Token) : new InvalidOperationException("body")
            };
            IServiceCollection services = new ServiceCollection(); services.AddSingleton(probe); bridge.GetMethod("Register")!.Invoke(null, [services]);
            await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
            var mediator = scope.ServiceProvider.GetRequiredService(mediatorType); var request = Activator.CreateInstance(requestType, [7]);
            var priorAmbient = SynchronousCompletionProbe.Ambient.Value; var priorCulture = CultureInfo.CurrentCulture; var priorContext = SynchronizationContext.Current;
            Task operation;
            try
            {
                SynchronousCompletionProbe.Ambient.Value = "caller";
                if (mode == "pre-cancel") probe.Cancellation.Cancel();
                var result = bridge.GetMethod("Dispatch")!.Invoke(null, [mediator, request, probe.Cancellation.Token])!;
                Assert.Equal("caller", SynchronousCompletionProbe.Ambient.Value);
                Assert.Same(priorCulture, CultureInfo.CurrentCulture); Assert.Same(priorContext, SynchronizationContext.Current);
                operation = AsyncTask(result, isVoid);
                if (mode.StartsWith("pending", StringComparison.Ordinal))
                { Assert.False(operation.IsCompleted); Assert.Equal(0, probe.Consumptions); probe.Complete(); }
                var error = await Record.ExceptionAsync(() => operation.WaitAsync(TimeSpan.FromSeconds(10)));
                if (mode is "success" or "pending")
                { Assert.Null(error); if (!isVoid) Assert.Equal(withHooks ? 9 : 7, await (Task<int>)operation); }
                else if (mode == "pre-cancel")
                { var canceled = Assert.IsAssignableFrom<OperationCanceledException>(error); Assert.Equal(probe.Cancellation.Token, canceled.CancellationToken); Assert.Empty(probe.Trace); }
                else Assert.Same(probe.Failure, error);
                Assert.Equal(mode.Contains("oce", StringComparison.Ordinal) || mode == "pre-cancel", operation.IsCanceled);
                Assert.Equal(mode == "throw", operation.IsFaulted);
                var consumesSource = mode is "success" or "pending" or "source-oce" or "pending-oce";
                Assert.Equal(consumesSource ? 1 : 0, probe.Consumptions);
                if (consumesSource) Assert.Equal("handler", probe.ConsumptionAmbient);
            }
            finally
            { SynchronousCompletionProbe.Ambient.Value = priorAmbient; CultureInfo.CurrentCulture = priorCulture; SynchronizationContext.SetSynchronizationContext(priorContext); probe.Cancellation.Dispose(); }
        }
    }
}
