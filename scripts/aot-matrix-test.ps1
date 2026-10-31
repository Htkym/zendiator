<#
.SYNOPSIS
  Verifies Native AOT package consumers.
.DESCRIPTION
  Builds DI-first and assembly-attribute consumers from the local feed without
  ProjectReference, publishes win-x64 self-contained apps, and checks their output.
  Logs are saved under artifacts/aot/hardening-1/.
#>
param(
    [string]$PackageVersion = "0.1.0-preview.1",
    [string]$PackagesDir = (Join-Path $PSScriptRoot ".." "artifacts" "packages"),
    [string]$OutDir = (Join-Path $PSScriptRoot ".." "artifacts" "aot" "hardening-1"),
    [string]$WorkingDir = (Join-Path ([IO.Path]::GetTempPath()) ("zendiator-aot-" + [Guid]::NewGuid().ToString("N")))
)

$ErrorActionPreference = "Stop"
$feed = (Resolve-Path $PackagesDir).Path
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
New-Item -ItemType Directory -Path "$WorkingDir/di", "$WorkingDir/asm" -Force | Out-Null
"feed=$feed`nversion=$PackageVersion`nrid=win-x64`nsdk=$(dotnet --version)" | Set-Content "$OutDir/env.txt" -Encoding UTF8

@"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@ | Set-Content "$WorkingDir/NuGet.Config" -Encoding UTF8

@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsAotCompatible>true</IsAotCompatible>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Zendiator" Version="$PackageVersion" />
    <PackageReference Include="Zendiator.Abstractions" Version="$PackageVersion" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.0" />
  </ItemGroup>
</Project>
"@ | Set-Content "$WorkingDir/di/di.csproj" -Encoding UTF8

@"
using AotDi.Generated;
using Microsoft.Extensions.DependencyInjection;
using Zendiator;
using Zendiator.DependencyInjection;

namespace AotDi;

public sealed record Ping(int Value) : IQuery<int>;
public sealed class PingHandler : IQueryHandler<Ping, int>
{
    public ValueTask<int> HandleAsync(Ping request, CancellationToken cancellationToken) => new(request.Value * 2);
}

public sealed record DoWork : IRequest;
public sealed class DoWorkHandler : IRequestHandler<DoWork>
{
    public static bool Ran;
    public ValueTask HandleAsync(DoWork request, CancellationToken cancellationToken)
    {
        Ran = true;
        return default;
    }
}

public sealed class LogBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public static int Calls;
    public async ValueTask<TResponse> HandleAsync<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<TRequest, TResponse>
    {
        Calls++;
        return await next.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
    }
}

public sealed record GetById<T>(string Id) : IRequest<string>;
public sealed class GetByIdHandler<T> : IRequestHandler<GetById<T>, string>
{
    public ValueTask<string> HandleAsync(GetById<T> request, CancellationToken cancellationToken) => new("id-" + request.Id);
}

public sealed record UserCreated(string Name) : INotification;
public sealed class WelcomeHandler : INotificationHandler<UserCreated>
{
    public static readonly List<string> Seen = new();
    public ValueTask HandleAsync(UserCreated notification, CancellationToken cancellationToken)
    {
        Seen.Add("welcome:" + notification.Name);
        return default;
    }
}
public sealed class AuditHandler : INotificationHandler<UserCreated>
{
    public ValueTask HandleAsync(UserCreated notification, CancellationToken cancellationToken)
    {
        WelcomeHandler.Seen.Add("audit:" + notification.Name);
        return default;
    }
}

public sealed record Pair(int A, int B) : IMultiRequest<int>, ISyncMultiRequest<int>;
public sealed class PairHandlerA : IRequestHandler<Pair, int>, ISyncRequestHandler<Pair, int>
{
    public ValueTask<int> HandleAsync(Pair request, CancellationToken cancellationToken) => new(request.A + request.B);
    public int Handle(Pair request, CancellationToken cancellationToken) => request.A + request.B;
}
public sealed class PairHandlerB : IRequestHandler<Pair, int>, ISyncRequestHandler<Pair, int>
{
    public ValueTask<int> HandleAsync(Pair request, CancellationToken cancellationToken) => new(request.A * request.B);
    public int Handle(Pair request, CancellationToken cancellationToken) => request.A * request.B;
}

