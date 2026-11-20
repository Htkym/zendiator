# Mediator performance comparison

[日本語](README.ja.md)

## Results by workload

We measured Release builds on Windows 11 x64 with .NET 10.0.14 and BenchmarkDotNet 0.15.8.

| Workload | Zendiator 1.0.1 | Immediate.Handlers 4.3.0 | MediatR 15.0.0 | Mediator 3.0.2 | DispatchR.Mediator 2.3.1 | Foundatio.Mediator 1.3.3 |
|---|---:|---:|---:|---:|---:|---:|
| Send, 0 stages | 1.84 ns / 0 B | 11.20 ns / 0 B | 57.30 ns / 200 B | 58.15 ns / 64 B | 39.16 ns / 0 B | 40.85 ns / 0 B |
| Send, 5 stages | 8.29 ns / 0 B | 23.26 ns / 0 B | 201.46 ns / 1,016 B | 129.84 ns / 544 B | 47.19 ns / 0 B | 209.79 ns / 0 B |
| Resolve again in the same scope and send, 0 stages | 32.23 ns / 0 B | 49.77 ns / 0 B | 80.44 ns / 200 B | 97.64 ns / 64 B | 70.73 ns / 0 B | 82.08 ns / 0 B |
| Resolve again in the same scope and send, 5 stages | 41.24 ns / 0 B | 56.67 ns / 0 B | 196.21 ns / 1,016 B | 146.43 ns / 544 B | 80.05 ns / 0 B | 224.51 ns / 0 B |
| Create a scope, send once and dispose, 5 stages | 126.58 ns / 560 B | 152.24 ns / 568 B | 260.63 ns / 1,400 B | 219.39 ns / 920 B | 652.91 ns / 1,712 B | 384.20 ns / 856 B |
| Notify 16 synchronous subscribers | 144.53 ns / 0 B | N/A | 1,207.51 ns / 4,784 B | 229.39 ns / 0 B | 209.52 ns / 0 B | 1,005.43 ns / 0 B |
| Stream, all 1,024 items, 5 stages, sync | 15.013 µs / 928 B | 107.542 µs / 1,104 B | 214.211 µs / 3,592.5 B | 108.836 µs / 1,688.5 B | 106.151 µs / 1,104.5 B | 127.107 µs / 1,808 B |
| Stream, all 1,024 items, 5 stages, async | 166.127 µs / 1,586 B | 313.403 µs / 1,755 B | 613.929 µs / 4,726 B | 320.580 µs / 2,339.5 B | 314.207 µs / 1,756 B | 353.049 µs / 2,539 B |
| Stream, 1 of 16 items, 5 stages, sync | 306.31 ns / 928 B | 347.98 ns / 1,104 B | 977.11 ns / 3,592 B | 450.96 ns / 1,688 B | 377.69 ns / 1,104 B | 744.31 ns / 1,808 B |
| Stream, 1 of 16 items, 5 stages, async | 2.105 µs / 1,256 B | 2.436 µs / 1,752 B | 4.843 µs / 4,720 B | 2.735 µs / 2,336 B | 2.426 µs / 1,752 B | 3.022 µs / 2,536 B |
| Short-circuit at first of 3 stages, warm | 5.64 ns / 0 B | 6.48 ns / 0 B | 116.34 ns / 704 B | 75.54 ns / 496 B | 41.13 ns / 0 B | 77.22 ns / 384 B |
| Async hooks, successful first call | 1.023 µs / 448 B | 1.711 µs / 504 B | 1.560 µs / 1,352 B | 1.349 µs / 808 B | 1.338 µs / 560 B | 2.154 µs / 856 B |
| Async hooks, successful warm call | 41.48 ns / 0 B | 61.46 ns / 0 B | 780.85 ns / 944 B | 612.56 ns / 2,900.5 B | 82.21 ns / 0 B | 600.23 ns / 480 B |
| Async hooks, outer hook fails on first call | 5.492 µs / 1,712 B | 10.858 µs / 3,808 B | 10.142 µs / 3,416 B | 9.032 µs / 3,216 B | 8.069 µs / 2,968 B | 13.138 µs / 4,872 B |

[Results and 99.9% confidence intervals by execution order (CSV)](results/f2973af-representative-summary.csv) · [1.0.0 control run (CSV)](results/8140637-allocation-baseline.csv)

## What the workloads include

All workloads register services and build the DI provider before timing begins. The five-stage Send workload uses five synchronous Before hooks in Zendiator. Each returns the original request. Send uses an already resolved mediator unless the row includes scope creation or resolution. In the short-circuit case the first middleware returns before the handler runs. The async hook APIs complete synchronously in these workloads. The first-call hook measurements include resolving the mediator in an existing scope.

Full stream enumeration reads 1,024 items. Early exit reads the first of 16 items and includes disposal. Zendiator uses a named cursor, which the five middleware stages forward. Competitors use compiler-generated iterators that enumerate and yield items at each stage. The timings include the cost of those different input implementations. Every asynchronous input executes one `Task.Yield()` per item. The Zendiator cursor in this run marks its asynchronous helper with `[GeneratePooledProducer]`, available since 1.0.0. [Producer example](../docs/pooled-producer.md)

## Run the comparison

The recorded runs used Windows 11 x64, .NET SDK 10.0.404, .NET 10.0.14, BenchmarkDotNet 0.15.8, Release, workstation GC and a single logical processor. Each of the 83 library/workload combinations ran in its own process, with 20 warmups, 12 measurement iterations and a requested iteration time of 500 ms.

Use Python 3 and the specified .NET SDK and runtime. Start with a clean Git checkout. From its root, run the [representative workloads](representative.json) once in each order. Give each run a new output directory outside the repository.

```powershell
$driver = 'benchmarks/Zendiator.UseCaseBenchmarks/run_interleaved.py'
$matrix = 'benchmarks/representative.json'
python $driver --group custom --matrix $matrix --expect-sdk 10.0.404 --output ../zendiator-forward
python $driver --group custom --matrix $matrix --expect-sdk 10.0.404 --output ../zendiator-reverse --reverse
```

These commands measure the checked-out code. The 1.0.1 benchmarks used the runtime and source generator included in the release, before the package version was updated. [Source used for the benchmarks](https://github.com/Htkym/zendiator/commit/f2973af7aba8726147a7481961f69f424cae3a78) · [1.0.1 results](results/f2973af-representative-summary.csv) · [1.0.0 prerelease results](results/0d53c53-representative-summary.csv)
