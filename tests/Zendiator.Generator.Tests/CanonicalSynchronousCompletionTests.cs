using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks.Sources;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
    {
        var result = base.Before(id, request, next, token);
        Mutate("before");
        return result;
    }
    private void Begin(object request, int value, CancellationToken token)
    {
        base.Handle(request, value, token);
        _value = value;
        Mutate("handler");
        if (Mode is "throw" or "throw-oce")
            throw Failure;
        if (Mode == "success")
            _source.SetResult(value);
        if (Mode == "source-oce")
            _source.SetException(Failure);
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
    {
        if (Mode == "pending-oce")
            _source.SetException(Failure);
        else
            _source.SetResult(_value);
    }
    private int Consume(short token)
    {
        Consumptions++;
        ConsumptionAmbient = Ambient.Value;
        Mutate("get-result");
        return _source.GetResult(token);
    }
    int IValueTaskSource<int>.GetResult(short token) => Consume(token);
    void IValueTaskSource.GetResult(short token) => Consume(token);
    public ValueTaskSourceStatus GetStatus(short token) => _source.GetStatus(token);
    public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) =>
        _source.OnCompleted(continuation, state, token, flags);
}

public sealed partial class GeneratorTests
{
    [Fact]
    public async Task Plain_SendAsync_synchronous_before_binding_preserves_generic_DI_overrides_and_contract_fallbacks()
    {
        var source = CapabilitySource(true, false, "s---", false)
            .Replace("s.AddZendiator<Reads>", "s.AddTransient<Reimplemented<Ping>,DerivedReimplemented<Ping>>().AddTransient<Virtual<Ping>,DerivedVirtual<Ping>>().AddZendiator<Reads>")
            .Replace("Stage.Hook<Hook0>()", "Stage.Hook<Reimplemented<Ping>>(),Stage.Hook<Virtual<Ping>>(),Stage.Hook<Explicit<Ping>>(),Stage.Hook<Restricted<Ping>>()") + """
            namespace App{
                public class Reimplemented<T>:ISendRequestTransform<Ping> where T:class,IRequest<int>{
                    protected readonly Probe P;public Reimplemented(Probe p){P=p;}
                    public Ping Before(Ping r,CancellationToken t)=>throw new InvalidOperationException("Wrong base contract");
                }
                public sealed class DerivedReimplemented<T>:Reimplemented<T>,ISendRequestTransform<Ping> where T:class,IRequest<int>{
                    public DerivedReimplemented(Probe p):base(p){}
                    public new Ping Before(Ping r,CancellationToken t)=>P.Before(0,r,new Ping(r.Value+1),t);
                }
                public class Virtual<T>:ISendRequestTransform<Ping> where T:class,IRequest<int>{
                    protected readonly Probe P;public Virtual(Probe p){P=p;}
                    public virtual Ping Before(Ping r,CancellationToken t)=>throw new InvalidOperationException("Wrong virtual dispatch");
                }
                public sealed class DerivedVirtual<T>:Virtual<T> where T:class,IRequest<int>{
                    public DerivedVirtual(Probe p):base(p){}
                    public override Ping Before(Ping r,CancellationToken t)=>P.Before(1,r,new Ping(r.Value+1),t);
                }
                public sealed class Explicit<T>:ISendRequestTransform<Ping> where T:class,IRequest<int>{
                    private readonly Probe _p;public Explicit(Probe p){_p=p;}
                    public Ping Before(Ping r,CancellationToken t)=>throw new InvalidOperationException("Wrong public method");
                    Ping ISendRequestTransform<Ping>.Before(Ping r,CancellationToken t)=>_p.Before(2,r,new Ping(r.Value+1),t);
                }
                public sealed class Restricted<T>:ISendRequestTransform<Ping> where T:class,IRequest<int>{
                    private readonly Probe _p;public Restricted(Probe p){_p=p;}
                    [Obsolete("Call through the contract",true)]
                    public Ping Before(Ping r,CancellationToken t)=>_p.Before(3,r,new Ping(r.Value+1),t);
                }
            }
            """;
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "SyncBeforeBinding_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        var generated = driver.GetRunResult().Results.Single().GeneratedSources;
        var route = Assert.Single(generated, IsCanonicalRoute);
        var model = output.GetSemanticModel(route.SyntaxTree);
        var before = route.SyntaxTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(m => m.Identifier.ValueText.StartsWith("Before", StringComparison.Ordinal)).OrderBy(m => m.Identifier.ValueText, StringComparer.Ordinal).ToArray();
        Assert.Equal(4, before.Length);
        for (var i = 0; i < before.Length; i++)
        {
            var calls = before[i].DescendantNodes().OfType<InvocationExpressionSyntax>().Select(n => model.GetSymbolInfo(n).Symbol).OfType<IMethodSymbol>().Where(m => m.Name == "Before").ToArray();
            Assert.Contains(calls, m => m.ContainingType.TypeKind == TypeKind.Interface);
            Assert.Equal(i < 2, before[i].ToString().Contains("GetType()", StringComparison.Ordinal));
            var direct = calls.Where(m => m.ContainingType.TypeKind == TypeKind.Class).ToArray();
            if (i >= 2)
                Assert.Empty(direct);
            else
                Assert.Equal(i == 0 ? "Reimplemented" : "Virtual", Assert.Single(direct).ContainingType.Name);
        }
        using var bytes = new MemoryStream();
        var emitted = output.Emit(bytes);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(evidence))
        {
            var path = Path.Combine(evidence, "sync-before-binding");
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "input.cs"), source);
            foreach (var item in generated)
                File.WriteAllText(Path.Combine(path, item.HintName), item.SourceText.ToString());
        }
        bytes.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var bridge = assembly.GetType("App.Bridge")!;
        var probe = new CapabilityProbe();
        using var cancellation = probe.Cancellation;
        IServiceCollection services = new ServiceCollection();
        services.AddSingleton(probe);
        bridge.GetMethod("Register")!.Invoke(null, [services]);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService(CanonicalEntryService(assembly.GetType("App.Generated.Reads.Zendiator")!));
        var request = Activator.CreateInstance(assembly.GetType("App.Ping")!, [7]);
        var result = (ValueTask<int>)bridge.GetMethod("Dispatch")!.Invoke(null, [mediator, request, cancellation.Token])!;
        Assert.Equal(11, await result);
        Assert.Equal(new[] { "h0.before", "h1.before", "h2.before", "h3.before", "handler" }, probe.Trace);
        Assert.All(probe.Calls, c => Assert.Equal(cancellation.Token, c.Token));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Plain_SendAsync_forwards_unconsumed_operation_and_synchronous_prefix(bool isVoid, bool withHooks)
    {
        var source = CapabilitySource(true, isVoid, "s---", true).Replace("CapabilityProbe", "SynchronousCompletionProbe");
        if (!withHooks)
            source = source.Replace(",Stage.Hook<Hook0>(),Stage.Hook<Hook1>()", "");
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "SyncCompletion_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        using var bytes = new MemoryStream();
        var emitted = output.Emit(bytes);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(evidence))
        {
            var path = Path.Combine(evidence, "sync-completion-" + isVoid + "-" + withHooks);
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "input.cs"), source);
            foreach (var generated in driver.GetRunResult().Results.Single().GeneratedSources)
                File.WriteAllText(Path.Combine(path, generated.HintName), generated.SourceText.ToString());
        }
        bytes.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var bridge = assembly.GetType("App.Bridge")!;
        var requestType = assembly.GetType("App.Ping")!;
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
            IServiceCollection services = new ServiceCollection();
            services.AddSingleton(probe);
            bridge.GetMethod("Register")!.Invoke(null, [services]);
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var mediator = scope.ServiceProvider.GetRequiredService(CanonicalEntryService(mediatorType));
            var request = Activator.CreateInstance(requestType, [7]);
            var priorAmbient = SynchronousCompletionProbe.Ambient.Value;
            var priorCulture = CultureInfo.CurrentCulture;
            var priorContext = SynchronizationContext.Current;
            try
            {
                SynchronousCompletionProbe.Ambient.Value = "caller";
                if (mode == "pre-cancel")
                    probe.Cancellation.Cancel();
                if (mode is "throw" or "throw-oce" or "pre-cancel")
                {
                    var thrown = Assert.Throws<TargetInvocationException>(() =>
                        bridge.GetMethod("Dispatch")!.Invoke(null, [mediator, request, probe.Cancellation.Token]));
                    if (mode == "pre-cancel")
                    {
                        var canceled = Assert.IsAssignableFrom<OperationCanceledException>(thrown.InnerException);
                        Assert.Equal(probe.Cancellation.Token, canceled.CancellationToken);
                        Assert.Empty(probe.Trace);
                        Assert.Equal("caller", SynchronousCompletionProbe.Ambient.Value);
                    }
                    else
                    {
                        Assert.Same(probe.Failure, thrown.InnerException);
                        Assert.Equal("handler", SynchronousCompletionProbe.Ambient.Value);
                    }
                    Assert.Equal(0, probe.Consumptions);
                    continue;
                }
                var result = bridge.GetMethod("Dispatch")!.Invoke(null, [mediator, request, probe.Cancellation.Token])!;
                Assert.Equal("handler", SynchronousCompletionProbe.Ambient.Value);
                Assert.Equal("fr-FR", CultureInfo.CurrentCulture.Name);
                Assert.NotSame(priorContext, SynchronizationContext.Current);
                Assert.Equal(0, probe.Consumptions);
                if (mode == "fault-oce")
                {
                    Assert.True(isVoid ? ((ValueTask)result).IsFaulted : ((ValueTask<int>)result).IsFaulted);
                    Assert.False(isVoid ? ((ValueTask)result).IsCanceled : ((ValueTask<int>)result).IsCanceled);
                }
                SynchronousCompletionProbe.Ambient.Value = "consumer";
                CultureInfo.CurrentCulture = priorCulture;
                SynchronizationContext.SetSynchronizationContext(priorContext);
                var operation = AsyncTask(result, isVoid);
                if (mode.StartsWith("pending", StringComparison.Ordinal))
                {
                    Assert.False(operation.IsCompleted);
                    Assert.Equal(0, probe.Consumptions);
                    probe.Complete();
                }
                var error = await Record.ExceptionAsync(() => operation.WaitAsync(TimeSpan.FromSeconds(10)));
                if (mode is "success" or "pending")
                {
                    Assert.Null(error);
                    if (!isVoid)
                        Assert.Equal(withHooks ? 9 : 7, await (Task<int>)operation);
                }
                else
                    Assert.Same(probe.Failure, error);
                Assert.Equal(mode is "source-oce" or "pending-oce", operation.IsCanceled);
                Assert.Equal(mode == "fault-oce", operation.IsFaulted);
                var consumesSource = mode is "success" or "pending" or "source-oce" or "pending-oce";
                Assert.Equal(consumesSource ? 1 : 0, probe.Consumptions);
                if (mode is "success" or "source-oce")
                    Assert.Equal("consumer", probe.ConsumptionAmbient);
            }
            finally
            {
                SynchronousCompletionProbe.Ambient.Value = priorAmbient;
                CultureInfo.CurrentCulture = priorCulture;
                SynchronizationContext.SetSynchronizationContext(priorContext);
                probe.Cancellation.Dispose();
            }
        }
    }
}