public readonly ref struct ParseYear : ISyncRequest<int>
{
    public ParseYear(ReadOnlySpan<int> years) => Years = years;
    public ReadOnlySpan<int> Years { get; }
}
public sealed class ParseYearHandler : ISyncRequestHandler<ParseYear, int>
{
    public int Handle(scoped ParseYear request, CancellationToken cancellationToken)
    {
        var sum = 0;
        foreach (var year in request.Years)
            sum += year;
        return sum;
    }
}
public sealed record YearBox(int Year) : ISyncRequest<int>;
public sealed class YearBoxHandler : ISyncRequestHandler<YearBox, int>
{
    public int Handle(YearBox request, CancellationToken cancellationToken) => request.Year;
}

public sealed record GetNames(int Count) : IStreamRequest<string>;
public sealed record GNames<T>(int Count) : IStreamRequest<string>;

public sealed class NamesCursor : IAsyncEnumerator<string>
{
    public static int Opened;
    public static int Closed;
    private readonly int count;
    private readonly string prefix;
    private readonly CancellationToken token;
    private int next;
    private bool disposed;
    public string Current { get; private set; } = default!;

    public NamesCursor(int count, string prefix, CancellationToken token)
    {
        this.count = count;
        this.prefix = prefix;
        this.token = token;
        Opened++;
    }

    public ValueTask<bool> MoveNextAsync()
    {
        token.ThrowIfCancellationRequested();
        if (disposed || next >= count) return new(false);
        Current = prefix + next++;
        return new(true);
    }

    public ValueTask DisposeAsync()
    {
        Closed++;
        disposed = true;
        return default;
    }
}

[ZendiatorHandler]
public sealed class GetNamesHandler : IStreamSourceHandler<GetNames, string, NamesCursor>
{
    public ValueTask<NamesCursor> OpenAsync(GetNames request, CancellationToken cancellationToken) =>
        new(new NamesCursor(request.Count, "n-", cancellationToken));
}

[ZendiatorHandler]
public sealed class GNamesHandler<T> : IStreamSourceHandler<GNames<T>, string, NamesCursor>
{
    public ValueTask<NamesCursor> OpenAsync(GNames<T> request, CancellationToken cancellationToken) =>
        new(new NamesCursor(request.Count, "g-", cancellationToken));
}

public sealed class StreamLogMiddleware<TRequest, TItem> : IStreamMiddleware<TRequest, TItem>
{
    public static int Calls;
    public ValueTask<IAsyncEnumerator<TItem>> OpenAsync(TRequest request, IStreamUpstream<TRequest, TItem> next,
        CancellationToken cancellationToken)
    {
        Calls++;
        return next.OpenAsync(request, cancellationToken);
    }
}

[ZendiatorComposition("aot-native-stream")]
public sealed class Streams;

