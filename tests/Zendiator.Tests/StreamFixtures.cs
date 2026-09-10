using global::Zendiator;

namespace Zendiator.Tests;

public sealed record GetNumbers(int Count) : IStreamRequest<int>;

public sealed class GetNumbersHandler : IStreamRequestHandler<GetNumbers, int>
{
    public static int Calls;
    public async IAsyncEnumerable<int> HandleAsync(GetNumbers request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref Calls);
        for (var i = 0; i < request.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return i;
        }
    }
}

public sealed record GetLazy(int Count) : IStreamRequest<int>;

public sealed class GetLazyHandler : IStreamRequestHandler<GetLazy, int>
{
    public static int Started;
    public static int Items;
    public async IAsyncEnumerable<int> HandleAsync(GetLazy request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Started);
        for (var i = 0; i < request.Count; i++)
        {
            Interlocked.Increment(ref Items);
            await Task.Yield();
            yield return i;
        }
    }
}

public sealed record GetOne(int Value) : IStreamRequest<int>;

public sealed class GetOneHandler : IStreamRequestHandler<GetOne, int>
{
    public async IAsyncEnumerable<int> HandleAsync(GetOne request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        yield return request.Value;
    }
}

public sealed record GetPiped(int Count) : IStreamRequest<int>;

public sealed class GetPipedHandler : IStreamRequestHandler<GetPiped, int>
{
    public static int Calls;
    public async IAsyncEnumerable<int> HandleAsync(GetPiped request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        for (var i = 0; i < request.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return i;
        }
    }
}

public sealed record GetPiped3(int Count) : IStreamRequest<int>;

public sealed class GetPiped3Handler : IStreamRequestHandler<GetPiped3, int>
{
    public async IAsyncEnumerable<int> HandleAsync(GetPiped3 request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var i = 0; i < request.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return i;
        }
    }
}

public sealed record GetPiped5(int Count) : IStreamRequest<int>;

public sealed class GetPiped5Handler : IStreamRequestHandler<GetPiped5, int>
{
    public async IAsyncEnumerable<int> HandleAsync(GetPiped5 request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var i = 0; i < request.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return i;
        }
    }
}

public sealed record GetTransform(int Count) : IStreamRequest<int>;

public sealed class GetTransformHandler : IStreamRequestHandler<GetTransform, int>
{
    public async IAsyncEnumerable<int> HandleAsync(GetTransform request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var i = 0; i < request.Count; i++)
        {
            await Task.Yield();
            yield return i;
        }
    }
}

public sealed record GetFilter(int Count) : IStreamRequest<int>;

public sealed class GetFilterHandler : IStreamRequestHandler<GetFilter, int>
{
    public async IAsyncEnumerable<int> HandleAsync(GetFilter request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var i = 0; i < request.Count; i++)
        {
            await Task.Yield();
            yield return i;
        }
    }
}

public sealed record GetGated(bool Open, int Count) : IStreamRequest<int>;

public sealed class GetGatedHandler : IStreamRequestHandler<GetGated, int>
{
    public static int Calls;
    public async IAsyncEnumerable<int> HandleAsync(GetGated request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        for (var i = 0; i < request.Count; i++)
        {
            await Task.Yield();
            yield return i;
        }
    }
}

public sealed record GetReplaced(int Count) : IStreamRequest<int>;

public sealed class GetReplacedHandler : IStreamRequestHandler<GetReplaced, int>
{
    public static int Seen = -1;
    public async IAsyncEnumerable<int> HandleAsync(GetReplaced request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Seen = request.Count;
        for (var i = 0; i < request.Count; i++)
        {
            await Task.Yield();
            yield return i;
        }
    }
}

public sealed record GetFail(int Count, int FailAt) : IStreamRequest<int>;

public sealed class GetFailHandler : IStreamRequestHandler<GetFail, int>
{
    public static readonly InvalidOperationException Failure = new("stream boom");
    public async IAsyncEnumerable<int> HandleAsync(GetFail request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var i = 0; i < request.Count; i++)
        {
            await Task.Yield();
            if (i == request.FailAt) throw Failure;
            yield return i;
        }
    }
}

public sealed record GetScopedItems(int Count) : IStreamRequest<int>;

