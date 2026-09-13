// Hardening-1 formal stream matrix: Direct vs Zendiator StreamAsync.
// Items 0/1/16/1024/16384, behaviors 0/1/3/5, sync/async yield,
// creation/first/early-break/token boundaries, generic stream.
// Formal: Throughput, LaunchCount 3, MemoryDiagnoser. No HTTP/DB/disk.
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.CompilerServices;
using Zendiator;

namespace Zendiator.FeatureBench;

// Closed stream behaviors are additive only; existing routes untouched.
// Orders share the single global Order space (all unique).
[PipelineBehavior(typeof(HStream1B), Order = 0)]
[PipelineBehavior(typeof(HStream3B1), Order = 1)]
[PipelineBehavior(typeof(HStream3B2), Order = 2)]
[PipelineBehavior(typeof(HStream3B3), Order = 3)]
[PipelineBehavior(typeof(HStream5B1), Order = 4)]
[PipelineBehavior(typeof(HStream5B2), Order = 5)]
[PipelineBehavior(typeof(HStream5B3), Order = 6)]
[PipelineBehavior(typeof(HStream5B4), Order = 7)]
[PipelineBehavior(typeof(HStream5B5), Order = 8)]
[PipelineBehavior(typeof(HStreamA5B1), Order = 9)]
[PipelineBehavior(typeof(HStreamA5B2), Order = 10)]
[PipelineBehavior(typeof(HStreamA5B3), Order = 11)]
[PipelineBehavior(typeof(HStreamA5B4), Order = 12)]
[PipelineBehavior(typeof(HStreamA5B5), Order = 13)]
public sealed partial class Zendiator;
