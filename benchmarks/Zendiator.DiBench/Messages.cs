using Microsoft.Extensions.DependencyInjection;
using Zendiator;
using Zendiator.DependencyInjection;

namespace Zendiator.DiBench;

public sealed class BenchMarker;

public readonly record struct Warm(int Value) : IRequest<int>;

public sealed class WarmHandler : IRequestHandler<Warm, int>
{
    public ValueTask<int> HandleAsync(Warm request, CancellationToken cancellationToken) => new(request.Value + 1);
}

public sealed record Wipe(int Value) : ICommand;

public sealed class WipeHandler : IRequestHandler<Wipe>
{
    public ValueTask HandleAsync(Wipe request, CancellationToken cancellationToken) => default;
}

public sealed record Ticked(int Value) : INotification;

public sealed class TickedHandlerA : INotificationHandler<Ticked>
{
    public ValueTask HandleAsync(Ticked notification, CancellationToken cancellationToken) => default;
}

public sealed class TickedHandlerB : INotificationHandler<Ticked>
{
    public ValueTask HandleAsync(Ticked notification, CancellationToken cancellationToken) => default;
}

public sealed record Duo(int Value) : IMultiRequest<int>;

public sealed class DuoHandlerA : IRequestHandler<Duo, int>
{
    public ValueTask<int> HandleAsync(Duo request, CancellationToken cancellationToken) => new(request.Value + 1);
}

public sealed class DuoHandlerB : IRequestHandler<Duo, int>
{
    public ValueTask<int> HandleAsync(Duo request, CancellationToken cancellationToken) => new(request.Value + 2);
}

public readonly ref struct SpanSum : ISyncRequest<int>
{
    public SpanSum(ReadOnlySpan<byte> data) => Data = data;
    public ReadOnlySpan<byte> Data { get; }
}

public sealed class SpanSumHandler : ISyncRequestHandler<SpanSum, int>
{
    public int Handle(scoped SpanSum request, CancellationToken cancellationToken) => request.Data.Length;
}

public static class BenchRegistration
{
    public static IServiceCollection AddBench(this IServiceCollection services)
    {
        services.AddZendiator(static configuration =>
        {
            configuration.Namespace = "Zendiator.DiBench.Generated";
            configuration.RegisterServicesFromAssemblyContaining<BenchMarker>();
        });
        return services;
    }
}
