# Migrating from MediatR

[日本語](migrating-from-mediatr.ja.md)

This guide moves MediatR 12 applications using `IMediator`, `ISender` and
`IPublisher` to Zendiator 0.6.0 on .NET 10 and C# 14. Later MediatR versions may
have different registration APIs. Start with the [README](../README.md) and
[current configuration guide](getting-started-0.5.0.md). The configuration guide
keeps its existing filename so published links continue to work; its contents
target 0.6.0.

Use an explicit composition for new Send and Stream routes: mark handlers with
`[Handler]`, select them with `Handlers`, and order stages in `Pipeline`. The
older assembly-discovery, `AddOpenBehavior` and `Order` APIs remain compatibility
paths. For existing Zendiator code, also apply the
[0.6.0 handler and Stream changes](release/0.6.0-release-notes.md).

## API correspondence

| MediatR 12 | Zendiator 0.6.0 | Migration |
|---|---|---|
| `IRequest<TResponse>` | `IRequest<TResponse>` | Replace the namespace |
| `IRequest` | `IRequest` or `ICommand` | One-argument handler returning `ValueTask` |
| `IRequestHandler<TRequest,TResponse>.Handle` | `IRequestHandler<TRequest,TResponse>.HandleAsync` | Return `ValueTask<TResponse>` and add `[Handler]` |
| `IRequestHandler<TRequest>.Handle` | `IRequestHandler<TRequest>.HandleAsync` | Return `ValueTask` and add `[Handler]` |
| Runtime assembly registration | `AddZendiator<App>` with `Handlers` / `Pipeline` | Select routes at compile time |
| `IMediator` / `ISender` | Generated `IZendiator` | Import the composition's generated namespace |
| `Send(request)` | `SendAsync(request)` | Await once |
| `IPipelineBehavior<TRequest,TResponse>` | Send hooks or `IAsyncSendWrapper<TRequest,TResponse>` | Choose according to continuation control |
| Behavior registration order | Ordered `Pipeline` stages | Earlier stages are outer stages |
| `INotification` / `Publish` | `INotification` / `PublishAsync` or `Publish` | Separate notification registration; sequential dispatch |
| `IStreamRequest<T>` / `CreateStream` | `IStreamRequest<T>` / `StreamAsync` | Select one source contract |
| `Send(object)` | No equivalent | Keep a concrete static request type |

`ICommand<T>` and `IQuery<T>` are Zendiator alternatives for application-specific
command/query conventions, not MediatR interfaces. Runtime-object request dispatch
and notification type erasure have different support boundaries.

## Packages and project layout

Install in the project that declares the composition:

```shell
dotnet add package Zendiator --version 0.6.0
dotnet add package Microsoft.Extensions.DependencyInjection --version 10.0.0
```

`Zendiator` includes the generator and depends on `Zendiator.Abstractions`.
Contracts-only projects can reference `Zendiator.Abstractions` alone. Keep all
Zendiator packages at the same version. The optional `Zendiator.CodeFixes` package
is also 0.6.0 and uses `PrivateAssets="all"`.

Generation examines the composition's compilation and explicitly configured
assemblies. It does not scan arbitrary assemblies at runtime. Keep contract,
implementation and composition references explicit in multi-project applications.
See the [ComposedApp sample](../samples/Zendiator.Sample.ComposedApp/) and
[configuration diagnostics](diagnostics/configuration.md).

## Requests, handlers and registration

Change the MediatR handler's namespace, method name and return type. Mark and
select the Zendiator handler. This complete example follows the
[GettingStarted sample](../samples/Zendiator.Sample.GettingStarted/Program.cs):

```csharp
using Migration.Generated.App;
using Microsoft.Extensions.DependencyInjection;
using Zendiator;
using Zendiator.DependencyInjection;

namespace Migration;

public static class Program
{
    public static async Task Main()
    {
        var services = new ServiceCollection();
        services.AddZendiator<App>(configuration =>
        {
            configuration.Handlers<GetUserQuery>(DispatchFamily.SendAsync, typeof(GetUserQueryHandler));
        });

        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var user = await mediator.SendAsync(new GetUserQuery(1));
        Console.WriteLine(user.Id);
    }
}

[ZendiatorComposition("migration")]
public sealed class App;

public sealed record UserDto(int Id);
public sealed record GetUserQuery(int Id) : IRequest<UserDto>;

[Handler]
public sealed class GetUserQueryHandler : IRequestHandler<GetUserQuery, UserDto>
{
    public ValueTask<UserDto> HandleAsync(GetUserQuery request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return new(new UserDto(request.Id));
    }
}
```

