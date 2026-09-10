using BenchmarkDotNet.Running;
using Zendiator.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

namespace Zendiator.Benchmarks
{
    public static class Program
    {
    }
}
