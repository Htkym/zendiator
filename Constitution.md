# Zendiator Constitution

[日本語](Constitution.ja.md)

This document describes Zendiator's architecture, design principles, and quality standards. It is a reference for users evaluating the library and developers working on its implementation. See the [README](README.md) for usage and [source layout](docs/source-layout.md) for placement details.

The following sections cover the library's responsibilities, public contracts, source generator, source organization, and development practices. Keep the implementation, tests, and documentation consistent when changing these contracts or principles.

## Library purpose

Zendiator is a Mediator for .NET 10 / C# 14 that generates typed dispatch at compile time. It connects user-defined messages, handlers, and behaviors through simple registration and a low-overhead execution path.

- Use generated APIs for concrete request types. Do not introduce request dispatch through `Send(object)` or a general-purpose runtime API accepting `IRequest<TResponse>`.
- Support single requests, multi-handler requests, notifications, synchronous requests, and streams. User-defined `Result` types and nullable types work as ordinary responses.
- Do not depend on runtime assembly scanning, reflective invocation, or `dynamic` for dispatch. Distinguish recording type and assembly information during registration from executing a dispatch.
- Aim for the fastest implementation without sacrificing usability, type safety, correctness, or readability.
- The library is a pre-V1 preview; its APIs and architecture are not yet frozen. Evaluate changes against usability, correctness, and maintainability, and document their impact on users.

Parallel notification delivery, fire-and-forget, persistence/outbox, and built-in logging, validation, or Result mapping are outside the current scope. Application-specific processing belongs in handlers and behaviors.

## Construction and project boundaries

The normal entry point is `services.AddZendiator()`. Do not require a custom ServiceProvider, ProviderFactory, optimized-mode selection, or additional initialization call.

```csharp
services.AddZendiator();
```

Build the provider or host using standard APIs. Do not build an extra ServiceProvider while registering services with a host. Use the `AddZendiator` configuration lambda only when specifying a namespace, assemblies, behaviors, or other configuration.

| Project | Responsibility |
| --- | --- |
| `src/Zendiator.Abstractions` | Message, handler, continuation, and behavior contracts, plus configuration attributes. net10.0, no external package dependencies |
| `src/Zendiator` | DI configuration recording, registration entry points, dependency capture, and dispatch lifetime management. net10.0 |
| `src/Zendiator.SourceGenerator` | Roslyn analysis, diagnostics, and generation of typed APIs, dispatch, and registration. netstandard2.0 |

Distribute two packages, `Zendiator.Abstractions` and `Zendiator`, versioned together. Ship the generator inside `Zendiator` under `analyzers/dotnet/cs`; do not make Roslyn a runtime dependency of consuming applications. Supply the interceptor configuration needed for DI registration through the package's `buildTransitive` assets.

Consumers can keep Contracts, Application, and Host separate. Contracts references only Abstractions; Application contains handlers and generation configuration; Host calls Application's registration method. Do not introduce a Contracts-to-Application back-reference just to configure generation.

## Execution path and dependency lifetime

There is one execution path, using a lazy cache per mediator. DI constructs and disposes dependencies; a mediator retains the first resolved instance for its own lifetime.

- Register with `TryAdd` and preserve existing registrations. New registrations default to Scoped.
- `ZendiatorServiceResolver` belongs to a mediator instance. Resolve each service type from that mediator's bound provider when first needed.
- Reuse Transient handlers and behaviors within the same mediator too. This is not a contract of resolving a new Transient instance for every send.
- Do not share instances through static caches or another provider's or mediator's cache. Sharing performed by DI itself for Singleton or Scoped registrations follows the container's registrations.
- Short-circuiting must not resolve or construct downstream behaviors or handlers that have not been reached. Do not break this property through eager resolution of all dependencies.
- Synchronize cold resolution per mediator. Warm retrieval avoids the initialization lock. Thread safety of user-defined handlers and behaviors remains a separate responsibility.
- Do not dispose DI-owned dependencies twice. Disposing a mediator prevents subsequent dispatch; it does not cancel or join operations already running.
- Keep the scope alive until sends and stream enumeration finish. Do not reuse a mediator or scope after disposal starts, even if disposal throws.
- Avoid Singleton mediators capturing Scoped dependencies. Explain that lazy resolution can defer some validation until the first dispatch.

