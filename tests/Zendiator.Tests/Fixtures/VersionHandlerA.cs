using global::Zendiator;

namespace Zendiator.Tests;

public sealed class VersionHandlerA : IRequestHandler<GetVersion, int>
{
    public static int SeenA = -1;
    public ValueTask<int> HandleAsync(GetVersion request, CancellationToken cancellationToken)
    {
        SeenA = request.V;
        return new(request.V);
    }
}
