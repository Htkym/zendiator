using Zendiator;

namespace Zendiator.DiConfiguration.Tests;

public sealed record ValidatedStream(int Count) : IStreamRequest<int>;
public readonly record struct OtherValidatedStream(int Count) : IStreamRequest<int>;
public sealed record ValidationLookup : IRequest<int>;

public sealed class StreamValidationTrace
{
    public List<string> Events { get; } = [];
    public InvalidOperationException Failure { get; } = new("invalid input");
    public InvalidOperationException ActivationFailure { get; } = new("activation failed");
    public bool FailFirst, FailMiddle, FailMiddleActivation;
    public int FirstConstructions, MiddleConstructions, LastConstructions, BehaviorConstructions, OtherHandlerConstructions;
    public int FirstValidations, OtherValidations, LastValidations, HandlerCalls, BehaviorCalls;
    public int MiddleFactories, FirstDisposals, MiddleDisposals, LastDisposals, IteratorDisposals;
    public CancellationToken ObservedToken;
}

[ZendiatorHandler]
public sealed class SharedStreamValidator : IStreamMiddleware<ValidatedStream, int>, IStreamMiddleware<OtherValidatedStream, int>,
    IRequestHandler<ValidationLookup, int>, IDisposable
{
    private readonly StreamValidationTrace _trace;
    public SharedStreamValidator(StreamValidationTrace trace)
    {
        _trace = trace;
        Id = ++trace.FirstConstructions;
        trace.Events.Add("first:new");
    }
    public int Id
    {
        get;
    }
    public ValueTask<IAsyncEnumerator<int>> OpenAsync(ValidatedStream request, IStreamUpstream<ValidatedStream, int> upstream, CancellationToken cancellationToken)
    {
        _trace.FirstValidations++;
        _trace.Events.Add("first:validate");
        if (_trace.FailFirst || request.Count < 0)
            throw _trace.Failure;
        return upstream.OpenAsync(request, cancellationToken);
    }
    ValueTask<IAsyncEnumerator<int>> IStreamMiddleware<OtherValidatedStream, int>.OpenAsync(OtherValidatedStream request,
        IStreamUpstream<OtherValidatedStream, int> upstream, CancellationToken cancellationToken)
    {
        _trace.OtherValidations++;
        return upstream.OpenAsync(request, cancellationToken);
    }
    public ValueTask<int> HandleAsync(ValidationLookup request, CancellationToken cancellationToken) => new(Id);
    public void Dispose() => _trace.FirstDisposals++;
}

public static class EntryValidators<T>
{
    public sealed class Middle : IStreamMiddleware<ValidatedStream, int>, IDisposable
    {
        private readonly StreamValidationTrace _trace;
        public Middle(StreamValidationTrace trace)
        {
            _trace = trace;
            trace.MiddleConstructions++;
            trace.Events.Add("middle:new");
        }
        public ValueTask<IAsyncEnumerator<int>> OpenAsync(ValidatedStream request, IStreamUpstream<ValidatedStream, int> upstream, CancellationToken cancellationToken)
        {
            _trace.Events.Add("middle:validate");
            if (_trace.FailMiddle)
                throw _trace.Failure;
            return upstream.OpenAsync(request, cancellationToken);
        }
        public void Dispose() => _trace.MiddleDisposals++;
    }
}

[ZendiatorHandler]
public sealed class ValidatedStreamHandler : IStreamSourceHandler<ValidatedStream, int, DiNumbersCursor>, IDisposable
{
    private readonly StreamValidationTrace _trace;
    public ValidatedStreamHandler(StreamValidationTrace trace)
    {
        _trace = trace;
        trace.LastConstructions++;
        trace.Events.Add("last:new");
    }
    public ValueTask<DiNumbersCursor> OpenAsync(ValidatedStream request, CancellationToken cancellationToken)
    {
        _trace.LastValidations++;
        _trace.Events.Add("last:validate");
        _trace.HandlerCalls++;
        _trace.ObservedToken = cancellationToken;
        return new(new DiNumbersCursor(request.Count, cancellationToken, () => _trace.IteratorDisposals++));
    }
    public void Dispose() => _trace.LastDisposals++;
}

public sealed class ValidatedStreamBehavior : IStreamMiddleware<ValidatedStream, int>
{
    private readonly StreamValidationTrace _trace;
    public ValidatedStreamBehavior(StreamValidationTrace trace)
    {
        _trace = trace;
        trace.BehaviorConstructions++;
    }
    public ValueTask<IAsyncEnumerator<int>> OpenAsync(ValidatedStream request, IStreamUpstream<ValidatedStream, int> upstream, CancellationToken cancellationToken)
    {
        _trace.BehaviorCalls++;
        return upstream.OpenAsync(request, cancellationToken);
    }
}

[ZendiatorHandler]
public sealed class OtherValidatedStreamHandler : IStreamSourceHandler<OtherValidatedStream, int, DiNumbersCursor>
{
    public OtherValidatedStreamHandler(StreamValidationTrace trace) => trace.OtherHandlerConstructions++;
    public ValueTask<DiNumbersCursor> OpenAsync(OtherValidatedStream request, CancellationToken cancellationToken) =>
        new(new DiNumbersCursor(request.Count, cancellationToken));
}
