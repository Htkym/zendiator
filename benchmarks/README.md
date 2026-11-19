# Mediator performance comparison

[日本語](README.ja.md)

Zendiator recorded the lowest mean time in all 14 workloads below, both in the original execution order and when that order was reversed. The comparison used Zendiator revision `0d53c53` before the 1.0.0 release, Immediate.Handlers 4.3.0, MediatR 15.0.0, Mediator 3.0.2, DispatchR.Mediator 2.3.1 and Foundatio.Mediator 1.3.3. Immediate.Handlers has no corresponding notification case, so that row compares five libraries.

## Time per operation

The two runs are shown separately. Each peer column gives the fastest competing library in that run. “Immediate” means Immediate.Handlers. The [full results](results/0d53c53-representative-summary.csv) include the mean, 99.9% confidence interval and allocated bytes for every library in each run.

| Workload | Unit | Zendiator, forward | Fastest peer, forward | Zendiator, reverse | Fastest peer, reverse |
|---|---|---:|---|---:|---|
| Send, no middleware | ns | 1.645 | Immediate 11.82 | 1.739 | Immediate 11.41 |
| Send, five synchronous Before hooks | ns | 6.708 | Immediate 20.95 | 6.397 | Immediate 20.25 |
| Resolve again in the same scope and send, no middleware | ns | 36.69 | Immediate 47.57 | 35.25 | Immediate 44.22 |
| Resolve again in the same scope and send, five stages | ns | 36.98 | Immediate 58.94 | 35.57 | Immediate 59.94 |
| Create a scope, send once and dispose, five stages | ns | 126.38 | Immediate 173.62 | 135.44 | Immediate 159.43 |
| Publish to 16 synchronous subscribers | ns | 140.12 | Mediator 229.14 | 145.94 | DispatchR 204.91 |
| Read all 1,024 items, synchronous input, five stages | µs | 15.22 | Immediate 110.91 | 15.12 | Mediator 110.22 |
| Read all 1,024 items, asynchronous input, five stages | µs | 201.29 | DispatchR 317.36 | 192.90 | DispatchR 323.66 |
| Read one item and dispose, synchronous input, five stages | ns | 317.31 | Immediate 385.47 | 296.50 | DispatchR 379.99 |
| Read one item and dispose, asynchronous input, five stages | µs | 2.260 | Immediate 2.508 | 2.048 | Immediate 2.283 |
| First of three middleware stages returns early, warm | ns | 4.830 | Immediate 6.008 | 4.635 | Immediate 5.444 |
| Async hooks, successful first call | µs | 1.023 | DispatchR 1.335 | 0.995 | DispatchR 1.231 |
| Async hooks, successful warm call | ns | 37.93 | Immediate 58.05 | 36.08 | Immediate 57.34 |
| Async hooks, outer hook fails on first call | µs | 4.422 | Mediator 7.776 | 4.578 | DispatchR 7.846 |

In 13 workloads, Zendiator’s upper confidence bound was below the lower bound of every competing library in both orders. For asynchronous stream early exit, the intervals overlapped in both runs. Time and allocation also tell different stories. For full asynchronous enumeration in the forward run, Zendiator averaged 201.29 µs and allocated 116,322 B per operation. DispatchR averaged 317.36 µs and allocated 1,756 B.

## What the workloads include

All workloads register services and build the DI provider before timing begins. The five-stage Send workload uses five synchronous Before hooks in Zendiator. Each returns the original request. Send uses an already resolved mediator unless the row includes scope creation or resolution. In the short-circuit case the first middleware returns before the handler runs. The async hook APIs complete synchronously in these workloads. The first-call hook measurements include resolving the mediator in an existing scope.

Full stream enumeration reads 1,024 items. Early exit reads the first of 16 items and includes disposal. Zendiator uses a named cursor, which the five middleware stages forward. Competitors use compiler-generated iterators that enumerate and yield items at each stage. The timings include the cost of those different input implementations.

## Run the comparison

The recorded runs used Windows 11 x64, .NET SDK 10.0.404, .NET 10.0.14, BenchmarkDotNet 0.15.8, Release, workstation GC and a single logical processor. Each of the 83 library/workload combinations ran in its own process, with 20 warmups, 12 measurement iterations and a requested iteration time of 500 ms.

Use Python 3 and the specified .NET SDK and runtime. Start with a clean Git checkout. From its root, run the [representative workloads](representative.json) once in each order. Give each run a new output directory outside the repository.

```powershell
$driver = 'benchmarks/Zendiator.UseCaseBenchmarks/run_interleaved.py'
$matrix = 'benchmarks/representative.json'
python $driver --group custom --matrix $matrix --expect-sdk 10.0.404 --output ../zendiator-forward
python $driver --group custom --matrix $matrix --expect-sdk 10.0.404 --output ../zendiator-reverse --reverse
```

These commands measure the version you have checked out. The CSV records the two runs of revision `0d53c53`.
