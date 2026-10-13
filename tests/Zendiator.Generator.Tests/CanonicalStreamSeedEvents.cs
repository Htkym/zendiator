namespace Zendiator.Generator.Tests;

// Optional test-probe observation. Values are recorded where the operation actually occurs.
public sealed record CanonicalStreamProbeEvent(string Kind, string Stage, object? Request, CancellationToken Token,
    int? Sequence = null, object? Input = null, object? Output = null, bool? Result = null, Exception? Error = null);

internal sealed record StreamSeedEvent(string Kind, string Stage, int Invocation, int Request, int Token,
    int? Sequence, int? Input, int? Output, bool? Result, int Error, string? ErrorType,
    int ParentInvocation = 0, int RelatedInvocation = 0, int? RelatedSequence = null, int? EnumeratorToken = null);

internal static class StreamEventItems
{
    public static int? Int32(object? value) => value switch
    {
        null => null, int item => item,
        _ => throw new InvalidOperationException("This corpus has only typed int items.")
    };
}

internal sealed class StreamSeedEvents(object expectedRequest, CancellationToken expectedToken)
{
    private readonly List<StreamSeedEvent> _events = [];
    private readonly Dictionary<Exception, int> _errors = new(ReferenceEqualityComparer.Instance);
    public Exception? FirstError { get; private set; }
    public int ErrorId(Exception? error)
    {
        if (error is null) return 0;
        if (!_errors.TryGetValue(error, out var id))
        { _errors.Add(error, id = _errors.Count + 1); FirstError ??= error; }
        return id;
    }
    public void Add(CanonicalStreamProbeEvent value)
    {
        if (!ReferenceEquals(expectedRequest, value.Request) || value.Token != expectedToken)
            throw new InvalidOperationException("The corpus must retain its request/token identity.");
        _events.Add(new(value.Kind, value.Stage, 1, 1, value.Token.CanBeCanceled ? 1 : 0,
            value.Sequence, StreamEventItems.Int32(value.Input), StreamEventItems.Int32(value.Output), value.Result, ErrorId(value.Error), value.Error?.GetType().FullName));
    }
    public StreamSeedEvent[] Snapshot() => [.. _events];
}
