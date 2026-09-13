# Known limitations (0.2.0)

These boundaries apply to the 0.2.0 design. See the [release notes](0.2.0-release-notes.md)
and [construction and dispatch lifetime](../optimized-dispatch.md) before upgrading.

## Dependency capture and lifetime

Each mediator lazily captures the first resolved instance of each handler or behavior
service type, including Transient registrations. A new send or retry does not imply a
new instance. DI still constructs and disposes dependencies and may share Scoped or
Singleton instances across mediators according to its registrations.

Keep the scope alive until dispatch and stream enumeration finish. Do not use a
mediator after it or its scope is disposed. Disposal does not cancel or join ongoing
operations. Concurrent initialization is synchronized, but handlers and behaviors
must support any application-level concurrent use themselves.

Lazy resolution means `ValidateOnBuild` alone cannot validate every dispatch dependency.
Exercise the routes used by the application, especially when changing service lifetimes.
Do not use a Singleton mediator to capture Scoped services.

## Performance evidence and stream allocation - KL-01

The [0.1.0 formal measurements](../performance.md) describe an older implementation,
including its async-stream allocation costs. They do not establish current timings or
allocation counts. No new formal cross-library ranking is published for 0.2.0.
Distinguish cold resolution, warm dispatch, scope lifecycle, stream startup, and
steady-state enumeration; not every operation or user handler is allocation-free.

## Native AOT generic value-type closure - KL-02

The tested open-generic DI handlers closed over value types require runtime generic
construction that is unavailable under Native AOT. Both request and stream probes
report this limitation. Reference-type closures are covered by AOT04/AOT10.
The release verification target is AOT01-AOT12 on win-x64 with package consumers;
it is not an every-RID guarantee.

## Stream enumeration - KL-03 / KL-04

Re-enumerating the same returned stream and concurrently enumerating it are not
guaranteed. Call `StreamAsync` again for a fresh stream. Early termination and disposal
are covered separately by tests.

## Unsupported dispatch - KL-05 / KL-06

Parallel Publish is unsupported; notification dispatch is sequential.
Runtime-object request/stream dispatch is unsupported. Notification erasure is a
separate supported path. Parallel continuation calls and retention of continuations
after completion are not guaranteed.

## IDE behavior - KL-07

IDE behavior is not established by CLI diagnostics and generated-consumer tests.
No separate IDE verification is claimed for this release.

## Other RIDs - KL-08

Native AOT beyond win-x64 is unverified.

## Incremental generation

Compilation changes still rerun semantic analysis. Equal value-based emission models
skip template expansion; this is not per-type incremental analysis. A handler change
affecting output regenerates the mediator body. Interceptor locations are tracked
separately so moving a registration call need not regenerate that body.

## Publication boundary

Local checks do not establish GitHub-hosted workflow execution, OIDC authentication,
or public-feed availability. Publication uses the verified CI artifacts built from
the final `v0.2.0` tag commit. `0.2.0` is a pre-V1 release: APIs and architecture may
change before `1.0.0`, and preview-to-preview binary compatibility is not guaranteed.
