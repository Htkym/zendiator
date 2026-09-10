using Zendiator;

namespace Zendiator.FeatureBench;

public sealed class ZGenHandler<T> : IRequestHandler<ZGen<T>, T>
    where T : class
{
    private readonly Func<int, T> _create;
    public ZGenHandler() => _create = static value => (T)(object)new ZDto(value);
    public ValueTask<T> HandleAsync(ZGen<T> request, CancellationToken cancellationToken) => new(_create(request.Value));
}
