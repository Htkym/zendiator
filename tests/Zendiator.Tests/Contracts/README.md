# Canonical contract regressions

These test-only controls make the selected 0.5 semantics executable before the
new generator backends are connected. They do not change the current product API
or establish a performance comparison. Existing generated-code tests remain intact.

Run the focused suite with the repository's SDK:

```powershell
dotnet test tests/Zendiator.Tests/Zendiator.Tests.csproj -c Release --filter FullyQualifiedName~CanonicalContractRegressionTests
```

`ContractEvents` records family, branch, invocation, stage, request/token/error/service
identity, resolution kind, ambient value, culture and synchronization context.
Object identities are reference-based and local to a recorder; value requests retain
their value representation. Future backend adapters can use the same recorder and
compare entire event records. Resolve events in the controls mark an attempt;
adapters with real DI services can attach the service and first/reuse information.

`ContractAwait` distinguishes a synchronous callsite throw from the status of a
returned ValueTask, snapshots status before awaiting, and consumes it once.
`ContractOnce` is a real suspending source that fails on double GetResult.

| Control | Required observations |
| --- | --- |
| Stream | Lazy first Move, cheap synchronous argument guards, 0/1/16/1024 items, cached Current, early break and independent replay cursors |
| Tokens | API/enum/same/distinct tokens, owned links and cleanup after move/dispose failure |
| Send | Typed saved requests, entered stages, nested unwind, exception replacement and canceled/faulted boundaries |
| Context | Ordinary synchronous callback effects, native async callback isolation, caller and suppressed-flow behavior |
| Ownership | Caller-owned DI scope/services, scoped reuse and transient factory override with separate enumerators |
| Multiple/void | Sequential branches, Span validation and atomic commit, genuine void/non-generic ValueTask, notification stop-on-failure |

The synchronous Send control is ordinary nested C#; the async control has one
orchestration boundary and per-invocation state. They compare complete normal
traces after normalizing the dispatch-family label. Stream Current is read once
inside Move and cached for the caller. Opaque wrapper probes preserve zero/one/two
next calls and explicit request/token replacement.

This suite is the P01 contract gate. Product emitter integration, hook capability
and filter/map matrices, full wrapper binding, open generic/ref-like compiler
fixtures, lowest-host compatibility, generated backend differential tests, and
allocation/code-size acceptance remain in their planned G/C/S/H/N/I tasks.
Do not equate this class's case count with the preserved 394 benchmark gates.
