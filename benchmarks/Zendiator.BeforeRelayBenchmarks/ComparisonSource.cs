using BenchmarkDotNet.Attributes;
using Competitive;
using Microsoft.Extensions.DependencyInjection;
using Zendiator;
using Zendiator.DependencyInjection;

namespace BeforeRelayComparison;

public sealed record Items(int Count, Probe? Probe = null) : IStreamRequest<int>, IWork;

[ZendiatorHandler]
public sealed class Handler : IStreamHandler<Items, int>
{
    public IAsyncEnumerable<int> Handle(Items request, CancellationToken token) =>
        Work.Stream(request.Count, asynchronous: false, request.Probe, token);
}

public sealed class Before1 : IAsyncStreamBefore<Items>
{
    public ValueTask BeforeAsync(Items request, CancellationToken token)
    {
        Work.Relay(request, 1);
        return default;
    }
}
public sealed class Before2 : IAsyncStreamBefore<Items>
{
    public ValueTask BeforeAsync(Items request, CancellationToken token)
    {
        Work.Relay(request, 2);
        return default;
    }
}
public sealed class Before3 : IAsyncStreamBefore<Items>
{
    public ValueTask BeforeAsync(Items request, CancellationToken token)
    {
        Work.Relay(request, 3);
        return default;
    }
}
public sealed class Before4 : IAsyncStreamBefore<Items>
{
    public ValueTask BeforeAsync(Items request, CancellationToken token)
    {
        Work.Relay(request, 4);
        return default;
    }
}
public sealed class Before5 : IAsyncStreamBefore<Items>
{
    public ValueTask BeforeAsync(Items request, CancellationToken token)
    {
        Work.Relay(request, 5);
        return default;
    }
}

public sealed class Relay1 : IStreamMiddleware<Items, int>
{
    public ValueTask<IAsyncEnumerator<int>> OpenAsync(Items request, IStreamUpstream<Items, int> next, CancellationToken token)
    {
        Work.Relay(request, 1);
        return next.OpenAsync(request, token);
    }
}
public sealed class Relay2 : IStreamMiddleware<Items, int>
{
    public ValueTask<IAsyncEnumerator<int>> OpenAsync(Items request, IStreamUpstream<Items, int> next, CancellationToken token)
    {
        Work.Relay(request, 2);
        return next.OpenAsync(request, token);
    }
}
public sealed class Relay3 : IStreamMiddleware<Items, int>
{
    public ValueTask<IAsyncEnumerator<int>> OpenAsync(Items request, IStreamUpstream<Items, int> next, CancellationToken token)
    {
        Work.Relay(request, 3);
        return next.OpenAsync(request, token);
    }
}
public sealed class Relay4 : IStreamMiddleware<Items, int>
{
    public ValueTask<IAsyncEnumerator<int>> OpenAsync(Items request, IStreamUpstream<Items, int> next, CancellationToken token)
    {
        Work.Relay(request, 4);
        return next.OpenAsync(request, token);
    }
}
public sealed class Relay5 : IStreamMiddleware<Items, int>
{
    public ValueTask<IAsyncEnumerator<int>> OpenAsync(Items request, IStreamUpstream<Items, int> next, CancellationToken token)
    {
        Work.Relay(request, 5);
        return next.OpenAsync(request, token);
    }
}

[ZendiatorComposition("matchedBefore")]
public sealed class BeforeApp;

[ZendiatorComposition("matchedRelay")]
public sealed class RelayApp;

public enum Route
{
    Before, Relay
}

internal static class Registration
{
    internal static ServiceProvider CreateBefore()
    {
        var services = new ServiceCollection();
        services.AddZendiator<BeforeApp>(configuration =>
        {
            configuration.Handlers<Items>(DispatchFamily.Stream, typeof(Handler));
            configuration.Pipeline<Items>(DispatchFamily.Stream);
            configuration.StreamBefore<Items>(typeof(Before1), typeof(Before2), typeof(Before3), typeof(Before4), typeof(Before5));
        });
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    internal static ServiceProvider CreateRelay()
    {
        var services = new ServiceCollection();
        services.AddZendiator<RelayApp>(configuration =>
        {
            configuration.Handlers<Items>(DispatchFamily.Stream, typeof(Handler));
            configuration.Pipeline<Items>(DispatchFamily.Stream,
                Stage.Wrapper<Relay5>(), Stage.Wrapper<Relay4>(), Stage.Wrapper<Relay3>(), Stage.Wrapper<Relay2>(), Stage.Wrapper<Relay1>());
        });
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}

internal sealed class Fixture : IDisposable
{
    private readonly ServiceProvider provider;
    private readonly IServiceScope scope;
    private readonly CancellationTokenSource cancellation = new();
    internal Func<Items, CancellationToken, IAsyncEnumerable<int>> Invoke
    {
        get;
    }
    internal CancellationToken Token => cancellation.Token;

    internal Fixture(Route route)
    {
        provider = route == Route.Before ? Registration.CreateBefore() : Registration.CreateRelay();
        scope = provider.CreateScope();
        if (route == Route.Before)
        {
            var entry = scope.ServiceProvider.GetRequiredService<Generated.BeforeApp.IZendiator>();
            Invoke = (request, token) => entry.StreamAsync(request, token);
        }
        else
        {
            var entry = scope.ServiceProvider.GetRequiredService<Generated.RelayApp.IZendiator>();
            Invoke = (request, token) => entry.StreamAsync(request, token);
        }
    }

    public void Dispose()
    {
        scope.Dispose();
        provider.Dispose();
        cancellation.Dispose();
    }
}

internal static class Consumer
{
    internal static async ValueTask<int> Full(IAsyncEnumerable<int> source)
    {
        var sum = 0;
        await foreach (var item in source.ConfigureAwait(false))
            sum += item;
        return sum;
    }

    internal static async ValueTask<int> Early(IAsyncEnumerable<int> source)
    {
        await foreach (var item in source.ConfigureAwait(false))
            return item;
        return -1;
    }
}

[MemoryDiagnoser]
public class BeforeRelayBenchmarks
{
    private Fixture fixture = null!;
    private readonly Items shortInput = new(16);
    private readonly Items fullInput = new(1024);
    [Params("Before", "Relay")] public string Mode { get; set; } = null!;

    [GlobalSetup]
    public void Setup()
    {
        fixture = new Fixture(Enum.Parse<Route>(Mode));
    }

    [GlobalCleanup] public void Cleanup() => fixture.Dispose();
    [Benchmark] public ValueTask<int> Full16() => Consumer.Full(fixture.Invoke(shortInput, fixture.Token));
    [Benchmark] public ValueTask<int> Early16() => Consumer.Early(fixture.Invoke(shortInput, fixture.Token));
    [Benchmark] public ValueTask<int> Full1024() => Consumer.Full(fixture.Invoke(fullInput, fixture.Token));
}
