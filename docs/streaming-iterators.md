# Stream handlers using async iterators

Use `IStreamHandler<TRequest,TItem>` when the input is an `IAsyncEnumerable<TItem>`.
Use `IStreamSourceHandler<TRequest,TItem,TCursor>` when the handler opens a named
class or struct cursor. Both use the same generated Mediator and pipeline stages.

## Choose an input contract

For general asynchronous input, start with `IStreamHandler` and a compiler async
iterator. Use a custom named cursor when an existing source or its state requires
it. A cursor is not inherently slower: synchronous input can benefit from its own
implementation. Avoid a separate async operation per move unless the source needs
one. Pooling is an input implementation choice with its own consumption and
lifetime requirements, not a default promise made by Zendiator.

| Route | Input acquisition | Cancellation | EOF, failure and disposal |
|---|---|---|---|
| Flat public `StreamAsync` with an `IStreamHandler` and two noncancelable tokens | `GetAsyncEnumerator` resolves the receiver, calls `Handle` and acquires the cursor; no pull or `Current` read | The producer's contract applies | The consumer must await disposal, including after EOF or a failed move; Zendiator adds no automatic input cleanup |
| `IStreamHandler` with a cancelable API or enumeration token, or a Before/hook/middleware/owned session | First public move | Zendiator combines API/enumeration tokens and checks cancellation before pulls; the input receives the effective token | Required cleanup finishes before move completion publishes EOF or failure |
| `IStreamSourceHandler`, or `StreamSource(...).ConsumeAsync(...)` | First move of the guarded operation | The guarded operation's effective token | The guarded operation closes acquired cursors and owned sessions |

Two noncancelable tokens means `CanBeCanceled` is false for both, not merely that
neither has been canceled. Keep the cancellation your application requires.
For HTTP, forward `HttpContext.RequestAborted`; an enumeration token passed with
`WithCancellation` also participates. Do not remove a token to select the direct
path. A custom enumerable must honor the token and disposal behavior its API
promises; do not assume every enumerable has compiler-iterator `finally` semantics.

Both contracts borrow their receiver from DI, and a Transient receiver can remain
cached in the mediator. Each enumeration needs an independent cursor. Keep the
scope alive through enumeration and awaited disposal, including serialization when
returning a source from an HTTP action. Do not return a source from an already
disposed action-local scope.

## Compiler iterator example

```csharp
using Microsoft.Extensions.DependencyInjection;
using Zendiator;
using Zendiator.DependencyInjection;

namespace IteratorExample;

public sealed record ReadNumbers(int Count) : IStreamRequest<int>;

[ZendiatorHandler]
public sealed class ReadNumbersHandler : IStreamHandler<ReadNumbers, int>
{
    public async IAsyncEnumerable<int> Handle(ReadNumbers request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
    {
        for (var i = 0; i < request.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return i;
        }
    }
}

[ZendiatorComposition("iteratorExample")]
public sealed class App;

public static class Registration
{
    public static void Configure(IServiceCollection services)
    {
        services.AddZendiator<App>(configuration =>
        {
            configuration.Handlers<ReadNumbers>(DispatchFamily.Stream,
                typeof(ReadNumbersHandler));
            configuration.Pipeline<ReadNumbers>(DispatchFamily.Stream);
        });
    }
}
```

Call `Registration.Configure`, resolve the generated
`IteratorExample.Generated.App.IZendiator`, and call
`StreamAsync(new ReadNumbers(10), token)`. The same source
can be returned from an HTTP action while its DI scope remains alive.

For a flat public `StreamAsync` route with no Before, hooks, middleware or owned
session, two noncancelable tokens select a direct input cursor. Source creation
remains lazy; each `GetAsyncEnumerator` resolves the borrowed receiver, calls
`Handle` and acquires one cursor without moving it or reading `Current`.
Acquisition failures are thrown by `GetAsyncEnumerator`. Disposing before the
first move therefore closes an already acquired input.

For this direct path, use `await foreach`, `await using` or explicit awaited
`DisposeAsync`. EOF or a failed move does not make Zendiator dispose the input
automatically. Current validity, overlapping operations, repeated disposal,
raw Task cancellation status and cleanup-failure precedence follow the producer
and consumer. Do not assume an arbitrary enumerable has compiler iterator
`finally` behavior. Acquisition restores the caller's ambient context, but the
first pull runs later in its caller's context; subsequent raw operations are not
surrounded by additional Zendiator context boundaries.

