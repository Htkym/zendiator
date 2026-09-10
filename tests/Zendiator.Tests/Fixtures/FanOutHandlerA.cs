using global::Zendiator;

namespace Zendiator.Tests;

public sealed class FanOutHandlerA<T> : IRequestHandler<FanOut<T>, string>
{
    public ValueTask<string> HandleAsync(FanOut<T> request, CancellationToken cancellationToken) => new($"a:{request.Id}");
}
