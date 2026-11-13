using System.Runtime.CompilerServices;
using Competitive;
using F = Foundatio.Mediator;
namespace Competitive.FoundatioRelease;

// Candidate: native ExecuteAsync response wrapping, not an ignored After return.
public sealed record FoundatioStreamRequest1(int Count, bool Asynchronous, Probe? Probe = null) : ILevel1;
[F.Handler]
[F.UseMiddleware(typeof(FoundatioStreamWrapper1Middleware))]
public sealed class FoundatioStream1Handler
{
    public IAsyncEnumerable<int> HandleAsync(FoundatioStreamRequest1 request, CancellationToken token) =>
        Work.Stream(request.Count, request.Asynchronous, request.Probe, token);
}
public sealed record FoundatioStreamRequest3(int Count, bool Asynchronous, Probe? Probe = null) : ILevel3;
[F.Handler]
[F.UseMiddleware(typeof(FoundatioStreamWrapper1Middleware))]
[F.UseMiddleware(typeof(FoundatioStreamWrapper2Middleware))]
[F.UseMiddleware(typeof(FoundatioStreamWrapper3Middleware))]
public sealed class FoundatioStream3Handler
{
    public IAsyncEnumerable<int> HandleAsync(FoundatioStreamRequest3 request, CancellationToken token) =>
        Work.Stream(request.Count, request.Asynchronous, request.Probe, token);
}
public sealed record FoundatioStreamRequest5(int Count, bool Asynchronous, Probe? Probe = null) : ILevel5;
[F.Handler]
[F.UseMiddleware(typeof(FoundatioStreamWrapper1Middleware))]
[F.UseMiddleware(typeof(FoundatioStreamWrapper2Middleware))]
[F.UseMiddleware(typeof(FoundatioStreamWrapper3Middleware))]
[F.UseMiddleware(typeof(FoundatioStreamWrapper4Middleware))]
[F.UseMiddleware(typeof(FoundatioStreamWrapper5Middleware))]
public sealed class FoundatioStream5Handler
{
    public IAsyncEnumerable<int> HandleAsync(FoundatioStreamRequest5 request, CancellationToken token) =>
        Work.Stream(request.Count, request.Asynchronous, request.Probe, token);
}
[F.Middleware(1, ExplicitOnly = true)]
public sealed class FoundatioStreamWrapper1Middleware
{
    public async ValueTask<object?> ExecuteAsync(ILevel1 request, F.HandlerExecutionDelegate next, CancellationToken token)
    {
        var sequence = (IAsyncEnumerable<int>)(await next().ConfigureAwait(false))!;
        return Enumerate(request, sequence, token);
    }
    private static async IAsyncEnumerable<int> Enumerate(IWork request, IAsyncEnumerable<int> sequence,
        [EnumeratorCancellation] CancellationToken token)
    {
        Work.Enter(request, 1);
        await foreach (int item in sequence.WithCancellation(token).ConfigureAwait(false))
            yield return item;
    }
}
[F.Middleware(2, ExplicitOnly = true)]
public sealed class FoundatioStreamWrapper2Middleware
{
    public async ValueTask<object?> ExecuteAsync(ILevel2 request, F.HandlerExecutionDelegate next, CancellationToken token)
    {
        var sequence = (IAsyncEnumerable<int>)(await next().ConfigureAwait(false))!;
        return Enumerate(request, sequence, token);
    }
    private static async IAsyncEnumerable<int> Enumerate(IWork request, IAsyncEnumerable<int> sequence,
        [EnumeratorCancellation] CancellationToken token)
    {
        Work.Enter(request, 2);
        await foreach (int item in sequence.WithCancellation(token).ConfigureAwait(false))
            yield return item;
    }
}
[F.Middleware(3, ExplicitOnly = true)]
public sealed class FoundatioStreamWrapper3Middleware
{
    public async ValueTask<object?> ExecuteAsync(ILevel3 request, F.HandlerExecutionDelegate next, CancellationToken token)
    {
        var sequence = (IAsyncEnumerable<int>)(await next().ConfigureAwait(false))!;
        return Enumerate(request, sequence, token);
    }
    private static async IAsyncEnumerable<int> Enumerate(IWork request, IAsyncEnumerable<int> sequence,
        [EnumeratorCancellation] CancellationToken token)
    {
        Work.Enter(request, 3);
        await foreach (int item in sequence.WithCancellation(token).ConfigureAwait(false))
            yield return item;
    }
}
[F.Middleware(4, ExplicitOnly = true)]
public sealed class FoundatioStreamWrapper4Middleware
{
    public async ValueTask<object?> ExecuteAsync(ILevel4 request, F.HandlerExecutionDelegate next, CancellationToken token)
    {
        var sequence = (IAsyncEnumerable<int>)(await next().ConfigureAwait(false))!;
        return Enumerate(request, sequence, token);
    }
    private static async IAsyncEnumerable<int> Enumerate(IWork request, IAsyncEnumerable<int> sequence,
        [EnumeratorCancellation] CancellationToken token)
    {
        Work.Enter(request, 4);
        await foreach (int item in sequence.WithCancellation(token).ConfigureAwait(false))
            yield return item;
    }
}
[F.Middleware(5, ExplicitOnly = true)]
public sealed class FoundatioStreamWrapper5Middleware
{
    public async ValueTask<object?> ExecuteAsync(ILevel5 request, F.HandlerExecutionDelegate next, CancellationToken token)
    {
        var sequence = (IAsyncEnumerable<int>)(await next().ConfigureAwait(false))!;
        return Enumerate(request, sequence, token);
    }
    private static async IAsyncEnumerable<int> Enumerate(IWork request, IAsyncEnumerable<int> sequence,
        [EnumeratorCancellation] CancellationToken token)
    {
        Work.Enter(request, 5);
        await foreach (int item in sequence.WithCancellation(token).ConfigureAwait(false))
            yield return item;
    }
}