Expected output is `1`. The generated namespace includes the application
namespace and composition marker (`Migration.Generated.App` here). A handler-only
Send route may omit an empty `Pipeline`. An `AddApplication` method can wrap this
registration; no extra initialization is required.

For requests without responses, implement `IRequestHandler<TRequest>` and return
`ValueTask` from `HandleAsync`. Select the handler with `DispatchFamily.SendAsync`
in the same way. The old `IRequestHandler<TRequest,Unit>` shape remains a
compatibility path; do not mix the two handler contracts for one route.

Open generic handlers are supported when their arguments can be determined from
the request and their constraints are satisfied. Register open definitions with
`Handlers(typeof(Lookup<>), DispatchFamily.SendAsync, typeof(LookupHandler<>))`.
See [GenericLookup](../samples/Zendiator.Sample.GenericLookup/) for a runnable
example. Ambiguous closed/open matches and unbound arguments are diagnosed.

## Send sites, lifetime and errors

Resolve or inject the generated `IZendiator`. Keep concrete request types at call
sites: an `object` or `IRequest<T>` variable does not select a generated overload.
Use `Send` for synchronous routes, `SendAsync` for asynchronous routes, and
`SendAll` / `SendAllAsync` for explicitly configured multiple-handler routes.
The older `SendSync` / `SendAllSync` names belong to the compatibility API.

Generated registration defaults to a Scoped Mediator, with Transient handlers,
hooks and session factories. MediatR 12 defaults to a Transient Mediator. Register
service overrides before `AddZendiator`; its `TryAdd` registration preserves them.
Reused services must support the application's concurrent use.