Any cancelable API or enumeration token retains the guarded cursor, including
when both tokens are equal. It acquires the handler and input on first Move,
combines API/enumeration tokens, checks cancellation before input pulls and
finishes required cleanup before publishing EOF or a failed move. A synchronous
acquisition throw or null result fails that move. A late enumeration-only token
requires a facade as well as the guarded cursor. No equal-cost promise is made.
`StreamSource(...).ConsumeAsync(...)` and routes with Before, hooks, middleware
or owned sessions keep their guarded behavior.

Each enumeration calls the handler again; its DI receiver may remain cached,
including a Transient receiver. The source must return independent cursors.
The receiver remains owned by DI; keep its scope alive until enumeration and
cleanup finish. Neither route reads Current ahead of an explicit consumer read.

Select exactly one source contract for a route. A handler implementing both source
contracts for the same request is ambiguous and is rejected. Existing overlap,
middleware and binding rules still apply. Direct enumerations use the producer's
operation contract described above; guarded inputs retain Zendiator's completion,
exception-precedence and context rules. This input choice does not promise a speed or allocation result.

## Named cursor example

The [GettingStarted sample](../samples/Zendiator.Sample.GettingStarted/Program.cs)
uses `IStreamSourceHandler` with a named `NumberCursor`, a pending first move and
typed Filter/Map hooks. Use that form for a source that already owns cursor state.
The generated operation disposes the acquired cursor, while DI still owns the
borrowed handler and hook services. Its guarded acquisition and cleanup rules
apply even when its input completes synchronously.

## Choose Before or middleware

Use existing `IAsyncStreamBefore<TRequest>` for preprocessing that runs once per
root enumeration without opening a cursor. The validation below leaves the
request and input unchanged. Register several Before types in their required
order with `StreamBefore<TRequest>(...)`.

| Needed behavior | Use |
|---|---|
| Validate or prepare the unchanged request once before any input opens | `IAsyncStreamBefore` |
| Replace the request or return an empty Stream as a normal result | Middleware |
| Pair setup with cleanup around enumeration | Middleware |
| Repeat work for each upstream reopening or validate a replacement request | Middleware |

This is an explicit choice of capabilities, not an automatic conversion of
middleware. Reuse the same work and input when comparing the two forms; results
from a preprocessing-only example do not replace general middleware comparisons.

The [runnable Before example and matched comparison](../benchmarks/Zendiator.BeforeRelayBenchmarks/README.md)
keep the producer, preprocessing work and consumer the same for both forms.

## Run preprocessing once per enumeration

Use `IAsyncStreamBefore<TRequest>` for preprocessing that does not need to wrap
the cursor's lifetime. It works with both Stream handler source contracts.

```csharp
public sealed class ValidateReadNumbers : IAsyncStreamBefore<ReadNumbers>
{
    public ValueTask BeforeAsync(ReadNumbers request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (request.Count < 0)
            throw new ArgumentOutOfRangeException(nameof(request.Count));
        return default;
    }
}
```

Update the existing registration to include `StreamBefore` inside its lambda:

```csharp
services.AddZendiator<App>(configuration =>
{
    configuration.Handlers<ReadNumbers>(DispatchFamily.Stream,
        typeof(ReadNumbersHandler));
    configuration.Pipeline<ReadNumbers>(DispatchFamily.Stream);
    configuration.StreamBefore<ReadNumbers>(typeof(ValidateReadNumbers));
});
```

Callbacks run in their declared order, once on the first public move, before the
handler or middleware opens. Creating the source, acquiring an enumerator, or
disposing it before its first move does not run them. Reopening an upstream from
middleware does not repeat preprocessing or revalidate a replacement request.

A callback has no `next` or cursor and cannot replace the request, return an
empty Stream, or surround enumeration with paired cleanup. Use middleware when
those capabilities are needed.

While a callback is pending, its move remains pending and `DisposeAsync` is
rejected. The route settles each already issued callback operation once. A
callback failure is preserved if cancellation also occurs; after normal callback
completion, the route checks cancellation again before continuing.

Receivers are borrowed and cached per mediator, including receivers registered
with a Transient lifetime. Support reuse and concurrent enumerations if the
mediator is shared; do not keep per-enumeration state in receiver fields. DI owns
receiver disposal. Keep the DI scope alive until enumeration and disposal finish.

Synchronous ambient changes made by a callback may reach the remaining
initialization and first source pull; the caller's context is restored on return.
Later moves use their caller's context. Changes inside an asynchronous callback
are not promised to flow back to its caller.
