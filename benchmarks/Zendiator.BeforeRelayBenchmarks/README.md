# Before and Relay preprocessing

This example compares two application compositions using existing APIs:
five `IAsyncStreamBefore<Items>` callbacks, and five `IStreamMiddleware<Items,int>`
callbacks that perform the same preprocessing and return the upstream cursor.
Both execute `Work.Relay` in order1,2,3,4,5 before the producer starts.

Use Before when preprocessing needs no request replacement, normal-empty short
circuit, paired cleanup or processing on every upstream reopening. Keep
middleware when any of those capabilities is required. Before runs once per root
enumeration; it does not rerun on an inner reopening. Receivers may be cached and
remain owned by DI. Keep the scope alive until enumeration and disposal finish.
See [the streaming guide](../../docs/streaming-iterators.md) for pending callback,
cancellation, ownership and context rules.

From the repository root, run the reusable Before example:

```sh
dotnet run --project benchmarks/Zendiator.BeforeRelayBenchmarks -c Release -- --example
```

It consumes16 items, checks checksum120 and preprocessing order, and checks one
producer finalization. It prints one success message. Running without application
arguments prints usage rather than starting benchmarks.

To run the six BenchmarkDotNet definitions:

```sh
dotnet run --project benchmarks/Zendiator.BeforeRelayBenchmarks -c Release -- --filter '*'
```

The two `Mode` values, `Before` and `Relay`, share the same compiler iterator,
request values and consumers:

| Method | Input count | Consumption |
|---|---:|---|
| Full16 | 16 | Consume all items and await disposal |
| Early16 | 16 | Consume the first item and await disposal |
| Full1024 | 1024 | Consume all items and await disposal |

The producer and callbacks complete synchronously in this workload. Each
operation includes fresh source/enumerator creation, preprocessing, consumption
and awaited disposal. Mediator/scope creation, request creation and creation of
the live uncanceled API token are outside timing. The scope and mediator are
reused, so receivers may be cached before measured iterations. Do not remove
cancellation from application code to obtain a different route.

The timing path uses a null probe on both sides and the same no-inline
preprocessing helper. One delegate, created outside timing, calls the selected
generated public entry once per enumeration; both modes include that common
call. The example enables the same producer's probe outside timing to demonstrate
order and consumed work.

The supplied profile uses Release, one logical processor through affinity1,
warmup20, Actual12, target500ms and one launch. Memory diagnostics and the full
JSON exporter are enabled. Record the checked-out commit, actual SDK/runtime
patch, resolved dependency versions, CPU/OS, power and affinity when reporting
results. The project targets .NET10 and uses the installed compatible patched
runtime; it does not pin a machine path or runtime patch. The source and dependency
lock do not guarantee identical binaries or results across environments. This
entry point does not automate paired execution orders.

Treat the output as a comparison of these two legal compositions with different
capabilities. It does not replace another Relay workload, measure competitor
libraries, or demonstrate performance for arbitrary middleware, cold DI,
pending callbacks, failure paths or real I/O.