See [construction and dispatch lifetime](docs/optimized-dispatch.md). Treat lifetime changes as user-visible contract changes, not merely as performance optimizations.

## Preserve dispatch semantics

Behaviors are DI-resolved classes; generated continuation nodes are `readonly struct`s. Pass continuations as generic `TNext` values and avoid allocations or boxing from delegates and interface variables. When explicit implementation or inheritance prevents a direct concrete call, generate an interface call based on semantic analysis.

- Lower behavior `Order` values wrap outermost. Diagnose duplicate types or orders and incompatible contracts.
- Check type-argument mappings and constraints when applying open generics. Do not skip constraint checks for speed.
- Preserve request and CancellationToken replacement and sequential repeated `next` calls. Parallel continuation calls or retention after completion are not guaranteed.
- Do not change null handling, cancellation, exceptions, handler ordering, short-circuiting, or disposal through optimization or code movement.
- Notifications have an erased dispatch route for known notification types. Do not confuse this with concrete request APIs or claim that every API is free of runtime type checks.
- Handle `ref struct` requests through synchronous routes. Diagnose invalid combinations involving asynchronous routes, ref-like responses, or stream items.
- Start streams on the first `MoveNextAsync`. Handle both API and enumeration cancellation, linking tokens only when necessary. Distinguish stream startup from steady-state enumeration costs.

## Source Generator responsibilities

The entry point is `ZendiatorGenerator.cs`. Separate configuration, discovery, route analysis, projection into emission models, code generation, and diagnostic reporting.

| Location | Responsibility |
| --- | --- |
| `Analysis/Configuration` | Validate supported configuration expressions, generation modes, namespaces, assemblies, and configuration agreement |
| `Analysis/Discovery` | Discover types and contracts; maintain the per-analysis interface index |
| `Analysis/Routes` | Build single, multi-handler, synchronous, notification, and stream routes and their pipelines |
| `Analysis/EmissionModelFactory.cs` | Project analysis results into the values needed for generation |
| `Models` | Mutable analysis data and models handed to emission |
| `Symbols` / `Diagnostics` | Shared symbol and generic-constraint operations, and diagnostic definitions |
| `Emission` | Feature-specific templates and code-fragment generation |

Analyze the current compilation and explicitly included assemblies. Also discover the exact request types referenced by handler contracts. Do not execute arbitrary user code inside the generator to obtain configuration.

Connect DI-first configuration to the generated registrar through interceptors. Compare recorded runtime configuration with the generated structure using a fingerprint, keeping normalization identical on both sides. Keep `ServiceLifetime`, a runtime registration value, separate from the generated structure. Do not silently fall back to runtime scanning for unsupported configuration or unconnected registration calls.

DI-first and attribute-based configuration share the same emission and execution design. Diagnose conflicts such as mixing DI configuration lambdas with attributes in one compilation.

### Incremental boundaries

Keep a clear boundary between semantic analysis using Roslyn symbols and emission using values alone.

- Do not retain `Compilation`, symbols, or syntax nodes in the incrementally compared `GenerationModel` and `GenerationTarget`, including their reachable child models.
- Make emission models immutable. Compare every fact affecting output, including type names, nullable annotations, constraints, ordering, and whether direct calls are possible.
- Compare arrays by their elements. Do not assume `ImmutableArray` alone provides structural equality; use `EquatableArray<T>` for content comparison.
- Keep interface indexes and type-name caches within one analysis. Contract-definition helpers that retain symbols must not enter the cached emission model.
- Separate mediator content from interceptor location data. Moving a registration call alone should not regenerate the mediator body.
- Report diagnostics at the current source locations. Equal generated text does not justify reusing stale diagnostics or locations.
- Preserve deterministic ordering and isolation between compilations and concurrent runs.

Currently, compilation changes rerun semantic analysis; equal emission models skip template expansion. This is not per-type incremental semantic analysis or per-route output generation. A handler change affecting generated code regenerates the mediator body. Do not claim that all work for unrelated edits is skipped without explaining this boundary.

### Readable code generation

