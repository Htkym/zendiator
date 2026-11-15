using System.Diagnostics;
using System.Runtime.CompilerServices;
using M = MediatR;
using G = Mediator;
using D = DispatchR.Abstractions.Send;
using Z = Zendiator;

namespace Competitive.ShortCircuit;

public sealed record ShortRequest(int Value = 41, ShortProbe? Probe = null) :
    Z.IRequest<int>, M.IRequest<int>, G.IRequest<int>, D.IRequest<ShortRequest, ValueTask<int>>;

public sealed class ShortProbe(CancellationToken expectedToken)
{
    public CancellationToken ExpectedToken { get; } = expectedToken;
    public List<int> Events { get; } = [];
    public int HandlerCalls;
    public int TokenChecks;
}

public static class ShortWork
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Enter(ShortRequest request, int stage, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (request.Probe is not { } probe)
            return;
        if (probe.ExpectedToken != token)
            throw new InvalidOperationException("Token changed");
        probe.TokenChecks++;
        probe.Events.Add(stage);
    }

    public static int Short(ShortRequest request, CancellationToken token)
    {
        Enter(request, 1, token);
        return 42;
    }

    public static int Handle(ShortRequest request, CancellationToken token)
    {
        Enter(request, 100, token);
        if (request.Probe is { } probe)
            probe.HandlerCalls++;
        return request.Value + 1;
    }
}

public sealed class ConstructionLog
{
    private readonly Dictionary<string, int> counts = [];
    private readonly HashSet<object> instances = new(ReferenceEqualityComparer.Instance);
    public bool Enabled
    {
        get; set;
    }

    [Conditional("SHORT_CONSTRUCTION_DIAGNOSTICS")]
    public void Construct(string role, object receiver)
    {
        if (!Enabled || !instances.Add(receiver))
            return;
        counts[role] = counts.GetValueOrDefault(role) + 1;
    }

    public Dictionary<string, int> Snapshot() => new(counts);

    public static bool Instrumented
    {
        get
        {
#if SHORT_CONSTRUCTION_DIAGNOSTICS
            return true;
#else
            return false;
#endif
        }
    }
}
