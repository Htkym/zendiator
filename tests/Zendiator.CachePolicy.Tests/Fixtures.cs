using Microsoft.Extensions.DependencyInjection;
using Zendiator;

namespace Zendiator.CachePolicy.Tests;

// One mediator per compilation; all behaviors below are closed.

[GenerateZendiator]
[PipelineBehavior(typeof(PipeB0), Order = 0)]
[PipelineBehavior(typeof(PipeB1), Order = 1)]
[PipelineBehavior(typeof(PipeB2), Order = 2)]
[PipelineBehavior(typeof(GateBehavior), Order = 3)]
[PipelineBehavior(typeof(RetryTwiceBehavior), Order = 4)]
[PipelineBehavior(typeof(TokBehavior), Order = 5)]
[PipelineBehavior(typeof(AddTenBehavior), Order = 6)]
[PipelineBehavior(typeof(NullPassBehavior), Order = 7)]
[PipelineBehavior(typeof(ExpPipeBehavior), Order = 8)]
[PipelineBehavior(typeof(SuspBehavior), Order = 9)]
[PipelineBehavior(typeof(FailFinallyBehavior), Order = 10)]
[PipelineBehavior(typeof(ReenterBehavior), Order = 11)]
public sealed partial class Zendiator;

public static class TestCounters
{
    public static void ResetAll()
    {
        Val0Handler.FactoryCalls = 0; Val0Handler.Constructions = 0;
        Guid0Handler.FactoryCalls = 0; Guid0Handler.Constructions = 0;
        ProbeHandler.FactoryCalls = 0; ProbeHandler.Constructions = 0;
        PipeHandler.FactoryCalls = 0; PipeHandler.Constructions = 0;
        PipeB0.FactoryCalls = 0; PipeB0.Constructions = 0; PipeB0.HandleCalls = 0;
        PipeB1.FactoryCalls = 0; PipeB1.Constructions = 0; PipeB1.HandleCalls = 0;
        PipeB2.FactoryCalls = 0; PipeB2.Constructions = 0; PipeB2.HandleCalls = 0;
        GateHandler.FactoryCalls = 0; GateHandler.Constructions = 0;
        GateBehavior.HandleCalls = 0;
        RetryHandler.FactoryCalls = 0; RetryHandler.Constructions = 0;
        RetryTwiceBehavior.HandleCalls = 0;
        TokHandler.FactoryCalls = 0; TokHandler.Constructions = 0;
        TokBehavior.HandleCalls = 0;
        AddHandler.FactoryCalls = 0; AddHandler.Constructions = 0;
        AddTenBehavior.HandleCalls = 0;
        RefHandler.FactoryCalls = 0; RefHandler.Constructions = 0;
        NullPassBehavior.HandleCalls = 0;
        ExpHandler.FactoryCalls = 0; ExpHandler.Constructions = 0;
        ExpPipeHandler.FactoryCalls = 0; ExpPipeHandler.Constructions = 0;
        ExpPipeBehavior.HandleCalls = 0;
        SuspHandler.FactoryCalls = 0; SuspHandler.Constructions = 0;
        SuspPipeHandler.FactoryCalls = 0; SuspPipeHandler.Constructions = 0;
        SuspBehavior.HandleCalls = 0; SuspBehavior.FinallyCalls = 0;
        FailHandler.FactoryCalls = 0; FailHandler.Constructions = 0;
        FailFinallyBehavior.HandleCalls = 0; FailFinallyBehavior.FinallyCalls = 0;
        DispHandler.FactoryCalls = 0; DispHandler.Constructions = 0; DispHandler.DisposeCount = 0;
        ReHandler.FactoryCalls = 0; ReHandler.Constructions = 0;
        ReenterBehavior.HandleCalls = 0;
    }
}

public readonly record struct Val0(int Value) : IRequest<int>;

public sealed class Val0Handler : IRequestHandler<Val0, int>
{
    public static int FactoryCalls;
    public static int Constructions;
    public readonly Guid Id = Guid.NewGuid();

