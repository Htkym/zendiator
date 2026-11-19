# Pool asynchronous operations in a custom producer

A custom cursor whose `MoveNextAsync` suspends can opt into .NET's pooling async `ValueTask` builder for a private helper method. The pooled object belongs to the asynchronous operation. Each enumeration still needs its own cursor. Ordinary `async IAsyncEnumerable<T>` handlers can use the [Stream API](streaming.md) directly.

This feature is experimental. Its accepted method bodies and diagnostics may change within 1.x.

## Mark a producer method

The following class shows a producer with a single item. C# 14 and the `Zendiator` package supply the interceptor support for explicit calls to the marked method.

```csharp
public sealed partial class SingleItemProducer(CancellationToken token)
{
    private bool _available = true;

    public ValueTask<bool> MoveNextAsync() => MovePendingAsync();

    [Zendiator.GeneratePooledProducer]
    private async ValueTask<bool> MovePendingAsync()
    {
        token.ThrowIfCancellationRequested();
        await Task.Yield();
        token.ThrowIfCancellationRequested();
        return TakeItem();
    }

    private bool TakeItem()
    {
        var available = _available;
        _available = false;
        return available;
    }
}
```

Await each returned `ValueTask<bool>` exactly once. Only explicit, supported calls to the helper use pooling. Reflection invokes the original method, and the compiler's implicit calls for `await foreach` continue to use the ordinary iterator implementation.

## Supported declarations and calls

Place a private, parameterless, instance `async ValueTask<bool>` method in a top-level, non-generic partial class. Its body may use supported token checks, await expressions, return statements and try/catch cleanup. A primary-constructor argument such as `token` is supported when it binds to the same value in the pooled method.

Local declarations, local functions, lambdas, generic or virtual methods, dynamic operations, recursive calls and escaping the method as a delegate can prevent pooling. A build error identifies the unsupported declaration, call or binding. Keep the namespace `Zendiator.Generated.Interceptors` in `InterceptorsNamespaces` if your project overrides the package's build settings. [ZEN0040–ZEN0042 diagnostics](diagnostics/configuration.md#opt-in-pooled-producers)
