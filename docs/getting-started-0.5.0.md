# Getting started with 0.7.0 development

0.7.0 is unreleased. This guide describes its development contracts, including
[route-local capture changes](release/0.7.0-release-notes.md). The published
0.6.0 packages retain their existing behavior; see the
[0.6.0 release notes](release/0.6.0-release-notes.md).

The sample commands below use matching published 0.6.0 packages from NuGet.org
and do not exercise the new capture behavior. To try 0.7.0, build matching
Zendiator.Abstractions and Zendiator packages from this checkout into a local
NuGet feed. Restore the sample from that feed and NuGet.org, and pass
`-p:ZendiatorPackageVersion=0.7.0` to both restore and run. This does not assume
that a 0.7.0 package is available from NuGet.org.

## Run the published 0.6.0 sample

The [GettingStarted project](../samples/Zendiator.Sample.GettingStarted/Zendiator.Sample.GettingStarted.csproj)
requires .NET 10 and C# 14. Its [source](../samples/Zendiator.Sample.GettingStarted/Program.cs)
shows synchronous Send, asynchronous Send and Stream with ordered hooks.
From the repository root:

```powershell
$version = '0.6.0'
$project = 'samples/Zendiator.Sample.GettingStarted/Zendiator.Sample.GettingStarted.csproj'

dotnet restore $project -p:ZendiatorPackageVersion=$version --source https://api.nuget.org/v3/index.json
dotnet run --project $project -c Release --no-restore -p:ZendiatorPackageVersion=$version
```

Expected output:

```text
Send: 42
SendAsync: Hello, Ada!
StreamAsync: 20, 40
```

## Scope and asynchronous results

Resolve the generated `IZendiator` from a DI scope. Keep that scope alive until
the operation or Stream enumeration, including cleanup, has finished. Let DI
dispose its handler and hook services. Generated registration uses a Scoped
`IZendiator` entry and Transient defaults for handlers, hooks and session
factories. Each Mediator reuses captured services, either per dependency or as
a route-local set for eligible SendAsync hook routes described below.
Concurrent successful initializations share the published winner; a failed
initialization still reports its own error. Await each returned `ValueTask` once.

An eligible plain `SendAsync` route with no hooks, or only synchronous Before
hooks, and no factories, wrappers or sessions returns the handler's original
`ValueTask`. Dependency resolution and synchronous callbacks run during the
call. Their failures, including cancellation, can throw before a `ValueTask` is
returned. Synchronous changes to `AsyncLocal`, culture or `SynchronizationContext`
are visible to the caller. Other asynchronous failures arrive when awaiting the
result. Put both the call and the await inside your `try` block.

<a id="route-local-capture-for-asynchronous-hooks"></a>

## Route-local capture for SendAsync hooks

Eligible closed `SendAsync` routes capture a route-local dependency set. This
includes routes with only synchronous Before hooks when the handler and all hooks
are borrowed reference types, use no Zendiator session/factory contracts or
wrappers, and include at least two distinct receiver types. Open generic route
definitions, value-type receivers, handler-only routes and other unsupported
shapes retain their existing capture behavior. Ordinary DI implementation
factories are not Zendiator session/factory contracts.

On the first Send, after the initial request and cancellation checks, DI resolves
all receivers before the first hook runs. Any required construction takes place
before callbacks begin. An outer hook failure can therefore leave already
constructed downstream hooks and the handler unused. If set construction fails,
hook callbacks are not entered. Native constructor injection does not check
cancellation between arguments. A null required receiver prevents set publication;
a failed initialization may be retried. Native DI may already have obtained later
arguments before the constructor rejects a null receiver.

The synchronous-Before-only path still runs dependency resolution and callbacks
during the call, can throw synchronously, and returns the handler's original
`ValueTask`. Other asynchronous hook routes keep their existing asynchronous
execution boundary.

The Mediator reuses the successful set for that route. Separate routes can capture
different Transient instances of the same receiver type. Register a receiver as
Scoped or Singleton when its identity must be shared across routes. DI owns
construction and disposal, including unused receivers and concurrent losing
initializations; the set does not dispose borrowed services. Keep the owning scope
alive through dispatch and cleanup.

