using BenchmarkDotNet.Running;
using Zendiator.DiBench;

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
