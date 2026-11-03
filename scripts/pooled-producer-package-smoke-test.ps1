<#
.SYNOPSIS
  Verifies the opt-in pooled producer through packed packages and optional Native AOT.
.DESCRIPTION
  Uses a PackageReference-only C# 14 consumer and the package's interceptor props.
  Checks emitted pooled helper/interceptor source, checksum, cleanup, exception
  identity and cancellation. The generator tests cover guaranteed-pending and
  execution-context contracts; this is a package/deployment smoke, not a benchmark.
.PARAMETER Aot
  Also publish and run win-x64 Native AOT. Requires the Windows C++ toolchain.
#>
param(
    [Parameter(Mandatory = $true)][string]$PackageVersion,
    [Parameter(Mandatory = $true)][string]$PackagesDir,
    [string]$OutDir = (Join-Path $PSScriptRoot '..' 'artifacts' 'aot' 'pooled-producer'),
    [string]$WorkingDir = (Join-Path ([IO.Path]::GetTempPath()) ('zendiator-pooled-producer-' + [Guid]::NewGuid().ToString('N'))),
    [switch]$Aot
)

$ErrorActionPreference = 'Stop'
$feed = (Resolve-Path $PackagesDir).Path
$escapedFeed = [System.Security.SecurityElement]::Escape($feed)
$escapedVersion = [System.Security.SecurityElement]::Escape($PackageVersion)
New-Item -ItemType Directory -Path $WorkingDir, $OutDir -Force | Out-Null
$OutDir = (Resolve-Path $OutDir).Path
$WorkingDir = (Resolve-Path $WorkingDir).Path
$project = Join-Path $WorkingDir 'PooledProducerSmoke.csproj'
$config = Join-Path $WorkingDir 'NuGet.Config'

@"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$escapedFeed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@ | Set-Content $config -Encoding UTF8

# Deliberately no ProjectReference or hand-written InterceptorsNamespaces setting.
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>14.0</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsAotCompatible>true</IsAotCompatible>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <IlcTreatWarningsAsErrors>true</IlcTreatWarningsAsErrors>
    <TrimmerTreatWarningsAsErrors>true</TrimmerTreatWarningsAsErrors>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Zendiator" Version="$escapedVersion" />
  </ItemGroup>
</Project>
"@ | Set-Content $project -Encoding UTF8

@'
using System.Runtime.CompilerServices;

if (args.Length != 1 || args[0] is not ("--expect-managed" or "--expect-native"))
    throw new InvalidOperationException("Specify the expected execution mode.");
var expectNative = args[0] == "--expect-native";
if (RuntimeFeature.IsDynamicCodeSupported == expectNative)
    throw new InvalidOperationException("Unexpected managed/native execution mode.");

foreach (var asynchronous in new[] { false, true })
{
    var cursor = new SmokeCursor(32, asynchronous, CancellationToken.None, null);
    var sum = 0;
    try
    {
        while (await cursor.MoveNextAsync()) sum += cursor.Current;
    }
    finally
    {
        await cursor.DisposeAsync();
        await cursor.DisposeAsync();
    }
    if (sum != 496 || cursor.Items != 32 || cursor.Disposals != 1)
        throw new InvalidOperationException("Full enumeration checksum/cleanup mismatch.");
}

var early = new SmokeCursor(32, true, CancellationToken.None, null);
try
{
    for (var i = 0; i < 3; i++)
        if (!await early.MoveNextAsync() || early.Current != i)
            throw new InvalidOperationException("Early enumeration value mismatch.");
}
finally
{
    await early.DisposeAsync();
    await early.DisposeAsync();
}
if (early.Items != 3 || early.Disposals != 1 || await early.MoveNextAsync())
    throw new InvalidOperationException("Early disposal mismatch.");

var sentinel = new FormatException("pooled producer smoke sentinel");
var fault = new SmokeCursor(1, true, CancellationToken.None, sentinel);
try
{
    await fault.MoveNextAsync();
    throw new InvalidOperationException("Expected the producer to fail.");
}
catch (FormatException error) when (ReferenceEquals(error, sentinel)) { }
finally
{
    await fault.DisposeAsync();
}
if (fault.Items != 0 || fault.Disposals != 1)
    throw new InvalidOperationException("Fault cleanup mismatch.");

using var cancellation = new CancellationTokenSource();
cancellation.Cancel();
var canceled = new SmokeCursor(1, true, cancellation.Token, null);
try
{
    await canceled.MoveNextAsync();
    throw new InvalidOperationException("Expected the producer to cancel.");
}
catch (OperationCanceledException error) when (error.CancellationToken == cancellation.Token) { }
finally
{
    await canceled.DisposeAsync();
}
if (canceled.Items != 0 || canceled.Disposals != 1)
    throw new InvalidOperationException("Cancellation cleanup mismatch.");

Console.WriteLine("POOLED-PRODUCER ALL-OK " + (expectNative ? "native" : "managed"));

