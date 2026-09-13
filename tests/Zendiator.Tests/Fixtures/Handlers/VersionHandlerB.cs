using global::Zendiator;

namespace Zendiator.Tests;

public sealed class VersionHandlerB : IRequestHandler<GetVersion, int>
{
    public static int SeenB = -1;
    public ValueTask<int> HandleAsync(GetVersion request, CancellationToken cancellationToken)
    {
        SeenB = request.V;
        return new(request.V);
    }
}
