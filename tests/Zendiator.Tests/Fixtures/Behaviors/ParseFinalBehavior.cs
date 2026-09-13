using global::Zendiator;

namespace Zendiator.Tests;

public sealed class ParseFinalBehavior(Trace trace) : ISyncPipelineBehavior<ParseRequest, int>
{
    public int Handle<TNext>(ParseRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, ISyncRequestContinuation<ParseRequest, int>
    {
        trace.Add("final");
        try
        {
            return next.Invoke(request, cancellationToken);
        }
        finally
        {
            trace.Add("/final");
        }
    }
}