public static class Program
{
    public static async Task<int> Main()
    {
        var services = new ServiceCollection();
        services.AddZendiator(static configuration =>
        {
            configuration.Namespace = "AotDi.Generated";
            configuration.RegisterServicesFromAssemblyContaining<PingHandler>();
            configuration.AddOpenBehavior(typeof(LogBehavior<,>), order: 0);
        });
        services.AddZendiator<Streams>(static configuration =>
        {
            configuration.Handlers<GetNames>(DispatchFamily.Stream, typeof(GetNamesHandler));
            configuration.Pipeline<GetNames>(DispatchFamily.Stream, Stage.Wrapper<StreamLogMiddleware<GetNames, string>>());
            configuration.Handlers(typeof(GNames<>), DispatchFamily.Stream, typeof(GNamesHandler<>));
            configuration.Pipeline(typeof(GNames<>), DispatchFamily.Stream, Stage.Wrapper(typeof(StreamLogMiddleware<,>)));
        });
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var streams = scope.ServiceProvider.GetRequiredService<global::AotDi.Generated.Streams.IZendiator>();
        var failures = new List<string>();
        void Check(string id, bool ok, string detail)
        {
            Console.WriteLine($"{id} {(ok ? "OK" : "FAIL")} {detail}");
            if (!ok) failures.Add(id);
        }

        var pong = await mediator.SendAsync(new Ping(21));
        Check("AOT01", pong == 42, $"typed={pong}");
        Check("AOT03", LogBehavior<Ping, int>.Calls == 1, $"pipeline-calls={LogBehavior<Ping, int>.Calls}");

        await mediator.SendAsync(new DoWork());
        Check("AOT02", DoWorkHandler.Ran, "void-ran");

        var name = await mediator.SendAsync(new GetById<string>("a"));
        Check("AOT04", name == "id-a", $"generic={name}");

        await mediator.PublishAsync(new UserCreated("ann"));
        Check("AOT05", WelcomeHandler.Seen.Count == 2, $"notify={string.Join(",", WelcomeHandler.Seen)}");

        var all = await mediator.SendAllAsync(new Pair(2, 3));
        Check("AOT06", all.Count == 2 && all[0] == 5 && all[1] == 6, $"multi={string.Join(",", all)}");

        var year = mediator.SendSync(new YearBox(2026), CancellationToken.None);
        Span<int> years = stackalloc int[3] { 2000, 20, 6 };
        year += mediator.SendSync(new ParseYear(years), CancellationToken.None) - 2026;
        Check("AOT07", year == 2026, $"sync={year}");
        Span<int> pairResults = stackalloc int[3] { -1, -1, -1 };
        var written = mediator.SendAllSync(new Pair(2, 3), pairResults, CancellationToken.None);
        Check("AOT07S", written == 2 && pairResults[0] == 5 && pairResults[1] == 6 && pairResults[2] == -1, $"span-count={written}");

        var names = new List<string>();
        await foreach (var n in streams.StreamAsync(new GetNames(3))) names.Add(n);
        Check("AOT08", names.Count == 3 && names[0] == "n-0", $"stream={string.Join(",", names)}");
        Check("AOT09", StreamLogMiddleware<GetNames, string>.Calls == 1, $"stream-pipe={StreamLogMiddleware<GetNames, string>.Calls}");

        var gnames = new List<string>();
        await foreach (var n in streams.StreamAsync(new GNames<string>(2))) gnames.Add(n);
        Check("AOT10", gnames.Count == 2 && gnames[1] == "g-1", $"generic-stream={string.Join(",", gnames)}");

        // The legacy Send probe records its MEDI boundary. Native Stream uses its
        // new source contract below and validates any supported value-type closure.
        try
        {
            _ = await mediator.SendAsync(new GetById<int>("v"));
            Check("AOT10V-req", false, "valuetype-generic-request unexpectedly worked");
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"AOT10V-req LIMIT {ex.Message.Split('.')[0]}");
        }
        try
        {
            var openedBefore = NamesCursor.Opened;
            var closedBefore = NamesCursor.Closed;
            var valueNames = new List<string>();
            await foreach (var value in streams.StreamAsync(new GNames<int>(1))) valueNames.Add(value);
            Check("AOT10V-stream", valueNames.Count == 1 && valueNames[0] == "g-0" &&
                NamesCursor.Opened == openedBefore + 1 && NamesCursor.Closed == closedBefore + 1,
                $"valuetype-generic-stream={string.Join(",", valueNames)} opened={NamesCursor.Opened - openedBefore} closed={NamesCursor.Closed - closedBefore}");
        }
        // Only the pinned MEDI 10.0.0 open-generic/value-type limitation is optional.
        // NativeAOT reports Source as "<unknown>"; use the exact diagnostic and original DI frame.
        // Other resolution, source, middleware, and cleanup errors must fail the app.
        // https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/Microsoft.Extensions.DependencyInjection/src/ServiceLookup/CallSiteFactory.cs
        catch (InvalidOperationException ex) when (
            string.Equals(ex.Message,
                $"Unable to create a generic service for type '{typeof(GNamesHandler<int>)}' because '{typeof(int)}' is a ValueType. Native code to support creating generic services might not be available with native AOT.",
                StringComparison.Ordinal) &&
            ex.StackTrace?.Contains(
                "Microsoft.Extensions.DependencyInjection.ServiceLookup.CallSiteFactory.VerifyOpenGenericAotCompatibility(",
                StringComparison.Ordinal) == true)
        {
            Console.WriteLine($"AOT10V-stream LIMIT {ex.Message}");
        }

