# Mediator performance comparison

[日本語](README.ja.md)

We compared Zendiator 1.0.1, Immediate.Handlers 4.3.0, MediatR 15.0.0, Mediator 3.0.2, DispatchR.Mediator 2.3.1 and Foundatio.Mediator 1.3.3. Zendiator had the lowest mean time in all 14 workloads in both execution orders.

## Results by workload

Each cell gives the mean time and bytes allocated per operation. Immediate.Handlers has no matching notification workload, shown as `N/A`. [Comparison across workloads](../README.md#performance) · [All 166 measurements with 99.9% confidence intervals](results/f2973af-representative-summary.csv)

### Send, 0 stages

| Library | Forward | Reverse |
|---|---:|---:|
| Zendiator 1.0.1 | 2.04 ns / 0 B | 1.65 ns / 0 B |
| Immediate.Handlers 4.3.0 | 13.19 ns / 0 B | 9.22 ns / 0 B |
| MediatR 15.0.0 | 70.45 ns / 200 B | 44.16 ns / 200 B |
| Mediator 3.0.2 | 70.59 ns / 64 B | 45.71 ns / 64 B |
| DispatchR.Mediator 2.3.1 | 45.65 ns / 0 B | 32.68 ns / 0 B |
| Foundatio.Mediator 1.3.3 | 44.90 ns / 0 B | 36.79 ns / 0 B |

### Send, 5 stages

| Library | Forward | Reverse |
|---|---:|---:|
| Zendiator 1.0.1 | 9.58 ns / 0 B | 7.01 ns / 0 B |
| Immediate.Handlers 4.3.0 | 26.84 ns / 0 B | 19.67 ns / 0 B |
| MediatR 15.0.0 | 239.07 ns / 1,016 B | 163.85 ns / 1,016 B |
| Mediator 3.0.2 | 153.75 ns / 544 B | 105.94 ns / 544 B |
| DispatchR.Mediator 2.3.1 | 52.36 ns / 0 B | 42.02 ns / 0 B |
| Foundatio.Mediator 1.3.3 | 235.80 ns / 0 B | 183.77 ns / 0 B |

### Resolve again in the same scope and send, 0 stages

| Library | Forward | Reverse |
|---|---:|---:|
| Zendiator 1.0.1 | 33.52 ns / 0 B | 30.94 ns / 0 B |
| Immediate.Handlers 4.3.0 | 54.41 ns / 0 B | 45.13 ns / 0 B |
| MediatR 15.0.0 | 82.66 ns / 200 B | 78.22 ns / 200 B |
| Mediator 3.0.2 | 114.87 ns / 64 B | 80.41 ns / 64 B |
| DispatchR.Mediator 2.3.1 | 79.80 ns / 0 B | 61.66 ns / 0 B |
| Foundatio.Mediator 1.3.3 | 86.41 ns / 0 B | 77.75 ns / 0 B |

### Resolve again in the same scope and send, 5 stages

| Library | Forward | Reverse |
|---|---:|---:|
| Zendiator 1.0.1 | 35.75 ns / 0 B | 46.74 ns / 0 B |
| Immediate.Handlers 4.3.0 | 57.85 ns / 0 B | 55.50 ns / 0 B |
| MediatR 15.0.0 | 197.13 ns / 1,016 B | 195.29 ns / 1,016 B |
| Mediator 3.0.2 | 147.42 ns / 544 B | 145.44 ns / 544 B |
| DispatchR.Mediator 2.3.1 | 83.03 ns / 0 B | 77.08 ns / 0 B |
| Foundatio.Mediator 1.3.3 | 226.87 ns / 0 B | 222.15 ns / 0 B |

### Create a scope, send once and dispose, 5 stages

| Library | Forward | Reverse |
|---|---:|---:|
| Zendiator 1.0.1 | 131.93 ns / 560 B | 121.22 ns / 560 B |
| Immediate.Handlers 4.3.0 | 155.14 ns / 568 B | 149.34 ns / 568 B |
| MediatR 15.0.0 | 268.97 ns / 1,400 B | 252.29 ns / 1,400 B |
| Mediator 3.0.2 | 227.14 ns / 920 B | 211.64 ns / 920 B |
| DispatchR.Mediator 2.3.1 | 774.62 ns / 1,712 B | 531.21 ns / 1,712 B |
| Foundatio.Mediator 1.3.3 | 389.28 ns / 856 B | 379.11 ns / 856 B |

### Notify 16 synchronous subscribers

| Library | Forward | Reverse |
|---|---:|---:|
| Zendiator 1.0.1 | 143.94 ns / 0 B | 145.13 ns / 0 B |
| Immediate.Handlers 4.3.0 | N/A | N/A |
| MediatR 15.0.0 | 1,249.78 ns / 4,784 B | 1,165.25 ns / 4,784 B |
| Mediator 3.0.2 | 230.08 ns / 0 B | 228.70 ns / 0 B |
| DispatchR.Mediator 2.3.1 | 209.27 ns / 0 B | 209.78 ns / 0 B |
| Foundatio.Mediator 1.3.3 | 1,006.80 ns / 0 B | 1,004.07 ns / 0 B |

### Stream, all 1,024 items, 5 stages, sync

| Library | Forward | Reverse |
|---|---:|---:|
| Zendiator 1.0.1 | 15.242 µs / 928 B | 14.783 µs / 928 B |
| Immediate.Handlers 4.3.0 | 108.246 µs / 1,104 B | 106.837 µs / 1,104 B |
| MediatR 15.0.0 | 216.464 µs / 3,593 B | 211.958 µs / 3,592 B |
| Mediator 3.0.2 | 108.292 µs / 1,688 B | 109.380 µs / 1,689 B |
| DispatchR.Mediator 2.3.1 | 106.468 µs / 1,104 B | 105.834 µs / 1,105 B |
| Foundatio.Mediator 1.3.3 | 127.324 µs / 1,808 B | 126.890 µs / 1,808 B |

### Stream, all 1,024 items, 5 stages, async

| Library | Forward | Reverse |
|---|---:|---:|
| Zendiator 1.0.1 | 167.413 µs / 1,586 B | 164.840 µs / 1,586 B |
| Immediate.Handlers 4.3.0 | 311.844 µs / 1,755 B | 314.962 µs / 1,755 B |
| MediatR 15.0.0 | 624.129 µs / 4,726 B | 603.728 µs / 4,726 B |
| Mediator 3.0.2 | 329.834 µs / 2,340 B | 311.326 µs / 2,339 B |
| DispatchR.Mediator 2.3.1 | 316.544 µs / 1,756 B | 311.871 µs / 1,756 B |
| Foundatio.Mediator 1.3.3 | 352.939 µs / 2,539 B | 353.159 µs / 2,539 B |

### Stream, 1 of 16 items, 5 stages, sync

| Library | Forward | Reverse |
|---|---:|---:|
| Zendiator 1.0.1 | 306.07 ns / 928 B | 306.55 ns / 928 B |
| Immediate.Handlers 4.3.0 | 348.98 ns / 1,104 B | 346.99 ns / 1,104 B |
| MediatR 15.0.0 | 951.59 ns / 3,592 B | 1,002.63 ns / 3,592 B |
| Mediator 3.0.2 | 453.85 ns / 1,688 B | 448.07 ns / 1,688 B |
| DispatchR.Mediator 2.3.1 | 377.63 ns / 1,104 B | 377.74 ns / 1,104 B |
| Foundatio.Mediator 1.3.3 | 741.37 ns / 1,808 B | 747.24 ns / 1,808 B |

### Stream, 1 of 16 items, 5 stages, async

| Library | Forward | Reverse |
|---|---:|---:|
| Zendiator 1.0.1 | 1.985 µs / 1,256 B | 2.224 µs / 1,256 B |
| Immediate.Handlers 4.3.0 | 2.477 µs / 1,752 B | 2.395 µs / 1,752 B |
| MediatR 15.0.0 | 3.764 µs / 4,720 B | 5.922 µs / 4,720 B |
| Mediator 3.0.2 | 2.655 µs / 2,336 B | 2.814 µs / 2,336 B |
| DispatchR.Mediator 2.3.1 | 2.426 µs / 1,752 B | 2.427 µs / 1,752 B |
| Foundatio.Mediator 1.3.3 | 2.979 µs / 2,536 B | 3.066 µs / 2,536 B |

### Short-circuit at first of 3 stages, warm

| Library | Forward | Reverse |
|---|---:|---:|
| Zendiator 1.0.1 | 4.88 ns / 0 B | 6.39 ns / 0 B |
| Immediate.Handlers 4.3.0 | 5.72 ns / 0 B | 7.23 ns / 0 B |
| MediatR 15.0.0 | 111.61 ns / 704 B | 121.07 ns / 704 B |
| Mediator 3.0.2 | 72.43 ns / 496 B | 78.65 ns / 496 B |
| DispatchR.Mediator 2.3.1 | 36.11 ns / 0 B | 46.14 ns / 0 B |
| Foundatio.Mediator 1.3.3 | 72.04 ns / 384 B | 82.39 ns / 384 B |

### Async hooks, successful first call

| Library | Forward | Reverse |
|---|---:|---:|
| Zendiator 1.0.1 | 1.050 µs / 448 B | 0.995 µs / 448 B |
| Immediate.Handlers 4.3.0 | 1.518 µs / 504 B | 1.905 µs / 504 B |
| MediatR 15.0.0 | 1.570 µs / 1,352 B | 1.550 µs / 1,352 B |
| Mediator 3.0.2 | 1.341 µs / 808 B | 1.357 µs / 808 B |
| DispatchR.Mediator 2.3.1 | 1.296 µs / 560 B | 1.379 µs / 560 B |
| Foundatio.Mediator 1.3.3 | 2.182 µs / 856 B | 2.126 µs / 856 B |

### Async hooks, successful warm call

| Library | Forward | Reverse |
|---|---:|---:|
| Zendiator 1.0.1 | 37.28 ns / 0 B | 45.68 ns / 0 B |
| Immediate.Handlers 4.3.0 | 60.46 ns / 0 B | 62.46 ns / 0 B |
| MediatR 15.0.0 | 626.77 ns / 944 B | 934.94 ns / 944 B |
| Mediator 3.0.2 | 582.95 ns / 2,909 B | 642.18 ns / 2,892 B |
| DispatchR.Mediator 2.3.1 | 82.83 ns / 0 B | 81.59 ns / 0 B |
| Foundatio.Mediator 1.3.3 | 600.53 ns / 480 B | 599.94 ns / 480 B |

### Async hooks, outer hook fails on first call

| Library | Forward | Reverse |
|---|---:|---:|
| Zendiator 1.0.1 | 5.755 µs / 1,744 B | 5.229 µs / 1,680 B |
| Immediate.Handlers 4.3.0 | 10.522 µs / 3,808 B | 11.194 µs / 3,808 B |
| MediatR 15.0.0 | 10.140 µs / 3,384 B | 10.144 µs / 3,448 B |
| Mediator 3.0.2 | 9.811 µs / 3,216 B | 8.254 µs / 3,216 B |
| DispatchR.Mediator 2.3.1 | 7.679 µs / 2,968 B | 8.459 µs / 2,968 B |
| Foundatio.Mediator 1.3.3 | 12.730 µs / 4,872 B | 13.546 µs / 4,872 B |

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

These commands measure the checked-out code. The 1.0.1 benchmarks used the runtime and source generator included in the release, before the package version was updated. [Source used for the benchmarks](https://github.com/Htkym/zendiator/commit/f2973af7aba8726147a7481961f69f424cae3a78) · [1.0.1 results](results/f2973af-representative-summary.csv) · [1.0.0 prerelease results](results/0d53c53-representative-summary.csv)