    public Val0Handler() => Interlocked.Increment(ref Constructions);

    public ValueTask<int> HandleAsync(Val0 request, CancellationToken cancellationToken) => new(request.Value + 1);
}

public readonly record struct Guid0 : IRequest<Guid>;

public sealed class Guid0Handler : IRequestHandler<Guid0, Guid>
{
    public static int FactoryCalls;
    public static int Constructions;
    public readonly Guid Id = Guid.NewGuid();

    public Guid0Handler() => Interlocked.Increment(ref Constructions);

    public ValueTask<Guid> HandleAsync(Guid0 request, CancellationToken cancellationToken) => new(Id);
}

public readonly record struct ProbeReq : IRequest<int>;

public sealed class ProbeHandler : IRequestHandler<ProbeReq, int>
{
    public static int FactoryCalls;
    public static int Constructions;
    public int Marker;

    public ProbeHandler() => Interlocked.Increment(ref Constructions);

    public ValueTask<int> HandleAsync(ProbeReq request, CancellationToken cancellationToken) => new(Marker);
}

public readonly record struct PipeReq(int Value) : IRequest<int>;

public sealed class PipeHandler : IRequestHandler<PipeReq, int>
{
    public static int FactoryCalls;
    public static int Constructions;

    public PipeHandler() => Interlocked.Increment(ref Constructions);

    public ValueTask<int> HandleAsync(PipeReq request, CancellationToken cancellationToken) => new(request.Value + 1000);
}

public sealed class PipeB0 : IPipelineBehavior<PipeReq, int>
{
    public static int FactoryCalls;
    public static int Constructions;
    public static int HandleCalls;

    public PipeB0() => Interlocked.Increment(ref Constructions);

    public ValueTask<int> HandleAsync<TNext>(PipeReq request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<PipeReq, int>
    {
        Interlocked.Increment(ref HandleCalls);
        return next.InvokeAsync(request, cancellationToken);
    }
}

public sealed class PipeB1 : IPipelineBehavior<PipeReq, int>
{
    public static int FactoryCalls;
    public static int Constructions;
    public static int HandleCalls;

    public PipeB1() => Interlocked.Increment(ref Constructions);

    public ValueTask<int> HandleAsync<TNext>(PipeReq request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<PipeReq, int>
    {
        Interlocked.Increment(ref HandleCalls);
        return next.InvokeAsync(request, cancellationToken);
    }
}

public sealed class PipeB2 : IPipelineBehavior<PipeReq, int>
{
    public static int FactoryCalls;
    public static int Constructions;
    public static int HandleCalls;

    public PipeB2() => Interlocked.Increment(ref Constructions);

    public ValueTask<int> HandleAsync<TNext>(PipeReq request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<PipeReq, int>
    {
        Interlocked.Increment(ref HandleCalls);
        return next.InvokeAsync(request, cancellationToken);
    }
}

public readonly record struct GateReq(bool Open) : IRequest<int>;

public sealed class GateHandler : IRequestHandler<GateReq, int>
{
    public static int FactoryCalls;
    public static int Constructions;

    public GateHandler() => Interlocked.Increment(ref Constructions);

    public ValueTask<int> HandleAsync(GateReq request, CancellationToken cancellationToken) => new(7);
}

public sealed class GateBehavior : IPipelineBehavior<GateReq, int>
{
    public static int HandleCalls;

    public ValueTask<int> HandleAsync<TNext>(GateReq request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<GateReq, int>
    {
        Interlocked.Increment(ref HandleCalls);
        return request.Open ? next.InvokeAsync(request, cancellationToken) : new(-1);
    }
}

public readonly record struct RetryReq : IRequest<int>;

public sealed class RetryHandler : IRequestHandler<RetryReq, int>
{
    public static int FactoryCalls;
    public static int Constructions;
    private int _calls;

    public RetryHandler() => Interlocked.Increment(ref Constructions);

