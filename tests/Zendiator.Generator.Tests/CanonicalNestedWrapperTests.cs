using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Zendiator.Generator.Tests;

public sealed class NestedWrapperProbe
{
    public readonly List<string> Trace = []; public readonly List<(string Phase, object Request, CancellationToken Token, Exception? Error, SendExitInfo? Exit)> Calls = [];
    public readonly Dictionary<object, string> Created = new(ReferenceEqualityComparer.Instance); public readonly HashSet<object> Released = new(ReferenceEqualityComparer.Instance);
    public readonly Dictionary<string, Exception> Failures = []; public readonly Dictionary<string, int> FactoryDisposals = [];
    public readonly CancellationTokenSource Token = new(), Foreign = new(); public string? Skip, Repeat, NullReceiver, NullNext, CancelNext, Pending; public Exception? PendingError; public IAsyncOnceGate? Gate;
    public void Hit(string phase, object request, CancellationToken token, Exception? error = null, SendExitInfo? exit = null)
    {
        Trace.Add(phase);
        Calls.Add((phase, request, token, error, exit));
        if (Failures.TryGetValue(phase, out var failure))
            throw failure;
    }
    public void Construct(string id, object receiver) => Created.Add(receiver, id);
    public T Create<T>(string id, object request, CancellationToken token, Func<T> make) where T : class
    {
        Hit(id + ".acquire", request, token);
        return NullReceiver == id ? null! : make();
    }
    public ValueTask<T> CreateAsync<T>(string id, object request, CancellationToken token, Func<T> make) where T : class
    {
        Hit(id + ".acquire", request, token);
        if (Pending != id + ".acquire")
            return new(NullReceiver == id ? null! : make());
        var g = new AsyncOnceGate<T>(PendingError == null ? make() : null!);
        Gate = g;
        return g.Task;
    }
    public void Release(string id, object receiver)
    {
        Assert.True(Created.TryGetValue(receiver, out var actual));
        Assert.Equal(id, actual);
        Assert.True(Released.Add(receiver));
        Trace.Add(id + ".cleanup");
        if (Failures.TryGetValue(id + ".cleanup", out var error))
            throw error;
    }
    public ValueTask ReleaseAsync(string id, object receiver)
    {
        Release(id, receiver);
        if (Pending != id + ".cleanup")
            return default;
        var g = new AsyncOnceGate<int>(0);
        Gate = g;
        return g.Void;
    }
    public void DisposeFactory(string id)
    {
        Trace.Add(id + ".factory.dispose");
        FactoryDisposals[id] = FactoryDisposals.GetValueOrDefault(id) + 1;
    }
}

