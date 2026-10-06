using System.Collections.Immutable;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Running;

// BenchmarkDotNet calls GetExecutionOrder for one benchmark type at a time.
// Each type here belongs to one library, so this cannot interleave libraries.
internal sealed class InterleavedOrderer : DefaultOrderer
{
    private static readonly string[] Libraries = ["MediatRHistorical", "Mediator", "Zendiator", "Immediate", "DispatchR", "Direct"];

    public override IEnumerable<BenchmarkCase> GetExecutionOrder(ImmutableArray<BenchmarkCase> benchmarkCases, IEnumerable<BenchmarkLogicalGroupRule>? order = null)
        => base.GetExecutionOrder(benchmarkCases, order).GroupBy(CaseKey).SelectMany((group, index) =>
            group.OrderBy(benchmark => (Array.IndexOf(Libraries, Library(benchmark)) + index) % Libraries.Length));

    private static string Library(BenchmarkCase benchmark) =>
        Libraries.First(library => benchmark.Descriptor.Type.Name.StartsWith(library, StringComparison.Ordinal));

    private static string CaseKey(BenchmarkCase benchmark) =>
        benchmark.Descriptor.Type.Name[Library(benchmark).Length..] + "." + benchmark.Descriptor.WorkloadMethod.Name + "." + benchmark.Parameters.FolderInfo;
}
