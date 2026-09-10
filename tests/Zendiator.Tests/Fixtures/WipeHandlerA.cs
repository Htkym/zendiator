using global::Zendiator;

namespace Zendiator.Tests;

public sealed class WipeHandlerA<T> : IRequestHandler<Wipe<T>>
{
    public static List<int> Got { get; } = [];
    public ValueTask HandleAsync(Wipe<T> request, CancellationToken cancellationToken)
    {
        Got.AddRange(request.Ids);
        return default;
    }
}
