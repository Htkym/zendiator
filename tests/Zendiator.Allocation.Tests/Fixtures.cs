using Zendiator;

namespace Zendiator.Allocation.Tests;

[GenerateZendiator]
[PipelineBehavior(typeof(PassThrough), Order = 0)]
public sealed partial class Zendiator;

/// <summary>Zero-allocation request with one pass-through behavior.</summary>
public readonly record struct Ping(int Value) : IRequest<int>;

/// <summary>Synchronously completed handler with no allocations.</summary>
public sealed class PingHandler : IRequestHandler<Ping, int>
{
    public ValueTask<int> HandleAsync(Ping request, CancellationToken cancellationToken) => new(request.Value);
}

/// <summary>Zero-allocation request with no matching behavior.</summary>
public readonly record struct Direct(int Value) : IRequest<int>;

/// <summary>Handler for the behavior-free route.</summary>
public sealed class DirectHandler : IRequestHandler<Direct, int>
{
    public ValueTask<int> HandleAsync(Direct request, CancellationToken cancellationToken) => new(request.Value);
}

/// <summary>Pass-through behavior for Ping only: no logging, allocation or async state machine.</summary>
public sealed class PassThrough : IPipelineBehavior<Ping, int>
{
    public ValueTask<int> HandleAsync<TNext>(Ping request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<Ping, int>
        => next.InvokeAsync(request, cancellationToken);
}

public sealed record AllocStream(int Count) : IStreamRequest<int>;

public sealed class AllocStreamHandler : IStreamRequestHandler<AllocStream, int>
{
    public async IAsyncEnumerable<int> HandleAsync(AllocStream request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var i = 0; i < request.Count; i++)
            yield return i;
        await Task.CompletedTask;
    }
}
