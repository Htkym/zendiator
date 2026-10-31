using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Zendiator.Sample.Application;
using Zendiator.Sample.Application.Generated;
using Zendiator.Sample.Contracts;
using Zendiator.Sample.Host;

var services = new ServiceCollection();
services.AddLogging(static builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
services.AddApplication();

await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
await using var scope = provider.CreateAsyncScope();
var zendiator = scope.ServiceProvider.GetRequiredService<IZendiator>();

var targets = await zendiator.SendAsync(new GetTargetYearQuery(2026));
Console.WriteLine($"Year {targets.Year}: {string.Join(", ", targets.Names)}");

var created = await zendiator.SendAsync(new RegisterHouseholdCommand("Tanaka"));
Console.WriteLine(created.IsSuccess ? $"Created {created.Value.Value}" : $"Failed {created.Error.Field}: {created.Error.Message}");

var invalid = await zendiator.SendAsync(new RegisterHouseholdCommand("  "));
Console.WriteLine(invalid.IsSuccess ? $"Created {invalid.Value.Value}" : $"Failed {invalid.Error.Field}: {invalid.Error.Message}");

var purged = Guid.NewGuid();
await zendiator.SendAsync(new PurgeHouseholdCommand(purged));
Console.WriteLine($"Purged {purged}");

Console.WriteLine($"Counted {await LoadCount<HostMarker>(zendiator, CancellationToken.None)}");

await zendiator.PublishAsync(new HouseholdPurged(purged));
Console.WriteLine($"Published purge of {purged}");

foreach (var quote in await zendiator.SendAllAsync(new GetHouseholdQuotes("P1")))
    Console.WriteLine($"Quote {quote.Vendor}: {quote.Price}");

Span<byte> yearBytes = stackalloc byte[4] { (byte)'2', (byte)'0', (byte)'2', (byte)'6' };
Console.WriteLine($"Parsed {zendiator.SendSync(new ParseYear(yearBytes))}");

zendiator.SendSync(new ResetCache());
Console.WriteLine("Reset cache");

await zendiator.SendAsync(new LegacyPing());
Console.WriteLine("Legacy ping");

// Stream has its own named composition; the existing Send composition is unchanged.
var streams = scope.ServiceProvider.GetRequiredService<global::Zendiator.Sample.Application.Generated.ApplicationStreams.IZendiator>();
await foreach (var name in streams.StreamAsync(new GetHouseholdNames(3)))
    Console.WriteLine($"Streamed {name}");

return 0;

static ValueTask<int> LoadCount<TMarker>(IZendiator sender, CancellationToken cancellationToken) =>
    sender.SendAsync(new GetHouseholdCount<TMarker>(), cancellationToken);