    public ValueTask<int> HandleAsync(RetryReq request, CancellationToken cancellationToken) =>
        new(Interlocked.Increment(ref _calls));
}

public sealed class RetryTwiceBehavior : IPipelineBehavior<RetryReq, int>
{
    public static int HandleCalls;

    public async ValueTask<int> HandleAsync<TNext>(RetryReq request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<RetryReq, int>
    {
        Interlocked.Increment(ref HandleCalls);
        await next.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
        return await next.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
    }
}

public readonly record struct TokReq(CancellationToken Replacement) : IRequest<CancellationToken>;

public sealed class TokHandler : IRequestHandler<TokReq, CancellationToken>
{
    public static int FactoryCalls;
    public static int Constructions;

    public TokHandler() => Interlocked.Increment(ref Constructions);

    public ValueTask<CancellationToken> HandleAsync(TokReq request, CancellationToken cancellationToken) =>
        new(cancellationToken);
}

public sealed class TokBehavior : IPipelineBehavior<TokReq, CancellationToken>
{
    public static int HandleCalls;

    public ValueTask<CancellationToken> HandleAsync<TNext>(TokReq request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<TokReq, CancellationToken>
    {
        Interlocked.Increment(ref HandleCalls);
        return next.InvokeAsync(request, request.Replacement);
    }
}

public readonly record struct AddReq(int Value) : IRequest<int>;

public sealed class AddHandler : IRequestHandler<AddReq, int>
{
    public static int FactoryCalls;
    public static int Constructions;
    public int Seen;

    public AddHandler() => Interlocked.Increment(ref Constructions);

    public ValueTask<int> HandleAsync(AddReq request, CancellationToken cancellationToken)
    {
        Seen = request.Value;
        return new(request.Value);
    }
}

public sealed class AddTenBehavior : IPipelineBehavior<AddReq, int>
{
    public static int HandleCalls;

    public ValueTask<int> HandleAsync<TNext>(AddReq request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<AddReq, int>
    {
        Interlocked.Increment(ref HandleCalls);
        return next.InvokeAsync(new AddReq(request.Value + 10), cancellationToken);
    }
}

public sealed record RefReq(string? Text) : IRequest<string?>;

public sealed class RefHandler : IRequestHandler<RefReq, string?>
{
    public static int FactoryCalls;
    public static int Constructions;

    public RefHandler() => Interlocked.Increment(ref Constructions);

    public ValueTask<string?> HandleAsync(RefReq request, CancellationToken cancellationToken) => new(request.Text);
}

public sealed class NullPassBehavior : IPipelineBehavior<RefReq, string?>
{
    public static int HandleCalls;

    public ValueTask<string?> HandleAsync<TNext>(RefReq request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<RefReq, string?>
    {
        Interlocked.Increment(ref HandleCalls);
        return next.InvokeAsync(null!, cancellationToken);
    }
}

public readonly record struct ExpReq(int Value) : IRequest<int>;

public sealed class ExpHandler : IRequestHandler<ExpReq, int>
{
    public static int FactoryCalls;
    public static int Constructions;

    public ExpHandler() => Interlocked.Increment(ref Constructions);

    ValueTask<int> IRequestHandler<ExpReq, int>.HandleAsync(ExpReq request, CancellationToken cancellationToken) =>
        new(request.Value + 1);
}

public readonly record struct ExpPipeReq(int Value) : IRequest<int>;

public sealed class ExpPipeHandler : IRequestHandler<ExpPipeReq, int>
{
    public static int FactoryCalls;
    public static int Constructions;

    public ExpPipeHandler() => Interlocked.Increment(ref Constructions);

    ValueTask<int> IRequestHandler<ExpPipeReq, int>.HandleAsync(ExpPipeReq request, CancellationToken cancellationToken) =>
        new(request.Value + 1);
}

public sealed class ExpPipeBehavior : IPipelineBehavior<ExpPipeReq, int>
{
    public static int HandleCalls;

