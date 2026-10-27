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