A receiver used only through route-local sets has no separate per-type Mediator
capture. Resolving it through the Mediator's explicit `IServiceProvider` interface
delegates to the original provider; a Transient registration can produce a new
instance on each resolution. If another direct route uses that type, its existing
per-type capture remains. Scoped and Singleton identity continues to follow DI.

Explicit `EagerDependencies` keeps its existing construction contract. If a
selected receiver of a synchronous-Before-only route is listed, that entire route
retains direct per-type capture and uses the injected instance. Listed services
are obtained when DI resolves the Mediator. Unlisted receivers retain their
existing per-use capture rules; native DI may obtain them earlier as constructor
dependencies of listed services. Unrelated eligible routes still use route-local
sets. This does not enable
EagerDependencies for previously rejected asynchronous hook routes.

Use generated composition registration so DI can construct the dependency set.
A custom provider must resolve the generated set as well as its receivers. Its
generated name, members and layout are implementation details, not an API for
manual construction.

With Microsoft.Extensions.DependencyInjection, `ValidateOnBuild` can inspect the
declared constructor dependency graph of registered route sets. It does not
validate every dispatch dependency, arbitrary DI factory body, open generic
closure or later operation. Exercise the routes your application uses and enable
`ValidateScopes`; a Singleton Mediator must not capture Scoped services.

For asynchronous Hooks, error callbacks receive the current failure. An error
callback can replace it, and a failing cleanup callback takes precedence. Entered
Hook scopes unwind from inner to outer before the returned `ValueTask` completes.
Ordinary exceptions keep their original instance and throw origin unless a
callback replaces them. Internal rethrow frames and `FirstChanceException` event
counts are implementation details.

## Closed dependency storage

Closed dependency slots are inline by default, at every composition size. This
controls storage only: dependency or route-set activation stays deferred until
first use, and the Mediator reuses the first published successful result without
owning its disposal. An eligible route set resolves its receivers together as
described above.

For a composition known to touch few dependencies in short-lived scopes, select
lazy blocks on its marker:

```csharp
[ZendiatorComposition("reads", ClosedServiceCache = ClosedServiceCacheMode.LazyBlocks)]
public sealed class Reads;
```

`ClosedServiceCacheMode.Inline` explicitly selects the default. `LazyBlocks`
groups at most 16 closed dependencies per block and allocates a block after its
first successful native resolution. It applies to small compositions too; a
composition with 1–16 closed dependencies uses one block, and one with no closed
dependencies creates none. The choice is fixed during generation, with no
layout branch added to Send. Undefined enum values produce ZEN0035.

Compositions that previously selected blocks solely from their dependency count
now use inline slots. Choose `LazyBlocks` explicitly when its tradeoff fits your
workload. A prior Windows x64 comparison of lazy-block against inline storage,
before this explicit option existed, used 64 dependencies. Sparse cold use
touching one block allocated 336 B less per operation. A dense 63-send batch
allocated 96 B more, and warm sparse dispatch had a 29.2% higher mean
(+0.258 ns). These observations do not guarantee gains for sparse dependencies
spread across blocks or frequent warm reuse. The block size is not an optimized
boundary for every workload.

Open generic captures use a separate cache, independent of this setting. It keeps
up to four closed types before dictionary promotion. In a prior comparison
against dictionary storage, a one-type cold scope allocated 488 B less;
five-type cold promotion allocated 160 B more with an 8.35% higher mean.
Four is not a universally optimal capacity,
and these results do not establish a gain for all scopes.

## Stream execution