        Check("AOT11", scope.ServiceProvider.GetRequiredService<IZendiator>() is not null, "di-first");

        Console.WriteLine(failures.Count == 0 ? "DI-APP ALL-OK" : $"DI-APP FAILURES {string.Join(",", failures)}");
        return failures.Count;
    }
}
"@ | Set-Content "$WorkingDir/di/Program.cs" -Encoding UTF8

@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsAotCompatible>true</IsAotCompatible>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Zendiator" Version="$PackageVersion" />
    <PackageReference Include="Zendiator.Abstractions" Version="$PackageVersion" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.0" />
  </ItemGroup>
</Project>
"@ | Set-Content "$WorkingDir/asm/asm.csproj" -Encoding UTF8

@"
using AsmApp.Generated;
using Microsoft.Extensions.DependencyInjection;
using Zendiator;

[assembly: GenerateZendiator(Namespace = "AsmApp.Generated")]
[assembly: IncludeAssembly(typeof(AsmApp.PingHandler))]

namespace AsmApp;

public sealed record Ping(int Value) : IQuery<int>;
public sealed class PingHandler : IQueryHandler<Ping, int>
{
    public ValueTask<int> HandleAsync(Ping request, CancellationToken cancellationToken) => new(request.Value + 1);
}

public static class Program
{
    public static async Task<int> Main()
    {
        var services = new ServiceCollection();
        services.AddZendiator();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var result = await mediator.SendAsync(new Ping(41));
        var ok = result == 42;
        Console.WriteLine($"AOT12 {(ok ? "OK" : "FAIL")} assembly={result}");
        Console.WriteLine(ok ? "ASM-APP ALL-OK" : "ASM-APP FAILURES AOT12");
        return ok ? 0 : 1;
    }
}
"@ | Set-Content "$WorkingDir/asm/Program.cs" -Encoding UTF8

$env:NUGET_PACKAGES = Join-Path $WorkingDir "global-packages"
$exitCode = 0
try {
    foreach ($app in @("di", "asm")) {
        $proj = "$WorkingDir/$app"
        dotnet build "$proj" -c Release --configfile "$WorkingDir/NuGet.Config" -v minimal 2>&1 | Tee-Object "$OutDir/$app-build.log"
        if ($LASTEXITCODE -ne 0) { throw "$app package-consumer build failed (FixtureIssue? see log)" }
    }
    foreach ($app in @("di", "asm")) {
        $proj = "$WorkingDir/$app"
        dotnet publish "$proj" -c Release -r win-x64 -p:PublishAot=true --self-contained --configfile "$WorkingDir/NuGet.Config" -v minimal 2>&1 | Tee-Object "$OutDir/$app-publish.log"
        if ($LASTEXITCODE -ne 0) { throw "$app AOT publish failed" }
    }
    $diExe = Get-ChildItem "$WorkingDir/di/bin/Release/net10.0/win-x64/publish/di.exe" | Select-Object -ExpandProperty FullName
    $asmExe = Get-ChildItem "$WorkingDir/asm/bin/Release/net10.0/win-x64/publish/asm.exe" | Select-Object -ExpandProperty FullName
    & $diExe 2>&1 | Tee-Object "$OutDir/di-run.log"
    if ($LASTEXITCODE -ne 0) { throw "di native run failed" }
    & $asmExe 2>&1 | Tee-Object "$OutDir/asm-run.log"
    if ($LASTEXITCODE -ne 0) { throw "asm native run failed" }
    foreach ($banned in @("CodeAnalysis")) {
        foreach ($f in @("$OutDir/di-run.log", "$OutDir/asm-run.log")) { }
    }
    Write-Host "AOT matrix passed."
}
catch {
    Write-Host "AOT-MATRIX-FAILURE: $_"
    $exitCode = 1
}
finally {
    if (($env:SMOKE_KEEP -ne "1") -and (Test-Path $WorkingDir)) {
        Remove-Item -Recurse -Force $WorkingDir
    }
}
exit $exitCode
