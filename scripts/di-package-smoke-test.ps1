<#
.SYNOPSIS
  Verifies packed Zendiator nupkgs through DI-only multi-project consumption.

.DESCRIPTION
  Builds a temporary solution against a local feed with an isolated global
  packages folder. No ProjectReference to this repo is used.
  - contracts: Abstractions only.
  - app0: assembly-attribute mode (compatibility coexistence).
  - app1/app2: DI configuration with AddOrders()/AddBilling() wrappers,
    each generating its own unit (multi-application, no cycles).
  - host: calls the wrappers only; its own compilation has no AddZendiator
    calls and needs no Zendiator package reference.
  Asserts generation, interception (via package props, no hand-written
  MSBuild XML), execution results, and clean runtime assets.

.PARAMETER PackageVersion
  Version of Zendiator packages to consume (e.g. 0.1.0-preview.1).

.PARAMETER PackagesDir
  Directory containing the packed .nupkg files (local feed).

.PARAMETER WorkingDir
  Scratch directory for the temporary solution. Removed on success.
#>
param(
    [Parameter(Mandatory = $true)][string]$PackageVersion,
    [Parameter(Mandatory = $true)][string]$PackagesDir,
    [string]$WorkingDir = (Join-Path ([IO.Path]::GetTempPath()) ("zendiator-di-smoke-" + [Guid]::NewGuid().ToString("N")))
)

$ErrorActionPreference = "Stop"

$feed = (Resolve-Path $PackagesDir).Path
New-Item -ItemType Directory -Path "$WorkingDir/contracts", "$WorkingDir/app0", "$WorkingDir/app1", "$WorkingDir/app2", "$WorkingDir/host" -Force | Out-Null

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

public sealed class ContractsMarker;

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
    <PackageReference Include="Zendiator" Version="$PackageVersion" />
  </ItemGroup>
</Project>
"@ | Set-Content "$WorkingDir/app0/app0.csproj" -Encoding UTF8

@"
using Smoke.Contracts;

[assembly: global::Zendiator.GenerateZendiator(Namespace = "App0.Generated")]
[assembly: global::Zendiator.IncludeAssembly(typeof(ContractsMarker))]

namespace Smoke.App0;

public sealed class PingHandler : global::Zendiator.IQueryHandler<Ping, int>
{
    public ValueTask<int> HandleAsync(Ping request, CancellationToken cancellationToken) => new(request.Value * 2);
}
"@ | Set-Content "$WorkingDir/app0/Handlers.cs" -Encoding UTF8

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
    <PackageReference Include="Zendiator" Version="$PackageVersion" />
  </ItemGroup>
</Project>
"@ | Set-Content "$WorkingDir/app1/app1.csproj" -Encoding UTF8

@"
using Microsoft.Extensions.DependencyInjection;
using Zendiator.DependencyInjection;
using Smoke.Contracts;

namespace Smoke.App1;

public sealed class OrdersMarker;

public readonly record struct GetOrderTotal(int Id) : global::Zendiator.IQuery<int>;

public sealed class GetOrderTotalHandler : global::Zendiator.IQueryHandler<GetOrderTotal, int>
{
    public ValueTask<int> HandleAsync(GetOrderTotal request, CancellationToken cancellationToken) => new(request.Id + 100);
}

public static class DependencyInjection
{
    public static IServiceCollection AddOrders(this IServiceCollection services)
    {
        services.AddZendiator(static configuration =>
        {
            configuration.Namespace = "Orders.Generated";
            configuration.RegisterServicesFromAssemblyContaining<OrdersMarker>();
        });
        return services;
    }
}
"@ | Set-Content "$WorkingDir/app1/Orders.cs" -Encoding UTF8

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
    <PackageReference Include="Zendiator" Version="$PackageVersion" />
  </ItemGroup>
</Project>
"@ | Set-Content "$WorkingDir/app2/app2.csproj" -Encoding UTF8

@"
using Microsoft.Extensions.DependencyInjection;
using Zendiator.DependencyInjection;

namespace Smoke.App2;

public sealed class BillingMarker;

public readonly record struct GetInvoiceTotal(int Id) : global::Zendiator.IQuery<int>;

public sealed class GetInvoiceTotalHandler : global::Zendiator.IQueryHandler<GetInvoiceTotal, int>
{
    public ValueTask<int> HandleAsync(GetInvoiceTotal request, CancellationToken cancellationToken) => new(request.Id + 200);
}

public static class DependencyInjection
{
    public static IServiceCollection AddBilling(this IServiceCollection services)
    {
        services.AddZendiator(static configuration =>
        {
            configuration.Namespace = "Billing.Generated";
            configuration.RegisterServicesFromAssemblyContaining<BillingMarker>();
        });
        return services;
    }
}
"@ | Set-Content "$WorkingDir/app2/Billing.cs" -Encoding UTF8

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
    <ProjectReference Include="..\app0\app0.csproj" />
    <ProjectReference Include="..\app1\app1.csproj" />
    <ProjectReference Include="..\app2\app2.csproj" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.0" />
  </ItemGroup>
</Project>
"@ | Set-Content "$WorkingDir/host/host.csproj" -Encoding UTF8

@"
using App0.Generated;
using Microsoft.Extensions.DependencyInjection;
using Smoke.App1;
using Smoke.App2;
using Smoke.Contracts;

var services = new ServiceCollection();
services.AddZendiator();
services.AddOrders();
services.AddBilling();
await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var ping = await scope.ServiceProvider.GetRequiredService<App0.Generated.IZendiator>().SendAsync(new Ping(21));
var order = await scope.ServiceProvider.GetRequiredService<Orders.Generated.IZendiator>().SendAsync(new Smoke.App1.GetOrderTotal(1));
var invoice = await scope.ServiceProvider.GetRequiredService<Billing.Generated.IZendiator>().SendAsync(new Smoke.App2.GetInvoiceTotal(2));
Console.WriteLine($"RESULT {ping} {order} {invoice}");
return ping == 42 && order == 101 && invoice == 202 ? 0 : 1;
"@ | Set-Content "$WorkingDir/host/Program.cs" -Encoding UTF8

$env:NUGET_PACKAGES = Join-Path $WorkingDir "global-packages"
try {
    dotnet build "$WorkingDir/host/host.csproj" -c Release --configfile "$WorkingDir/NuGet.Config" -v minimal
    if ($LASTEXITCODE -ne 0) { throw "smoke build failed" }

    $output = dotnet "$WorkingDir/host/bin/Release/net10.0/host.dll"
    if ($LASTEXITCODE -ne 0) { throw "smoke run failed" }
    if ($output -notcontains "RESULT 42 101 202") { throw "unexpected smoke output: $output" }

    $runtimeFiles = Get-ChildItem "$WorkingDir/host/bin/Release/net10.0/" -Recurse | Select-Object -ExpandProperty Name
    foreach ($banned in @("Microsoft.CodeAnalysis", "MessagePipe")) {
        if ($runtimeFiles -like "*$banned*") { throw "banned runtime asset found: $banned" }
    }
    $deps = Get-Content "$WorkingDir/host/bin/Release/net10.0/host.deps.json" -Raw
    foreach ($banned in @("CodeAnalysis", "MessagePipe")) {
        if ($deps -match $banned) { throw "banned entry in deps.json: $banned" }
    }

    Write-Host "DI package smoke test passed (Zendiator $PackageVersion)."
}
finally {
    if (($env:SMOKE_KEEP -ne "1") -and (Test-Path $WorkingDir)) {
        Remove-Item -Recurse -Force $WorkingDir
    }
}
