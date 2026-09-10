using global::Zendiator;

namespace Zendiator.Tests;

public sealed class LatchHandler : IRequestHandler<LatchCommand>
{
    public async ValueTask HandleAsync(LatchCommand request, CancellationToken cancellationToken)
    {
        await request.Gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
}
