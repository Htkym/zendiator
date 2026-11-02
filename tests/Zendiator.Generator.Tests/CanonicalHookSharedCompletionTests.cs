using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed class SharedHookCompletionProbe
{
    public readonly List<string> Trace = [];
    public readonly List<(int Role, SendExitInfo Exit)> Exits = [];
    public readonly List<Exception> Observed = [];
    public readonly Exception Body = new InvalidOperationException("body");
    public readonly Exception Observer = new InvalidOperationException("observer");
    public readonly Exception Final = new OperationCanceledException("finally");
    public readonly List<object> Created = [], Released = [];
    public readonly AsyncOnceGate<int> Gate = new(0);
    public string Scenario = "success";
    public bool Pending;
    public void Construct(int role, object receiver) { Created.Add(receiver); Trace.Add("construct:" + role); }
    public void Release(object receiver) { Assert.DoesNotContain(Released, item => ReferenceEquals(item, receiver)); Released.Add(receiver); }
    public ValueTask<T> Transform<T>(int role, object receiver, T request, CancellationToken token)
    {
        Trace.Add("before:" + role);
        if (role == 0 && Scenario != "success" && Scenario != "finally-success") throw Body;
        return new(request);
    }
    public ValueTask<int> Handle(int role, object receiver, int value) { Trace.Add("handle:" + role); return new(value); }
    public ValueTask<int> After(int role, int value) { Trace.Add("after:" + role); return new(value); }
    public ValueTask Observe(int role, Exception error)
    {
        Trace.Add("observe:" + role); Observed.Add(error);
        if (Scenario == "observer") throw Observer;
        return default;
    }
    public ValueTask Finally(int role, SendExitInfo exit)
    {
        Trace.Add("finally:" + role); Exits.Add((role, exit));
        if (role != 0) return default;
        if (Pending) return Gate.Void;
        if (Scenario is "finally-success" or "finally-failure") throw Final;
        return default;
    }
}

public sealed partial class GeneratorTests
{
    private sealed record SharedHookCompletionFixture(Type Mediator, Action<IServiceCollection> Register,
        Func<object, int, CancellationToken, ValueTask<int>> Dispatch, string Generated);
    private static readonly Lazy<SharedHookCompletionFixture> SharedHookCompletion = new(() =>
    {
        // Three hooks deliberately select the capability/failure-as-data backend, unlike the one/two-hook backend.
        var input = RouteDependencySource("three")
            .Replace("Zendiator.Generator.Tests.RouteDependencyProbe", "Zendiator.Generator.Tests.SharedHookCompletionProbe", StringComparison.Ordinal);
        for (var role = 0; role < 3; role++)
        {
            var name = role == 0 ? "Outer" : role == 1 ? "Middle" : "Inner";
            var start = input.IndexOf("public sealed class " + name + ":", StringComparison.Ordinal);
            var end = input.IndexOf("public void Dispose()", start, StringComparison.Ordinal);
            var callbacks = input[start..end]
                .Replace("=>new(value);", $"=>_p.After({role},value);", StringComparison.Ordinal)
                .Replace("OnExceptionAsync(Ping r,Exception error,CancellationToken t)=>default;", $"OnExceptionAsync(Ping r,Exception error,CancellationToken t)=>_p.Observe({role},error);", StringComparison.Ordinal)
                .Replace("FinallyAsync(Ping r,SendExitInfo exit,CancellationToken t)=>default;", $"FinallyAsync(Ping r,SendExitInfo exit,CancellationToken t)=>_p.Finally({role},exit);", StringComparison.Ordinal);
            input = input[..start] + callbacks + input[end..];
        }
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(
            SyncCompilation(input, "SharedHookCompletion_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        using var bytes = new MemoryStream();
        var emitted = output.Emit(bytes);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        bytes.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var bridge = assembly.GetType("RouteCapture.Bridge")!;
        return new(assembly.GetType("RouteCapture.Generated.Routes.Zendiator")!,
            bridge.GetMethod("Register")!.CreateDelegate<Action<IServiceCollection>>(),
            bridge.GetMethod("Ping")!.CreateDelegate<Func<object, int, CancellationToken, ValueTask<int>>>(),
            string.Join("\n", driver.GetRunResult().Results.Single().GeneratedSources.Select(s => s.SourceText.ToString())));
    });

    [Theory]
    [InlineData("success", false)]
    [InlineData("success", true)]
    [InlineData("body", false)]
    [InlineData("body", true)]
    [InlineData("observer", true)]
    [InlineData("finally-success", false)]
    [InlineData("finally-failure", false)]
    [InlineData("finally-failure", true)]
    public async Task Three_hook_shared_completion_preserves_failure_identity_finally_once_and_pending_consumption(string scenario, bool pending)
    {
        var fixture = SharedHookCompletion.Value;
        for (var role = 0; role < 3; role++)
            Assert.Equal(3, fixture.Generated.Split("await Finally" + role + "(", StringSplitOptions.None).Length - 1);
        Assert.Contains("finalize0:", fixture.Generated, StringComparison.Ordinal);
        var probe = new SharedHookCompletionProbe { Scenario = scenario, Pending = pending };
        IServiceCollection services = new ServiceCollection();
        services.AddSingleton(probe);
        fixture.Register(services);
        using (var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }))
        using (var scope = provider.CreateScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService(CanonicalEntryService(fixture.Mediator));
            var operation = fixture.Dispatch(mediator, 42, default).AsTask();
            if (pending)
            {
                Assert.False(operation.IsCompleted);
                Assert.Equal(0, probe.Gate.Consumptions);
                if (scenario == "finally-failure") probe.Gate.Fail(probe.Final);
                else probe.Gate.Complete();
            }
            var expected = scenario switch
            {
                "success" => null,
                "observer" => probe.Observer,
                "finally-success" or "finally-failure" => probe.Final,
                _ => probe.Body
            };
            if (expected == null) Assert.Equal(42, await operation.WaitAsync(TimeSpan.FromSeconds(10)));
            else Assert.Same(expected, await Assert.ThrowsAnyAsync<Exception>(() => operation.WaitAsync(TimeSpan.FromSeconds(10))));
            Assert.Equal(expected is OperationCanceledException, operation.IsCanceled);
            if (pending) Assert.Equal(1, probe.Gate.Consumptions);
            Assert.Equal(4, probe.Created.Count);
            Assert.Empty(probe.Released);
            var succeeded = scenario is "success" or "finally-success";
            var trace = new List<string> { "construct:0", "construct:1", "construct:2", "construct:3", "before:0" };
            if (succeeded) trace.AddRange(["before:1", "before:2", "handle:3", "after:2", "finally:2", "after:1", "finally:1", "after:0"]);
            else trace.Add("observe:0");
            trace.Add("finally:0");
            Assert.Equal(trace, probe.Trace);
            var outer = Assert.Single(probe.Exits, item => item.Role == 0).Exit;
            Assert.Equal(succeeded, outer.Success);
            Assert.Same(succeeded ? null : scenario == "observer" ? probe.Observer : probe.Body, outer.Error);
            if (!succeeded) Assert.Same(probe.Body, Assert.Single(probe.Observed));
        }
        Assert.Equal(probe.Created.Count, probe.Released.Count);
    }
}
