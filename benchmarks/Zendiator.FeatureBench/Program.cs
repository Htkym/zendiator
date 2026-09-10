using BenchmarkDotNet.Running;
using Zendiator.FeatureBench;

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
