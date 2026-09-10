# Known limitations (0.1.0)

0.1.0 の現行制限。性能値は `0.1.0-release-notes.md` に記載の正式測定だけを根拠にする。
RC1／Hardening-1 の旧数値は現行値として使用しない。

## Async Stream allocation — KL-01 / OPT-stream-async

The async 1024-item, zero-behavior run allocates 113,959 B versus 360 B direct: an additional 110.94 B/item. Current measurements,
including the direct baseline and behavior count, are in [the 0.1.0 release notes](0.1.0-release-notes.md).
The old Hardening-1 value of approximately 109 B/item is `HistoricalSuperseded` and is not inherited as a current value.
This is a performance concern, not a correctness rollback reason. Optimization is deferred to a separate phase (`Deferred`,
not a 0.1.0 blocker, no correctness defect).

## Native AOT generic value-type closure — KL-02

Native AOT cannot dynamically construct the tested open-generic DI handlers closed over value types.
The request and stream probes report the same documented LIMIT. Reference-type closures pass AOT04/AOT10.
The verified scope is AOT01-AOT12 on win-x64 (verified at release with package consumers); it is not an every-RID guarantee.

## Stream enumeration — KL-03 / KL-04

Re-enumerating the same returned stream and concurrently enumerating it are not guaranteed.
Call `StreamAsync` again for a fresh stream. Early termination and disposal are covered separately by tests.

## Unsupported dispatch — KL-05 / KL-06

Parallel Publish is unsupported; notification dispatch is sequential.
Runtime-object request/stream dispatch is unsupported. The notification erasure route is a separate supported path.

## IDE behavior — KL-07

IDE integration is `UnverifiedCurrent`. CLI diagnostics and generated consumers do not establish IDE behavior.

## Other RIDs — KL-08

Native AOT beyond win-x64 is unverified. Other RIDs remain future work and are not 0.1.0 blockers.

## Send performance after the correctness fix

Removing the custom lifetime cache changes the cost of each invoked node. Standard DI remains the source of truth.
Current [Send measurements](0.1.0-release-notes.md) replace prior RC1 numbers; Send is measured,
not unmeasured.
Scoped/Singleton warm sends with synchronously completing handlers, full scope lifecycle and generic responses
have different costs and are reported separately. No fastest or general allocation-free claim is made.

## Publication boundary

Local package/AOT/manifest checks do not establish GitHub-hosted workflow execution, OIDC or NuGet ownership.
The final publication must use the verified CI artifact from the final tag commit (`v0.1.0`).
`0.1.0` is a `0.x` release: its API is not frozen permanently and may change before `1.0.0`.
Preview-to-preview binary compatibility is not guaranteed.
