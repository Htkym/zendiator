using global::Zendiator;

namespace Zendiator.AssemblyGen.Tests;

/// <summary>Marks this assembly for compile-time discovery.</summary>
public sealed class AssemblyMarker;

/// <summary>Tracing state for behavior verification.</summary>
public sealed class Trace
{
    public List<string> Events { get; } = [];
}

/// <summary>Single open behavior applied to every request in this compilation.</summary>
public sealed class TracingBehavior<TRequest, TResponse>(Trace trace) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async ValueTask<TResponse> HandleAsync<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<TRequest, TResponse>
    {
        trace.Events.Add("before");
        try
        {
            return await next.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            trace.Events.Add("after");
        }
    }
}

public readonly record struct Ping(int Value) : IRequest<int>;

public sealed class PingHandler : IRequestHandler<Ping, int>
{
    public ValueTask<int> HandleAsync(Ping request, CancellationToken cancellationToken) => new(request.Value * 2);
}

public sealed record AsmNumbers(int Count) : IStreamRequest<int>;

public sealed class AsmNumbersHandler : IStreamRequestHandler<AsmNumbers, int>
{
    public async IAsyncEnumerable<int> HandleAsync(AsmNumbers request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var i = 0; i < request.Count; i++)
        {
            await Task.Yield();
            yield return i;
        }
    }
}

public sealed class AsmStreamTrace(Trace trace) : IStreamPipelineBehavior<AsmNumbers, int>
{
    public async IAsyncEnumerable<int> HandleAsync<TNext>(AsmNumbers request, TNext next, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        where TNext : struct, IStreamContinuation<AsmNumbers, int>
    {
        trace.Events.Add("s-before");
        try
        {
            await foreach (var i in next.InvokeAsync(request, cancellationToken).WithCancellation(cancellationToken))
                yield return i;
        }
        finally { trace.Events.Add("s-after"); }
    }
}
