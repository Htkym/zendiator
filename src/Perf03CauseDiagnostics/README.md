# PERF-03 cause diagnostic control

These files compile only into the use-case benchmark through its explicit compile link. Production runtime and generator sources remain unchanged.

V0 copies the saved named Stream0 and Relay5 generated routes from the accepted PERF-02a source tree. `originals/` retains the source bytes. The directory-local `.gitattributes` marks only the generated snapshots, copied cursor state and original text archives as `-text`, preserving their BOM and line endings through Git storage and checkout. `snapshot-provenance.json` binds their original and transformed hashes, source revisions and tree, saved-build analyzer binary, and separate prebuild workspace analyzer identity. The transformations relocate only namespace, generated-service alias and cursor-state references. The factory retains the public request null check and uses the same cached native DI entry as the ordinary generated route.

The separate saved-consumer gate compares the 24 input cases and two isolated controls, including concrete producer/token/cache identities and sequential cleanup/failure observations. Compilation, alias compatibility and gate execution are required before declaring V0 faithful or starting a factor contrast. Both routes share the new small composition; this parity does not reproduce the original BenchmarkStreams whole-composition layout or JIT behavior.

Consumer-observed incomplete results are a separate preflight. Raw generated decision-site counters and deterministic registration/reentry probes are still required for causal pending-path attribution. `Task.Yield` and 1024 produced items do not prove 1024 pending moves. No timed callback contains counters, and this control makes no ns-per-pending claim.
