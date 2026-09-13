# Source layout

Folders group related code without changing namespaces or public APIs. Project
files, executable entry points, and assembly-level configuration remain at the
project root. Small, already focused projects do not need extra folder layers.

## Library

| Project | Folders |
| --- | --- |
| `src/Zendiator.Abstractions` | `Configuration`, `Requests`, `Notifications`, `Synchronous`, `Streams` |
| `src/Zendiator` | `Configuration`, `DependencyInjection`, `Dispatch` |
| `src/Zendiator.SourceGenerator` | `Analysis`, `Models`, `Symbols`, `Diagnostics`, `Emission` |

The generator entry point is `ZendiatorGenerator.cs`. `Analysis` contains
configuration processing, discovery, and route analysis in separate subfolders.
`Models` holds the data passed between analysis and emission. `Emission` contains
the raw-string templates and code-fragment helpers, grouped by generated feature.

## Tests

Runtime dispatch tests are grouped under `Dispatch`, `Streams`, and `Lifetimes`.
Larger fixture collections use `Messages`, `Handlers`, `Behaviors`, `Support`, and
`Composition`. Stream-specific runtime fixtures live under `Fixtures/Streams`.
Small fixture collections and narrowly scoped test projects retain their existing
layout rather than adding a folder for every file.

## Samples

The application separates `Handlers` from `Behaviors`. Contracts separate
`Messages` from result and data `Models`. Application registration and assembly
markers remain easy to find at the project root.

## Benchmarks

The main comparison project keeps benchmark methods in `Benchmarks` and container
construction in `Hosts`. Its fixtures are grouped by comparison library under
`Fixtures`, replacing the large flat `Shared` folder.

Feature and DI benchmarks separate benchmark classes, composition, and fixtures.
Direct-call reference implementations in the feature project live in `Baselines`.
Generated `Fixtures.g.cs` files remain at their existing paths; reorganizing
handwritten sources does not change generator scripts or historical result files.

## Adding files

Place a new file with code of the same responsibility. Keep namespaces stable
unless an API change is intentional. Do not move project files or generated
artifacts just to make every directory look uniform. SDK-style projects include
C# files in subfolders automatically, so individual compile-item lists are not
needed.
