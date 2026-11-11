# Experimental struct upstream boundary

`IStructStreamMiddleware<TRequest,TItem>` is an additive, experimental capability of the
existing `IStreamMiddleware<TRequest,TItem>` stage. Both implementations must have equivalent
behavior. Existing interface signatures, stage registration, service keys and native DI/session
ownership are unchanged.

The initial generator support directly calls a public ordinary nonvirtual generic implementation
declared on an accessible sealed class. Explicit, inherited, nonsealed, virtual, value receiver,
restricted-attribute and ambiguous-overload forms retain the legacy method and class port.
The generator never invokes the generic method through its interface. An invalid opted-in generic
signature or constraint receives ZEN0025 with the required shape; ordinary C# implementation errors
can additionally be reported by the compiler.

The generic method returns `ValueTask<IAsyncEnumerator<TItem>>`, just like the existing boundary.
An identity relay returns `next.OpenAsync(request, token)` unchanged. Do not add a wrapper cursor
to a relay just to opt in. A middleware that retains next should use a generic cursor with a field
of type `TNext`; assigning next to an interface or object can box. A class cursor is still allocated
by that middleware. Concrete generic cursor return types are not part of this experiment.

Generated readonly ports contain one owner reference. Opening and active cursor state remain in
the independent per-operation owner, so port copies share the same opening/reopening gate.
The default port is uninitialized. Normal EOF plus completed cleanup allows sequential reopen;
early disposal or failure does not. Root closure rejects retained ports. Identity transfer still
requires the same initialized effective token along the complete generated identity path.

The default `IAsyncEnumerable<TItem>` and typed `StreamSource.ConsumeAsync` facade are unchanged.
Private ports never appear in a public signature. Concrete handler cursors and existing identity,
group advance and identity lease paths are retained. There is no owner sharing, rare-state split,
producer pooling or new DI lifetime in this change.

Compatibility: `IStreamMiddleware<TRequest,TItem>.OpenAsync(TRequest,
IStreamUpstream<TRequest,TItem>, CancellationToken)` remains unchanged so existing source
implementers and already-compiled consumers still bind to their original contract.
This repository has no PublicAPI.Shipped/Unshipped analyzer baseline; package validation is the
existing conditional binary compatibility gate. It must be run against the applicable released
package baseline before publication. No existing released member is changed.

Performance is unmeasured. Compare the separate ordinary and opt-in series with the same source
cursor, token checks, Task.Yield, probes, consumer and cleanup. Reject new boxing, added per-item
dispatch, ownership changes, or time regressions. Object-size savings are a layout hypothesis.

## Standard contract update (2026-11-11)

The experiment above is historical. The generic overload is now on IStreamMiddleware itself;
IStructStreamMiddleware is an optional compatibility name. The generator automatically selects the
proved concrete generic implementation. Standard benchmark wrappers and the logging sample have
been migrated. See [migration and fallback boundaries](../migrating-stream-upstream.md).
The earlier unmeasured/experimental statements do not supersede the preserved WP04 adoption results.
New final measurement and package/native verification must refer to this updated source, not the old canonical d2f3 build.
