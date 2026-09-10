<#
.SYNOPSIS
  Hardening-1 AOT verification matrix (AOT01-AOT12) + package consumer check.
.DESCRIPTION
  Scaffolds two temp apps (DI-first, Assembly-compat) against the local
  package feed only (no ProjectReference), publishes each as Native AOT
  (win-x64, self-contained), runs the native binaries, and asserts every
  AOTxx scenario line. Logs are copied to artifacts/aot/hardening-1/.
  Product code is not modified; failures are classified per plan section 5.
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

public sealed record Pair(int A, int B) : IMultiRequest<int>;
public sealed class PairHandlerA : IRequestHandler<Pair, int>
{
    public ValueTask<int> HandleAsync(Pair request, CancellationToken cancellationToken) => new(request.A + request.B);
}
public sealed class PairHandlerB : IRequestHandler<Pair, int>
{
    public ValueTask<int> HandleAsync(Pair request, CancellationToken cancellationToken) => new(request.A * request.B);
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
public sealed class GetNamesHandler : IStreamRequestHandler<GetNames, string>
{
    public async IAsyncEnumerable<string> HandleAsync(GetNames request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var i = 0; i < request.Count; i++)
            yield return "n-" + i;
        await Task.CompletedTask;
    }
}

public sealed class StreamLogBehavior<TRequest, TItem> : IStreamPipelineBehavior<TRequest, TItem>
    where TRequest : IStreamRequest<TItem>
{
    public static int Calls;
    public async IAsyncEnumerable<TItem> HandleAsync<TNext>(TRequest request, TNext next, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        where TNext : struct, IStreamContinuation<TRequest, TItem>
    {
        Calls++;
        await foreach (var item in next.InvokeAsync(request, cancellationToken))
            yield return item;
    }
}

public sealed record GNames<T>(int Count) : IStreamRequest<string>;
public sealed class GNamesHandler<T> : IStreamRequestHandler<GNames<T>, string>
{
    public async IAsyncEnumerable<string> HandleAsync(GNames<T> request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var i = 0; i < request.Count; i++)
            yield return "g-" + i;
        await Task.CompletedTask;
    }
}

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
            configuration.AddOpenStreamBehavior(typeof(StreamLogBehavior<,>), order: 1);
        });
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
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

        var names = new List<string>();
        await foreach (var n in mediator.StreamAsync(new GetNames(3))) names.Add(n);
        Check("AOT08", names.Count == 3 && names[0] == "n-0", $"stream={string.Join(",", names)}");
        Check("AOT09", StreamLogBehavior<GetNames, string>.Calls == 1, $"stream-pipe={StreamLogBehavior<GetNames, string>.Calls}");

        var gnames = new List<string>();
        await foreach (var n in mediator.StreamAsync(new GNames<string>(2))) gnames.Add(n);
        Check("AOT10", gnames.Count == 2 && gnames[1] == "g-1", $"generic-stream={string.Join(",", gnames)}");

        // Boundary probe: open-generic handlers closed over a value type need
        // runtime generic construction, unavailable under NativeAOT (MEDI).
        // Reported, never failed: proves the boundary for request AND stream.
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
            await foreach (var _ in mediator.StreamAsync(new GNames<int>(1))) { }
            Check("AOT10V-stream", false, "valuetype-generic-stream unexpectedly worked");
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"AOT10V-stream LIMIT {ex.Message.Split('.')[0]}");
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
