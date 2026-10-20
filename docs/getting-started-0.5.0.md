# Getting started with 0.5.0

0.5.0 has not been released. To use this API, obtain matching locally built
Zendiator packages.

## Run the sample

The [GettingStarted project](../samples/Zendiator.Sample.GettingStarted/Zendiator.Sample.GettingStarted.csproj)
requires .NET 10 and C# 14. Its [source](../samples/Zendiator.Sample.GettingStarted/Program.cs)
shows synchronous Send, asynchronous Send and Stream with ordered hooks.
From the repository root, enter your package feed and version:

```powershell
$feed = Read-Host 'Package feed path'
$version = Read-Host 'Package version'
$project = 'samples/Zendiator.Sample.GettingStarted/Zendiator.Sample.GettingStarted.csproj'

dotnet restore $project -p:ZendiatorPackageVersion=$version --source $feed --source https://api.nuget.org/v3/index.json
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
dispose its scoped handler and hook services. Await each returned `ValueTask`
once.

## Use typed queries and an update command in one App

The [GenericLookup sample](../samples/Zendiator.Sample.GenericLookup/) returns
`T` from `Lookup<T>` and `InventoryStatus` from `StatusQuery`. It awaits
`RefreshInventory` before querying the updated state.

This example uses class requests and DI-owned class handlers. With no hooks,
it can omit empty `Pipeline` declarations. Await each returned `ValueTask` once
and keep the scope alive until the asynchronous operation finishes.

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

Add the optional CodeFix package at the same candidate version as `Zendiator`:

```xml
<PackageReference Include="Zendiator.CodeFixes"
                  Version="$(ZendiatorPackageVersion)" PrivateAssets="all" />
```

Restore the ComposedApp project from the selected feed:

```powershell
$project = 'samples/Zendiator.Sample.ComposedApp/Zendiator.Sample.ComposedApp.csproj'
dotnet restore $project -p:ZendiatorPackageVersion=$version --source $feed --source https://api.nuget.org/v3/index.json
```

Open the project in current Visual Studio. If `RefreshInventoryHandler` is missing
its attribute, use **Add ZendiatorHandler attribute** on ZEN0035. The fix adds only
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

- Mark selected handlers with `[ZendiatorHandler]` and select them explicitly
  with `Handlers` in the composition.
- Declare each route's hooks and wrappers in an ordered `Pipeline` list. Use an
  empty list for a route without hooks. Use dedicated hooks for their declared
  operation and wrappers for retry, caching or custom continuation behavior.
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

Supply `[ZendiatorHandler]` classes `Handler<TMarker>` implementing
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

Select both handlers and declare a pipeline for each route. The closed route has
an empty pipeline; the wrapper is selected only for `Query<>`:

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