    ValueTask<int> IPipelineBehavior<ExpPipeReq, int>.HandleAsync<TNext>(ExpPipeReq request, TNext next, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref HandleCalls);
        return next.InvokeAsync(request, cancellationToken);
    }
}

public readonly record struct SuspReq(int Value) : IRequest<int>;

public sealed class SuspHandler : IRequestHandler<SuspReq, int>
{
    public static int FactoryCalls;
    public static int Constructions;

    public SuspHandler() => Interlocked.Increment(ref Constructions);

    public async ValueTask<int> HandleAsync(SuspReq request, CancellationToken cancellationToken)
    {
        await Task.Yield();
        return request.Value + 1;
    }
}

public readonly record struct SuspPipeReq(int Value) : IRequest<int>;

public sealed class SuspPipeHandler : IRequestHandler<SuspPipeReq, int>
{
    public static int FactoryCalls;
    public static int Constructions;

    public SuspPipeHandler() => Interlocked.Increment(ref Constructions);

    public ValueTask<int> HandleAsync(SuspPipeReq request, CancellationToken cancellationToken) =>
        new(request.Value + 1);
}

public sealed class SuspBehavior : IPipelineBehavior<SuspPipeReq, int>
{
    public static int HandleCalls;
    public static int FinallyCalls;

    public async ValueTask<int> HandleAsync<TNext>(SuspPipeReq request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<SuspPipeReq, int>
    {
        Interlocked.Increment(ref HandleCalls);
        try
        {
            await Task.Yield();
            return await next.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Increment(ref FinallyCalls);
        }
    }
}

public readonly record struct FailReq(int Value) : IRequest<int>;

public sealed class FailHandler : IRequestHandler<FailReq, int>
{
    public static int FactoryCalls;
    public static int Constructions;

    public FailHandler() => Interlocked.Increment(ref Constructions);

    public ValueTask<int> HandleAsync(FailReq request, CancellationToken cancellationToken) =>
        throw CacheFixtureErrors.Instance;
}

public static class CacheFixtureErrors
{
    public static readonly InvalidOperationException Instance = new("fixture failure");
}

public sealed class FailFinallyBehavior : IPipelineBehavior<FailReq, int>
{
    public static int HandleCalls;
    public static int FinallyCalls;

    public async ValueTask<int> HandleAsync<TNext>(FailReq request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<FailReq, int>
    {
        Interlocked.Increment(ref HandleCalls);
        try
        {
            return await next.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Increment(ref FinallyCalls);
        }
    }
}

public readonly record struct DispReq : IRequest<int>;

public sealed class DispHandler : IRequestHandler<DispReq, int>, IAsyncDisposable
{
    public static int FactoryCalls;
    public static int Constructions;
    public static int DisposeCount;

    public DispHandler() => Interlocked.Increment(ref Constructions);

    public ValueTask<int> HandleAsync(DispReq request, CancellationToken cancellationToken) => new(5);

    public ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref DisposeCount);
        return default;
    }
}

public readonly record struct ReReq(int Value) : IRequest<int>;

public sealed class ReHandler : IRequestHandler<ReReq, int>
{
    public static int FactoryCalls;
    public static int Constructions;

    public ReHandler() => Interlocked.Increment(ref Constructions);

    public ValueTask<int> HandleAsync(ReReq request, CancellationToken cancellationToken) =>
        new(1000 + request.Value);
}

public sealed class ReenterBehavior : IPipelineBehavior<ReReq, int>
{
    public static int HandleCalls;
    private readonly IServiceProvider _services;

    public ReenterBehavior(IServiceProvider services) => _services = services;

    public async ValueTask<int> HandleAsync<TNext>(ReReq request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<ReReq, int>
    {
        Interlocked.Increment(ref HandleCalls);
        var inner = await _services.GetRequiredService<IZendiator>().SendAsync(new Val0(1), cancellationToken).ConfigureAwait(false);
        return await next.InvokeAsync(request, cancellationToken).ConfigureAwait(false) + inner;
    }
}
