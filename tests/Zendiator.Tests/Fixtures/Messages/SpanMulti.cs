using global::Zendiator;

namespace Zendiator.Tests;

public readonly ref struct SpanMulti(ReadOnlySpan<int> values, bool failSecond = false, Action? afterFirst = null) : ISyncMultiRequest<int>
{
    public ReadOnlySpan<int> Values { get; } = values;
    public bool FailSecond { get; } = failSecond;
    public Action? AfterFirst { get; } = afterFirst;
}

[HandlerOrder(Order = 0)]
public sealed class SpanMultiFirst : ISyncRequestHandler<SpanMulti, int>
{
    public int Handle(scoped SpanMulti request, CancellationToken cancellationToken)
    {
        request.AfterFirst?.Invoke();
        return request.Values[0] + 10;
    }
}

[HandlerOrder(Order = 1)]
public sealed class SpanMultiSecond : ISyncRequestHandler<SpanMulti, int>
{
    public int Handle(scoped SpanMulti request, CancellationToken cancellationToken) =>
        request.FailSecond ? throw new InvalidOperationException("second") : request.Values[0] + 20;
}
