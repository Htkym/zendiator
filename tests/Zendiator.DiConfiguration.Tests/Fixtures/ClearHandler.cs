using Zendiator;
using Zendiator.DependencyInjection;

namespace Zendiator.DiConfiguration.Tests;

public sealed class ClearHandler : IRequestHandler<Clear>
{
    public static List<int> Cleared { get; } = [];
    public ValueTask HandleAsync(Clear request, CancellationToken cancellationToken)
    {
        Cleared.Add(request.Id);
        return default;
    }
}
