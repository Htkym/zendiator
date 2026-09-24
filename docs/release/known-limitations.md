# Known limitations (0.3.0)

These boundaries apply to the 0.3.0 design. See the [release notes](0.3.0-release-notes.md)
and [construction and dispatch lifetime](../optimized-dispatch.md) before upgrading.

## Dependency capture and lifetime

Each mediator lazily captures the first resolved instance of each handler or behavior
service type, including Transient registrations. A new send or retry does not imply a
new instance. DI still constructs and disposes dependencies and may share Scoped or
Singleton instances across mediators according to its registrations.

Keep the scope alive until dispatch and stream enumeration finish. Do not use a
mediator after its scope is disposed. Disposal does not cancel or join ongoing
operations. Concurrent initialization is synchronized, but handlers and behaviors
must support any application-level concurrent use themselves.

Lazy resolution means `ValidateOnBuild` alone cannot validate every dispatch dependency.
Exercise the routes used by the application, especially when changing service lifetimes.
Do not use a Singleton mediator to capture Scoped services.

## Allocation

Not every operation or user handler is allocation-free. Cold dependency resolution,
scope creation, asynchronous suspension, and stream enumeration have separate costs.

## Native AOT generic value-type closure

The tested open-generic DI handlers closed over value types require runtime generic
construction that is unavailable under Native AOT. Both request and stream probes
report this limitation. Reference-type closures are covered by AOT04/AOT10.
The release verification target is AOT01-AOT12 on win-x64 with package consumers;
it is not an every-RID guarantee.

## Stream enumeration

Re-enumerating the same returned stream and concurrently enumerating it are not
guaranteed. Call `StreamAsync` again for a fresh stream. Early termination and disposal
are covered separately by tests.

## Unsupported dispatch

Parallel Publish is unsupported; notification dispatch is sequential.
Runtime-object request/stream dispatch is unsupported. Notification erasure is a
separate supported path. Parallel continuation calls and retention of continuations
after completion are not guaranteed.

## Other RIDs

Native AOT beyond win-x64 is unverified.
