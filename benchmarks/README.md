# Mediator performance comparison

[日本語](README.ja.md)

The comparison covers Zendiator revision `f2973af`, Immediate.Handlers 4.3.0, MediatR 15.0.0, Mediator 3.0.2, DispatchR.Mediator 2.3.1 and Foundatio.Mediator 1.3.3. In both orders, Zendiator had the lowest mean time and the lowest allocation, including ties, in all 14 workloads. The notification workload has five libraries because Immediate.Handlers has no matching case.

## Time per operation

Each peer column shows the fastest competing library for that workload and order. Immediate means Immediate.Handlers. [Mean times and allocations for all six libraries](../README.md#performance) · [All 166 measurements with 99.9% confidence intervals](results/f2973af-representative-summary.csv)

| Workload | Unit | Zendiator, forward | Fastest peer, forward | Zendiator, reverse | Fastest peer, reverse |
|---|---|---:|---|---:|---|
| Send, no middleware | ns | 2.04 | Immediate 13.19 | 1.65 | Immediate 9.22 |
| Send, five synchronous Before hooks | ns | 9.58 | Immediate 26.84 | 7.01 | Immediate 19.67 |
| Resolve again in the same scope and send, no middleware | ns | 33.52 | Immediate 54.41 | 30.94 | Immediate 45.13 |
| Resolve again in the same scope and send, five stages | ns | 35.75 | Immediate 57.85 | 46.74 | Immediate 55.50 |
| Create a scope, send once and dispose, five stages | ns | 131.93 | Immediate 155.14 | 121.22 | Immediate 149.34 |
| Publish to 16 synchronous subscribers | ns | 143.94 | DispatchR 209.27 | 145.13 | DispatchR 209.78 |
| Read all 1,024 items, synchronous input, five stages | µs | 15.242 | DispatchR 106.468 | 14.783 | DispatchR 105.834 |
| Read all 1,024 items, asynchronous input, five stages | µs | 167.413 | Immediate 311.844 | 164.840 | Mediator 311.326 |
| Read one item and dispose, synchronous input, five stages | ns | 306.07 | Immediate 348.98 | 306.55 | Immediate 346.99 |
| Read one item and dispose, asynchronous input, five stages | µs | 1.985 | DispatchR 2.426 | 2.224 | Immediate 2.395 |
| First of three middleware stages returns early, warm | ns | 4.88 | Immediate 5.72 | 6.39 | Immediate 7.23 |
| Async hooks, successful first call | µs | 1.050 | DispatchR 1.296 | 0.995 | Mediator 1.357 |
| Async hooks, successful warm call | ns | 37.28 | Immediate 60.46 | 45.68 | Immediate 62.46 |
| Async hooks, outer hook fails on first call | µs | 5.755 | DispatchR 7.679 | 5.229 | Mediator 8.254 |

Zendiator’s upper 99.9% confidence bound for the mean was below every competitor’s lower bound in 14 workloads forward and 13 in reverse. The full asynchronous stream allocated 1,586 B forward and 1,586 B in reverse. Creating a scope, sending once and disposing it allocated 560 B forward and 560 B in reverse. Rerunning those two workloads on 1.0.0 on the same machine gave 116,322 B and 624 B, respectively. [1.0.0 control run](results/8140637-allocation-baseline.csv)

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

These commands measure the checked-out commit. [Revision `f2973af` results](results/f2973af-representative-summary.csv) · [1.0.0 prerelease comparison at `0d53c53`](results/0d53c53-representative-summary.csv)
