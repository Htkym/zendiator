# Stream handlers using async iterators

Use `IStreamHandler<TRequest,TItem>` when the input is an `IAsyncEnumerable<TItem>`.
Use `IStreamSourceHandler<TRequest,TItem,TCursor>` when the handler opens a named
class or struct cursor. Both use the same generated Mediator and pipeline stages.

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

Creating the source or acquiring its generated enumerator does not invoke the
handler. On the first move the route resolves the handler, calls `Handle`, then
calls `GetAsyncEnumerator` once. Both calls receive the effective token formed
from the API and enumeration tokens. A synchronous throw from either call is
observed by that move; a null enumerable or cursor fails the move. The route does
not dispose a cursor whose acquisition failed.

Each enumeration calls the handler again and owns its acquired cursor. The
returned enumerable must provide an independent cursor for each enumeration.
The route reads `Current` only when needed and performs required cleanup before
publishing EOF or a failed move. Early disposal also closes the cursor once.
The handler and other borrowed dependencies remain owned by DI. Keep their scope
alive until enumeration and cleanup finish.

Select exactly one source contract for a route. A handler implementing both source
contracts for the same request is ambiguous and is rejected. Existing overlap,
ValueTask consumption, middleware, exception precedence and context rules apply to
both input forms. This input choice does not promise a speed or allocation result.

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