// Same token/Yield/Publish/Finish shape as the representative WorkStreamCursor.
public sealed partial class SmokeCursor(int count, bool asynchronous, CancellationToken token, Exception? failure)
    : IAsyncEnumerator<int>
{
    private int index;
    private bool finished;
    public int Current { get; private set; }
    public int Items { get; private set; }
    public int Disposals { get; private set; }

    public ValueTask<bool> MoveNextAsync()
    {
        if (finished) return new(false);
        if (index >= count)
        {
            Finish();
            return new(false);
        }
        if (asynchronous) return MovePendingAsync();
        try
        {
            token.ThrowIfCancellationRequested();
            token.ThrowIfCancellationRequested();
            return new(Publish());
        }
        catch
        {
            Finish();
            throw;
        }
    }

    [Zendiator.GeneratePooledProducer]
    private async ValueTask<bool> MovePendingAsync()
    {
        try
        {
            token.ThrowIfCancellationRequested();
            await Task.Yield();
            token.ThrowIfCancellationRequested();
            return Publish();
        }
        catch
        {
            Finish();
            throw;
        }
    }

    private bool Publish()
    {
        if (failure is not null) throw failure;
        Current = index++;
        Items++;
        return true;
    }

    private void Finish()
    {
        if (finished) return;
        finished = true;
        Disposals++;
    }

    public ValueTask DisposeAsync()
    {
        Finish();
        return default;
    }
}
'@ | Set-Content (Join-Path $WorkingDir 'Program.cs') -Encoding UTF8

function Assert-GeneratedProducer([string]$Mode) {
    $generated = Join-Path $WorkingDir "obj/generated/$Mode"
    $helpers = @(Get-ChildItem $generated -Recurse -Filter 'Zendiator.PooledProducer.*.g.cs' |
        Where-Object { $_.Name -notlike 'Zendiator.PooledProducer.Interceptors.*' })
    $interceptors = @(Get-ChildItem $generated -Recurse -Filter 'Zendiator.PooledProducer.Interceptors.*.g.cs')
    if ($helpers.Count -ne 1 -or $interceptors.Count -ne 1) {
        throw "$Mode must emit exactly one pooled helper and one producer interceptor."
    }
    $helper = Get-Content $helpers[0].FullName -Raw
    $interceptor = Get-Content $interceptors[0].FullName -Raw
    if ($helper -notmatch 'PoolingAsyncValueTaskMethodBuilder<>' -or
        $helper -notmatch 'await Task\.Yield\(\)' -or
        $helper -notmatch 'internal async global::System\.Threading\.Tasks\.ValueTask<bool> (__ZendiatorPooled_[A-F0-9]+)\(\)') {
        throw "$Mode did not emit the expected pooled producer implementation."
    }
    $helperName = $Matches[1]
    if ($interceptor -notmatch 'InterceptsLocation\(' -or
        $interceptor -notmatch 'namespace Zendiator\.Generated\.Interceptors' -or
        $interceptor -notmatch ([regex]::Escape("return receiver.$helperName();"))) {
        throw "$Mode interceptor does not target the generated pooled helper."
    }
    $evidence = Join-Path $OutDir "$Mode-generated"
    New-Item -ItemType Directory -Path $evidence -Force | Out-Null
    Copy-Item $helpers[0].FullName, $interceptors[0].FullName -Destination $evidence -Force
    "GENERATED-OK $Mode helper=$helperName" | Tee-Object (Join-Path $OutDir "$Mode-generated.log")
}

$previousPackages = $env:NUGET_PACKAGES
$env:NUGET_PACKAGES = Join-Path $WorkingDir 'global-packages'
try {
    $sdk = dotnet --version
    if ($LASTEXITCODE -ne 0) { throw 'Could not determine the .NET SDK version.' }
    "feed=$feed`nversion=$PackageVersion`nsdk=$sdk`nlang=14.0`naot=$Aot`nrid=win-x64" |
        Set-Content (Join-Path $OutDir 'env.txt') -Encoding UTF8

    dotnet build $project -c Release --configfile $config -p:CompilerGeneratedFilesOutputPath=obj/generated/managed -v minimal 2>&1 |
        Tee-Object (Join-Path $OutDir 'managed-build.log')
    if ($LASTEXITCODE -ne 0) { throw 'Pooled producer package-consumer build failed.' }
    Assert-GeneratedProducer 'managed'
    $managedOutput = @(dotnet (Join-Path $WorkingDir 'bin/Release/net10.0/PooledProducerSmoke.dll') --expect-managed 2>&1 |
        Tee-Object (Join-Path $OutDir 'managed-run.log'))
    if ($LASTEXITCODE -ne 0 -or $managedOutput -notcontains 'POOLED-PRODUCER ALL-OK managed') {
        throw 'Pooled producer managed smoke failed.'
    }

    if ($Aot) {
        dotnet publish $project -c Release -r win-x64 -p:PublishAot=true --self-contained --configfile $config `
            -p:CompilerGeneratedFilesOutputPath=obj/generated/native -o (Join-Path $WorkingDir 'native') -v minimal 2>&1 |
            Tee-Object (Join-Path $OutDir 'native-publish.log')
        if ($LASTEXITCODE -ne 0) { throw 'Pooled producer Native AOT publish failed.' }
        Assert-GeneratedProducer 'native'
        $nativeOutput = @(& (Join-Path $WorkingDir 'native/PooledProducerSmoke.exe') --expect-native 2>&1 |
            Tee-Object (Join-Path $OutDir 'native-run.log'))
        if ($LASTEXITCODE -ne 0 -or $nativeOutput -notcontains 'POOLED-PRODUCER ALL-OK native') {
            throw 'Pooled producer Native AOT smoke failed.'
        }
    }
    Write-Host "Pooled producer package smoke passed (Zendiator $PackageVersion, AOT=$Aot)."
}
finally {
    $env:NUGET_PACKAGES = $previousPackages
    if (($env:SMOKE_KEEP -ne '1') -and (Test-Path $WorkingDir)) {
        Remove-Item -Recurse -Force $WorkingDir
    }
}