public sealed class GetScopedItemsHandler(Trace trace) : IStreamRequestHandler<GetScopedItems, int>, IAsyncDisposable
{
    public static int Disposed;
    public static int? FirstHash;
    public async IAsyncEnumerable<int> HandleAsync(GetScopedItems request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try
        {
            for (var i = 0; i < request.Count; i++)
            {
                await Task.Yield();
                yield return i;
            }
        }
        finally
        {
            FirstHash ??= trace.GetHashCode();
        }
    }
    public ValueTask DisposeAsync() { Interlocked.Increment(ref Disposed); return default; }
}

public sealed record GetCancel(int Count) : IStreamRequest<int>;

public sealed class GetCancelHandler : IStreamRequestHandler<GetCancel, int>
{
    public static CancellationToken Seen;
    public async IAsyncEnumerable<int> HandleAsync(GetCancel request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Seen = cancellationToken;
        for (var i = 0; i < request.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return i;
        }
    }
}

public sealed record StreamEntities<T>(int Count) : IStreamRequest<T>
    where T : new();

public sealed class StreamEntity
{
    public Guid Id { get; } = Guid.NewGuid();
}

public sealed class StreamEntitiesHandler<T> : IStreamRequestHandler<StreamEntities<T>, T>
    where T : new()
{
    public async IAsyncEnumerable<T> HandleAsync(StreamEntities<T> request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var i = 0; i < request.Count; i++)
        {
            await Task.Yield();
            yield return new T();
        }
    }
}

public abstract class StreamTraced(Trace trace, string name)
{
    protected void Before() => trace.Add(name);
    protected void After() => trace.Add("/" + name);
}

