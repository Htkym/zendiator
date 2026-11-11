# Opt-in pooled producer methods

This experimental opt-in targets private, parameterless `async ValueTask<bool>` producer helpers in top-level non-generic partial classes. It generates a helper in the same class using the .NET pooling async ValueTask builder and intercepts supported explicit source calls to the original helper. The cursor instance is not pooled. Existing mediator contracts, input work, `Task.Yield`, and consumer logic remain unchanged.

```csharp
public sealed partial class Cursor(CancellationToken token)
{
    public ValueTask<bool> MoveNextAsync() => MovePendingAsync();

    [Zendiator.GeneratePooledProducer]
    private async ValueTask<bool> MovePendingAsync()
    {
        token.ThrowIfCancellationRequested();
        await Task.Yield();
        token.ThrowIfCancellationRequested();
        return Publish();
    }

    private bool Publish() => true;
}
```

Each returned `ValueTask` must be consumed exactly once. Do not opt in methods whose callers depend on repeated consumption. The original method remains in the assembly; reflection or invocation outside supported source call sites is not optimized. Generated method/state-machine names and exception stack traces can differ. This is an explicit source transformation, not an attribute retroactively changing the original method's builder.

The Zendiator package already supplies `Zendiator.Generated.Interceptors` in `InterceptorsNamespaces` through its build-transitive props. Project-reference users must preserve that setting as existing generator tests do. The generator uses its existing Roslyn dependency and requires C# 14. Package consumers receive the build-transitive namespace configuration automatically. See [Native AOT limitations](../release/known-limitations.md) for deployment constraints.

## Supported and rejected source

The initial implementation intentionally accepts a restricted body grammar covering token checks, awaited operations, return, and try/catch cleanup used by the native producer. A primary-constructor parameter referenced by the body is supported only when its binding is preserved in the generated same-type helper. Symbol binding, expression types/conversions, and awaiter members are checked against an augmented compilation before source is published.

Unsupported signatures, local declarations, lambdas/local functions, generic/virtual/static methods, unsupported operators, dynamic/base/unsafe/ref-like constructs, caller-information-sensitive invocations, recursive/escaping method references, and conflicting builder annotations receive error diagnostics rather than silent partial optimization. Calls must be supported explicit invocations; implicit `await foreach` compiler lowering is not intercepted. Ordinary `async IAsyncEnumerable<T>` iterators are outside this feature and already use a different reusable compiler implementation.

- ZEN0040: unsupported producer declaration or body.
- ZEN0041: missing/unsupported call sites, escaping references, or interceptor configuration.
- ZEN0042: generated helper validation or required support failure.

## Evaluation

Compare an ordinary configuration and the opt-in configuration with identical producer body, input and consumer. Report opt-in results as a separate series. Do not substitute a handwritten pooled diagnostic producer and label it generated-feature performance. An all-framework same-producer comparison answers a different question from rankings with a feature enabled for one framework.

The first version also validates a bounded, statically visible source-helper graph. Helpers are restricted to supported syntax; source-defined constructors, custom awaitables, user-defined conversions/operators and untraceable delegate/dynamic calls are rejected. Standard .NET Task, ValueTask, Yield and configured awaitables are supported. This check cannot inspect opaque external assembly implementations or arbitrary reflection performed elsewhere. Avoid methods whose callers inspect stack/caller identity. Package consumers receive compiler diagnostics for unsupported opted-in methods; the feature does not silently fall back after promising pooling.


## Historical performance observations and limitations

On the merged PR #10 base (90e6315e), .NET 10.0.14 on Linux, three paired rounds of the original representative 1,024-item asynchronous workloads gave:

| Case | Ordinary / opt-in median time | Ordinary / opt-in allocation |
| --- | --- | --- |
| Native Stream0 | 207.67 / 175.04 microseconds | 115,456 / 768 bytes |
| Native Relay5 | 241.89 / 218.76 microseconds | 116,560 / 1,872 bytes |

The body, `Task.Yield`, inputs and consumer were unchanged; only the producer class's `partial` modifier and the marker attribute opted into the feature. All three asynchronous pairs improved and each saved 114,688 bytes (112 bytes per item). This does not promise equivalent savings for arbitrary producers or ordinary C# async iterators.

Competitive median ranks did not improve: Stream0 remained sixth and Relay5 fifth in the six-framework representative set. This is a feature-enabled comparison, not an all-framework shared-producer comparison.

The synchronous Stream0 control was slower in all three pairs (+9.91%, +2.10%, +2.57%; median time 12,391.41 to 12,710.17 ns), with 160 bytes unchanged. Synchronous Relay5 had mixed time directions and unchanged allocation. The asynchronous call is not executed on these synchronous paths, and all 109 common generated files were byte-identical, but this does not rule out JIT/code-layout effects in the containing method or other variation. No causal explanation or absence-of-regression claim has been established. These synchronous observations limit claims of improvement across workloads.