public sealed partial class GeneratorTests
{
    private sealed record NestedWrapperFixture(Assembly Assembly, Type Mediator, Type Request, MethodInfo Register, MethodInfo Dispatch, MethodInfo Reference, string Source);
    private static readonly ConcurrentDictionary<(bool Async, bool Void, bool Two, string W0, string W1, string Handler, bool Hooks), Lazy<NestedWrapperFixture>> NestedWrapperFixtures = new();
    private static string NestedWrapperSource(bool async, bool isVoid, bool two, string w0, string w1, string handler, bool hooks)
    {
        var result = async ? (isVoid ? "ValueTask" : "ValueTask<int>") : (isVoid ? "void" : "int");
        var res = isVoid ? "" : ",int";
        var disposable = async ? "IAsyncDisposable" : "IDisposable";
        string Dispose(string id) => async ? $"public ValueTask DisposeAsync()=>_p.ReleaseAsync(\"{id}\",this);" : $"public void Dispose()=>_p.Release(\"{id}\",this);";
        string Factory(string mode, string type, string name, string id)
        {
            if (mode == "direct")
                return "";
            var a = mode.StartsWith("async-", StringComparison.Ordinal);
            var role = "I" + (a ? "Async" : "") + (mode.EndsWith("owned", StringComparison.Ordinal) ? "Owned" : "Borrowed") + "SessionFactory";
            return $"public sealed class {name}:{role}<Ping,{type}>,IDisposable{{private readonly Probe _p;public {name}(Probe p){{_p=p;}}public {(a ? "ValueTask<" + type + ">" : type)} {(a ? "CreateAsync" : "Create")}(Ping r,CancellationToken t)=>_p.Create{(a ? "Async" : "")}(\"{id}\",r,t,()=>new {type}(_p));public void Dispose()=>_p.DisposeFactory(\"{id}\");}}";
        }
        string Wrapper(int id)
        {
            var n = "w" + id;
            var call = async ? "InvokeAsync" : "Invoke";
            var cfg = async ? ".ConfigureAwait(false)" : "";
            var aw = async ? "await " : "";
            return $$"""
                public sealed class W{{id}}:{{(async ? "IAsyncSendWrapper" : "ISendWrapper")}}<Ping{{res}}>,{{disposable}}{
                    private readonly Probe _p;public W{{id}}(Probe p){_p=p;p.Construct("{{n}}",this);}
                    public {{(async ? "async " : "")}}{{result}} {{call}}<TNext>(Ping r,TNext next,CancellationToken t)where TNext:{{(async ? "IAsyncSendContinuation" : "ISendContinuation")}}<Ping{{res}}>{
                        _p.Hit("{{n}}.before",r,t);if(_p.Skip=="{{n}}"){_p.Hit("{{n}}.skip",r,t);{{(isVoid ? "return;" : "return 91;")}}}
                        var token=_p.CancelNext=="{{n}}"?_p.Foreign.Token:t;
                        {{(isVoid ? "" : "int value;")}}if(_p.Repeat=="{{n}}"){
                            {{(isVoid ? "" : "var a=")}}{{aw}}next.{{call}}(new Ping(10),token){{cfg}};{{(isVoid ? "" : "var b=")}}{{aw}}next.{{call}}(new Ping(20),token){{cfg}};{{(isVoid ? "" : "value=a+b;")}}
                        }else{ {{(isVoid ? "" : "value=")}}{{aw}}next.{{call}}(_p.NullNext=="{{n}}"?null!:r,token){{cfg}}; }
                        _p.Hit("{{n}}.after",r,t);{{(isVoid ? "" : "return value+1;")}}
                    }
                    {{Dispose(n)}}
                }
                """;
        }
        var text = "using System;using System.Threading;using System.Threading.Tasks;using Zendiator;using Zendiator.DependencyInjection;using Microsoft.Extensions.DependencyInjection;using Probe=global::Zendiator.Generator.Tests.NestedWrapperProbe;namespace App{";
        text += $"public sealed record Ping(int Value):{(async ? "IRequest" : "ISyncRequest")}{(isVoid ? "" : "<int>")};[ZendiatorHandler]public sealed class Handler:{(async ? "IRequestHandler" : "ISyncRequestHandler")}<Ping{res}>,{disposable}{{private readonly Probe _p;public Handler(Probe p){{_p=p;p.Construct(\"h\",this);}}public {result} {(async ? "HandleAsync" : "Handle")}(Ping r,CancellationToken t){{_p.Hit(\"h.body\",r,t);{(async ? (isVoid ? "return default;" : "return new(r.Value);") : (isVoid ? "" : "return r.Value;"))}}}{Dispose("h")}}}";
        for (var i = 0; i <= (two ? 2 : 1); i++)
            if (hooks)
            {
                var id = "k" + i;
                text += $"public sealed class K{i}:ISendRequestTransform<Ping>,{(async ? "IAsyncSend" : "ISend")}{(isVoid ? "SuccessObserver<Ping>" : "ResponseTransform<Ping,int>")},{(async ? "IAsyncSend" : "ISend")}ExceptionObserver<Ping>,ISendFinallyObserver<Ping>{{private readonly Probe _p;public K{i}(Probe p){{_p=p;}}public Ping Before(Ping r,CancellationToken t){{_p.Hit(\"{id}.before\",r,t);return new(r.Value+1);}}public {result} {(async ? "AfterAsync" : "After")}(Ping r,{(isVoid ? "" : "int value,")}CancellationToken t){{_p.Hit(\"{id}.after\",r,t);{(async ? (isVoid ? "return default;" : "return new(value+1);") : (isVoid ? "" : "return value+1;"))}}}public {(async ? "ValueTask" : "void")} {(async ? "OnExceptionAsync" : "OnException")}(Ping r,Exception e,CancellationToken t){{_p.Hit(\"{id}.exception\",r,t,e);{(async ? "return default;" : "")}}}public void Finally(Ping r,SendExitInfo e,CancellationToken t)=>_p.Hit(\"{id}.finally\",r,t,exit:e);}}";
            }
        text += Wrapper(0) + Factory(w0, "W0", "WF0", "w0");
        if (two)
            text += Wrapper(1) + Factory(w1, "W1", "WF1", "w1");
        text += Factory(handler, "Handler", "HF", "h");
        string Stage(int i, string mode) => $"Stage.Wrapper<W{i}{(mode == "direct" ? "" : ",WF" + i)}>()";
        var pipeline = (hooks ? "Stage.Hook<K0>()," : "") + Stage(0, w0) + (hooks ? ",Stage.Hook<K1>()" : "") + (two ? "," + Stage(1, w1) + (hooks ? ",Stage.Hook<K2>()" : "") : "");
        text += $"[ZendiatorComposition(\"nested-wrapper\")]public sealed class Reads;public static class Bridge{{public static IServiceCollection Register(IServiceCollection s)=>s.AddZendiator<Reads>(c=>{{c.Handlers<Ping>(DispatchFamily.{(async ? "SendAsync" : "SendSync")},{(handler == "direct" ? "typeof(Handler)" : "HandlerEntry.Session<Handler,HF>()")});c.Pipeline<Ping>(DispatchFamily.{(async ? "SendAsync" : "SendSync")},{pipeline});}});public static object? Dispatch(object m,object r,CancellationToken t){{{(async || !isVoid ? "return " : "")}((global::App.Generated.Reads.IZendiator)m).{(async ? "SendAsync" : "Send")}( (Ping)r,t);{(!async && isVoid ? "return null;" : "")}}}}}}}";
        return text + CanonicalNestedWrapperReference.Source(async, isVoid, two, w0, w1, handler, hooks);
    }
    private static NestedWrapperFixture NestedFixture(bool async, bool isVoid, bool two, string w0, string w1, string handler, bool hooks) => NestedWrapperFixtures.GetOrAdd((async, isVoid, two, w0, w1, handler, hooks), k => new(() =>
    {
        var source = NestedWrapperSource(k.Async, k.Void, k.Two, k.W0, k.W1, k.Handler, k.Hooks);
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "Nested_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        using var bytes = new MemoryStream();
        var emitted = output.Emit(bytes);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        var sources = driver.GetRunResult().Results.Single().GeneratedSources;
        AssertCanonicalOutputRoles(sources);
        var body = CanonicalRouteSource(sources);
        Assert.Contains("struct WrapperNext0", body);
        Assert.Equal(k.Two, body.Contains("struct WrapperNext1", StringComparison.Ordinal));
        Assert.DoesNotContain("struct WrapperNext2", body);
        Assert.DoesNotContain("GetAwaiter", body);
        if (k.Void)
            Assert.DoesNotContain(" response;", body);
        if (!k.Hooks)
        {
            Assert.DoesNotContain("var success", body);
            Assert.DoesNotContain("global::System.Exception? error", body);
        }
        var e = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(e))
        {
            var p = Path.Combine(e, "nested-" + k.Async + "-" + k.Void + "-" + k.Two + "-" + k.W0 + "-" + k.W1 + "-" + k.Handler + "-" + k.Hooks);
            Directory.CreateDirectory(p);
            File.WriteAllText(Path.Combine(p, "input.cs"), source);
            foreach (var s in sources)
                File.WriteAllText(Path.Combine(p, s.HintName), s.SourceText.ToString());
            File.WriteAllBytes(Path.Combine(p, "generated.dll"), bytes.ToArray());
            File.WriteAllText(Path.Combine(p, "compile.json"), JsonSerializer.Serialize(new
            {
                emitted.Success,
                productionEnabled = true,
                candidateOnly = false,
                warnings = 0,
                errors = 0,
                generatorErrors = diagnostics.Length
            }));
        }
        bytes.Position = 0;
        var a = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var b = a.GetType("App.Bridge")!;
        return new(a, a.GetType("App.Generated.Reads.Zendiator")!, a.GetType("App.Ping")!, b.GetMethod("Register")!, b.GetMethod("Dispatch")!, a.GetType("App.Oracle")!.GetMethod("Send")!, source);
    })).Value;
    public static IEnumerable<object[]> NestedWrapperShapes()
    {
        yield return [false, false, true, "direct", "direct", "owned", true];
        yield return [false, true, false, "owned", "direct", "owned", false];
        yield return [false, false, true, "owned", "borrowed", "owned", true];
        yield return [false, true, true, "borrowed", "owned", "borrowed", false];
        yield return [true, false, true, "direct", "direct", "async-owned", true];
        yield return [true, true, false, "async-owned", "direct", "async-owned", false];
        yield return [true, false, true, "async-owned", "owned", "async-owned", true];
        yield return [true, true, true, "owned", "async-borrowed", "borrowed", false];
        yield return [true, false, true, "borrowed", "async-owned", "async-borrowed", true];
        yield return [true, true, true, "async-borrowed", "async-borrowed", "async-owned", true];
    }
    [Theory]
    [MemberData(nameof(NestedWrapperShapes))]
    public async Task Nested_wrappers_session_reference_generation_and_standard_API_preserve_regions(bool async, bool isVoid, bool two, string w0, string w1, string handler, bool hooks)
    {
        var f = NestedFixture(async, isVoid, two, w0, w1, handler, hooks);
        var expected = await RunNested(f, async, isVoid, two, w0, w1, handler, hooks, false, "success");
        Assert.Equal(expected, await RunNested(f, async, isVoid, two, w0, w1, handler, hooks, true, "success"));
    }
    [Theory]
    [InlineData(false, "outer-skip")]
    [InlineData(true, "inner-skip")]
    [InlineData(false, "outer-repeat")]
    [InlineData(true, "inner-repeat")]
    [InlineData(false, "inner-acquire")]
    [InlineData(true, "inner-null")]
    [InlineData(false, "handler-acquire")]
    [InlineData(true, "handler-cleanup")]
    [InlineData(false, "outer-cleanup-priority")]
    [InlineData(false, "null-next")]
    [InlineData(true, "cancel-next")]
    [InlineData(true, "pending-acquire")]
    [InlineData(false, "pending-cleanup")]
    public async Task Nested_wrappers_distinct_next_acquire_and_cleanup_boundaries_preserve_ownership_and_exception_priority(bool isVoid, string scenario)
    {
        var f = NestedFixture(true, isVoid, true, "async-owned", "async-owned", "async-owned", true);
        var expected = await RunNested(f, true, isVoid, true, "async-owned", "async-owned", "async-owned", true, false, scenario);
        Assert.Equal(expected, await RunNested(f, true, isVoid, true, "async-owned", "async-owned", "async-owned", true, true, scenario));
    }
    private static async Task<string> RunNested(NestedWrapperFixture f, bool async, bool isVoid, bool two, string w0, string w1, string handler, bool hooks, bool generated, string scenario)
    {
        var p = new NestedWrapperProbe();
        Exception body = new("body"), acquire = new("acquire"), inner = new("inner"), outer = new("outer");
        if (scenario == "outer-skip")
            p.Skip = "w0";
        if (scenario == "inner-skip")
            p.Skip = "w1";
        if (scenario == "outer-repeat")
            p.Repeat = "w0";
        if (scenario == "inner-repeat")
            p.Repeat = "w1";
        if (scenario == "inner-acquire")
            p.Failures["w1.acquire"] = acquire;
        if (scenario == "inner-null")
            p.NullReceiver = "w1";
        if (scenario == "handler-acquire")
            p.Failures["h.acquire"] = acquire;
        if (scenario == "handler-cleanup")
            p.Failures["h.cleanup"] = inner;
        if (scenario == "outer-cleanup-priority")
        {
            p.Failures["h.body"] = body;
            p.Failures["h.cleanup"] = acquire;
            p.Failures["w1.cleanup"] = inner;
            p.Failures["w0.cleanup"] = outer;
        }
        if (scenario == "null-next")
            p.NullNext = "w1";
        if (scenario == "cancel-next")
        {
            p.CancelNext = "w1";
            p.Foreign.Cancel();
        }
        if (scenario.StartsWith("pending-", StringComparison.Ordinal))
        {
            p.Pending = scenario == "pending-acquire" ? "w1.acquire" : "w1.cleanup";
            p.PendingError = inner;
        }
        string Identity(Exception? e) => e == null ? "" : ReferenceEquals(e, body) ? "body" : ReferenceEquals(e, acquire) ? "acquire" : ReferenceEquals(e, inner) ? "inner" : ReferenceEquals(e, outer) ? "outer" : e.GetType().Name;
        try
        {
            IServiceCollection services = new ServiceCollection();
            services.AddSingleton(p);
            foreach (var name in new[] { "WF0", "WF1", "HF" })
                if (f.Assembly.GetType("App." + name) is { } factory)
                    services.AddScoped(factory, factory);
            f.Register.Invoke(null, [services]);
            await using var provider = services.BuildServiceProvider();
            object? value = null;
            Exception? error = null;
            string snapshot;
            await using (var scope = provider.CreateAsyncScope())
            {
                var request = Activator.CreateInstance(f.Request, [10])!;
                object? Invoke()
                {
                    try
                    {
                        return generated ? f.Dispatch.Invoke(null, [scope.ServiceProvider.GetRequiredService(CanonicalEntryService(f.Mediator)), request, p.Token.Token]) : f.Reference.Invoke(null, [scope.ServiceProvider, request, p.Token.Token]);
                    }
                    catch (TargetInvocationException e)
                    {
                        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException!).Throw();
                        throw;
                    }
                }
                if (async)
                {
                    var task = AsyncTask(Invoke()!, isVoid);
                    if (p.Pending != null)
                    {
                        Assert.False(task.IsCompleted);
                        Assert.NotNull(p.Gate);
                        Assert.Equal(0, p.Gate.Consumptions);
                        p.Gate.Fail(p.PendingError!);
                    }

                    try
                    {
                        await task.WaitAsync(TimeSpan.FromSeconds(10));
                        if (!isVoid)
                            value = await (Task<int>)task;
                    }
                    catch (Exception e)
                    {
                        error = e;
                    }

                    Assert.Equal(error is OperationCanceledException, task.IsCanceled);
                    Assert.Equal(error != null && error is not OperationCanceledException, task.IsFaulted);
                }
                else
                {
                    try
                    {
                        value = Invoke();
                    }
                    catch (Exception e)
                    {
                        error = e;
                    }
                }
                if (p.Gate != null)
                    Assert.Equal(1, p.Gate.Consumptions);
                var expected = scenario switch
                {
                    "inner-acquire" or "handler-acquire" => acquire,
                    "handler-cleanup" or "pending-acquire" or "pending-cleanup" => inner,
                    "outer-cleanup-priority" => outer,
                    _ => null
                };
                if (expected != null)
                    Assert.Same(expected, error);
                else if (scenario == "inner-null")
                    Assert.IsType<InvalidOperationException>(error);
                else if (scenario == "null-next")
                    Assert.IsType<ArgumentNullException>(error);
                else if (scenario == "cancel-next")
                {
                    var oce = Assert.IsAssignableFrom<OperationCanceledException>(error);
                    Assert.Equal(p.Foreign.Token, oce.CancellationToken);
                }
                else
                    Assert.Null(error);
                foreach (var receiver in p.Created)
                {
                    var mode = receiver.Value == "w0" ? w0 : receiver.Value == "w1" ? w1 : handler;
                    Assert.Equal(mode.EndsWith("owned", StringComparison.Ordinal), p.Released.Contains(receiver.Key));
                }
                int Count(string id) => p.Created.Count(x => x.Value == id);
                if (scenario == "outer-skip")
                {
                    Assert.Equal(0, Count("w1"));
                    Assert.Equal(0, Count("h"));
                    Assert.DoesNotContain(p.Calls, c => c.Phase.StartsWith("k1", StringComparison.Ordinal));
                }
                if (scenario == "inner-skip" || scenario is "inner-acquire" or "inner-null" or "null-next" or "cancel-next" or "pending-acquire")
                    Assert.Equal(0, Count("h"));
                if (scenario == "outer-repeat")
                {
                    Assert.Equal(2, Count("w1"));
                    Assert.Equal(2, Count("h"));
                }
                if (scenario == "inner-repeat")
                {
                    Assert.Equal(1, Count("w1"));
                    Assert.Equal(2, Count("h"));
                }
                if (scenario is "inner-acquire" or "inner-null" or "pending-acquire")
                    Assert.Equal(0, Count("w1"));
                if (scenario == "handler-acquire")
                    Assert.Equal(0, Count("h"));
                if (p.Trace.Contains("h.cleanup") && hooks)
                {
                    var index = p.Trace.IndexOf("h.cleanup");
                    var observed = p.Trace.FindIndex(index, x => x == "k2.after" || x == "k2.exception");
                    if (observed >= 0)
                        Assert.True(index < observed);
                }
                if (p.Trace.Contains("w1.cleanup") && hooks)
                {
                    var index = p.Trace.IndexOf("w1.cleanup");
                    var observed = p.Trace.FindIndex(index, x => x == "k1.after" || x == "k1.exception");
                    if (observed >= 0)
                        Assert.True(index < observed);
                }
                if (scenario == "outer-cleanup-priority")
                {
                    Assert.Same(acquire, p.Calls.Single(c => c.Phase == "k2.exception").Error);
                    Assert.Same(inner, p.Calls.Single(c => c.Phase == "k1.exception").Error);
                    Assert.Same(outer, p.Calls.Single(c => c.Phase == "k0.exception").Error);
                }
                foreach (var id in new[] { "w0", "w1", "h" })
                {
                    var acquisitions = p.Calls.Where(c => c.Phase == id + ".acquire").ToArray();
                    var invocations = p.Calls.Where(c => c.Phase == id + (id == "h" ? ".body" : ".before")).ToArray();
                    for (var i = 0; i < invocations.Length && i < acquisitions.Length; i++)
                    {
                        Assert.Same(acquisitions[i].Request, invocations[i].Request);
                        Assert.Equal(acquisitions[i].Token, invocations[i].Token);
                    }
                }
                Assert.All(p.Calls.Where(c => c.Phase.StartsWith("k", StringComparison.Ordinal) && c.Phase.EndsWith(".finally", StringComparison.Ordinal)), c => Assert.Equal(c.Exit!.Value.Error == null, c.Exit.Value.Success));
                var operation = new
                {
                    async,
                    isVoid,
                    two,
                    w0,
                    w1,
                    handler,
                    hooks,
                    scenario,
                    value,
                    error = Identity(error),
                    trace = p.Trace.ToArray(),
                    calls = p.Calls.Select(c => new { c.Phase, value = f.Request.GetProperty("Value")!.GetValue(c.Request), foreign = c.Token == p.Foreign.Token, error = Identity(c.Error), exitError = Identity(c.Exit?.Error), success = c.Exit?.Success }),
                    created = p.Created.Values.GroupBy(x => x).ToDictionary(x => x.Key, x => x.Count()),
                    released = p.Released.Select(x => p.Created[x]).GroupBy(x => x).ToDictionary(x => x.Key, x => x.Count()),
                    consumptions = p.Gate?.Consumptions
                };
                snapshot = JsonSerializer.Serialize(operation);
                WriteNestedHookEvidence("nested-observations", async + "-" + isVoid + "-" + two + "-" + w0 + "-" + w1 + "-" + handler + "-" + hooks + "-" + generated + "-" + scenario, new
                {
                    generated,
                    operation
                });
                p.Failures.Clear();
                p.Pending = null;
            }
            foreach (var receiver in p.Created.Keys.Where(x => !p.Released.Contains(x)).ToArray())
            {
                if (async)
                    await ((IAsyncDisposable)receiver).DisposeAsync();
                else
                    ((IDisposable)receiver).Dispose();
            }
            Assert.Equal(p.Created.Count, p.Released.Count);
            Assert.All(p.FactoryDisposals, x => Assert.Equal(1, x.Value));
            WriteNestedHookEvidence("nested-lifetime", async + "-" + isVoid + "-" + two + "-" + w0 + "-" + w1 + "-" + handler + "-" + hooks + "-" + generated + "-" + scenario, new
            {
                generated,
                scenario,
                created = p.Created.Count,
                released = p.Released.Count,
                p.FactoryDisposals,
                p.Trace
            });
            return snapshot;
        }
        finally
        {
            p.Token.Dispose();
            p.Foreign.Dispose();
        }
    }
    [Fact]
    public void Nested_wrappers_preserve_accepted_single_Wrapper_partial_session_and_DI_generation_only()
    {
        WrapperRuntimeFixture(true, false, "async-owned", "both");
        IntegrationFixture(true, false, "saas", "async-owned", "async-owned", false);
        CapabilityRuntimeFixture(true, false, "saas", false);
    }
}
