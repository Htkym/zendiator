# Return items as a Stream

[日本語](streaming.ja.md)

Use `StreamAsync` when a handler can return items before all the results are ready. The caller can process each item with `await foreach` and stop early with `break`.

## Define a handler

In the [GettingStarted sample](../samples/Zendiator.Sample.GettingStarted/Program.cs), the handler yields the numbers from 1 through the requested count. Apply `[EnumeratorCancellation]` so an async iterator receives cancellation from the caller.

```csharp
using System.Runtime.CompilerServices;
using Zendiator;

public sealed record ReadNumbers(int Count) : IStreamRequest<int>;

[Handler]
public sealed class ReadNumbersHandler : IStreamHandler<ReadNumbers, int>
{
    public async IAsyncEnumerable<int> Handle(ReadNumbers request,
        [EnumeratorCancellation] CancellationToken token)
    {
        await Task.Yield();
        for (var index = 0; index < request.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            yield return index + 1;
        }
    }
}
```

Register the handler inside the composition's `AddZendiator<App>` lambda. Declare an empty pipeline with `c.Pipeline<ReadNumbers>(DispatchFamily.Stream)` to yield the original numbers. The sample registers three stages to keep even numbers, multiply each by ten and discard results above forty.

```csharp
c.Handlers<ReadNumbers>(DispatchFamily.Stream, typeof(ReadNumbersHandler));
c.Pipeline<ReadNumbers>(DispatchFamily.Stream,
    Stage.Hook<KeepEven>(), Stage.Hook<TimesTen>(), Stage.Hook<KeepAtMostForty>());
```

This loop prints `20` and `40`. The sample defines the three hooks with `IStreamItemFilter` and `IStreamItemMap`. If a map changes the item type, register the final type with `StreamPipeline<TRequest, TFinal>`. For example, the [ComposedApp sample](../samples/Zendiator.Sample.ComposedApp/Program.cs) adds a map from `int` to `string` and registers `StreamPipeline<ReadNumbers, string>`.

```csharp
await foreach (var number in mediator.StreamAsync(new ReadNumbers(6), token))
{
    Console.WriteLine(number);
}
```

## Choose an input contract

Use `IStreamHandler<TRequest, TItem>.Handle` to return an `IAsyncEnumerable<TItem>`. If your input already has a cursor with its own state, implement `IStreamSourceHandler<TRequest, TItem, TCursor>.OpenAsync`. The cursor can be a class or a struct. Select one input contract per request and provide a fresh cursor on each enumeration. DI may reuse the handler.

If a custom cursor calls a `private async ValueTask<bool>` helper for each item, each call that suspends allocates a state machine. A partial cursor class can apply `[GeneratePooledProducer]` to a supported helper to reuse that state. Await each returned `ValueTask<bool>` once. [Supported methods and example](pooled-producer.md)

Creating the enumerable does not open the input. The point at which the input opens depends on the route and the cancellation tokens.

| Route | When the input opens | Who handles cleanup |
|---|---|---|
| An `IStreamHandler` with an empty pipeline, no Before callback or owned session, and neither the API nor enumeration token can be canceled | `GetAsyncEnumerator` resolves the handler, calls `Handle` and acquires the cursor. The first move reads the first item. | The consumer must await disposal even after end of input or failure. The returned cursor follows the producer's rules for moves, exceptions and disposal. |
| Other `StreamAsync` routes | The first move starts the route and any Before callbacks. Middleware decides whether and when to open its upstream. | Zendiator closes acquired cursors and owned sessions before reporting end of input or failure. Early exit from `await foreach` also awaits disposal. |
| `StreamSource(request, token).ConsumeAsync(sink)` | Each call starts a separate guarded consumption. | Zendiator closes acquired cursors and owned sessions when consumption ends. The caller owns the sink. |

Pass the actual cancellation token to `StreamAsync`. An enumeration token from `WithCancellation` also participates. On guarded routes, Zendiator combines the two and checks cancellation before pulling input. Await each move before another move or disposal. Read `Current` after a successful move and before starting the next operation. Guarded routes dispose owned resources once in reverse order. If cleanup throws, the last cleanup exception takes precedence.

## Run a check before reading items

Implement `IAsyncStreamBefore<TRequest>.BeforeAsync` to validate the request once per enumeration. Register the type with `c.StreamBefore<ReadNumbers>(typeof(ValidateReadNumbers))`. The callbacks run in registration order on the first move, before the input opens. Reopening an upstream cursor does not repeat them. Use middleware if you need to change the request, return an empty stream or pair setup with cleanup.

## Forwarding or decorating a cursor

Implement `IStreamMiddleware<TRequest, TItem>` and register it with `Stage.Wrapper<T>()`. In the typed overload, `OpenAsync<TNext>` accepts `TNext next` with the constraint `where TNext : struct, IStreamUpstream<TRequest, TItem>`. The overload accepting an `IStreamUpstream<TRequest, TItem>` also works.

Return the cursor from `next.OpenAsync(request, token)` to pass its items and disposal through. Zendiator recognizes forwarded cursors from their identity and cancellation token at enumeration time, including those returned through a local variable or an async helper. Return your own cursor to change how items are read or resources are released. A decorator owns the cleanup of the upstream cursor it wraps. [Logging middleware example](../samples/Zendiator.Sample.Application/Behaviors/StreamLoggingBehavior.cs)

A middleware can have one upstream cursor open at a time. It may reopen the input only after normal end of input and completed cleanup. Reopening can repeat items that the caller has already received. An early exit or a failure prevents reopening, and Zendiator does not retry automatically.

## Send items to a sink

If another component processes items one at a time, it can implement `IStreamSink<TItem>` and call `await mediator.StreamSource(request, token).ConsumeAsync(sink)`. Zendiator awaits each `OnNextAsync(item, token)` before continuing. Return `true` for another item or `false` to finish normally. A struct sink is passed by value. Store results in a shared object if the caller needs them after consumption.

## Return a Stream from an HTTP endpoint

Return the `IAsyncEnumerable<TItem>` from `StreamAsync` and pass the request cancellation token. Keep the DI scope alive until serialization and cursor disposal finish. ASP.NET Core writes the response with the configured serializer. A failure after the response has started may leave an incomplete response. [Scope and lifetime](release/known-limitations.md#dependency-capture-and-lifetime)
