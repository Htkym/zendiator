using Zendiator;

namespace Zendiator.Allocation.Tests;

/// <summary>Handler for the behavior-free route.</summary>
public sealed class DirectHandler : IRequestHandler<Direct, int>
{
    public ValueTask<int> HandleAsync(Direct request, CancellationToken cancellationToken) => new(request.Value);
}
