using global::Zendiator;

namespace Zendiator.Tests;

public sealed class StreamReplace : IStreamPipelineBehavior<GetReplaced, int>
{
    public IAsyncEnumerable<int> HandleAsync<TNext>(GetReplaced request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IStreamContinuation<GetReplaced, int> =>
        next.InvokeAsync(new GetReplaced(request.Count + 5), cancellationToken);
}
