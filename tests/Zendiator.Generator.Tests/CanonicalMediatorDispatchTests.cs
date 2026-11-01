using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    [Fact]
    public void Mediator_call_site_dispatch_preserves_constrained_mutable_value_receivers()
    {
        var source = CapabilitySource(true, false, "s---", false) + """
            namespace App {
                public struct StatefulFake:global::App.Generated.Reads.IZendiator {
                    public int Calls;
                    public System.Threading.Tasks.ValueTask<int> SendAsync(Ping request,System.Threading.CancellationToken token=default)=>new(++Calls);
                }
                public static class GenericDispatchCases {
                    private static System.Threading.Tasks.ValueTask<int> Through<T>(ref T mediator)
                        where T:global::App.Generated.Reads.IZendiator=>mediator.SendAsync(new Ping(1));
                    public static int[] Mutable() {
                        var mediator=new StatefulFake();
                        var result=Through(ref mediator).GetAwaiter().GetResult();
                        return new[]{result,mediator.Calls};
                    }
                }
            }
            """;
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "MediatorValueDispatch_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        using var bytes = new MemoryStream();
        var emitted = output.Emit(bytes);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        bytes.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var result = assembly.GetType("App.GenericDispatchCases")!.GetMethod("Mutable")!.Invoke(null, null);
        Assert.Equal(new[] { 1, 1 }, Assert.IsType<int[]>(result));
    }

    [Fact]
    public async Task Mediator_call_site_dispatch_preserves_custom_interface_implementations_and_unrelated_calls()
    {
        var source = CapabilitySource(true, false, "s---", false) + """
            namespace App {
                public sealed class Other { public int SendAsync(Ping request)=>17; }
                public sealed class Fake:global::App.Generated.Reads.IZendiator {
                    public System.Threading.Tasks.ValueTask<int> SendAsync(Ping request,System.Threading.CancellationToken token=default)=>new(99);
                }
                public static class DispatchCases {
                    private static async System.Threading.Tasks.Task<int> Through(global::App.Generated.Reads.IZendiator mediator)=>await mediator.SendAsync(new Ping(1));
                    public static System.Threading.Tasks.Task<int> Custom()=>Through(new Fake());
                    public static System.Threading.Tasks.Task<int> Null()=>Through(null!);
                    public static int Unrelated()=>new Other().SendAsync(new Ping(1));
                }
            }
            """;
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "MediatorDispatch_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        Assert.Contains(driver.GetRunResult().Results.Single().GeneratedSources, f => f.HintName.StartsWith("Zendiator.Dispatch.", StringComparison.Ordinal));
        using var bytes = new MemoryStream();
        var emitted = output.Emit(bytes);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        bytes.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var cases = assembly.GetType("App.DispatchCases")!;
        Assert.Equal(99, await (Task<int>)cases.GetMethod("Custom")!.Invoke(null, null)!);
        Assert.Equal(17, cases.GetMethod("Unrelated")!.Invoke(null, null));
        await Assert.ThrowsAsync<NullReferenceException>(() => (Task<int>)cases.GetMethod("Null")!.Invoke(null, null)!);
    }

    [Fact]
    public void Mediator_call_site_projection_preserves_service_provider_overload_resolution()
    {
        var source = CapabilitySource(true, false, "s---", false) + """
            namespace App {
                public sealed class Other {
                    public System.Threading.Tasks.ValueTask<int> SendAsync(Ping request,System.Threading.CancellationToken token=default)=>new(17);
                }
                public static class OverloadCases {
                    private static global::App.Generated.Reads.IZendiator Choose(object provider)=>null!;
                    private static Other Choose(System.IServiceProvider provider)=>new();
                    public static int Unrelated() {
                        using var provider=new ServiceCollection().BuildServiceProvider();
                        var mediator=new global::App.Generated.Reads.Zendiator(provider);
                        return Choose(mediator).SendAsync(new Ping(1)).GetAwaiter().GetResult();
                    }
                }
            }
            """;
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "MediatorProviderOverload_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        using var bytes = new MemoryStream();
        var emitted = output.Emit(bytes);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        bytes.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        Assert.Equal(17, assembly.GetType("App.OverloadCases")!.GetMethod("Unrelated")!.Invoke(null, null));
    }
}
