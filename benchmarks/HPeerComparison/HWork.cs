using Zendiator;
using M = MediatR;
using G = Mediator;
using D = DispatchR.Abstractions.Send;

namespace Competitive;

public sealed class HRequest(int value, bool failOuter, CancellationToken token)
    : IRequest<int>, M.IRequest<int>, G.IRequest<int>, D.IRequest<HRequest, ValueTask<int>>
{
    public int Value { get; } = value;
    public bool FailOuter { get; } = failOuter;
    public CancellationToken Token { get; } = token;
    public int Phase;
    public int Accumulator;
}

public sealed record HActivation(string Role, object Receiver);
public sealed class HOuterFailure : Exception;

public sealed class HWork
{
    public const int Expected = 507473;
    public bool Record;
    // Gate-only controls: read only inside the existing Record blocks below.
    public Exception? Error1Replacement, Finally1Failure, ObservedError;
    public SendExitInfo? ObservedExit;
    public readonly List<HActivation> Created = [], Released = [], BeforeReceivers = [], Handled = [];
    public readonly List<string> Events = [], Trace = [];

    public void Construct(string role, object receiver)
    {
        if (Record)
        {
            Created.Add(new(role, receiver));
            Trace.Add("construct:" + role);
        }
    }
    public void Release(string role, object receiver)
    {
        if (Record)
            Released.Add(new(role, receiver));
    }
    public ValueTask<HRequest> Before(int role, object receiver, HRequest request, CancellationToken token)
    {
        if (token != request.Token)
            throw new InvalidOperationException("Token changed");
        token.ThrowIfCancellationRequested();
        if (role == 1)
        {
            request.Phase = 0;
            request.Accumulator = 17;
        }
        if (request.Phase != role - 1)
            throw new InvalidOperationException("Hook order changed");
        request.Accumulator = unchecked(request.Accumulator * 31 + role);
        request.Phase = role;
        if (Record)
        {
            BeforeReceivers.Add(new("Hook" + role, receiver));
            Events.Add("before:" + role);
            Trace.Add("before:" + role);
        }
        if (role == 1 && request.FailOuter)
            throw new HOuterFailure();
        return new(request);
    }
    public ValueTask<int> After(int role, object receiver, HRequest request, int value, CancellationToken token)
    {
        if (Record)
            Events.Add("after:" + role);
        return new(value);
    }
    public ValueTask Error(int role, object receiver, HRequest request, Exception error, CancellationToken token)
    {
        if (Record)
        {
            Events.Add("error:" + role);
            if (role == 1)
            {
                ObservedError = error;
                if (Error1Replacement is { } replacement)
                    throw replacement;
            }
        }
        return default;
    }
    public ValueTask Finally(int role, object receiver, HRequest request, SendExitInfo exit, CancellationToken token)
    {
        if (Record)
        {
            Events.Add("finally:" + role);
            if (role == 1)
            {
                ObservedExit = exit;
                if (Finally1Failure is { } failure)
                    throw failure;
            }
        }
        return default;
    }
    public ValueTask<int> Handle(object receiver, HRequest request, CancellationToken token)
    {
        if (token != request.Token || request.Phase != 3 || request.Accumulator != Expected)
            throw new InvalidOperationException("Handler did not observe ordered work");
        if (Record)
        {
            Handled.Add(new("Handler", receiver));
            Events.Add("handle");
        }
        return new(request.Value + request.Accumulator);
    }
}
