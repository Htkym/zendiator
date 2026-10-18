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

## Migrating from 0.4.0

- Mark selected handlers with `[ZendiatorHandler]` and select them explicitly
  with `Handlers` in the composition.
- Declare each route's hooks and wrappers in an ordered `Pipeline` list. Use an
  empty list for a route without hooks. Use dedicated hooks for their declared
  operation and wrappers for retry, caching or custom continuation behavior.
- For a type-changing Stream, declare its final type with `StreamPipeline`.
  The handler's request interface continues to describe the source item type.

See [configuration diagnostics](diagnostics/configuration.md) for invalid
configuration, including ZEN0026 and ZEN0037. Suppressing a diagnostic does not
enable a rejected route. The optional
[CodeFix package](../src/Zendiator.CodeFixes/README.md) can add a missing handler
attribute when the target is unambiguous.
