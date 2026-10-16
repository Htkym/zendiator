using System.Runtime.CompilerServices;
using GettingStarted.Generated.App;
using Microsoft.Extensions.DependencyInjection;
using Zendiator;
using Zendiator.DependencyInjection;

namespace GettingStarted;

public static class Program
{
    public static async Task Main()
    {
        var services = new ServiceCollection();
        services.AddZendiator<App>(configuration =>
        {
            configuration.Handlers<AddNumbers>(DispatchFamily.SendSync, typeof(AddNumbersHandler));
            configuration.Pipeline<AddNumbers>(DispatchFamily.SendSync);

            configuration.Handlers<Greet>(DispatchFamily.SendAsync, typeof(GreetHandler));
            configuration.Pipeline<Greet>(DispatchFamily.SendAsync,
                Stage.Hook<TrimName>(), Stage.Hook<AddExclamation>());

            configuration.Handlers<ReadNumbers>(DispatchFamily.Stream, typeof(ReadNumbersHandler));
            configuration.Pipeline<ReadNumbers>(DispatchFamily.Stream,
                Stage.Hook<KeepEven>(), Stage.Hook<TimesTen>(), Stage.Hook<KeepAtMostForty>());
        });

        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();

        var sum = mediator.Send(new AddNumbers(20, 22));
        var greeting = await mediator.SendAsync(new Greet(" Ada "));
        var numbers = new List<int>();
        await foreach (var number in mediator.StreamAsync(new ReadNumbers(6)))
        {
            numbers.Add(number);
        }

        Console.WriteLine($"Send: {sum}");
        Console.WriteLine($"SendAsync: {greeting}");
        Console.WriteLine($"StreamAsync: {string.Join(", ", numbers)}");
    }
}

[ZendiatorComposition("gettingStarted")]
public sealed class App;

public sealed record AddNumbers(int Left, int Right) : ISyncRequest<int>;

[ZendiatorHandler]
public sealed class AddNumbersHandler : ISyncRequestHandler<AddNumbers, int>
{
    public int Handle(AddNumbers request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return request.Left + request.Right;
    }
}

public sealed record Greet(string Name) : IRequest<string>;

[ZendiatorHandler]
public sealed class GreetHandler : IRequestHandler<Greet, string>
{
    public ValueTask<string> HandleAsync(Greet request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return new($"Hello, {request.Name}");
    }
}

public sealed class TrimName : ISendRequestTransform<Greet>
{
    public Greet Before(Greet request, CancellationToken token) => request with { Name = request.Name.Trim() };
}

public sealed class AddExclamation : ISendResponseTransform<Greet, string>
{
    public string After(Greet request, string response, CancellationToken token) => response + "!";
}

public sealed record ReadNumbers(int Count) : IStreamRequest<int>;

[ZendiatorHandler]
public sealed class ReadNumbersHandler : IStreamRequestHandler<ReadNumbers, int>
{
    public async IAsyncEnumerable<int> HandleAsync(
        ReadNumbers request, [EnumeratorCancellation] CancellationToken token)
    {
        await Task.Yield();
        for (var number = 1; number <= request.Count; number++)
        {
            token.ThrowIfCancellationRequested();
            yield return number;
        }
    }
}

public sealed class KeepEven : IStreamItemFilter<ReadNumbers, int>
{
    public bool Accept(ReadNumbers request, int item, CancellationToken token) => item % 2 == 0;
}

public sealed class TimesTen : IStreamItemMap<ReadNumbers, int>
{
    public int Map(ReadNumbers request, int item, CancellationToken token) => item * 10;
}

public sealed class KeepAtMostForty : IStreamItemFilter<ReadNumbers, int>
{
    public bool Accept(ReadNumbers request, int item, CancellationToken token) => item <= 40;
}
