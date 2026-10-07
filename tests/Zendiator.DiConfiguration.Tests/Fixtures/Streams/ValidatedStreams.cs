using System.Runtime.CompilerServices;
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
    public int MiddleFactories, PipelineFirstFactories, OtherPipelineFactories, FirstDisposals, MiddleDisposals, LastDisposals, IteratorDisposals;
    public CancellationToken ObservedToken;
}

public abstract class EntryValidatorBase<T>(StreamValidationTrace trace) : IStreamRequestValidator<ValidatedStream>
{
    protected StreamValidationTrace Trace { get; } = trace;
    public void Validate(ValidatedStream request)
    {
        Trace.FirstValidations++;
        Trace.Events.Add("first:validate");
        if (Trace.FailFirst || request.Count < 0) throw Trace.Failure;
    }
}

public sealed class SharedStreamValidator : EntryValidatorBase<int>, IStreamRequestValidator<OtherValidatedStream>,
    IRequestHandler<ValidationLookup, int>, IDisposable
{
    public SharedStreamValidator(StreamValidationTrace trace) : base(trace)
    {
        Id = ++trace.FirstConstructions;
        trace.Events.Add("first:new");
    }
    public int Id { get; }
    void IStreamRequestValidator<OtherValidatedStream>.Validate(OtherValidatedStream request) => Trace.OtherValidations++;
    public ValueTask<int> HandleAsync(ValidationLookup request, CancellationToken cancellationToken) => new(Id);
    public void Dispose() => Trace.FirstDisposals++;
}

public static class EntryValidators<T>
{
    public sealed class Middle : IStreamRequestValidator<ValidatedStream>, IDisposable
    {
        private readonly StreamValidationTrace _trace;
        public Middle(StreamValidationTrace trace)
        {
            _trace = trace;
            trace.MiddleConstructions++;
            trace.Events.Add("middle:new");
        }
        public void Validate(ValidatedStream request)
        {
            _trace.Events.Add("middle:validate");
            if (_trace.FailMiddle) throw _trace.Failure;
        }
        public void Dispose() => _trace.MiddleDisposals++;
    }
}

public sealed class ValidatedStreamHandler : IStreamRequestValidator<ValidatedStream>, IStreamRequestHandler<ValidatedStream, int>, IDisposable
{
    private readonly StreamValidationTrace _trace;
    public ValidatedStreamHandler(StreamValidationTrace trace)
    {
        _trace = trace;
        trace.LastConstructions++;
        trace.Events.Add("last:new");
    }
    public void Validate(ValidatedStream request)
    {
        _trace.LastValidations++;
        _trace.Events.Add("last:validate");
    }
    public async IAsyncEnumerable<int> HandleAsync(ValidatedStream request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _trace.HandlerCalls++;
        _trace.ObservedToken = cancellationToken;
        try
        {
            for (var i = 0; i < request.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return i;
            }
        }
        finally { _trace.IteratorDisposals++; }
    }
    public void Dispose() => _trace.LastDisposals++;
}

public sealed class ValidatedStreamBehavior : IStreamPipelineBehavior<ValidatedStream, int>
{
    private readonly StreamValidationTrace _trace;
    public ValidatedStreamBehavior(StreamValidationTrace trace)
    {
        _trace = trace;
        trace.BehaviorConstructions++;
    }
    public IAsyncEnumerable<int> HandleAsync<TNext>(ValidatedStream request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IStreamContinuation<ValidatedStream, int>
    {
        _trace.BehaviorCalls++;
        return next.InvokeAsync(request, cancellationToken);
    }
}

public sealed class OtherValidatedStreamHandler : IStreamRequestHandler<OtherValidatedStream, int>
{
    public OtherValidatedStreamHandler(StreamValidationTrace trace)
    {
        trace.OtherHandlerConstructions++;
    }
    public async IAsyncEnumerable<int> HandleAsync(OtherValidatedStream request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var i = 0; i < request.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return i;
        }
    }
}