public sealed class StreamTracePiped(Trace trace) : StreamTraced(trace, "spipe"), IStreamPipelineBehavior<GetPiped, int>
{
    public async IAsyncEnumerable<int> HandleAsync<TNext>(GetPiped request, TNext next, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        where TNext : struct, IStreamContinuation<GetPiped, int>
    {
        Before();
        try
        {
            await foreach (var item in next.InvokeAsync(request, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
                yield return item;
        }
        finally { After(); }
    }
}

public sealed class StreamA3(Trace trace) : IStreamPipelineBehavior<GetPiped3, int>
{
    public async IAsyncEnumerable<int> HandleAsync<TNext>(GetPiped3 request, TNext next, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        where TNext : struct, IStreamContinuation<GetPiped3, int>
    {
        trace.Add("a3");
        try { await foreach (var i in next.InvokeAsync(request, cancellationToken).WithCancellation(cancellationToken)) yield return i; }
        finally { trace.Add("/a3"); }
    }
}
public sealed class StreamB3(Trace trace) : IStreamPipelineBehavior<GetPiped3, int>
{
    public async IAsyncEnumerable<int> HandleAsync<TNext>(GetPiped3 request, TNext next, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        where TNext : struct, IStreamContinuation<GetPiped3, int>
    {
        trace.Add("b3");
        try { await foreach (var i in next.InvokeAsync(request, cancellationToken).WithCancellation(cancellationToken)) yield return i; }
        finally { trace.Add("/b3"); }
    }
}
public sealed class StreamC3(Trace trace) : IStreamPipelineBehavior<GetPiped3, int>
{
    public async IAsyncEnumerable<int> HandleAsync<TNext>(GetPiped3 request, TNext next, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        where TNext : struct, IStreamContinuation<GetPiped3, int>
    {
        trace.Add("c3");
        try { await foreach (var i in next.InvokeAsync(request, cancellationToken).WithCancellation(cancellationToken)) yield return i; }
        finally { trace.Add("/c3"); }
    }
}

public sealed class StreamA5(Trace trace) : IStreamPipelineBehavior<GetPiped5, int>
{
    public async IAsyncEnumerable<int> HandleAsync<TNext>(GetPiped5 request, TNext next, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        where TNext : struct, IStreamContinuation<GetPiped5, int>
    {
        trace.Add("a5");
        try { await foreach (var i in next.InvokeAsync(request, cancellationToken).WithCancellation(cancellationToken)) yield return i; }
        finally { trace.Add("/a5"); }
    }
}
public sealed class StreamB5(Trace trace) : IStreamPipelineBehavior<GetPiped5, int>
{
    public async IAsyncEnumerable<int> HandleAsync<TNext>(GetPiped5 request, TNext next, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        where TNext : struct, IStreamContinuation<GetPiped5, int>
    {
        trace.Add("b5");
        try { await foreach (var i in next.InvokeAsync(request, cancellationToken).WithCancellation(cancellationToken)) yield return i; }
        finally { trace.Add("/b5"); }
    }
}
public sealed class StreamC5(Trace trace) : IStreamPipelineBehavior<GetPiped5, int>
{
    public async IAsyncEnumerable<int> HandleAsync<TNext>(GetPiped5 request, TNext next, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        where TNext : struct, IStreamContinuation<GetPiped5, int>
    {
        trace.Add("c5");
        try { await foreach (var i in next.InvokeAsync(request, cancellationToken).WithCancellation(cancellationToken)) yield return i; }
        finally { trace.Add("/c5"); }
    }
}
public sealed class StreamD5(Trace trace) : IStreamPipelineBehavior<GetPiped5, int>
{
    public async IAsyncEnumerable<int> HandleAsync<TNext>(GetPiped5 request, TNext next, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        where TNext : struct, IStreamContinuation<GetPiped5, int>
    {
        trace.Add("d5");
        try { await foreach (var i in next.InvokeAsync(request, cancellationToken).WithCancellation(cancellationToken)) yield return i; }
        finally { trace.Add("/d5"); }
    }
}
public sealed class StreamE5(Trace trace) : IStreamPipelineBehavior<GetPiped5, int>
{
    public async IAsyncEnumerable<int> HandleAsync<TNext>(GetPiped5 request, TNext next, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        where TNext : struct, IStreamContinuation<GetPiped5, int>
    {
        trace.Add("e5");
        try { await foreach (var i in next.InvokeAsync(request, cancellationToken).WithCancellation(cancellationToken)) yield return i; }
        finally { trace.Add("/e5"); }
    }
}

public sealed class StreamTransform : IStreamPipelineBehavior<GetTransform, int>
{
    public async IAsyncEnumerable<int> HandleAsync<TNext>(GetTransform request, TNext next, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        where TNext : struct, IStreamContinuation<GetTransform, int>
    {
        await foreach (var i in next.InvokeAsync(request, cancellationToken).WithCancellation(cancellationToken))
            yield return i * 10;
    }
}

public sealed class StreamFilter : IStreamPipelineBehavior<GetFilter, int>
{
    public async IAsyncEnumerable<int> HandleAsync<TNext>(GetFilter request, TNext next, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        where TNext : struct, IStreamContinuation<GetFilter, int>
    {
        await foreach (var i in next.InvokeAsync(request, cancellationToken).WithCancellation(cancellationToken))
            if (i % 2 == 0) yield return i;
    }
}

public sealed class StreamGate : IStreamPipelineBehavior<GetGated, int>
{
    public async IAsyncEnumerable<int> HandleAsync<TNext>(GetGated request, TNext next, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        where TNext : struct, IStreamContinuation<GetGated, int>
    {
        if (!request.Open) yield break;
        await foreach (var i in next.InvokeAsync(request, cancellationToken).WithCancellation(cancellationToken))
            yield return i;
    }
}

public sealed class StreamReplace : IStreamPipelineBehavior<GetReplaced, int>
{
    public IAsyncEnumerable<int> HandleAsync<TNext>(GetReplaced request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IStreamContinuation<GetReplaced, int> =>
        next.InvokeAsync(new GetReplaced(request.Count + 5), cancellationToken);
}

public sealed record ThrowingDisposeStream : IStreamRequest<int>;

public sealed class ThrowingDisposeStreamHandler : IStreamRequestHandler<ThrowingDisposeStream, int>
{
    public CancellationToken Token { get; private set; }
    public InvalidOperationException Failure { get; } = new("dispose failed");
    public IAsyncEnumerable<int> HandleAsync(ThrowingDisposeStream request, CancellationToken cancellationToken)
    {
        Token = cancellationToken;
        return new Enumerator(Failure);
    }
    private sealed class Enumerator(Exception failure) : IAsyncEnumerable<int>, IAsyncEnumerator<int>
    {
        public int Current => 1;
        public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken = default) => this;
        public ValueTask<bool> MoveNextAsync() => new(true);
        public ValueTask DisposeAsync() => ValueTask.FromException(failure);
    }
}

public sealed class StreamTraceFail(Trace trace) : IStreamPipelineBehavior<GetFail, int>
{
    public async IAsyncEnumerable<int> HandleAsync<TNext>(GetFail request, TNext next, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        where TNext : struct, IStreamContinuation<GetFail, int>
    {
        trace.Add("fail-before");
        try
        {
            await foreach (var i in next.InvokeAsync(request, cancellationToken).WithCancellation(cancellationToken))
                yield return i;
            trace.Add("fail-after");
        }
        finally { trace.Add("/fail"); }
    }
}
