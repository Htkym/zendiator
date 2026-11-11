using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Generator.Tests;

public static class NonClsThrowProbe
{
    private static readonly Action ThrowRaw = Create();

    public static void Throw() => ThrowRaw();

    private static Action Create()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("NonClsThrowProbe"), AssemblyBuilderAccess.Run);
        assembly.SetCustomAttribute(new CustomAttributeBuilder(typeof(RuntimeCompatibilityAttribute).GetConstructor(Type.EmptyTypes)!, [],
            [typeof(RuntimeCompatibilityAttribute).GetProperty(nameof(RuntimeCompatibilityAttribute.WrapNonExceptionThrows))!], [false]));
        var type = assembly.DefineDynamicModule("thrower").DefineType("Thrower", TypeAttributes.Public | TypeAttributes.Sealed);
        var method = type.DefineMethod("Throw", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldstr, "Non-CLS boundary sentinel");
        il.Emit(OpCodes.Throw);
        return type.CreateType()!.GetMethod("Throw")!.CreateDelegate<Action>();
    }
}

public sealed partial class GeneratorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Async_failure_boundary_preserves_non_cls_wrapping_and_finally(bool wrapNonExceptionThrows)
    {
        var before = "public ValueTask<Ping> BeforeAsync(Ping r,CancellationToken t){_probe.CheckToken(t);var result=_probe.Before(_id,r,new Ping(r.Value+1));if(_id==0)global::Zendiator.Generator.Tests.NonClsThrowProbe.Throw();return result;}";
        var source = CapabilityThreeSource("inherited").Replace(InvokerBefore, before)
            .Replace("namespace App {", "[assembly:System.Runtime.CompilerServices.RuntimeCompatibility(WrapNonExceptionThrows=" + (wrapNonExceptionThrows ? "true" : "false") + ")]namespace App {");
        var fixture = CompileCapabilityOptimization(SyncCompilation(source, "NonClsBoundary_" + Guid.NewGuid().ToString("N")),
            "noncls-" + wrapNonExceptionThrows, "App.Generated.Reads.Zendiator", true);
        var probe = new AsyncHookProbe();
        IServiceCollection services = new ServiceCollection();
        services.AddSingleton(probe);
        fixture.Register.Invoke(null, [services]);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = provider.CreateAsyncScope();
        var request = Activator.CreateInstance(fixture.Request, [10])!;
        var prior = AsyncHookProbe.Ambient.Value;
        try
        {
            AsyncHookProbe.Ambient.Value = "caller";
            ValueTask<int> operation = default;
            var synchronous = Record.Exception(() => { operation = (ValueTask<int>)InvokeCapability(fixture, true, scope.ServiceProvider, request, probe.Cancellation.Token); });
            var completedBeforeConsumption = operation.IsCompleted;
            Assert.Equal(!wrapNonExceptionThrows, synchronous != null);
            var error = synchronous ?? await Record.ExceptionAsync(async () => await operation);
            var wrapped = Assert.IsType<RuntimeWrappedException>(error);
            Assert.Equal("Non-CLS boundary sentinel", wrapped.WrappedException);
            Assert.Equal("caller", AsyncHookProbe.Ambient.Value);
            var final = Assert.Single(probe.Calls, call => call.Phase == "h0.finally");
            Assert.False(final.Exit!.Value.Success);
            if (wrapNonExceptionThrows)
            {
                var observation = Assert.Single(probe.Calls, call => call.Phase == "h0.exception");
                Assert.Same(wrapped, observation.Error);
                Assert.Same(wrapped, final.Exit.Value.Error);
            }
            else
            {
                Assert.DoesNotContain(probe.Calls, call => call.Phase.EndsWith(".exception", StringComparison.Ordinal));
                Assert.Null(final.Exit.Value.Error);
            }
            Console.WriteLine($"NonCLS wrap={wrapNonExceptionThrows}, synchronousThrow={synchronous != null}, operationCompletedBeforeConsumption={completedBeforeConsumption}");
        }
        finally
        {
            AsyncHookProbe.Ambient.Value = prior;
            probe.Cancellation.Dispose();
        }
    }
}