Express static code blocks with C# raw string literals wherever practical. Use interpolated raw strings for substitutions instead of escape-heavy strings or fragmented `Append` chains.

Extract dynamic fragments into small methods with clear responsibilities. Group related arguments by meaning, such as target, routes, or contracts, instead of passing many unrelated parameters. Do not replace this with a giant all-purpose Context or an unused generic Builder framework.

`SourceEmitter` reads a completed model and creates a `StringBuilder` inside each call. Feature methods receive the output builder, route, and route index when needed. Avoid hidden ordering dependencies and mutable output state shared across generation runs.

## Source organization and change management

- Group files by responsibility. Preserve dependency direction between the library, generator, tests, samples, and benchmarks.
- Prefer one type per file. Closely related small helpers or same-name interface variants with different generic arity may stay together when that improves readability.
- Keep project definitions, executable entry points, and assembly-level configuration easy to find. Do not add deep folder hierarchies to small projects merely for uniformity.
- Separate folder organization from namespace and public API changes. Use SDK-style file inclusion instead of unnecessary explicit Compile item lists.
- Fix the source of generated code, such as analysis, templates, or generation scripts, rather than editing generated output as the solution.
- Prefer existing repository code, the standard library, and installed dependencies. Do not add abstractions, configuration, or packages for hypothetical future needs.
- Coordinate changes with other contributors and preserve unrelated work. Keep commits focused and make mechanical moves distinguishable from behavioral changes.
- Keep release tags, package versions, and release documentation aligned. Publish the same artifacts that passed release verification.

The README is the user entry point, this constitution guides design decisions, and `docs/` contains detailed contracts, layout, measurements, and release records. Link these documents instead of continually duplicating explanations. Keep the English and Japanese READMEs consistent.

## Performance acceptance criteria

Preserve zero-allocation behavior on the paths that provide it. An optimization must not add allocations to those paths. Distinguish cold construction from warm dispatch, synchronous completion from asynchronous suspension, and dispatch from the entire scope lifecycle.

- Measure runtime performance and generator performance separately. For the generator, measure time and allocation for initial generation, unrelated edits, and handler changes affecting output.
- Compare before and after with the same inputs and execution conditions, checking output or behavioral equivalence. Retain repeated samples and variability; do not cherry-pick results.
- Record the revision, SDK/runtime, configuration, warmup, and measurement scope. Disclose lifetime and feature differences in comparisons with other libraries.
- Use exploratory measurements to choose a direction. Restrict published performance claims to the measured revision, routes, and conditions. Account for the measurement floor of very short operations, and do not extrapolate results to unmeasured pipeline depths.
- Do not overlook changes to usability, lifetime, disposal, or safety merely because a benchmark becomes faster.

## Minimal tests that preserve quality

Do not optimize for test count. Consolidate tests and expensive operations that verify the same guarantee, while retaining boundary tests that detect distinct failures.

- Generator tests cover generated APIs, compilation diagnostics, constraints, configuration disagreement, incremental behavior, diagnostic locations, and concurrent isolation.
- Check tracked incremental steps as well as equal output text to establish reuse of template expansion. Cover transitions into invalid states and recovery after correction.
- Do not emit IL repeatedly in every successful case. Normally check compilation diagnostics and reserve IL emission for representative cases or necessary reference-assembly construction.
- Runtime tests cover dispatch results, pipelines, cancellation, ordering, lifetime, disposal, and stream boundaries. Preserve allocation regression tests as guarantees under their stated conditions.
- For substantial generator refactoring, a passing build alone does not establish equivalence. Compare generated output and diagnostics for representative consumers.
- Run checks relevant to the change, followed by the required integration checks. Repeat checks when changes or failures warrant it. Document completed checks and any remaining validation separately.

The authoritative CI checks are in [.github/workflows/ci.yml](.github/workflows/ci.yml): Release build, tests, formatting, package contents, samples, package-reference smoke tests, and Native AOT checks. Success with ProjectReference alone does not establish package distribution or Native AOT compatibility.

Use [Directory.Build.props](Directory.Build.props) for version definitions and [publish-nuget.yml](.github/workflows/publish-nuget.yml) for publication. Resolve AOT and trimming warnings or document the supported boundary; do not suppress them merely to pass verification.
