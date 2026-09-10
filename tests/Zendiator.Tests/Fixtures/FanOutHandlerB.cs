using global::Zendiator;

namespace Zendiator.Tests;

public sealed class FanOutHandlerB<T> : IRequestHandler<FanOut<T>, string>
{
    public ValueTask<string> HandleAsync(FanOut<T> request, CancellationToken cancellationToken) => new($"b:{request.Id}");
}