Each Mediator reuses captured dependencies, including Transients. Eligible
asynchronous Hook routes acquire a route-local dependency set before the first
Hook; other routes retain their own capture rules. A send or retry does not imply
a new dependency instance. See [current capture rules](getting-started-0.5.0.md#route-local-capture-for-asynchronous-hooks)
before relying on constructor timing or shared identity.

DI owns activation and disposal. Dispose the scope after all sends, enumeration
and cleanup finish. The generated Mediator is not an `IDisposable` owner. Using it
after scope or provider disposal is unsupported. Enable `ValidateScopes` and
`ValidateOnBuild`, and exercise lazily resolved routes your application uses.

Await each `ValueTask` once. Eligible direct `SendAsync` routes can throw during
dependency resolution, cancellation checks or synchronous callbacks before
returning a `ValueTask`. Put both the call and await inside the `try` block.
Exceptions propagate without an automatic recovery policy. The lifetime analyzer
reports provable misuse (ZEN0021–ZEN0023); absence of a warning does not prove
that complex asynchronous scope usage is safe.

## Behaviors, hooks and wrappers

Use dedicated hooks for request/response transformation and success, exception or
cleanup observation. Select them with `Stage.Hook<T>()`. Use
`IAsyncSendWrapper<TRequest,TResponse>` when the behavior controls whether or how
often the continuation runs, such as retry, cache or short-circuit logic.

This wrapper preserves logging around one invocation:

```csharp
public sealed class Logging<TRequest, TResponse> : IAsyncSendWrapper<TRequest, TResponse>
    where TRequest : class, IRequest<TResponse>
{
    public async ValueTask<TResponse> InvokeAsync<TNext>(
        TRequest request, TNext next, CancellationToken token)
        where TNext : IAsyncSendContinuation<TRequest, TResponse>
    {
        Console.WriteLine($"Handling {typeof(TRequest).Name}");
        try
        {
            return await next.InvokeAsync(request, token).ConfigureAwait(false);
        }
        finally
        {
            Console.WriteLine($"Handled {typeof(TRequest).Name}");
        }
    }
}
```

Add the stage to the example's existing `AddZendiator<App>` configuration:

```csharp
configuration.Pipeline<GetUserQuery>(DispatchFamily.SendAsync,
    Stage.Wrapper<Logging<GetUserQuery, UserDto>>());
```

Pipeline list order replaces MediatR registration order. Preserve and test the
behavior's intent: a wrapper may invoke `next` zero times or repeatedly in
sequence; each result is consumed once. Do not retain the continuation or invoke
it concurrently after its operation completes. Captured downstream services
remain reused across retries. Void routes use `IAsyncSendWrapper<TRequest>`;
synchronous routes use `ISendWrapper`.

The [ComposedApp sample](../samples/Zendiator.Sample.ComposedApp/Program.cs) shows
hooks and a cache/retry wrapper. Legacy `IPipelineBehavior`, `AddOpenBehavior` and
numeric `Order` remain separate compatibility APIs. When moving an existing
Zendiator discovery configuration to a composition, convert them to explicit
wrapper contracts and ordered `Pipeline` stages.

## Notifications and multiple handlers

Notifications implement `INotificationHandler<TNotification>.HandleAsync` returning
`ValueTask`. Their registration is separate from composition Send/Stream
selection. The existing assembly-discovery configuration supports notifications;
see [Sample.Application registration](../samples/Zendiator.Sample.Application/DependencyInjection.cs)
and [Sample.Host](../samples/Zendiator.Sample.Host/Program.cs) for the generated
notification facade and its use.

`PublishAsync` and its `Publish` alias dispatch sequentially, awaiting each
subscriber. Neither is fire-and-forget. `HandlerOrder` orders subscribers; ties
have stable deterministic order. The first failure stops dispatch. Known
notification types with no subscribers complete normally. Notification type
erasure supports registered closed types and keys on the exact runtime type.
A missing command handler is a composition error, not an empty subscription list.

MediatR 12's ordinary `Send` has no multiple-handler response contract. A Zendiator
composition explicitly selects handlers using `DispatchFamily.SendAllAsync` or
`DispatchFamily.SendAllSync`; results follow selected handler order, and each
branch runs the configured pipeline. Void fan-out returns no list of `Unit`.
Legacy discovery uses `IMultiRequest` / `IMultiRequest<T>` and its `HandlerOrder`
rules. See ComposedApp for the composition form.

## Streams and synchronous requests

Keep standard `IAsyncEnumerable<T>` consumers. For ordinary asynchronous input,
mark an `IStreamHandler<TRequest,TItem>` implementation with `[Handler]` and return
`IAsyncEnumerable<TItem>` from `Handle`, often using a compiler async iterator.
For a named class or struct cursor, use
`IStreamSourceHandler<TRequest,TItem,TCursor>.OpenAsync`. Select exactly one source
contract with `Handlers`, and declare a Stream `Pipeline`, including an empty
list for a zero-stage Stream.

Use `IStreamMiddleware` with `Stage.Wrapper` for custom upstream control and
Filter/Map contracts with `Stage.Hook` for typed item processing. A type-changing
pipeline declares its final type with `StreamPipeline`; the request's
`IStreamRequest<T>` still describes the source item type. The
[iterator guide](streaming-iterators.md) covers both inputs, cancellation and
direct/guarded paths. The [0.6.0 Stream migration](migrating-stream-upstream.md)
explains typed `OpenAsync<TNext>` middleware.

`StreamAsync` returns a standard enumerable. Prefer `await foreach`, forward API
and enumeration cancellation tokens, and keep the scope alive through disposal,
including early termination and failure. Acquisition timing and cleanup depend on
the direct or guarded path; not every path starts at the first move.
`StreamSource(...).ConsumeAsync(...)` is a separate typed terminal: its readonly
plan is not an enumerable, and value-type sinks share results through reference
state. Standard enumeration, typed terminal and HTTP performance need separate
measurements.

Ordinary synchronous requests use `ISyncRequest` and `ISyncRequestHandler`, with
`DispatchFamily.SendSync` and generated `Send`. Ref-like requests have additional
configuration restrictions; ordinary class-composition examples do not establish
their support. See [known limitations](release/known-limitations.md).

## Unsupported features and verification

| Feature | Handling |
|---|---|
| Runtime `Send(object)` or runtime-object Stream dispatch | Keep concrete static request types |
| Parallel Publish, parallel SendAll, fire-and-forget | Use the sequential contracts |
| MediatR pre/post processors and exception handlers | Port the operation to hooks or wrappers |
| `ref struct` responses or `ref return` | Unsupported |
| Arbitrary runtime generic closure under Native AOT | Check the documented closed-type and RID limits |
| Persistence, outbox or durable messaging | Supply application infrastructure separately |

Package-based Native AOT publish and execution are verified on win-x64 in the
documented configurations; other RIDs remain unverified. Open-generic DI closed
over value-type arguments has separate Native AOT restrictions. This does not
exclude struct Stream cursors.

Migration order:

1. Align Zendiator 0.6.0 package versions and select the composition project.
2. Replace request/handler contracts, add `[Handler]`, and declare `Handlers`.
3. Move behavior intent to hooks or wrappers and preserve order in `Pipeline`.
4. Replace call sites with the facade's concrete overloads; handle notification
   registration separately.
5. Port Stream input and middleware contracts, forwarding cancellation and
   preserving scope and disposal boundaries.
6. Enable DI validation and exercise selected routes. Reuse response/order tests
   and check constructor counts, repeated-send state and disposal against the
   actual capture rules.

Use [configuration diagnostics](diagnostics/configuration.md) and
[limited CodeFix support](../src/Zendiator.CodeFixes/README.md) to locate errors.
CodeFix can add an unambiguous missing handler marker. It does not migrate
pipeline order, lifetime boundaries or wrapper behavior automatically.
