using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    [Theory]
    [InlineData("method", "new System.Diagnostics.StackTrace(); return Publish();", "inspection")]
    [InlineData("method", "return Pending().GetAwaiter().GetResult();", "recursive")]
    [InlineData("property", "new System.Diagnostics.StackTrace(); return Publish();", "inspection")]
    [InlineData("property", "return Pending().GetAwaiter().GetResult();", "recursive")]
    [InlineData("indexer", "new System.Diagnostics.StackTrace(); return Publish();", "inspection")]
    [InlineData("indexer", "return Pending().GetAwaiter().GetResult();", "recursive")]
    public void Pooled_producer_partial_helper_rejects_unsafe_implementation(string kind, string implementation, string reason)
    {
        var source = PooledProducerWithPartialHelper(kind, implementation);
        var input = SyncCompilation(source, "UnsafePartialProducer");
        Assert.Empty(input.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(input, out _, out var diagnostics);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("ZEN0040", diagnostic.Id);
        Assert.Contains(reason, diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.DoesNotContain(driver.GetRunResult().GeneratedTrees,
            tree => tree.ToString().Contains("PoolingAsyncValueTaskMethodBuilder", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("method")]
    [InlineData("property")]
    [InlineData("indexer")]
    public async Task Pooled_producer_partial_helper_preserves_safe_implementation(string kind)
    {
        var source = PooledProducerWithPartialHelper(kind, "return Publish();");
        var input = SyncCompilation(source, "SafePartialProducer");
        Assert.Empty(input.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(input, out var output, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains(driver.GetRunResult().GeneratedTrees,
            tree => tree.ToString().Contains("PoolingAsyncValueTaskMethodBuilder", StringComparison.Ordinal));
        using var bytes = new MemoryStream();
        var emit = output.Emit(bytes);
        Assert.True(emit.Success, string.Join("\n", emit.Diagnostics));
        bytes.Position = 0;
        var load = new AssemblyLoadContext("SafePartialProducer", isCollectible: true);
        try
        {
            var assembly = load.LoadFromStream(bytes);
            var type = assembly.GetType("ProducerApp.Cursor")!;
            var cursor = Activator.CreateInstance(type, CancellationToken.None, null)!;
            var ambient = (AsyncLocal<string?>)type.GetField("Ambient")!.GetValue(null)!;
            ambient.Value = "partial-caller-context";
            var pending = (ValueTask<bool>)type.GetMethod("Move")!.Invoke(cursor, null)!;
            Assert.False(pending.IsCompleted);
            ambient.Value = "later-context";
            type.GetMethod("Release")!.Invoke(cursor, null);
            Assert.True(await pending);
            Assert.Equal(1, type.GetField("Count")!.GetValue(cursor));
            Assert.Equal(0, type.GetField("Cleanups")!.GetValue(cursor));
            Assert.Equal("partial-caller-context", type.GetField("Captured")!.GetValue(cursor));
            ambient.Value = null;
        }
        finally
        {
            load.Unload();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Pooled_producer_partial_helper_rejects_helper_only_cycles(bool mutual)
    {
        var source = PooledProducerWithPartialHelper("method", mutual ? "return OtherPartial();" : "return PartialPublish();");
        if (mutual)
        {
            source = source.Replace("private bool Publish()", """
                private partial bool OtherPartial();
                private partial bool OtherPartial() => PartialPublish();
                private bool Publish()
                """, StringComparison.Ordinal);
        }
        var input = SyncCompilation(source, "PartialHelperCycle");
        Assert.Empty(input.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(input, out _, out var diagnostics);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("ZEN0040", diagnostic.Id);
        Assert.Contains("recursive", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.DoesNotContain(driver.GetRunResult().GeneratedTrees,
            tree => tree.ToString().Contains("PoolingAsyncValueTaskMethodBuilder", StringComparison.Ordinal));
    }

    private static string PooledProducerWithPartialHelper(string kind, string implementation)
    {
        var helper = kind switch
        {
            "method" => $$"""
                private partial bool PartialPublish();
                private partial bool PartialPublish() { {{implementation}} }
                private bool InvokePartial() => PartialPublish();
                """,
            "property" => $$"""
                private partial bool PartialValue { get; }
                private partial bool PartialValue { get { {{implementation}} } }
                private bool InvokePartial() => PartialValue;
                """,
            "indexer" => $$"""
                private partial bool this[int index] { get; }
                private partial bool this[int index] { get { {{implementation}} } }
                private bool InvokePartial() => this[0];
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        return PooledProducerSource
            .Replace("return Publish();", "return InvokePartial();", StringComparison.Ordinal)
            .Replace("private bool Publish()", helper + "\nprivate bool Publish()", StringComparison.Ordinal);
    }
}
