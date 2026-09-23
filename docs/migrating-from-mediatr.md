# Migrating from MediatR

[日本語](migrating-from-mediatr.ja.md)

See also the [README](../README.md) and [design principles](../Constitution.md).

Procedures for moving MediatR 12 code to Zendiator. Covers API correspondences,
required rewrites, and unsupported features.

This guide describes the current repository. Published packages may differ;
check the release notes for the version you install before applying the lifetime
and configuration guidance below.

The current preview uses standard DI construction and captures Handler/Behavior dependencies lazily per mediator instance, including Transient dependencies. Resolve a new transient mediator for a fresh composition. Custom provider APIs were removed. The generated mediator does not implement `IDisposable`; DI owns dependency disposal. Keep the scope alive until sends and stream enumeration finish. See [construction and dispatch lifetime](optimized-dispatch.md).

Assumptions:

- The migration source is MediatR 12 (the `IMediator`, `ISender`, `IPublisher` setup).
- The target requires .NET 10 (C# 14, nullable enabled).
- Zendiator makes no competitor ranking. The [performance record](performance.md)
  contains measurements for specific revisions, not evidence of the current
  lazy-capture architecture's performance.

## API correspondence

| MediatR 12 | Zendiator | Notes |
|---|---|---|
| `IRequest<TResponse>` | `IRequest<TResponse>` | Direct replacement |
| `IRequest` (returns `Unit`) | `IRequest` (void, `ValueTask`) | The standard shape drops `Unit`. The old `Unit` shape stays as a compatibility route |
| `ICommand<T>`, `IQuery<T>` | `ICommand<T>`, `IQuery<T>` | Direct replacement |
| `IRequestHandler<T, R>` (`Task<R> Handle`) | `IRequestHandler<T, R>` (`ValueTask<R> HandleAsync`) | Return type and method name change |
| `IRequestHandler<T>` (`Task Handle`) | `IRequestHandler<T>` (`ValueTask HandleAsync`) | For void. No `Unit` |
| `ICommandHandler`, `IQueryHandler` | Same names exist (with responses). Void commands use `IRequestHandler<T>` | The old `ICommandHandler<T>` (returns `Unit`) is a compatibility route |
| `AddMediatR(cfg => cfg.RegisterServicesFromAssembly(...))` | `AddZendiator(configuration => ...)` | Generates through ordinary DI composition. No hand-written target class |
| `IMediator` / `ISender` | `IZendiator` | Resolved and used the same way |
| `Send(request)` (`Task<R>`) | `SendAsync(request)` (`ValueTask<R>`) | Requires `await` |
| Void `Send` (`Task`) | `SendAsync` (`ValueTask`) | Requires `await` |
| `IPipelineBehavior<T, R>` (`next()` delegate) | `IPipelineBehavior<T, R>` (struct continuation) | Rewrite required |
| Void Behavior | `IPipelineBehavior<T>` (struct continuation) | One type argument |
| Ordering | `Order` (smaller wraps outer) | MediatR depends on registration order; restate explicitly and verify with tests |
| `INotification`, `Publish` | `INotification`, `PublishAsync` / `Publish` | Sequential dispatch. Zero subscribers complete normally |
| Multiple handlers per request | `IMultiRequest<T>` / `IMultiRequest` + `SendAllAsync` | Only marked requests fan out |
| Generic requests | Open generics in supported patterns | Closed/open overlap is diagnosed (ZEN0010) |
| `ref struct` requests | Sync requests (`ISyncRequest`) + `SendSync` | Unavailable on async routes |
| `Send(object)` | None (except the notification erasure route) | Send with concrete types instead |
| `IStreamRequest<T>` / `CreateStream` | `IStreamRequest<TItem>` + `StreamAsync` | One handler per stream; lazy, cancelable, disposable. No `CreateStream` alias |

Do not confuse notifications with commands. Commands treat a missing handler as a
composition error, while notifications complete normally when a known type has no
subscribers.

## Packages and project layout

Replace the MediatR package reference with `Zendiator` in the project containing
generation configuration:

```shell
dotnet add package Zendiator
```

`Zendiator` includes the Abstractions dependency and source generator. Projects
that only define contracts can reference `Zendiator.Abstractions` alone. Pin the
versions used by your application and keep both packages aligned.

Responsibilities follow the README: message definitions and handlers reference
only `Zendiator.Abstractions`, while the project holding the generation settings
references `Zendiator`.

For the current compilation with default settings, registration is one call:

```csharp
using Zendiator.DependencyInjection;

services.AddZendiator();
```

Build the provider or host using standard APIs. No custom provider or extra
initialization is required. A configuration lambda is needed only for settings
such as additional assemblies, behaviors, or an explicit generated namespace.
An `AddApplication` wrapper is optional and helps organize multi-project applications:

```csharp
// Composition root in a regular Application project.
using Microsoft.Extensions.DependencyInjection;
using Zendiator.DependencyInjection;

namespace MyApp.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddZendiator(static configuration =>
        {
            configuration.Namespace = "MyApp.Application.Generated";
            configuration.RegisterServicesFromAssemblyContaining<GetUserQueryHandler>();
            configuration.AddOpenBehavior(typeof(LoggingBehavior<,>), order: 0);
        });

        return services;
    }
}
```

```csharp
// On the host side, call the wrapper.
services.AddApplication();
```

Notes:

- `RegisterServicesFromAssemblyContaining` is optional when a single assembly is involved.
- Omitting `Namespace` defaults to `{AssemblyName}.Generated`.
- Partial-class and assembly-attribute configuration are also supported.
  Mixing them with DI configuration lambdas in one
  compilation is diagnosed (ZEN0015).
- Only the target's compilation and explicitly listed assemblies are inspected.
  Request types referenced by handler contracts are picked up automatically.
- `AddZendiator()` registers with `TryAdd`. Pre-existing registrations are not replaced.
- Resolve or inject `IZendiator`; `Zendiator` is its implementation type and is not
  registered separately. Custom mediator factories must register `IZendiator`.
  See the [registration example](../README.md#usage).
- Mediator lifetimes differ: MediatR defaults to Transient, while Zendiator
  defaults to Scoped. Generated Zendiator handlers and Behaviors default to
  Transient for a Scoped mediator and are reused within that mediator.
  Select other lifetimes through configuration.

```csharp
services.AddZendiator(static configuration =>
{
    configuration.RegisterServicesFromAssemblyContaining<GetUserQueryHandler>();
    configuration.ServiceLifetime = ServiceLifetime.Singleton;
});
```
- The specified lifetime is the default for the mediator, handlers, and Behaviors. Pre-registrations can override it; enable ValidateScopes to detect invalid scoped dependencies.
- Stateful handlers are reused within a mediator even when registered as Transient. For fresh transient dependencies, resolve a new transient mediator; AddTransient on the handler alone is insufficient.
- Enable `ValidateScopes` and `ValidateOnBuild` for post-migration verification.

Lazy resolution means that `ValidateOnBuild` alone cannot check every captured
dependency. Exercise the relevant dispatch routes and verify constructor counts,
state across repeated sends, concurrent use where supported, and disposal.
Keep each scope alive until its sends and stream enumeration have finished.

## Rewriting requests and handlers

Request declarations carry over almost unchanged. You can use `class`, `record`,
`struct`, and `record struct`.

```csharp
// Before (MediatR)
using MediatR;

public sealed record GetUserQuery(int Id) : IRequest<UserDto>;

public sealed class GetUserQueryHandler : IRequestHandler<GetUserQuery, UserDto>
{
    public Task<UserDto> Handle(GetUserQuery request, CancellationToken cancellationToken)
        => Task.FromResult(new UserDto(request.Id));
}
```

```csharp
// After (Zendiator)
using Zendiator;

public sealed record GetUserQuery(int Id) : IQuery<UserDto>;

public sealed class GetUserQueryHandler : IQueryHandler<GetUserQuery, UserDto>
{
    public ValueTask<UserDto> HandleAsync(GetUserQuery request, CancellationToken cancellationToken)
        => new(new UserDto(request.Id));
}
```

Three changes:

1. Change the namespace from `MediatR` to `Zendiator`.
2. Rename the method from `Handle` to `HandleAsync`.
3. Change the return type from `Task<T>` to `ValueTask<T>`. Wrap completed results
   with `new(...)` or make the method `async ValueTask<T>`.
   Do not use `Task.FromResult`.

Constraints: requests are public types with exactly one response contract.
More than one handler on a non-generic request is a generation error (ZEN0002).
Non-conforming types are reported as ZEN0003.

Generic requests are supported when handler type arguments are uniquely determined
from request type arguments. Reordered arguments, some fixed types, substitution
into the response type, and constraints such as `class` / `struct` / `new()` work.

```csharp
public sealed record GetById<T>(int Id) : IRequest<T> where T : class;

public sealed class GetByIdHandler<T>(IRepository<T> repository) : IRequestHandler<GetById<T>, T>
    where T : class
{
    public ValueTask<T> HandleAsync(GetById<T> request, CancellationToken cancellationToken)
        => repository.GetAsync(request.Id, cancellationToken);
}

// Callers may keep their type arguments open.
ValueTask<T> Load<T>(IZendiator sender, int id, CancellationToken cancellationToken)
    where T : class
    => sender.SendAsync(new GetById<T>(id), cancellationToken);
```

A closed handler overlapping an open handler on the same closed request is
diagnosed as ambiguous (ZEN0010). Extra type arguments that cannot be inferred
are diagnosed (ZEN0009), never filled with silent defaults.

## Requests without responses

The new standard shape drops `Unit`. MediatR's `Task Handle` corresponds to
Zendiator's `ValueTask HandleAsync`.

```csharp
// Before
using MediatR;

public sealed record Ping : IRequest;
public sealed class PingHandler : IRequestHandler<Ping>
{
    public Task Handle(Ping request, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
```

```csharp
// After
using Zendiator;

public sealed record DeleteUser(int UserId) : ICommand;

public sealed class DeleteUserHandler : IRequestHandler<DeleteUser>
{
    public ValueTask HandleAsync(DeleteUser command, CancellationToken cancellationToken)
        => default;
}
```

```csharp
// Call site
await zendiator.SendAsync(new DeleteUser(userId), cancellationToken);
```

Notes:

- Void handlers, Behaviors, and continuations use one type argument.
  The old `IRequestHandler<T, Unit>` / `ValueTask<Unit>` shape keeps building
  as a compatibility route. Old and new handlers on the same request are
  diagnosed (ZEN0008).
- Requests moved to the new shape send `ValueTask`, not `ValueTask<Unit>`.
- Old two-argument Behaviors must move to the new one-argument Behaviors.

## Rewriting registration

Replace runtime assembly-scan registration with `AddZendiator`. For default
configuration in the current compilation, use the parameterless call shown above.
For explicit settings, use ordinary DI configuration code as below. No empty
partial class or configuration attribute is required; attribute-based
configuration is an alternative that cannot be combined with DI configuration
lambdas in one compilation.

```csharp
// Before
services.AddMediatR(static configuration =>
{
    configuration.RegisterServicesFromAssembly(typeof(GetUserQuery).Assembly);
});
```

```csharp
// After: composition root in the Application project
using Microsoft.Extensions.DependencyInjection;
using Zendiator.DependencyInjection;

namespace MyApp.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddZendiator(static configuration =>
        {
            configuration.Namespace = "MyApp.Application.Generated";
            configuration.RegisterServicesFromAssemblyContaining<GetUserQueryHandler>();
            configuration.AddOpenBehavior(typeof(LoggingBehavior<,>), order: 0);
        });

        return services;
    }
}
```

```csharp
// After: on the host side, call the wrapper
services.AddApplication();
```

```csharp
// After: resolving the generated mediator (import the generated namespace)
using MyApp.Application.Generated;

var zendiator = scope.ServiceProvider.GetRequiredService<IZendiator>();
```

The wrapper organizes application registration; it is not an additional
initialization step required by Zendiator.

## Rewriting send sites

Resolve `IZendiator` instead of `IMediator` / `ISender`, and pick the send API
for each purpose.

| Purpose | API | Return |
|---|---|---|
| Single response | `SendAsync` | `ValueTask<TResponse>` |
| Void command | `SendAsync` | `ValueTask` |
| Notification | `PublishAsync` / `Publish` | `ValueTask` |
| Multiple responses | `SendAllAsync` | `ValueTask<IReadOnlyList<TResponse>>` |
| Void fan-out | `SendAllAsync` | `ValueTask` |
| Synchronous single response | `SendSync` | `TResponse` |
| Synchronous void | `SendSync` | `void` |
| Synchronous multiple | `SendAllSync` | `IReadOnlyList<TResponse>` / `void` |

```csharp
// Before
var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
var user = await mediator.Send(new GetUserQuery(1), cancellationToken);
```

```csharp
// After
var zendiator = scope.ServiceProvider.GetRequiredService<IZendiator>();
var user = await zendiator.SendAsync(new GetUserQuery(1), cancellationToken);
```

Notes:

- Async results are `ValueTask` based. Always `await` them.
  Do not reuse the return value (no repeated awaits).
- `SendAsync` generates overloads only for concrete request types.
  Sending from variables of type `object` or `IRequest<T>` is unavailable.
  Adjust call sites so the static type is concrete.
- `CancellationToken` is optional. A passed token is checked before each node runs,
  and cancellation surfaces as `OperationCanceledException`.
- Null reference-type requests are rejected. Exceptions propagate untranslated.

## Rewriting notifications

MediatR notifications move to Zendiator notifications. No manual fan-out is needed.

```csharp
using Zendiator;

public sealed record UserCreated(int UserId) : INotification;

public sealed class WelcomeEmail : INotificationHandler<UserCreated>
{
    public ValueTask HandleAsync(UserCreated notification, CancellationToken cancellationToken)
        => default;
}

public sealed class AuditLog : INotificationHandler<UserCreated>
{
    public ValueTask HandleAsync(UserCreated notification, CancellationToken cancellationToken)
        => default;
}
```

```csharp
await zendiator.PublishAsync(new UserCreated(1), cancellationToken);
```

Notes:

- Dispatch is sequential. Each subscriber starts after the previous completes.
- Ordering follows `[HandlerOrder(Order = ...)]` ascending. Unspecified means 0,
  and ties resolve in a stable deterministic order.
- The first failure stops dispatch; later subscribers are not constructed.
- Known notification types with no subscribers complete normally. Types outside
  the composition fail.
- Sending from an `INotification`-typed variable works for registered closed types.
  Typed routes key on the static notification type; erasure routes key on the exact
  runtime type.
- `Publish` is an alias with the same contract as `PublishAsync`.
  It never becomes fire-and-forget.

## Rewriting multiple handlers

MediatR 12 has no multi-handler `Send`. In Zendiator, only marked requests fan out.

```csharp
using Zendiator;

public sealed record GetQuotes(string ProductCode) : IMultiRequest<Quote>;

// Call site
IReadOnlyList<Quote> quotes = await zendiator.SendAllAsync(new GetQuotes("P1"), cancellationToken);
```

Notes:

- Several handlers on a single request stay a generation error (ZEN0002).
- Each handler runs its pipeline branch; responses are collected in run order.
- Void fan-out returns only `ValueTask`, never a `Unit` list.
- Ordering follows the notification rules (`HandlerOrder` ascending plus
  deterministic ties).

## Rewriting sync requests

`ref struct` requests use the synchronous contract. They cannot use async routes.

```csharp
using Zendiator;

public readonly ref struct ParseYear : ISyncRequest<int>
{
    public ParseYear(ReadOnlySpan<byte> data) => Data = data;
    public ReadOnlySpan<byte> Data { get; }
}

public sealed class ParseYearHandler : ISyncRequestHandler<ParseYear, int>
{
    public int Handle(scoped ParseYear request, CancellationToken cancellationToken)
        => request.Data[0] - (byte)'0';
}
```

```csharp
// Use inside a non-async calling scope.
int year = zendiator.SendSync(new ParseYear(buffer), cancellationToken);
```

Notes:

- Code converting the request to `object` or an interface type does not compile.
- Code retaining the request (fields, lambda captures, crossing `await`) does not compile.
- Generic `ref struct` requests use `allows ref struct` bounds.
- Responses are limited to ordinary types. `ref struct` responses and `ref return`
  are unsupported.

## Rewriting Behaviors

Change `next()` delegate calls to `InvokeAsync` on the struct continuation.
Execution order follows `Order`, not registration. Smaller wraps outer.

```csharp
// Before (MediatR)
using MediatR;

public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Handling {typeof(TRequest).Name}");
        try
        {
            return await next();
        }
        finally
        {
            Console.WriteLine($"Handled {typeof(TRequest).Name}");
        }
    }
}
```

```csharp
// After (Zendiator)
using Zendiator;

public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async ValueTask<TResponse> HandleAsync<TNext>(
        TRequest request,
        TNext next,
        CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<TRequest, TResponse>
    {
        Console.WriteLine($"Handling {typeof(TRequest).Name}");
        try
        {
            return await next.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Console.WriteLine($"Handled {typeof(TRequest).Name}");
        }
    }
}
```

```csharp
// After: registration in DI configuration (inside AddApplication)
services.AddZendiator(static configuration =>
{
    configuration.RegisterServicesFromAssemblyContaining<GetUserQueryHandler>();
    configuration.AddOpenBehavior(typeof(LoggingBehavior<,>), order: 0);
});
```

Continuation control flow maps as follows. Dependency lifetime is a separate
contract: downstream handlers and behaviors already captured by a mediator are
reused across retries and subsequent sends, including Transient registrations.

- Short-circuit by simply not calling `next`. Downstream Behaviors and handlers are not resolved.
- Retries use sequential repeated `next.InvokeAsync` calls. Parallel calls are out of scope.
- Replace request or token by calling `InvokeAsync` with the replaced values.
- Open Behaviors (two-argument generics) apply only to requests satisfying the constraints.
  Closed Behaviors apply only to matching routes.
- Duplicating a Behavior type or an `Order` is an error (ZEN0004).
  Restate any ordering that relied on MediatR registration order with `Order`.
- DI constructor injection keeps working.
- Void requests use one-argument `IPipelineBehavior<T>`.
  Sync requests use `ISyncPipelineBehavior`. Streams use `IStreamPipelineBehavior`.
- With multiple handlers, the pipeline applies to each handler branch.

## Rewriting streams

MediatR `IStreamRequest<T>` / `IStreamRequestHandler<T, R>` map to Zendiator streams with lazy semantics.

```csharp
// Before (MediatR)
public sealed record GetNames(int Count) : IStreamRequest<string>;
public sealed class GetNamesHandler : IStreamRequestHandler<GetNames, string>
{
    public async IEnumerable<string> Handle(GetNames request, CancellationToken ct)
    {
        for (var i = 0; i < request.Count; i++) yield return $"n-{i}";
        await Task.CompletedTask;
    }
}
await foreach (var name in mediator.CreateStream(new GetNames(3))) { }
```

```csharp
// After (Zendiator)
public sealed record GetNames(int Count) : IStreamRequest<string>;
public sealed class GetNamesHandler : IStreamRequestHandler<GetNames, string>
{
    public async IAsyncEnumerable<string> HandleAsync(GetNames request, CancellationToken ct)
    {
        for (var i = 0; i < request.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return $"n-{i}";
        }
    }
}
await foreach (var name in zendiator.StreamAsync(new GetNames(3))) { }
```

Notes:

- One stream request has exactly one handler. Fan-out streams are out of scope.
- `StreamAsync` alone runs nothing. The handler starts on first `MoveNextAsync`.
- Stream Behaviors implement `IStreamPipelineBehavior<TRequest, TItem>` and register with `AddOpenStreamBehavior`.
- Either the API token or `WithCancellation` cancels. `ref struct` streams are diagnosed.

## Unsupported features

Zendiator has no equivalents below. Decide replacements before migrating.

| MediatR feature | Handling |
|---|---|
| Runtime dispatch via `Send(object)` | None (except the notification erasure route). Send with concrete types instead |
| Pre/post processors, exception handlers | No dedicated mechanism. Implement as Behaviors |
| Parallel publish, parallel SendAll, fire-and-forget | None (sequential dispatch only) |
| `ref struct` responses, `ref return` | None |
| AOT setups enumerating every unused closed type | None. Spell out the closed types you use |

## Migration checklist

1. Replace the MediatR reference with `Zendiator` in the generation project; use `Zendiator.Abstractions` alone for contracts-only projects. Pin matching package versions.
2. Replace `using MediatR;` with `using Zendiator;`.
3. Change `Handle` to `HandleAsync` and `Task<T>` to `ValueTask<T>`.
4. Drop `Unit` for void requests: one-argument handlers with `ValueTask`
   (the old `Unit` shape still builds, but mixing old and new is diagnosed).
5. Move any explicit assemblies, behaviors, and orders into a DI configuration lambda. Use `AddZendiator()` alone when the defaults are sufficient.
6. Replace `AddMediatR` with `AddZendiator`. An `AddApplication`-style wrapper is optional. Check the lifetime defaults and reuse within each mediator, including Transient dependencies.
7. Change send sites to the purpose-built API
   (`SendAsync` / `PublishAsync` / `SendAllAsync` / `SendSync` / `StreamAsync`).
   Streams are lazy (start on first `MoveNextAsync`), cancel via the API token or
   `WithCancellation`, dispose with `await using`, and re-enumerate with a fresh call.
8. Search for variables typed as `object` or `IRequest<T>`.
9. Rewrite Behaviors for continuation calls and add `Order`
   (sync: `ISyncPipelineBehavior`; streams: `IStreamPipelineBehavior` +
   `AddOpenStreamBehavior`).
10. Move notification subscribers to `INotificationHandler`, adding `HandlerOrder`
    where order matters.
11. Mark fan-out requests with `IMultiRequest` and switch to `SendAllAsync`.
12. Move `ref struct` requests to the sync contract (`ISyncRequest` + `SendSync`).
13. Enable `ValidateScopes` and `ValidateOnBuild`, exercise lazy dispatch routes, and check dependency construction, retained state, and disposal. Keep scopes alive until sends and stream enumeration finish.
14. For generation errors (`ZEN0001`–`ZEN0020`), review type visibility, duplication, constraints, and configuration expressions.

Reuse existing tests for response values, Behavior invocation counts, and
invocation order. Review tests that assume a new Handler or Behavior instance per
send or retry: Transient dependencies are reused within one mediator. Also check
sharing and isolation across mediators and scopes according to their registrations,
concurrent use where supported, and disposal ownership.
