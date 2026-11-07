# Struct upstream opt-in comparison

This is a separate, unmeasured WP04 series. Original `GeneratedCases.cs`, `Work.cs`, native
handler/cursor types, token checks, `Task.Yield`, probes and cleanup are unchanged.

Controls: `ZendiatorStream0`, `ZendiatorStream5`, `ZendiatorRelay5`.
Opt-in: `ZendiatorStructUpstreamStream0`, `ZendiatorStructUpstreamStream5`, `ZendiatorStructUpstreamRelay5`.
The new benchmark class bodies are copied from those controls; only class/evidence names,
registration and generated composition type differ. The new registration declares the same
routes, ordering and handlers, replacing middleware with sealed implementations of
`IStructStreamMiddleware`. Their legacy and generic bodies perform identical Work calls and
return upstream unchanged. The zero-stage pair controls for unrelated code/JIT layout changes.

Run `--validate-struct-upstream <output>` before measurement. Its separate correctness evidence
compares both public pull and typed terminal paths, synchronous/asynchronous input, counts
0/1/16/1024 and full/early consumption, including probe events, items and cleanup. Existing
389-case correctness evidence remains a separate required gate and is not overwritten.

Formal paired measurements have not been authorized or run. Preserve separate output directories
for ordinary and opt-in series, record exact source/generator/runtime hashes and method parameters,
and interleave paired rounds. Compare A1/early, full and Relay; allocation savings without time
parity are not acceptance. Measure WP04 alone before combining it with other experiments.

The candidate matrix is `benchmarks/performance-plan/wp04-struct-upstream.json`.
Its ten entries change only Type from the agreed ordinary `wp02.json` (SHA256
03B1E4CAD9084BA3556FC65FEC7E49F939A56F859270D0ECF3B8AD839E7406A2).
`wp04-struct-upstream-pairs.json` records all ten explicit ordinary/opt-in pairs.
Keep these as separate matrices; the shared library/orderer guards are unchanged.

`interleaved_plan.py` and `run_interleaved.py --dry-run --group custom --matrix ...`
validate the new matrix without running a benchmark. The parity gate also discovers all
three types through BenchmarkDotNet and invokes the existing C# orderer/library classifier.
`--validate-struct-upstream-child <type> <output>` records actual setup/cleanup-only child
identity in a separate process. These identity checks are not formal measurement artifact
proofs. The formal driver's own preserved-child artifact gate remains mandatory.

The initial local prototype passed the complete Release suite, focused old/opt-in contract
fixtures, the original 389-case correctness gate, separate 96-case parity gate and package-only
managed/Native AOT smoke. This establishes correctness evidence, not performance acceptance.
A local checkpoint and a clean worktree are required by the unchanged formal driver.