For ordinary asynchronous input, start with `IStreamHandler<TRequest,TItem>.Handle`
returning an `IAsyncEnumerable<TItem>`, often a compiler async iterator. Choose
`IStreamSourceHandler<TRequest,TItem,TCursor>` when a named class or struct cursor
needs custom state or integration. Both support the same pipeline; select exactly
one source contract for each route. This choice does not guarantee a performance
result. See [input and lifetime choices](streaming-iterators.md#choose-an-input-contract)
for both examples and the direct/guarded contract table.

The sample opens a named NumberCursor through IStreamSourceHandler. Dedicated Filter/Map
hooks use the typed pipeline. StreamAsync returns a class implementing standard
IAsyncEnumerable. This example uses `StreamSource(...).ConsumeAsync(...)`, a readonly
plan with a fresh private owner per consumption, and a readonly sink with a shared List
result. The plan is not IAsyncEnumerable.

The standard source object is allocated at the `StreamAsync` call. The guarded
path defers DI/Open until the first move; a flat iterator route with two
noncancelable tokens instead acquires the input at `GetAsyncEnumerator`. Its
consumer must await `DisposeAsync` even after EOF or failure. Use `await foreach`
for normal enumeration, consume each `ValueTask` once, and keep the DI scope alive
through disposal. Forward API and enumeration cancellation tokens as required by
your application, including HTTP `RequestAborted`, rather than dropping them to
select the direct path. Custom enumerables must implement their cancellation and
disposal contracts; they need not behave like compiler iterators.

The typed terminal in this sample stays guarded. Await each move before another
move or disposal. A sink's false is a normal stop; sink exceptions are not source
middleware input. No Channel bridge or independent producer task is added. This
example does not guarantee lower time or allocation.

For once-per-enumeration preprocessing with no cursor or paired cleanup, use the
existing [Before example](streaming-iterators.md#choose-before-or-middleware).
Keep middleware when it replaces the request, returns an empty Stream normally,
surrounds enumeration with cleanup, or performs work on each upstream reopening.

See [migration, HTTP and the detailed contracts](streaming-0.5.0.ja.md).

## Use typed queries and an update command in one App

The [GenericLookup sample](../samples/Zendiator.Sample.GenericLookup/) returns
`T` from `Lookup<T>` and `InventoryStatus` from `StatusQuery`. It awaits
`RefreshInventory` before querying the updated state.

This example uses class requests and DI-owned class handlers. These handler-only
`SendAsync` routes can omit empty `Pipeline` declarations. Await each returned
`ValueTask` once and keep the scope alive until the asynchronous operation finishes.

## Use several dispatch families in one App

The [ComposedApp sample](../samples/Zendiator.Sample.ComposedApp/) combines seven
routes for typed queries, an update command, synchronous Send, SendAll and Stream
in one App and DI scope. Each route declares its own pipeline, preserving hook
order and the wrapper's own caching and retry behavior.

Each route must satisfy the supported contract for its dispatch family. An
ineligible route blocks generation for the whole App. Compositions containing
scoped ref-like requests remain outside this scope.

This example uses class requests and DI-owned class handlers, hooks and wrappers.
Await each returned `ValueTask` once. Keep the scope alive until asynchronous
operations and Stream enumeration, including cleanup, have finished.

## Repair a missing handler marker

Add the optional CodeFix package at the same version as `Zendiator`. Before
publication, restore matching locally built packages from your local feed:

```xml
<PackageReference Include="Zendiator.CodeFixes"
                  Version="$(ZendiatorPackageVersion)" PrivateAssets="all" />
```

Restore the ComposedApp project from the selected feed:

```powershell
$project = 'samples/Zendiator.Sample.ComposedApp/Zendiator.Sample.ComposedApp.csproj'
dotnet restore $project -p:ZendiatorPackageVersion=$version --source https://api.nuget.org/v3/index.json
```

Open the project in current Visual Studio. If `RefreshInventoryHandler` is missing
its attribute, use **Add Handler attribute** on ZEN0035. The fix adds only
the attribute. Existing Type registrations in the App are preserved; the installed
generator must accept the complete App after the edit. Ambiguous handlers, invalid
contracts and unadopted compositions receive no speculative fix. If the action
is absent, check that the generator is loaded and the contracts are supported.

After applying the fix, rebuild:

```powershell
dotnet build $project -c Release --no-restore -p:ZendiatorPackageVersion=$version
```

Migrate registration order, wrapper retry/cache logic and scope boundaries manually.
Do not convert an arbitrary wrapper body to hooks without checking its intent.

## Migrating from 0.4.0

- Mark selected handlers with `[Handler]` and select them explicitly
  with `Handlers` in the composition.
- Declare each route's hooks and wrappers in an ordered `Pipeline` list. Use an
  empty list for a zero-stage Stream route. Eligible handler-only `SendSync` and
  `SendAsync` routes may omit `Pipeline`; the samples can also declare it
  explicitly. Use dedicated hooks for their declared operation and wrappers for
  retry, caching or custom continuation behavior.
- For a type-changing Stream, declare its final type with `StreamPipeline`.
  The handler's request interface continues to describe the source item type.

### Register an open wrapper alongside a closed route

When moving an `AddOpenBehavior` registration from 0.4.0, implement the wrapper
contract explicitly and preserve the behavior's continuation logic. These snippets
are for an existing application, using two request records in `OpenSendWrapper`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Zendiator;
using Zendiator.DependencyInjection;

namespace OpenSendWrapper;

public sealed record Query<TMarker>(int Value) : IRequest<int>;
public sealed record ClosedQuery(int Value) : IRequest<int>;
```

Supply `[Handler]` classes `Handler<TMarker>` implementing
`IRequestHandler<Query<TMarker>, int>` and `ClosedHandler` implementing
`IRequestHandler<ClosedQuery, int>`, with your handler bodies and their DI dependencies.
The open wrapper below calls the continuation once and logs completion in `finally`:

```csharp
public sealed class Logging<TRequest, TResponse> : IAsyncSendWrapper<TRequest, TResponse>
    where TRequest : class, IRequest<TResponse>
{
    public async ValueTask<TResponse> InvokeAsync<TNext>(
        TRequest request, TNext next, CancellationToken token)
        where TNext : IAsyncSendContinuation<TRequest, TResponse>
    {
        Console.WriteLine("Before send");
        try
        {
            return await next.InvokeAsync(request, token).ConfigureAwait(false);
        }
        finally
        {
            Console.WriteLine("Send finished");
        }
    }
}
```

Select both handlers. This example declares a pipeline for each route, including
an explicit empty pipeline for the closed route; the wrapper is selected only
for `Query<>`:

```csharp
[ZendiatorComposition("openSendWrapperPair")]
public sealed class Pair;

public static class RegisterPair
{
    public static IServiceCollection Add(IServiceCollection services) =>
        services.AddZendiator<Pair>(c =>
        {
            c.Handlers<ClosedQuery>(DispatchFamily.SendAsync, typeof(ClosedHandler));
            c.Pipeline(typeof(Query<>), DispatchFamily.SendAsync,
                Stage.Wrapper(typeof(Logging<,>)));
            c.Handlers(typeof(Query<>), DispatchFamily.SendAsync, typeof(Handler<>));
            c.Pipeline<ClosedQuery>(DispatchFamily.SendAsync);
        });
}
```

From an async method in the same namespace, resolve the generated facade from a
scope and call both overloads. `IZendiator` is not generic; the open `SendAsync`
method infers its type argument from `Query<int>`:

```csharp
IServiceCollection services = new ServiceCollection();
RegisterPair.Add(services);
using var provider = services.BuildServiceProvider();
using var scope = provider.CreateScope();
var mediator = scope.ServiceProvider.GetRequiredService<Generated.Pair.IZendiator>();

var openResult = await mediator.SendAsync(new Query<int>(42), CancellationToken.None);
var closedResult = await mediator.SendAsync(new ClosedQuery(7), CancellationToken.None);
```

Keep the scope alive until both awaited operations finish. The pipeline borrows
the DI-owned handlers and wrapper; do not dispose them in the wrapper. Await each
returned `ValueTask` once. This example covers one open response wrapper on a
class request and a separate closed response route. Other dispatch families,
session factories and additional hook/wrapper combinations have their own
contracts; this example does not establish their registration patterns.

See [configuration diagnostics](diagnostics/configuration.md) for invalid
configuration, including ZEN0026 and ZEN0037. Suppressing a diagnostic does not
enable a rejected route. The optional
[CodeFix package](../src/Zendiator.CodeFixes/README.md) can add a missing handler
attribute when the target is unambiguous.
