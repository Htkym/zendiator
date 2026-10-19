using System.Collections.Concurrent;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private static bool CapabilityTyped => true;
    private sealed record CapabilityOptimizationFixture(Assembly Assembly, Type Mediator, Type Request, Type[] Hooks, Type Handler, MethodInfo Register, MethodInfo Dispatch);
    private static readonly ConcurrentDictionary<string, Lazy<CapabilityOptimizationFixture>> CapabilityOptimizationFixtures = new();

    private static string CapabilityThreeSource(string shape, bool isVoid = false)
    {
        var source = isVoid ? AsyncHookSource("two", true) : AsyncInvokerSource(shape);
        if (!isVoid) source = source.Replace("Stage.Hook<Hook<OuterTag>>()", "Stage.Hook<Hook<OuterTag>>(),Stage.Hook<Hook<InnerTag>>()");
        return source.Replace("public sealed class OuterTag;public sealed class InnerTag;", "public sealed class OuterTag;public sealed class InnerTag;public sealed class ThirdTag;")
            .Replace("typeof(T)==typeof(InnerTag)?1:0", "typeof(T)==typeof(ThirdTag)?2:typeof(T)==typeof(InnerTag)?1:0")
            .Replace("Stage.Hook<Hook<InnerTag>>()", "Stage.Hook<Hook<InnerTag>>(),Stage.Hook<Hook<ThirdTag>>()");
    }

    private static CapabilityOptimizationFixture CapabilityThree(string shape = "inherited") => CapabilityOptimizationFixtures.GetOrAdd(shape, key => new(() =>
        CompileCapabilityOptimization(SyncCompilation(CapabilityThreeSource(key), "Capability_" + key), key, "App.Generated.Reads.Zendiator", true))).Value;

    private static CapabilityOptimizationFixture CompileCapabilityOptimization(CSharpCompilation compilation, string label, string mediatorName, bool eligible)
    {
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        Assert.Empty(diagnostics); Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error));
        var sources = driver.GetRunResult().Results.Single().GeneratedSources; AssertCanonicalOutputRoles(sources);
        using var bytes = new MemoryStream(); var emitted = output.Emit(bytes); Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        var binary = bytes.ToArray(); bytes.Position = 0; var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var root = assembly.GetType(mediatorName)!;
        var invoker = root.GetNestedTypes(BindingFlags.NonPublic).SingleOrDefault(t => t.Name.StartsWith("SendInvoker__", StringComparison.Ordinal));
        var execution = eligible && CapabilityTyped
            ? invoker!.GetMethod("InvokeAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
            : Assert.Single(root.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly), m => m.Name.StartsWith("Execute__", StringComparison.Ordinal));
        Assert.Equal(eligible && CapabilityTyped, invoker != null);
        Assert.Equal(eligible ? typeof(PoolingAsyncValueTaskMethodBuilder<>) : null, execution.GetCustomAttribute<AsyncMethodBuilderAttribute>()?.BuilderType);
        var frame = execution.GetCustomAttribute<AsyncStateMachineAttribute>()!.StateMachineType;
        Assert.NotNull(frame.GetMethod("MoveNext", BindingFlags.NonPublic | BindingFlags.Instance));
        if (eligible && CapabilityTyped)
        {
            Assert.True(invoker!.IsValueType); Assert.NotNull(invoker.GetCustomAttribute<IsReadOnlyAttribute>());
            var bridges = invoker.GetMethods(BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly);
            Assert.Equal(13, bridges.Length); Assert.All(bridges, m => Assert.Null(m.GetCustomAttribute<AsyncStateMachineAttribute>()));
            var request = output.GetTypeByMetadataName(mediatorName.StartsWith("S07Pilot", StringComparison.Ordinal) ? "S07Pilot.Async.Query" : "App.Ping")!;
            foreach (var route in sources.Where(s => s.HintName.StartsWith("Zendiator.Route.", StringComparison.Ordinal)))
            {
                var model = output.GetSemanticModel(route.SyntaxTree);
                foreach (var before in route.SyntaxTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Where(m => m.Identifier.ValueText.StartsWith("Before", StringComparison.Ordinal)))
                {
                    var receiver = (INamedTypeSymbol)model.GetDeclaredSymbol(before.ParameterList.Parameters[0])!.Type;
                    var contract = receiver.AllInterfaces.Single(i => i.OriginalDefinition.MetadataName == "IAsyncSendRequestTransform`1" && SymbolEqualityComparer.Default.Equals(i.TypeArguments[0], request));
                    var expected = receiver.FindImplementationForInterfaceMember(contract.GetMembers("BeforeAsync").Single());
                    var concrete = before.DescendantNodes().OfType<InvocationExpressionSyntax>().Select(n => model.GetSymbolInfo(n).Symbol).OfType<IMethodSymbol>().Where(m => m.Name == "BeforeAsync" && m.ContainingType.TypeKind == TypeKind.Class).ToArray();
                    if (label == "explicit") Assert.Empty(concrete);
                    else Assert.True(SymbolEqualityComparer.Default.Equals(expected, Assert.Single(concrete)));
                }
            }
        }
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(evidence))
        {
            var folder = Path.Combine(evidence, "capability-" + label); Directory.CreateDirectory(folder);
            foreach (var tree in compilation.SyntaxTrees) File.WriteAllText(Path.Combine(folder, "input-" + compilation.SyntaxTrees.ToList().IndexOf(tree) + ".cs"), tree.ToString());
            foreach (var source in sources) File.WriteAllText(Path.Combine(folder, source.HintName), source.SourceText.ToString());
            File.WriteAllBytes(Path.Combine(folder, "generated.dll"), binary);
            static IEnumerable<Type> Types(Type type) => new[] { type }.Concat(type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).SelectMany(Types));
            var methods = Types(root).SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                .Select(m => new { owner = m.DeclaringType!.FullName, m.Name, token = m.MetadataToken,
                    stateMachine = m.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType.FullName,
                    stateFields = m.DeclaringType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Select(f => new { f.Name, type = f.FieldType.FullName }) }).ToArray();
            File.WriteAllText(Path.Combine(folder, "consumer.json"), JsonSerializer.Serialize(new { root = root.FullName, methods,
                assembly = assembly.GetName().Name, moduleVersionId = root.Module.ModuleVersionId, bytes = binary.Length,
                sha256 = Convert.ToHexString(SHA256.HashData(binary)), eligible, typed = eligible && CapabilityTyped, builder = execution.GetCustomAttribute<AsyncMethodBuilderAttribute>()?.BuilderType.FullName }));
        }
        if (mediatorName.StartsWith("S07Pilot", StringComparison.Ordinal))
            return new(assembly, root, assembly.GetType("S07Pilot.Async.Query")!, [], assembly.GetType("S07Pilot.Async.Handler")!, assembly.GetType("S07Pilot.Async.Registration")!.GetMethod("Add")!, root.GetMethod("SendAsync")!);
        var hook = assembly.GetType("App.Hook`1")!;
        return new(assembly, root, assembly.GetType("App.Ping")!, new[] { "OuterTag", "InnerTag", "ThirdTag" }.Select(n => hook.MakeGenericType(assembly.GetType("App." + n)!)).ToArray(),
            assembly.GetType("App.Handler")!, assembly.GetType("App.Bridge")!.GetMethod("Register")!, assembly.GetType("App.Bridge")!.GetMethod("Dispatch")!);
    }

    private static object InvokeCapability(CapabilityOptimizationFixture f, bool generated, IServiceProvider provider, object request, CancellationToken token)
    {
        try
        {
            if (generated) return f.Dispatch.Invoke(null, [provider.GetRequiredService(f.Mediator), request, token])!;
            return typeof(GeneratorTests).GetMethod(nameof(CapabilityThreeReference), BindingFlags.Static | BindingFlags.NonPublic)!
                .MakeGenericMethod([f.Request, .. f.Hooks, f.Handler]).Invoke(null, [provider, request, token])!;
        }
        catch (TargetInvocationException e) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException!).Throw(); throw; }
    }

    private static async ValueTask<int> CapabilityThreeReference<R, A, B, C, H>(IServiceProvider p, R request, CancellationToken token)
        where R : class, IRequest<int>
        where A : class, IAsyncSendRequestTransform<R>, IAsyncSendResponseTransform<R, int>, IAsyncSendExceptionObserver<R>, IAsyncSendFinallyObserver<R>
        where B : class, IAsyncSendRequestTransform<R>, IAsyncSendResponseTransform<R, int>, IAsyncSendExceptionObserver<R>, IAsyncSendFinallyObserver<R>
        where C : class, IAsyncSendRequestTransform<R>, IAsyncSendResponseTransform<R, int>, IAsyncSendExceptionObserver<R>, IAsyncSendFinallyObserver<R>
        where H : class, IRequestHandler<R, int>
    {
        ArgumentNullException.ThrowIfNull(request); token.ThrowIfCancellationRequested();
        var a = p.GetRequiredService<A>(); var ra = request; var oka = false; Exception? ea = null;
        try
        {
            ra = await a.BeforeAsync(ra, token).ConfigureAwait(false); ArgumentNullException.ThrowIfNull(ra, "request"); token.ThrowIfCancellationRequested();
            var b = p.GetRequiredService<B>(); var rb = ra; var okb = false; Exception? eb = null; int result;
            try
            {
                rb = await b.BeforeAsync(rb, token).ConfigureAwait(false); ArgumentNullException.ThrowIfNull(rb, "request"); token.ThrowIfCancellationRequested();
                var c = p.GetRequiredService<C>(); var rc = rb; var okc = false; Exception? ec = null;
                try
                {
                    rc = await c.BeforeAsync(rc, token).ConfigureAwait(false); ArgumentNullException.ThrowIfNull(rc, "request"); token.ThrowIfCancellationRequested();
                    result = await p.GetRequiredService<H>().HandleAsync(rc, token).ConfigureAwait(false);
                    result = await c.AfterAsync(rc, result, token).ConfigureAwait(false); okc = true;
                }
                catch (Exception e) { ec = e; try { await c.OnExceptionAsync(rc, e, token).ConfigureAwait(false); } catch (Exception replacement) { ec = replacement; throw; } throw; }
                finally { await c.FinallyAsync(rc, new(okc, ec), token).ConfigureAwait(false); }
                result = await b.AfterAsync(rb, result, token).ConfigureAwait(false); okb = true;
            }
            catch (Exception e) { eb = e; try { await b.OnExceptionAsync(rb, e, token).ConfigureAwait(false); } catch (Exception replacement) { eb = replacement; throw; } throw; }
            finally { await b.FinallyAsync(rb, new(okb, eb), token).ConfigureAwait(false); }
            result = await a.AfterAsync(ra, result, token).ConfigureAwait(false); oka = true; return result;
        }
        catch (Exception e) { ea = e; try { await a.OnExceptionAsync(ra, e, token).ConfigureAwait(false); } catch (Exception replacement) { ea = replacement; throw; } throw; }
        finally { await a.FinallyAsync(ra, new(oka, ea), token).ConfigureAwait(false); }
    }

    [Theory]
    [InlineData("success")]
    [InlineData("handler-pending")]
    [InlineData("pre-cancel")]
    [InlineData("inner-cancel")]
    [InlineData("inner-finally")]
    public async Task Capability_three_hooks_preserve_reference_unwind_once_consumption_and_DI(string scenario)
    {
        var f = CapabilityThree(); string[]? expected = null;
        foreach (var generated in new[] { false, true })
        {
            var p = new AsyncHookProbe();
            if (scenario is "handler-pending" or "inner-finally") p.Pending = "handler";
            if (scenario == "pre-cancel") p.Cancellation.Cancel();
            if (scenario == "inner-cancel") p.CancelPhase = "h1.before";
            var failure = new FormatException("inner finally"); if (scenario == "inner-finally") p.Failures.Add("h2.finally", failure);
            IServiceCollection services = new ServiceCollection(); services.AddSingleton(p); f.Register.Invoke(null, [services]);
            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            await using (var scope = provider.CreateAsyncScope())
            {
                var task = ((ValueTask<int>)InvokeCapability(f, generated, scope.ServiceProvider, Activator.CreateInstance(f.Request, [10])!, p.Cancellation.Token)).AsTask();
                if (p.Pending != null) { Assert.False(task.IsCompleted); Assert.Equal(0, Assert.Single(p.Gates).Consumptions); p.Gates[0].Complete(); }
                if (scenario is "pre-cancel" or "inner-cancel") { await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(10))); Assert.True(task.IsCanceled); }
                else if (scenario == "inner-finally")
                {
                    Assert.Same(failure, await Assert.ThrowsAsync<FormatException>(() => task.WaitAsync(TimeSpan.FromSeconds(10))));
                    Assert.DoesNotContain("h1.after", p.Trace); Assert.DoesNotContain("h0.after", p.Trace);
                    Assert.All(p.Calls.Where(c => c.Phase is "h1.finally" or "h0.finally"), c => { Assert.False(c.Exit!.Value.Success); Assert.Same(failure, c.Exit.Value.Error); });
                }
                else Assert.Equal(16, await task.WaitAsync(TimeSpan.FromSeconds(10)));
                Assert.Empty(p.ReleasedHooks); Assert.Equal(0, p.HandlerDisposals); Assert.All(p.Gates, g => Assert.Equal(1, g.Consumptions));
                if (scenario == "inner-cancel") { Assert.DoesNotContain("h2.construct", p.Trace); Assert.DoesNotContain("handler.construct", p.Trace); }
            }
            Assert.Equal(p.Hooks.Count, p.ReleasedHooks.Count); Assert.All(p.Hooks, h => Assert.Single(p.ReleasedHooks, x => ReferenceEquals(x, h)));
            if (expected == null) expected = p.Trace.ToArray(); else Assert.Equal(expected, p.Trace);
        }
    }

    [Fact]
    public async Task Capability_three_hook_overlapping_operations_keep_separate_frames_and_DI_receivers()
    {
        var f = CapabilityThree();
        foreach (var generated in new[] { false, true })
        {
            var p = new AsyncHookProbe { Pending = "handler" }; IServiceCollection services = new ServiceCollection(); services.AddSingleton(p); f.Register.Invoke(null, [services]);
            await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
            Task<int> Start(int value) => ((ValueTask<int>)InvokeCapability(f, generated, scope.ServiceProvider, Activator.CreateInstance(f.Request, [value])!, p.Cancellation.Token)).AsTask();
            var first = Start(10); var second = Start(20); Assert.False(first.IsCompleted); Assert.False(second.IsCompleted); Assert.Equal(2, p.Gates.Count);
            using var foreign = new CancellationTokenSource(); var cancellation = new OperationCanceledException("second", foreign.Token); p.Gates[1].Fail(cancellation);
            Assert.Same(cancellation, await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second.WaitAsync(TimeSpan.FromSeconds(10)))); Assert.True(second.IsCanceled); Assert.False(first.IsCompleted);
            p.Gates[0].Complete(); Assert.Equal(16, await first.WaitAsync(TimeSpan.FromSeconds(10)));
            var followup = Start(30); p.Gates[2].Complete(); Assert.Equal(36, await followup.WaitAsync(TimeSpan.FromSeconds(10))); Assert.All(p.Gates, g => Assert.Equal(1, g.Consumptions));
            Assert.Equal(3, p.Hooks.Count); Assert.Empty(p.ReleasedHooks);
        }
    }

    [Theory]
    [InlineData("reimplemented")]
    [InlineData("explicit")]
    public async Task Capability_three_hook_callback_binding_keeps_the_selected_contract(string shape)
    {
        var f = CapabilityThree(shape); var p = new AsyncHookProbe { Pending = "handler" }; IServiceCollection services = new ServiceCollection(); services.AddSingleton(p); f.Register.Invoke(null, [services]);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var task = ((ValueTask<int>)InvokeCapability(f, true, scope.ServiceProvider, Activator.CreateInstance(f.Request, [10])!, p.Cancellation.Token)).AsTask();
        Assert.False(task.IsCompleted); Assert.Single(p.Gates).Complete(); Assert.Equal(16, await task.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task Capability_pilot_retains_the_original_three_hook_input_oracle_and_pending_source()
    {
        var names = new[] { "Infrastructure.cs", "AsyncCase.cs", "AsyncReference.cs" };
        var assembly = typeof(GeneratorTests).Assembly;
        using var resource = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(x => x.EndsWith("CapabilityPilot.zip", StringComparison.Ordinal)))!;
        using var archive = new ZipArchive(resource);
        var inputs = names.Select(n => { using var stream = archive.GetEntry(n)!.Open(); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }).ToArray();
        var compilation = SyncCompilation("global using System;global using System.IO;global using System.Linq;global using System.Collections.Generic;global using System.Threading;global using System.Threading.Tasks;[assembly:System.Reflection.AssemblyVersion(\"1.0.0.0\")]", "Pilot")
            .AddSyntaxTrees(inputs.Select(s => CSharpSyntaxTree.ParseText(s, SyncParseOptions)));
        var f = CompileCapabilityOptimization(compilation, "pilot", "S07Pilot.Async.Generated.Routes.Zendiator", true); string[]? expected = null;
        foreach (var generated in new[] { false, true })
        {
            var probeType = f.Assembly.GetType("S07Pilot.Probe")!; var sourceType = f.Assembly.GetType("S07Pilot.PendingSource")!;
            var probe = Activator.CreateInstance(probeType)!; probeType.GetField("Recording")!.SetValue(probe, true);
            using var source = (IDisposable)Activator.CreateInstance(sourceType)!; sourceType.GetField("Automatic")!.SetValue(source, false);
            IServiceCollection services = new ServiceCollection(); services.AddSingleton(probeType, probe); services.AddSingleton(sourceType, source); f.Register.Invoke(null, [services]);
            using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            using (var scope = provider.CreateScope())
            {
                var mediator = generated ? scope.ServiceProvider.GetRequiredService(f.Mediator) : Activator.CreateInstance(f.Assembly.GetType("S07Pilot.Async.Reference")!, scope.ServiceProvider)!;
                using var cancellation = new CancellationTokenSource(); var request = Activator.CreateInstance(f.Request, [7])!;
                var task = ((ValueTask<int>)mediator.GetType().GetMethod("SendAsync")!.Invoke(mediator, [request, cancellation.Token])!).AsTask();
                await ((TaskCompletionSource)sourceType.GetField("Registered")!.GetValue(source)!).Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.False(task.IsCompleted);
                sourceType.GetMethod("QueueCompletion")!.Invoke(source, null); Assert.Equal(11, await task.WaitAsync(TimeSpan.FromSeconds(10))); sourceType.GetMethod("Drain")!.Invoke(source, null);
                Assert.True((bool)sourceType.GetProperty("IsBalanced")!.GetValue(source)!);
                foreach (var n in new[] { "Preparations", "Registrations", "Completions", "Consumptions" }) Assert.Equal(1L, sourceType.GetField(n)!.GetValue(source));
                Assert.All(((System.Collections.IEnumerable)probeType.GetField("Requests")!.GetValue(probe)!).Cast<object>(), r => Assert.Same(request, r));
                Assert.All(((System.Collections.IEnumerable)probeType.GetField("Tokens")!.GetValue(probe)!).Cast<CancellationToken>(), t => Assert.Equal(cancellation.Token, t));
                Assert.Empty(((System.Collections.IEnumerable)probeType.GetField("Disposed")!.GetValue(probe)!).Cast<string>());
            }
            var trace = ((System.Collections.IEnumerable)probeType.GetField("Trace")!.GetValue(probe)!).Cast<string>().ToArray();
            Assert.Equal(new[] { "h0.before", "h1.before", "h2.before", "handler", "h2.after", "h2.finally", "h1.after", "h1.finally", "h0.after", "h0.finally" }, trace);
            Assert.Equal(new[] { "handler", "h2", "h1", "h0" }, ((System.Collections.IEnumerable)probeType.GetField("Disposed")!.GetValue(probe)!).Cast<string>());
            if (expected == null) expected = trace; else Assert.Equal(expected, trace);
        }
    }

    [Theory]
    [InlineData("void")]
    [InlineData("partial")]
    public void Capability_three_hook_ineligible_routes_keep_their_original_backend(string shape)
    {
        var source = CapabilityThreeSource("inherited", shape == "void");
        if (shape == "partial") source = source.Replace(",IAsyncSendFinallyObserver<Ping>", "").Replace("public ValueTask FinallyAsync(Ping r,SendExitInfo e,CancellationToken t){_probe.CheckToken(t);return _probe.Finally(_id,r,e);}", "");
        CompileCapabilityOptimization(SyncCompilation(source, "CapabilityFallback_" + shape), shape, "App.Generated.Reads.Zendiator", false);
    }

    [Fact]
    public void Capability_three_hook_projection_caches_body_edits_and_invalidates_receiver_binding()
    {
        var source = CapabilityThreeSource("inherited"); var driver = SyncDriver().RunGenerators(SyncCompilation(source, "CapabilityIncremental"));
        var original = CanonicalRouteSource(driver.GetRunResult().Results.Single().GeneratedSources);
        driver = driver.RunGenerators(SyncCompilation(source.Replace("r.Value)", "r.Value+1)"), "CapabilityIncremental"));
        Assert.Equal(original, CanonicalRouteSource(driver.GetRunResult().Results.Single().GeneratedSources));
        Assert.All(driver.GetRunResult().Results.Single().TrackedSteps["CanonicalRouteOutputFile"].SelectMany(s => s.Outputs), o => Assert.True(o.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged));
        driver = driver.RunGenerators(SyncCompilation(source.Replace("public sealed class Hook<T>", "public class Hook<T>"), "CapabilityIncremental"));
        Assert.NotEqual(original, CanonicalRouteSource(driver.GetRunResult().Results.Single().GeneratedSources));
        Assert.Contains("GetType()", CanonicalRouteSource(driver.GetRunResult().Results.Single().GeneratedSources));
    }
}
