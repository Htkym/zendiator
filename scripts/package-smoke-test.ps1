<#
.SYNOPSIS
  Verifies packed Zendiator nupkgs through PackageReference-only multi-project consumption.

.DESCRIPTION
  Builds a temporary 3-project solution (contracts -> Abstractions only,
  app -> Abstractions only, host -> main package) against a local feed with an
  isolated global packages folder. No ProjectReference to this repo is used.
  Asserts the generator runs, the host executes, and no Roslyn/MessagePipe
  reaches the runtime output.

.PARAMETER PackageVersion
  Version of Zendiator packages to consume (e.g. 0.1.0-preview.1).

.PARAMETER PackagesDir
  Directory containing the packed .nupkg files (local feed).

.PARAMETER WorkingDir
  Scratch directory for the temporary solution. Removed on success.

.PARAMETER Aot
  Also publish the host as Native AOT (win-x64, self-contained) and run it.
  Requires the .NET C++ prerequisites (Desktop Development for C++).
#>
param(
    [Parameter(Mandatory = $true)][string]$PackageVersion,
    [Parameter(Mandatory = $true)][string]$PackagesDir,
    [string]$WorkingDir = (Join-Path ([IO.Path]::GetTempPath()) ("zendiator-smoke-" + [Guid]::NewGuid().ToString("N"))),
    [switch]$Aot
)

$ErrorActionPreference = "Stop"

$feed = (Resolve-Path $PackagesDir).Path
New-Item -ItemType Directory -Path "$WorkingDir/contracts", "$WorkingDir/app", "$WorkingDir/host" -Force | Out-Null

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
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Zendiator.Abstractions" Version="$PackageVersion" />
  </ItemGroup>
</Project>
"@ | Set-Content "$WorkingDir/contracts/contracts.csproj" -Encoding UTF8

@"
namespace Smoke.Contracts;

public sealed class Marker;

public readonly record struct Ping(int Value) : global::Zendiator.IQuery<int>;
"@ | Set-Content "$WorkingDir/contracts/Contracts.cs" -Encoding UTF8

@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\contracts\contracts.csproj" />
    <PackageReference Include="Zendiator.Abstractions" Version="$PackageVersion" />
  </ItemGroup>
</Project>
"@ | Set-Content "$WorkingDir/app/app.csproj" -Encoding UTF8

@"
using Smoke.Contracts;

namespace Smoke.App;

public sealed class Marker;

public sealed class PingHandler : global::Zendiator.IQueryHandler<Ping, int>
{
    public ValueTask<int> HandleAsync(Ping request, CancellationToken cancellationToken) => new(request.Value * 2);
}
"@ | Set-Content "$WorkingDir/app/Handlers.cs" -Encoding UTF8

@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\contracts\contracts.csproj" />
    <ProjectReference Include="..\app\app.csproj" />
    <PackageReference Include="Zendiator" Version="$PackageVersion" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.0" />
  </ItemGroup>
</Project>
"@ | Set-Content "$WorkingDir/host/host.csproj" -Encoding UTF8

@"
using Microsoft.Extensions.DependencyInjection;
using Smoke.Contracts;
using Smoke.Host;

var services = new ServiceCollection();
services.AddZendiator();
await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
var result = await mediator.SendAsync(new Ping(21));
Console.WriteLine($"RESULT {result}");
return result == 42 ? 0 : 1;
"@ | Set-Content "$WorkingDir/host/Program.cs" -Encoding UTF8

@"
namespace Smoke.Host;

[global::Zendiator.GenerateZendiator]
[global::Zendiator.IncludeAssembly(typeof(Contracts.Marker))]
[global::Zendiator.IncludeAssembly(typeof(App.Marker))]
public sealed partial class Zendiator;
"@ | Set-Content "$WorkingDir/host/Mediator.cs" -Encoding UTF8

$env:NUGET_PACKAGES = Join-Path $WorkingDir "global-packages"
try {
    dotnet build "$WorkingDir/host/host.csproj" -c Release --configfile "$WorkingDir/NuGet.Config" -v minimal
    if ($LASTEXITCODE -ne 0) { throw "smoke build failed" }

    $output = dotnet "$WorkingDir/host/bin/Release/net10.0/host.dll"
    if ($LASTEXITCODE -ne 0) { throw "smoke run failed" }
    if ($output -notcontains "RESULT 42") { throw "unexpected smoke output: $output" }

    $runtimeFiles = Get-ChildItem "$WorkingDir/host/bin/Release/net10.0/" -Recurse | Select-Object -ExpandProperty Name
    foreach ($banned in @("Microsoft.CodeAnalysis", "MessagePipe")) {
        if ($runtimeFiles -like "*$banned*") { throw "banned runtime asset found: $banned" }
    }
    $deps = Get-Content "$WorkingDir/host/bin/Release/net10.0/host.deps.json" -Raw
    foreach ($banned in @("CodeAnalysis", "MessagePipe")) {
        if ($deps -match $banned) { throw "banned entry in deps.json: $banned" }
    }

    if ($Aot) {
        dotnet publish "$WorkingDir/host/host.csproj" -c Release -r win-x64 -p:PublishAot=true --self-contained -v minimal
        if ($LASTEXITCODE -ne 0) { throw "AOT publish failed" }
        $native = "$WorkingDir/host/bin/Release/net10.0/win-x64/publish/host.exe"
        $nativeOutput = & $native
        if ($LASTEXITCODE -ne 0) { throw "AOT run failed" }
        if ($nativeOutput -notcontains "RESULT 42") { throw "unexpected AOT output: $nativeOutput" }
        Write-Host "Package AOT smoke test passed (Zendiator $PackageVersion)."
    }

    Write-Host "Package smoke test passed (Zendiator $PackageVersion)."
}
finally {
    if (($env:SMOKE_KEEP -ne "1") -and (Test-Path $WorkingDir)) {
        Remove-Item -Recurse -Force $WorkingDir
    }
}
