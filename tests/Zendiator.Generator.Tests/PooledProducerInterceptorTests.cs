using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private const string PooledProducerSource = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Zendiator;
        namespace ProducerApp;
        public sealed partial class Cursor(CancellationToken token, Exception? failure)
        {
            private readonly TaskCompletionSource<bool> gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public static readonly AsyncLocal<string?> Ambient = new();
            public string? Captured;
            public int Count;
            public int Cleanups;
            public ValueTask<bool> Move() => Pending();
            public void Release() => gate.TrySetResult(true);
            [GeneratePooledProducer]
            private async ValueTask<bool> Pending()
            {
                try
                {
                    token.ThrowIfCancellationRequested();
                    await gate.Task.ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    return Publish();
                }
                catch
                {
                    Finish();
                    throw;
                }
            }
            private bool Publish()
            {
                Captured = Ambient.Value;
                if (failure is not null) throw failure;
                Count++;
                return true;
            }
            private void Finish() => Cleanups++;
        }
        """;

    [Theory]
    [InlineData("success")]
    [InlineData("fault")]
    [InlineData("cancel")]
    [InlineData("precancel")]
    public async Task Pooled_producer_interceptor_preserves_primary_capture_pending_completion_and_context(string scenario)
    {
        var compilation = SyncCompilation(PooledProducerSource, "PooledProducer_" + Guid.NewGuid().ToString("N"))
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Enable));
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains(driver.GetRunResult().GeneratedTrees, t => t.ToString().Contains("PoolingAsyncValueTaskMethodBuilder", StringComparison.Ordinal));
        using var bytes = new MemoryStream();
        var emitted = output.Emit(bytes);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        bytes.Position = 0;
        var context = new AssemblyLoadContext("PooledProducer", isCollectible: true);
        var assembly = context.LoadFromStream(bytes);
        var type = assembly.GetType("ProducerApp.Cursor")!;
        var asyncMethods = type.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Select(m => (Method: m, State: m.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType))
            .Where(p => p.State is not null).ToArray();
        Assert.Equal(2, asyncMethods.Length);
        Assert.Single(asyncMethods, m => m.State!.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Any(f => f.FieldType.Name.Contains("PoolingAsyncValueTaskMethodBuilder", StringComparison.Ordinal)));
        using var cts = new CancellationTokenSource();
        if (scenario == "precancel")
            cts.Cancel();
        var sentinel = new FormatException("same producer exception");
        var cursor = Activator.CreateInstance(type, cts.Token, scenario == "fault" ? sentinel : null)!;
        var ambient = (AsyncLocal<string?>)type.GetField("Ambient")!.GetValue(null)!;
        ambient.Value = "caller-context";
        var value = (ValueTask<bool>)type.GetMethod("Move")!.Invoke(cursor, null)!;
        if (scenario != "precancel")
        {
            Assert.False(value.IsCompleted);
            var backing = typeof(ValueTask<bool>).GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .Single(f => f.FieldType == typeof(object)).GetValue(value);
            Assert.Contains("PoolingAsyncValueTaskMethodBuilder", backing!.GetType().ToString());
        }
        ambient.Value = "later-context";
        if (scenario == "cancel")
            cts.Cancel();
        type.GetMethod("Release")!.Invoke(cursor, null);
        Exception? failure = null;
        try
        {
            Assert.True(await value);
        }
        catch (Exception ex) { failure = ex; }
        if (scenario == "success")
        {
            Assert.Null(failure);
            Assert.Equal(1, type.GetField("Count")!.GetValue(cursor));
            Assert.Equal("caller-context", type.GetField("Captured")!.GetValue(cursor));
            Assert.Equal(0, type.GetField("Cleanups")!.GetValue(cursor));
        }
        else
        {
            if (scenario == "fault")
                Assert.Same(sentinel, failure);
            else
                Assert.Equal(cts.Token, Assert.IsAssignableFrom<OperationCanceledException>(failure).CancellationToken);
            Assert.Equal(0, type.GetField("Count")!.GetValue(cursor));
            Assert.Equal(1, type.GetField("Cleanups")!.GetValue(cursor));
        }
        ambient.Value = null;
        context.Unload();
    }

    [Theory]
    [InlineData("nonpartial")]
    [InlineData("parameter")]
    [InlineData("public")]
    [InlineData("generic")]
    [InlineData("local")]
    [InlineData("lambda")]
    [InlineData("recursive")]
    [InlineData("methodgroup")]
    [InlineData("callerinfo")]
    [InlineData("existingbuilder")]
    [InlineData("otherreceiver")]
    [InlineData("nocalls")]
    [InlineData("conditional")]
    [InlineData("implicitconversion")]
    [InlineData("indirectrecursion")]
    [InlineData("customawaiter")]
    [InlineData("sourceconstructor")]
    [InlineData("helperconversion")]
    [InlineData("targettypedconstructor")]
    [InlineData("indexerrecursion")]
    public void Pooled_producer_interceptor_rejects_unsupported_opt_ins(string shape)
    {
        var source = shape switch
        {
            "nonpartial" => PooledProducerSource.Replace("sealed partial class", "sealed class"),
            "parameter" => PooledProducerSource.Replace("Pending()", "Pending(int unused = 0)"),
            "public" => PooledProducerSource.Replace("private async ValueTask<bool> Pending", "public async ValueTask<bool> Pending"),
            "generic" => PooledProducerSource.Replace("class Cursor(", "class Cursor<T>("),
            "local" => PooledProducerSource.Replace("await gate.Task.ConfigureAwait(false);", "int temporary = 0; await gate.Task.ConfigureAwait(false);"),
            "lambda" => PooledProducerSource.Replace("await gate.Task.ConfigureAwait(false);", "await Task.Run(() => true);"),
            "recursive" => PooledProducerSource.Replace("await gate.Task.ConfigureAwait(false);", "await Pending();"),
            "methodgroup" => PooledProducerSource.Replace("public ValueTask<bool> Move() => Pending();", "public Func<ValueTask<bool>> Escape() => Pending; public ValueTask<bool> Move() => Pending();"),
            "callerinfo" => PooledProducerSource.Replace("return Publish();", "return Caller();").Replace("private bool Publish()", "private bool Caller([System.Runtime.CompilerServices.CallerMemberName] string name = \"\") => true; private bool Publish()"),
            "otherreceiver" => PooledProducerSource.Replace("public ValueTask<bool> Move() => Pending();", "public ValueTask<bool> Move(Cursor other) => other.Pending();"),
            "implicitconversion" => PooledProducerSource.Replace("return Publish();", "if (Flag) return true; return Publish();").Replace("private bool Publish()", "private Switch Flag => new(); public sealed class Switch { public static implicit operator bool(Switch value) => true; } private bool Publish()"),
            "customawaiter" => PooledProducerSource.Replace("await gate.Task.ConfigureAwait(false);", "await Custom();").Replace("private bool Publish()", "private CustomAwaitable Custom() => default; public struct CustomAwaitable { public System.Runtime.CompilerServices.YieldAwaitable.YieldAwaiter GetAwaiter() => Task.Yield().GetAwaiter(); } private bool Publish()"),
            "helperconversion" => PooledProducerSource.Replace("return Publish();", "return Converted();").Replace("private bool Publish()", "private Switch Flag => default!; public sealed class Switch { public static implicit operator bool(Switch value) => true; } private bool Converted() => Flag; private bool Publish()"),
            "indexerrecursion" => PooledProducerSource.Replace("return Publish();", "return ReadIndex();").Replace("private bool Publish()", "private bool this[int index] => Pending().GetAwaiter().GetResult(); private bool ReadIndex() => this[0]; private bool Publish()"),
            "targettypedconstructor" => PooledProducerSource.Replace("return Publish();", "return Construct();").Replace("private bool Publish()", "public sealed class Helper { public Helper() { } } private bool Construct() { Helper value = new(); return true; } private bool Publish()"),
            "sourceconstructor" => PooledProducerSource.Replace("await gate.Task.ConfigureAwait(false);", "new Helper(); await gate.Task.ConfigureAwait(false);").Replace("private bool Publish()", "public sealed class Helper { public Helper() { } } private bool Publish()"),
            "indirectrecursion" => PooledProducerSource.Replace("return Publish();", "return Reenter();").Replace("private bool Publish()", "private bool Reenter() => Pending().GetAwaiter().GetResult(); private bool Publish()"),
            "conditional" => "#define TRACE\n" + PooledProducerSource.Replace("return Publish();", "Observe(); return Publish();").Replace("private bool Publish()", "[System.Diagnostics.Conditional(\"TRACE\")] private void Observe() { Count++; } private bool Publish()"),
            "nocalls" => PooledProducerSource.Replace("public ValueTask<bool> Move() => Pending();", "public ValueTask<bool> Move() => new(false);"),
            "existingbuilder" => PooledProducerSource.Replace("[GeneratePooledProducer]", "[GeneratePooledProducer, System.Runtime.CompilerServices.AsyncMethodBuilder(typeof(System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder<>))]"),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
        if (shape == "parameter")
            source = source.Replace("Move() => Pending(int unused = 0)", "Move() => Pending()");
        var input = SyncCompilation(source, "InvalidProducer");
        Assert.Empty(input.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(input, out _, out var diagnostics);
        Assert.Contains(diagnostics, d => d.Severity == DiagnosticSeverity.Error && d.Id.StartsWith("ZEN", StringComparison.Ordinal));
        Assert.DoesNotContain(driver.GetRunResult().GeneratedTrees, t => t.ToString().Contains("PoolingAsyncValueTaskMethodBuilder", StringComparison.Ordinal));
    }
    [Theory]
    [InlineData(false, "full")]
    [InlineData(true, "full")]
    [InlineData(false, "early")]
    [InlineData(true, "early")]
    [InlineData(false, "fail")]
    [InlineData(true, "fail")]
    [InlineData(false, "cancel")]
    [InlineData(true, "cancel")]
    public async Task Pooled_producer_interceptor_preserves_actual_native_producer(bool asynchronous, string scenario)
    {
        using var resource = typeof(GeneratorTests).Assembly.GetManifestResourceStream("Zendiator.PooledProducer.WorkSource")!;
        using var reader = new StreamReader(resource);
        var original = "using System;using System.Collections.Generic;using System.Threading;using System.Threading.Tasks;\n" + await reader.ReadToEndAsync();
        var selected = original.Replace("public sealed class WorkStreamCursor(", "public sealed partial class WorkStreamCursor(")
            .Replace("private async ValueTask<bool> MovePendingAsync()", "[Zendiator.GeneratePooledProducer] private async ValueTask<bool> MovePendingAsync()");
        async Task<string> Run(string source)
        {
            SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "NativeProducer_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
            Assert.Empty(diagnostics);
            using var bytes = new MemoryStream();
            var emit = output.Emit(bytes);
            Assert.True(emit.Success, string.Join("\n", emit.Diagnostics));
            bytes.Position = 0;
            var load = new AssemblyLoadContext("NativeProducer", isCollectible: true);
            var assembly = load.LoadFromStream(bytes);
            var probeType = assembly.GetType("Competitive.Probe")!;
            var probe = Activator.CreateInstance(probeType)!;
            if (scenario == "fail")
                probeType.GetField("Fail")!.SetValue(probe, true);
            using var cancellation = new CancellationTokenSource();
            var cursor = (IAsyncEnumerator<int>)Activator.CreateInstance(assembly.GetType("Competitive.WorkStreamCursor")!, 4, asynchronous, probe, cancellation.Token)!;
            var values = new List<int>();
            string? error = null;
            try
            {
                while (await cursor.MoveNextAsync())
                {
                    values.Add(cursor.Current);
                    if (scenario == "early")
                        break;
                    if (scenario == "cancel")
                        cancellation.Cancel();
                }
            }
            catch (Exception failure)
            {
                error = failure.GetType().FullName + ":" + failure.Message;
                if (failure is OperationCanceledException canceled)
                    Assert.Equal(cancellation.Token, canceled.CancellationToken);
            }
            finally { await cursor.DisposeAsync(); await cursor.DisposeAsync(); }
            Assert.Equal(1, probeType.GetField("Disposals")!.GetValue(probe));
            var result = System.Text.Json.JsonSerializer.Serialize(new
            {
                values,
                error,
                events = probeType.GetProperty("Events")!.GetValue(probe),
                handlers = probeType.GetField("Handlers")!.GetValue(probe),
                items = probeType.GetField("Items")!.GetValue(probe),
                disposals = probeType.GetField("Disposals")!.GetValue(probe)
            });
            load.Unload();
            return result;
        }
        Assert.Equal(await Run(original), await Run(selected));
    }

    [Fact]
    public void Pooled_producer_interceptor_preserves_namespace_alias_scope()
    {
        var source = PooledProducerSource.Replace("namespace ProducerApp;", "namespace Outer { namespace ProducerApp { using AwaitTask = System.Threading.Tasks.Task;")
            .Replace("await gate.Task.ConfigureAwait(false);", "await AwaitTask.Yield();") + "\n} }";
        SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "AliasProducer"), out var output, out var diagnostics);
        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void Pooled_producer_interceptor_requires_namespace_opt_in()
    {
        var options = new CSharpParseOptions(LanguageVersion.Preview);
        var input = CSharpCompilation.Create("MissingNamespace", [CSharpSyntaxTree.ParseText(PooledProducerSource, options)], References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        var driver = CSharpGeneratorDriver.Create([new SourceGenerator.ZendiatorGenerator().AsSourceGenerator()], parseOptions: options)
            .RunGeneratorsAndUpdateCompilation(input, out _, out var diagnostics);
        Assert.Contains(diagnostics, d => d.Id == "ZEN0041" && d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(driver.GetRunResult().GeneratedTrees, t => t.ToString().Contains("PoolingAsyncValueTaskMethodBuilder", StringComparison.Ordinal));
    }

    [Fact]
    public void Pooled_producer_interceptor_updates_locations_and_removes_generation_after_opt_out()
    {
        var input = SyncCompilation(PooledProducerSource, "IncrementalProducer");
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(input, out _, out var firstDiagnostics);
        Assert.Empty(firstDiagnostics);
        string Interceptor(GeneratorDriver value) => Assert.Single(value.GetRunResult().GeneratedTrees,
            t => t.ToString().Contains("static global::System.Threading.Tasks.ValueTask<bool> Intercept", StringComparison.Ordinal)).ToString();
        var first = Interceptor(driver);
        var changed = SyncCompilation("\n\n" + PooledProducerSource.Replace("Move() => Pending()", "Move() => this.Pending()"), "IncrementalProducer");
        driver = driver.RunGeneratorsAndUpdateCompilation(changed, out var updated, out var secondDiagnostics);
        Assert.Empty(secondDiagnostics);
        Assert.Empty(updated.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.NotEqual(first, Interceptor(driver));
        var removed = SyncCompilation(PooledProducerSource.Replace("[GeneratePooledProducer]", ""), "IncrementalProducer");
        driver = driver.RunGeneratorsAndUpdateCompilation(removed, out var removedOutput, out var removedDiagnostics);
        Assert.Empty(removedDiagnostics);
        Assert.Empty(removedOutput.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.DoesNotContain(driver.GetRunResult().GeneratedTrees, t => t.ToString().Contains("PoolingAsyncValueTaskMethodBuilder", StringComparison.Ordinal));
    }

    [Fact]
    public void Pooled_producer_interceptor_uses_merged_partial_type_accessibility()
    {
        var source = PooledProducerSource.Replace("public sealed partial class Cursor", "sealed partial class Cursor")
            + "\npublic partial class Cursor { }";
        SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "PartialAccessProducer"), out var output, out var diagnostics);
        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    }

}
