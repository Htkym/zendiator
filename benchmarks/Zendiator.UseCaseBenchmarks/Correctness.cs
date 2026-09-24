using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Competitive;

public static class Correctness
{
    public static void Lifetime(string library, string lifetime, Type handlerType)
    {
        string expected = library switch
        {
            "MediatRHistorical" => "Transient",
            "DispatchR" => "Scoped",
            "Zendiator" => lifetime == "Singleton" ? "Singleton" : "Transient",
            "Mediator" => Registration.MediatorLifetime,
            "Immediate" => lifetime == "Default" ? "Scoped" : lifetime,
            _ => throw new ArgumentOutOfRangeException(nameof(library))
        };
        using var provider = Registration.Create(library, lifetime);
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        object a = first.ServiceProvider.GetRequiredService(handlerType);
        object b = first.ServiceProvider.GetRequiredService(handlerType);
        object c = second.ServiceProvider.GetRequiredService(handlerType);
        Require(ReferenceEquals(a, b) == (expected != "Transient") && ReferenceEquals(a, c) == (expected == "Singleton"), $"{library}: lifetime {lifetime}");
        Results.Add(new { library, suite = "LifetimeIdentity", lifetime, expected, status = "Passed" });
    }
    public static List<object> Results { get; } = [];
    private static void Require(bool value, string message)
    {
        if (!value)
            throw new InvalidOperationException(message);
    }

    private static void Order(Probe probe, int behaviors)
    {
        Require(probe.Events.SequenceEqual(Enumerable.Range(1, behaviors).Append(100)),
            $"Pipeline order: expected {behaviors} before handler; actual {string.Join(",", probe.Events)}");
        Require(probe.Handlers == 1, "Handler must execute exactly once");
    }

    public static async Task Send(string library, int behaviors, Func<Probe, CancellationToken, Task<int>> send)
    {
        Console.WriteLine($"Correctness: {library}, behaviors={behaviors}");
        var probe = new Probe();
        Require(await send(probe, default) == 42, $"{library}: response");
        Order(probe, behaviors);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        probe = new();
        try
        {
            await send(probe, cts.Token);
            throw new InvalidOperationException($"{library}: cancellation not propagated");
        }
        catch (OperationCanceledException) { }
        Require(probe.Handlers == 0, "Cancelled send executed handler");
        probe = new() { Fail = true };
        try
        {
            await send(probe, default);
            throw new InvalidOperationException($"{library}: exception not propagated");
        }
        catch (ArithmeticException) { }
        Results.Add(new { library, suite = "Send", behaviors, status = "Passed" });
    }

    public static async Task Stream(string library, int behaviors,
        Func<int, bool, Probe, CancellationToken, IAsyncEnumerable<int>> stream)
    {
        foreach (int count in new[] { 0, 1, 16, 1024 })
            foreach (bool asynchronous in new[] { false, true })
            {
                var probe = new Probe();
                var sequence = stream(count, asynchronous, probe, default);
                Require(probe.Handlers == 0, $"{library}: stream creation executed handler");
                var actual = new List<int>();
                await foreach (int item in sequence)
                    actual.Add(item);
                Require(actual.SequenceEqual(Enumerable.Range(0, count)), $"{library}: sequence");
                Order(probe, behaviors);
                Require(probe.Disposals == 1, $"{library}: full disposal");

                probe = new();
                await foreach (int item in stream(count, asynchronous, probe, default))
                    break;
                Require(probe.Items == Math.Min(count, 1) && probe.Disposals == 1, $"{library}: early break");

                using var cts = new CancellationTokenSource();
                probe = new();
                await using (var e = stream(count, asynchronous, probe, cts.Token).GetAsyncEnumerator())
                {
                    await e.MoveNextAsync();
                    cts.Cancel();
                    if (count > 1)
                    {
                        try
                        {
                            await e.MoveNextAsync();
                            throw new InvalidOperationException($"{library}: midstream cancellation not propagated");
                        }
                        catch (OperationCanceledException) { }
                    }
                }
                Require(probe.Disposals == 1, $"{library}: cancelled disposal");
                Results.Add(new { library, suite = "Stream", behaviors, count, asynchronous, status = "Passed" });
            }
        var failing = new Probe { Fail = true };
        try
        {
            await foreach (int item in stream(16, true, failing, default)) { }
            throw new InvalidOperationException($"{library}: stream exception not propagated");
        }
        catch (ArithmeticException) { }
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        foreach (bool enumerationToken in new[] { false, true })
        {
            var probe = new Probe();
            try
            {
                var sequence = stream(16, true, probe, enumerationToken ? default : cancelled.Token);
                await foreach (var item in sequence.WithCancellation(enumerationToken ? cancelled.Token : default)) { }
                throw new InvalidOperationException($"{library}: precancelled stream token was ignored (enumeration={enumerationToken})");
            }
            catch (OperationCanceledException) { }
            Require(probe.Handlers == 0, $"{library}: precancelled stream executed handler");
        }
    }

    public static void Save(string directory)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "correctness.json"),
            JsonSerializer.Serialize(Results, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static async Task Void(string library, int behaviors, Func<Counter, Probe, CancellationToken, ValueTask> send)
    {
        var counter = new Counter();
        await Send(library + "-Void", behaviors, async (p, t) =>
        {
            int before = counter.Value;
            await send(counter, p, t);
            Require(counter.Value == before + 1, "Void side effect");
            return 42;
        });
        Require(counter.Value == 1, "Failed/cancelled command changed state");
    }

    public static async Task Notification(string library, int handlers, Func<Counter, Probe, bool, CancellationToken, ValueTask> publish)
    {
        foreach (bool asynchronous in new[] { false, true })
        {
            var counter = new Counter(); var probe = new Probe();
            await publish(counter, probe, asynchronous, default);
            Require(counter.Value == handlers && probe.Handlers == handlers && counter.Active == 0, $"{library}: notification count");
            Require(probe.Events.Where(x => x != 100).Order().SequenceEqual(Enumerable.Range(1, handlers)), $"{library}: notification exactly once");
            if (handlers > 0)
            {
                using var cts = new CancellationTokenSource(); cts.Cancel();
                try { await publish(new(), new(), asynchronous, cts.Token); throw new InvalidOperationException("Notification cancellation"); }
                catch (OperationCanceledException) { }
                catch (AggregateException e) when (library == "Mediator" && e.InnerExceptions.Count == handlers && e.InnerExceptions.All(x => x is OperationCanceledException)) { }
                try { await publish(new(), new() { Fail = true }, asynchronous, default); throw new InvalidOperationException("Notification exception"); }
                catch (ArithmeticException) { }
                catch (AggregateException e) when (library == "Mediator" && e.InnerExceptions.Count == handlers && e.InnerExceptions.All(x => x is ArithmeticException)) { }
            }
            Results.Add(new { library, suite = "Notification", handlers, asynchronous, status = "Passed" });
        }
    }
}
